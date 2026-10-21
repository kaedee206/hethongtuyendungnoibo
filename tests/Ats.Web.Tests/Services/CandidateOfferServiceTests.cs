using Ats.Web.Data;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Models.ViewModels.Offers;
using Ats.Web.Services;
using Ats.Web.Services.Interfaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ats.Web.Tests.Services;

/// <summary>
/// Unit Tests kiểm thử toàn diện nghiệp vụ Ứng viên nhận thư mời nhận việc và phản hồi (SCRUM-342).
/// Yêu cầu:
/// 1. Email chứa nội dung offer và liên kết phản hồi có hiệu lực tới hết thời hạn.
/// 2. Ứng viên chọn Chấp nhận hoặc Từ chối, có ô ghi ý kiến.
/// 3. Phản hồi cập nhật ngay trạng thái trên pipeline.
/// 4. Quá thời hạn mà không phản hồi thì offer tự chuyển sang Hết hạn (EXPIRED).
/// </summary>
public class CandidateOfferServiceTests
{
    private static ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"AtsCandidateOfferDb_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static async Task<(
        Department dept,
        JobPosting posting,
        PipelineStage offerStage,
        PipelineStage hiredStage,
        Candidate candidate,
        Application app,
        User recruiter,
        JobOffer offer)> SeedOfferDataAsync(
        ApplicationDbContext context,
        OfferStatus initialStatus = OfferStatus.SENT_TO_CANDIDATE,
        DateOnly? expirationDate = null)
    {
        var recruiter = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Vũ Thu Trang",
            Email = $"recruiter_{Guid.NewGuid():N}@noveratech.vn",
            PasswordHash = "hash123",
            Status = "ACTIVE"
        };
        await context.Users.AddAsync(recruiter);

        var dept = new Department
        {
            Id = Guid.NewGuid(),
            Name = "Khối Công nghệ & Sản phẩm",
            Code = $"TECH_{Guid.NewGuid():N}"[..10]
        };
        await context.Departments.AddAsync(dept);

        var posting = new JobPosting
        {
            Id = Guid.NewGuid(),
            Title = "Senior .NET Backend Architect",
            WorkLocation = "Tòa nhà Novera Tower, Cầu Giấy, Hà Nội",
            Status = JobPostingStatus.PUBLISHED
        };
        await context.JobPostings.AddAsync(posting);

        var offerStage = new PipelineStage
        {
            Id = Guid.NewGuid(),
            Name = "Đề xuất Offer",
            StageOrder = 5
        };
        var hiredStage = new PipelineStage
        {
            Id = Guid.NewGuid(),
            Name = "Tuyển dụng thành công",
            StageOrder = 6
        };
        await context.PipelineStages.AddRangeAsync(offerStage, hiredStage);

        var candidate = new Candidate
        {
            Id = Guid.NewGuid(),
            FirstName = "Trần",
            LastName = "Quốc Bảo",
            Email = $"bao.tran_{Guid.NewGuid():N}@gmail.com",
            Phone = "0987654321"
        };
        await context.Candidates.AddAsync(candidate);

        var app = new Application
        {
            Id = Guid.NewGuid(),
            CandidateId = candidate.Id,
            JobPostingId = posting.Id,
            CurrentStageId = offerStage.Id,
            Status = ApplicationStatus.IN_PROCESS,
            Candidate = candidate,
            JobPosting = posting
        };
        await context.Applications.AddAsync(app);

        var offer = new JobOffer
        {
            Id = Guid.NewGuid(),
            ApplicationId = app.Id,
            CreatedByRecruiterId = recruiter.Id,
            JobTitle = "Senior .NET Backend Architect",
            WorkLocation = "Tòa nhà Novera Tower, Cầu Giấy, Hà Nội",
            BaseSalary = 45_000_000m,
            BonusAllowance = 5_000_000m,
            TotalPackage = 50_000_000m,
            ProposedJoinDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)),
            ExpirationDate = expirationDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            ProbationPeriodMonths = 2,
            ContractType = "Chính thức (Toàn thời gian)",
            Status = initialStatus,
            Application = app,
            CreatedByRecruiter = recruiter
        };
        await context.JobOffers.AddAsync(offer);

        await context.SaveChangesAsync();
        return (dept, posting, offerStage, hiredStage, candidate, app, recruiter, offer);
    }

    [Fact]
    public void GenerateAndValidateToken_ShouldSucceed_WhenTokenMatchesOfferAndEmail()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var mockEmail = new Mock<IEmailService>();
        var service = new CandidateOfferService(context, mockEmail.Object, NullLogger<CandidateOfferService>.Instance);

        var offerId = Guid.NewGuid();
        var candidateEmail = "bao.tran@gmail.com";

        // Tạo offer giả trong db để validate
        var cand = new Candidate { Id = Guid.NewGuid(), FirstName = "Bảo", LastName = "Trần", Email = candidateEmail };
        var app = new Application { Id = Guid.NewGuid(), CandidateId = cand.Id, Candidate = cand };
        var offer = new JobOffer { Id = offerId, ApplicationId = app.Id, Application = app };
        context.Candidates.Add(cand);
        context.Applications.Add(app);
        context.JobOffers.Add(offer);
        context.SaveChanges();

        // Act
        var token = service.GenerateOfferResponseToken(offerId, candidateEmail);
        var isValid = service.ValidateOfferResponseToken(token, out var extractedId);

        // Assert
        token.Should().NotBeNullOrWhiteSpace();
        token.Should().Contain(offerId.ToString("D"));
        isValid.Should().BeTrue();
        extractedId.Should().Be(offerId);
    }

    [Fact]
    public void ValidateOfferResponseToken_ShouldFail_WhenTokenTamperedOrInvalidFormat()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var mockEmail = new Mock<IEmailService>();
        var service = new CandidateOfferService(context, mockEmail.Object, NullLogger<CandidateOfferService>.Instance);

        // Act & Assert
        service.ValidateOfferResponseToken("", out _).Should().BeFalse();
        service.ValidateOfferResponseToken("invalid_token_format", out _).Should().BeFalse();
        service.ValidateOfferResponseToken($"{Guid.NewGuid():D}_fakehash12345678", out _).Should().BeFalse();
    }

    [Fact]
    public async Task GetOfferDetailsByTokenAsync_ShouldReturnDetails_WhenOfferIsValidAndWithinDeadline()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (_, _, _, _, candidate, _, _, offer) = await SeedOfferDataAsync(context);
        var mockEmail = new Mock<IEmailService>();
        var service = new CandidateOfferService(context, mockEmail.Object, NullLogger<CandidateOfferService>.Instance);

        var token = service.GenerateOfferResponseToken(offer.Id, candidate.Email);

        // Act
        var result = await service.GetOfferDetailsByTokenAsync(token);

        // Assert
        result.Should().NotBeNull();
        result!.OfferId.Should().Be(offer.Id);
        result.CandidateName.Should().Be("Trần Quốc Bảo");
        result.BaseSalary.Should().Be(45_000_000m);
        result.TotalPackage.Should().Be(50_000_000m);
        result.IsExpired.Should().BeFalse();
        result.CanRespond.Should().BeTrue();
        result.DaysRemaining.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetOfferDetailsByTokenAsync_ShouldAutoExpireOffer_WhenPastExpirationDate()
    {
        // Arrange: Offer có ngày hết hạn là 2 ngày trước
        using var context = CreateInMemoryDbContext();
        var pastDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2));
        var (_, _, _, _, candidate, app, _, offer) = await SeedOfferDataAsync(context, OfferStatus.SENT_TO_CANDIDATE, pastDate);
        var mockEmail = new Mock<IEmailService>();
        var service = new CandidateOfferService(context, mockEmail.Object, NullLogger<CandidateOfferService>.Instance);

        var token = service.GenerateOfferResponseToken(offer.Id, candidate.Email);

        // Act: Ứng viên truy cập link khi đã quá thời hạn
        var result = await service.GetOfferDetailsByTokenAsync(token);

        // Assert: Offer tự động chuyển sang Hết hạn (EXPIRED)
        result.Should().NotBeNull();
        result!.IsExpired.Should().BeTrue();
        result.CanRespond.Should().BeFalse();
        result.Status.Should().Be(OfferStatus.EXPIRED);

        // Kiểm tra trong CSDL trạng thái đã cập nhật
        var updatedOffer = await context.JobOffers.FindAsync(offer.Id);
        updatedOffer.Should().NotBeNull();
        updatedOffer!.Status.Should().Be(OfferStatus.EXPIRED);
        updatedOffer.ExpiredAt.Should().NotBeNull();

        // Kiểm tra lịch sử chuyển giai đoạn tự động ghi nhận
        var stageHistory = await context.ApplicationStageHistories
            .FirstOrDefaultAsync(h => h.ApplicationId == app.Id);
        stageHistory.Should().NotBeNull();
        stageHistory!.Comment.Should().Contain("HẾT HẠN");
    }

    [Fact]
    public async Task SubmitOfferResponseAsync_ShouldAcceptOfferAndUpdatePipelineToHired_WhenCandidateAccepts()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (_, _, _, hiredStage, candidate, app, _, offer) = await SeedOfferDataAsync(context);
        var mockEmail = new Mock<IEmailService>();
        var service = new CandidateOfferService(context, mockEmail.Object, NullLogger<CandidateOfferService>.Instance);

        var token = service.GenerateOfferResponseToken(offer.Id, candidate.Email);
        var input = new CandidateOfferResponseInputModel
        {
            Token = token,
            Decision = "ACCEPT",
            Feedback = "Tôi rất vinh hạnh và đồng ý gia nhập NoveraTech đúng ngày 15 tới!"
        };

        // Act
        var result = await service.SubmitOfferResponseAsync(input);

        // Assert: Kết quả xử lý thành công
        result.Success.Should().BeTrue();
        result.NewStatus.Should().Be(OfferStatus.ACCEPTED);
        result.Message.Should().Contain("Chúc mừng");

        // Assert: Offer trong CSDL cập nhật ACCEPTED và lưu ý kiến
        var updatedOffer = await context.JobOffers.FindAsync(offer.Id);
        updatedOffer.Should().NotBeNull();
        updatedOffer!.Status.Should().Be(OfferStatus.ACCEPTED);
        updatedOffer.CandidateFeedback.Should().Be(input.Feedback);
        updatedOffer.CandidateRespondedAt.Should().NotBeNull();

        // Assert: Pipeline cập nhật ngay sang Hired
        var updatedApp = await context.Applications.FindAsync(app.Id);
        updatedApp.Should().NotBeNull();
        updatedApp!.CurrentStageId.Should().Be(hiredStage.Id);
        updatedApp.Status.Should().Be(ApplicationStatus.HIRED);

        // Assert: Ghi nhận lịch sử chuyển giai đoạn
        var stageHistory = await context.ApplicationStageHistories
            .FirstOrDefaultAsync(h => h.ApplicationId == app.Id && h.ToStageId == hiredStage.Id);
        stageHistory.Should().NotBeNull();
        stageHistory!.Comment.Should().Contain("CHẤP NHẬN");

        // Assert: Ghi nhận tương tác ứng viên
        var interaction = await context.CandidateInteractions
            .FirstOrDefaultAsync(i => i.ApplicationId == app.Id && i.CandidateId == candidate.Id);
        interaction.Should().NotBeNull();
        interaction!.Title.Should().Contain("chấp nhận");
        interaction.Content.Should().Contain(input.Feedback);
    }

    [Fact]
    public async Task SubmitOfferResponseAsync_ShouldRejectOfferAndUpdatePipelineToRejected_WhenCandidateDeclines()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (_, _, _, _, candidate, app, _, offer) = await SeedOfferDataAsync(context);
        var mockEmail = new Mock<IEmailService>();
        var service = new CandidateOfferService(context, mockEmail.Object, NullLogger<CandidateOfferService>.Instance);

        var token = service.GenerateOfferResponseToken(offer.Id, candidate.Email);
        var input = new CandidateOfferResponseInputModel
        {
            Token = token,
            Decision = "DECLINE",
            Feedback = "Rất tiếc tôi vừa nhận được offer khác phù hợp hơn về mặt địa lý."
        };

        // Act
        var result = await service.SubmitOfferResponseAsync(input);

        // Assert: Kết quả xử lý thành công
        result.Success.Should().BeTrue();
        result.NewStatus.Should().Be(OfferStatus.REJECTED);

        // Assert: CSDL cập nhật REJECTED
        var updatedOffer = await context.JobOffers.FindAsync(offer.Id);
        updatedOffer.Should().NotBeNull();
        updatedOffer!.Status.Should().Be(OfferStatus.REJECTED);
        updatedOffer.CandidateFeedback.Should().Be(input.Feedback);

        var updatedApp = await context.Applications.FindAsync(app.Id);
        updatedApp.Should().NotBeNull();
        updatedApp!.Status.Should().Be(ApplicationStatus.REJECTED);

        // Assert: Lịch sử ghi nhận từ chối
        var stageHistory = await context.ApplicationStageHistories
            .FirstOrDefaultAsync(h => h.ApplicationId == app.Id);
        stageHistory.Should().NotBeNull();
        stageHistory!.Comment.Should().Contain("TỪ CHỐI");
    }

    [Fact]
    public async Task SubmitOfferResponseAsync_ShouldFailAndAutoExpire_WhenOfferIsOverdue()
    {
        // Arrange: Offer hết hạn từ 3 ngày trước
        using var context = CreateInMemoryDbContext();
        var pastDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-3));
        var (_, _, _, _, candidate, _, _, offer) = await SeedOfferDataAsync(context, OfferStatus.SENT_TO_CANDIDATE, pastDate);
        var mockEmail = new Mock<IEmailService>();
        var service = new CandidateOfferService(context, mockEmail.Object, NullLogger<CandidateOfferService>.Instance);

        var token = service.GenerateOfferResponseToken(offer.Id, candidate.Email);
        var input = new CandidateOfferResponseInputModel
        {
            Token = token,
            Decision = "ACCEPT",
            Feedback = "Tôi muốn đồng ý nhận việc."
        };

        // Act: Ứng viên cố gắng submit khi đã quá hạn
        var result = await service.SubmitOfferResponseAsync(input);

        // Assert: Thất bại và tự động chuyển sang Hết hạn
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("hết thời hạn");

        var updatedOffer = await context.JobOffers.FindAsync(offer.Id);
        updatedOffer!.Status.Should().Be(OfferStatus.EXPIRED);
    }

    [Fact]
    public async Task SubmitOfferResponseAsync_ShouldPreventDuplicateResponse_WhenAlreadyAcceptedOrDeclined()
    {
        // Arrange: Offer đã được chấp nhận trước đó
        using var context = CreateInMemoryDbContext();
        var (_, _, _, _, candidate, _, _, offer) = await SeedOfferDataAsync(context, OfferStatus.ACCEPTED);
        offer.CandidateRespondedAt = DateTimeOffset.UtcNow.AddHours(-1);
        await context.SaveChangesAsync();

        var mockEmail = new Mock<IEmailService>();
        var service = new CandidateOfferService(context, mockEmail.Object, NullLogger<CandidateOfferService>.Instance);

        var token = service.GenerateOfferResponseToken(offer.Id, candidate.Email);
        var input = new CandidateOfferResponseInputModel
        {
            Token = token,
            Decision = "DECLINE",
            Feedback = "Tôi muốn đổi ý từ chối."
        };

        // Act
        var result = await service.SubmitOfferResponseAsync(input);

        // Assert: Chặn không cho gửi lại phản hồi
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Không thể gửi lại phản hồi");
    }

    [Fact]
    public async Task SendOfferToCandidateAsync_ShouldUpdateStatusToSentAndSendEmailWithResponseLink()
    {
        // Arrange: Offer đã duyệt (APPROVED) sẵn sàng gửi cho ứng viên
        using var context = CreateInMemoryDbContext();
        var (_, _, _, _, candidate, _, recruiter, offer) = await SeedOfferDataAsync(context, OfferStatus.APPROVED);

        var mockEmail = new Mock<IEmailService>();
        mockEmail.Setup(e => e.SendOfferLetterNotificationAsync(
                candidate.Email,
                It.IsAny<string>(),
                offer.JobTitle,
                offer.BaseSalary,
                It.IsAny<DateTime?>(),
                It.IsAny<string?>(),
                offer.ExpirationDate,
                offer.WorkLocation,
                offer.BonusAllowance,
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = new CandidateOfferService(context, mockEmail.Object, NullLogger<CandidateOfferService>.Instance);

        // Act
        var (success, message, responseUrl) = await service.SendOfferToCandidateAsync(
            offer.Id,
            recruiter.Id,
            customMessage: "Chào mừng bạn đến với NoveraTech!",
            baseUrl: "https://tuyendung.noveratech.vn");

        // Assert
        success.Should().BeTrue();
        message.Should().Contain("Đã gửi thư mời");
        responseUrl.Should().NotBeNullOrWhiteSpace();
        responseUrl.Should().StartWith("https://tuyendung.noveratech.vn/ung-vien/offer/");

        // Kiểm tra Offer đã cập nhật sang SENT_TO_CANDIDATE và lưu ResponseToken
        var updatedOffer = await context.JobOffers.FindAsync(offer.Id);
        updatedOffer.Should().NotBeNull();
        updatedOffer!.Status.Should().Be(OfferStatus.SENT_TO_CANDIDATE);
        updatedOffer.ResponseToken.Should().NotBeNullOrWhiteSpace();

        // Kiểm tra email service đã được gọi đúng tham số
        mockEmail.Verify(e => e.SendOfferLetterNotificationAsync(
            candidate.Email,
            "Trần Quốc Bảo",
            offer.JobTitle,
            offer.BaseSalary,
            It.IsAny<DateTime?>(),
            responseUrl,
            offer.ExpirationDate,
            offer.WorkLocation,
            offer.BonusAllowance,
            "Chào mừng bạn đến với NoveraTech!",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CheckAndExpireOverdueOffersAsync_ShouldExpireAllOverdueOffers()
    {
        // Arrange: Tạo 2 offer quá hạn và 1 offer còn hạn
        using var context = CreateInMemoryDbContext();
        var pastDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));

        await SeedOfferDataAsync(context, OfferStatus.SENT_TO_CANDIDATE, pastDate);
        await SeedOfferDataAsync(context, OfferStatus.SENT_TO_CANDIDATE, pastDate);
        await SeedOfferDataAsync(context, OfferStatus.SENT_TO_CANDIDATE, futureDate);

        var mockEmail = new Mock<IEmailService>();
        var service = new CandidateOfferService(context, mockEmail.Object, NullLogger<CandidateOfferService>.Instance);

        // Act
        var expiredCount = await service.CheckAndExpireOverdueOffersAsync();

        // Assert: 2 offer quá hạn bị chuyển sang EXPIRED
        expiredCount.Should().Be(2);

        var activeOffers = await context.JobOffers
            .Where(o => o.Status == OfferStatus.SENT_TO_CANDIDATE)
            .ToListAsync();
        activeOffers.Should().HaveCount(1);

        var expiredOffers = await context.JobOffers
            .Where(o => o.Status == OfferStatus.EXPIRED)
            .ToListAsync();
        expiredOffers.Should().HaveCount(2);
    }
}
