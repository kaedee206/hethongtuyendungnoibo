using Ats.Web.Data;
using Ats.Web.Models.DTOs;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
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
/// Unit Tests kiểm thử toàn diện nghiệp vụ Dời và Hủy lịch phỏng vấn (Scrum - Số 55).
/// Tiêu chí nghiệm thu:
/// 1. Đổi được ngày giờ, hình thức và danh sách người phỏng vấn.
/// 2. Hủy bắt buộc nhập lý do (tối thiểu 5 ký tự).
/// 3. Ứng viên và toàn bộ người phỏng vấn nhận được thông báo thay đổi (In-App và Email).
/// 4. Không sửa hoặc hủy được buổi đã có phiếu đánh giá nộp (Core Guard Rule).
/// </summary>
public class InterviewRescheduleAndCancelServiceTests
{
    private static ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"AtsRescheduleCancelDb_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static async Task<(Application app, User iv1, User iv2, User iv3, User recruiter, Interview interview)> SeedTestDataAsync(
        ApplicationDbContext context,
        bool hasSubmittedEval = false)
    {
        var recruiter = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Nguyễn Chuyên Viên Tuyển Dụng",
            Email = $"recruiter_{Guid.NewGuid():N}@noveratech.vn",
            PasswordHash = "hash",
            Status = "ACTIVE"
        };

        var iv1 = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Trần Giám Khảo Một",
            Email = $"iv1_{Guid.NewGuid():N}@noveratech.vn",
            PasswordHash = "hash",
            Status = "ACTIVE"
        };

        var iv2 = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Lê Giám Khảo Hai",
            Email = $"iv2_{Guid.NewGuid():N}@noveratech.vn",
            PasswordHash = "hash",
            Status = "ACTIVE"
        };

        var iv3 = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Phạm Giám Khảo Ba",
            Email = $"iv3_{Guid.NewGuid():N}@noveratech.vn",
            PasswordHash = "hash",
            Status = "ACTIVE"
        };

        var candidate = new Candidate
        {
            Id = Guid.NewGuid(),
            FirstName = "Vũ",
            LastName = "Tuấn Anh",
            Email = $"candidate_{Guid.NewGuid():N}@gmail.com",
            Phone = "0912345678",
            Source = CandidateSource.LINKEDIN
        };

        var department = new Department
        {
            Id = Guid.NewGuid(),
            Name = "Phòng Kỹ Thuật Phần Mềm",
            Code = $"DEP_{Guid.NewGuid():N}"[..8]
        };

        var stage = new PipelineStage
        {
            Id = Guid.NewGuid(),
            Name = "Phỏng vấn kỹ thuật",
            StageOrder = 2
        };

        var requisition = new JobRequisition
        {
            Id = Guid.NewGuid(),
            Code = $"REQ_{Guid.NewGuid():N}"[..8],
            DepartmentId = department.Id,
            HiringManagerId = recruiter.Id,
            Status = RequisitionStatus.APPROVED
        };

        var jobPosting = new JobPosting
        {
            Id = Guid.NewGuid(),
            Title = "Senior .NET Core Backend Engineer",
            Slug = $"senior-net-core-{Guid.NewGuid():N}"[..20],
            RequisitionId = requisition.Id,
            Status = JobPostingStatus.PUBLISHED
        };

        var application = new Application
        {
            Id = Guid.NewGuid(),
            CandidateId = candidate.Id,
            JobPostingId = jobPosting.Id,
            CurrentStageId = stage.Id,
            Status = ApplicationStatus.IN_PROCESS,
            AppliedAt = DateTimeOffset.UtcNow.AddDays(-3)
        };

        var interview = new Interview
        {
            Id = Guid.NewGuid(),
            ApplicationId = application.Id,
            RoundNumber = 1,
            Title = "Phỏng vấn chuyên môn",
            InterviewType = InterviewType.ONLINE_MEET,
            LocationOrLink = "https://meet.google.com/old-room",
            StartTime = DateTimeOffset.UtcNow.AddDays(2),
            EndTime = DateTimeOffset.UtcNow.AddDays(2).AddMinutes(60),
            Status = InterviewStatus.SCHEDULED,
            CandidateConfirmed = CandidateConfirmStatus.ACCEPTED
        };

        interview.Panelists.Add(new InterviewPanelist
        {
            InterviewId = interview.Id,
            InterviewerId = iv1.Id,
            IsLead = true
        });

        if (hasSubmittedEval)
        {
            interview.Evaluations.Add(new InterviewEvaluation
            {
                Id = Guid.NewGuid(),
                InterviewId = interview.Id,
                InterviewerId = iv1.Id,
                IsSubmitted = true,
                SubmittedAt = DateTimeOffset.UtcNow.AddHours(-1),
                Recommendation = RecommendationType.STRONG_HIRE,
                OverallScore = 4.8m
            });
        }

        context.Users.AddRange(recruiter, iv1, iv2, iv3);
        context.Candidates.Add(candidate);
        context.Departments.Add(department);
        context.PipelineStages.Add(stage);
        context.JobRequisitions.Add(requisition);
        context.JobPostings.Add(jobPosting);
        context.Applications.Add(application);
        context.Interviews.Add(interview);

        await context.SaveChangesAsync();

        return (application, iv1, iv2, iv3, recruiter, interview);
    }

    [Fact]
    public async Task RescheduleInterview_WithValidNewData_ShouldSucceedAndSendEmailsAndNotifications()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (app, iv1, iv2, iv3, recruiter, interview) = await SeedTestDataAsync(context);

        var mockEmail = new Mock<IEmailService>();
        mockEmail.Setup(e => e.SendInterviewRescheduledAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = new InterviewService(context, mockEmail.Object, null, NullLogger<InterviewService>.Instance);

        var newTime = DateTime.UtcNow.AddDays(4);
        var input = new RescheduleInterviewInputModel
        {
            InterviewId = interview.Id,
            NewStartTime = newTime,
            DurationMinutes = 90,
            InterviewType = "OFFLINE_OFFICE",
            LocationOrLink = "Trụ sở NoveraTech, Tầng 12 Tòa nhà Innovation",
            InterviewerIds = [iv2.Id, iv3.Id], // Thay đổi hội đồng sang iv2 và iv3
            Reason = "Người phỏng vấn vòng 1 bận đi công tác đột xuất"
        };

        // Act
        var result = await service.RescheduleInterviewAsync(input, recruiter.Id, isRecruiterOnly: false);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.InterviewTypeDisplay.Should().Contain("Trực tiếp");

        // Verify CSDL
        var updatedInterview = await context.Interviews
            .Include(i => i.Panelists)
            .FirstOrDefaultAsync(i => i.Id == interview.Id);

        updatedInterview.Should().NotBeNull();
        updatedInterview!.Status.Should().Be(InterviewStatus.RESCHEDULED);
        updatedInterview.InterviewType.Should().Be(InterviewType.OFFLINE_OFFICE);
        updatedInterview.LocationOrLink.Should().Be("Trụ sở NoveraTech, Tầng 12 Tòa nhà Innovation");
        updatedInterview.CandidateConfirmed.Should().Be(CandidateConfirmStatus.PENDING); // Reset để ứng viên xác nhận lại
        (updatedInterview.EndTime!.Value - updatedInterview.StartTime!.Value).TotalMinutes.Should().Be(90);

        // Kiểm tra hội đồng được cập nhật sang iv2, iv3
        var activePanelistIds = updatedInterview.Panelists.Where(p => !p.IsDeleted).Select(p => p.InterviewerId).ToList();
        activePanelistIds.Should().BeEquivalentTo(new[] { iv2.Id, iv3.Id });

        // Kiểm tra ghi lịch sử ứng tuyển
        var history = await context.ApplicationStageHistories
            .FirstOrDefaultAsync(h => h.ApplicationId == app.Id && h.Comment != null && h.Comment.Contains("dời lịch"));
        history.Should().NotBeNull();
        history!.Comment.Should().Contain("Người phỏng vấn vòng 1 bận đi công tác đột xuất");

        // Kiểm tra gửi email cho ứng viên và toàn bộ người phỏng vấn mới (iv2, iv3)
        mockEmail.Verify(e => e.SendInterviewRescheduledAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.AtLeast(3)); // Ứng viên + 2 giám khảo
    }

    [Fact]
    public async Task RescheduleInterview_WithPastDateTime_ShouldFailAndNotModifyInterview()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (_, iv1, _, _, recruiter, interview) = await SeedTestDataAsync(context);

        var mockEmail = new Mock<IEmailService>();
        var service = new InterviewService(context, mockEmail.Object, null, NullLogger<InterviewService>.Instance);

        var pastTime = DateTime.UtcNow.AddHours(-3);
        var input = new RescheduleInterviewInputModel
        {
            InterviewId = interview.Id,
            NewStartTime = pastTime,
            DurationMinutes = 60,
            InterviewType = "ONLINE_MEET",
            LocationOrLink = "https://meet.google.com/new-room",
            InterviewerIds = [iv1.Id]
        };

        // Act
        var result = await service.RescheduleInterviewAsync(input, recruiter.Id, isRecruiterOnly: false);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("quá khứ");

        // Đảm bảo không thay đổi CSDL
        var unchanged = await context.Interviews.FindAsync(interview.Id);
        unchanged!.Status.Should().Be(InterviewStatus.SCHEDULED);
        unchanged.LocationOrLink.Should().Be("https://meet.google.com/old-room");
    }

    [Fact]
    public async Task RescheduleInterview_WhenEvaluationAlreadySubmitted_ShouldBeBlockedByCoreGuard()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (_, _, iv2, _, recruiter, interview) = await SeedTestDataAsync(context, hasSubmittedEval: true);

        var mockEmail = new Mock<IEmailService>();
        var service = new InterviewService(context, mockEmail.Object, null, NullLogger<InterviewService>.Instance);

        var input = new RescheduleInterviewInputModel
        {
            InterviewId = interview.Id,
            NewStartTime = DateTime.UtcNow.AddDays(3),
            DurationMinutes = 60,
            InterviewType = "ONLINE_MEET",
            LocationOrLink = "https://meet.google.com/room",
            InterviewerIds = [iv2.Id]
        };

        // Act
        var result = await service.RescheduleInterviewAsync(input, recruiter.Id, isRecruiterOnly: false);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("đã có phiếu đánh giá");

        // Đảm bảo trạng thái phỏng vấn không bị thay đổi
        var current = await context.Interviews.FindAsync(interview.Id);
        current!.Status.Should().Be(InterviewStatus.SCHEDULED);
    }

    [Fact]
    public async Task CancelInterview_WithValidReason_ShouldSetStatusCancelledAndSendNotificationsAndEmails()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (app, iv1, _, _, recruiter, interview) = await SeedTestDataAsync(context);

        var mockEmail = new Mock<IEmailService>();
        mockEmail.Setup(e => e.SendInterviewCancelledAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<DateTimeOffset>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = new InterviewService(context, mockEmail.Object, null, NullLogger<InterviewService>.Instance);

        var cancelReason = "Người phỏng vấn chính bận công tác đột xuất tại chi nhánh miền Nam";
        var input = new CancelInterviewInputModel
        {
            InterviewId = interview.Id,
            Reason = cancelReason
        };

        // Act
        var result = await service.CancelInterviewAsync(input, recruiter.Id, isRecruiterOnly: false);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Reason.Should().Be(cancelReason);

        // Verify CSDL
        var cancelledInterview = await context.Interviews.FindAsync(interview.Id);
        cancelledInterview.Should().NotBeNull();
        cancelledInterview!.Status.Should().Be(InterviewStatus.CANCELLED);
        cancelledInterview.CandidateNotes.Should().Contain(cancelReason);

        // Verify ApplicationStageHistory
        var history = await context.ApplicationStageHistories
            .FirstOrDefaultAsync(h => h.ApplicationId == app.Id && h.Comment != null && h.Comment.Contains("huỷ"));
        history.Should().NotBeNull();
        history!.Comment.Should().Contain(cancelReason);

        // Verify Emails sent (Ít nhất 2 email: ứng viên + giám khảo)
        mockEmail.Verify(e => e.SendInterviewCancelledAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<DateTimeOffset>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.AtLeast(2));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bận")]
    [InlineData("1234")]
    public async Task CancelInterview_WithEmptyOrTooShortReason_ShouldFailValidation(string invalidReason)
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (_, _, _, _, recruiter, interview) = await SeedTestDataAsync(context);

        var mockEmail = new Mock<IEmailService>();
        var service = new InterviewService(context, mockEmail.Object, null, NullLogger<InterviewService>.Instance);

        var input = new CancelInterviewInputModel
        {
            InterviewId = interview.Id,
            Reason = invalidReason
        };

        // Act
        var result = await service.CancelInterviewAsync(input, recruiter.Id, isRecruiterOnly: false);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.ToLower().Should().Contain("lý do");
        result.Message.Should().Contain("5 ký tự");

        // Đảm bảo không thay đổi trạng thái
        var current = await context.Interviews.FindAsync(interview.Id);
        current!.Status.Should().Be(InterviewStatus.SCHEDULED);
    }

    [Fact]
    public async Task CancelInterview_WhenEvaluationAlreadySubmitted_ShouldBeBlockedByCoreGuard()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (_, _, _, _, recruiter, interview) = await SeedTestDataAsync(context, hasSubmittedEval: true);

        var mockEmail = new Mock<IEmailService>();
        var service = new InterviewService(context, mockEmail.Object, null, NullLogger<InterviewService>.Instance);

        var input = new CancelInterviewInputModel
        {
            InterviewId = interview.Id,
            Reason = "Lý do hủy hợp lệ nhưng đã có phiếu nộp"
        };

        // Act
        var result = await service.CancelInterviewAsync(input, recruiter.Id, isRecruiterOnly: false);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("đã có phiếu đánh giá");

        // Đảm bảo trạng thái phỏng vấn không bị đổi thành CANCELLED
        var current = await context.Interviews.FindAsync(interview.Id);
        current!.Status.Should().Be(InterviewStatus.SCHEDULED);
    }

    [Fact]
    public async Task GetInterviewDetailForEdit_ShouldReturnAccurateDetail()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (_, iv1, _, _, _, interview) = await SeedTestDataAsync(context);

        var mockEmail = new Mock<IEmailService>();
        var service = new InterviewService(context, mockEmail.Object, null, NullLogger<InterviewService>.Instance);

        // Act
        var detail = await service.GetInterviewDetailForEditAsync(interview.Id);

        // Assert
        detail.Should().NotBeNull();
        detail!.CandidateName.Should().Be("Vũ Tuấn Anh");
        detail.JobTitle.Should().Be("Senior .NET Core Backend Engineer");
        detail.InterviewType.Should().Be("ONLINE_MEET");
        detail.LocationOrLink.Should().Be("https://meet.google.com/old-room");
        detail.HasSubmittedEvaluation.Should().BeFalse();
        detail.InterviewerIds.Should().Contain(iv1.Id);
    }
}
