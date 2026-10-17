using Ats.Web.Data;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Models.ViewModels.CandidateInterviews;
using Ats.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ats.Web.Tests.Services;

/// <summary>
/// Unit Tests kiểm thử nghiệp vụ Thư mời phỏng vấn và Xác nhận/Đổi lịch dành cho Ứng viên (Scrum - Số 54).
/// Các tiêu chuẩn cốt lõi:
/// 1. Email/Liên kết chứa đầy đủ: thời gian, địa điểm hoặc link họp, tên vị trí, hướng dẫn chuẩn bị.
/// 2. Ứng viên bấm xác nhận hoặc đề nghị đổi lịch kèm lý do qua liên kết.
/// 3. Trạng thái xác nhận hiển thị rõ ràng cho recruiter.
/// 4. Liên kết chỉ dùng được cho đúng buổi phỏng vấn đó (bảo mật HMAC-SHA256, không thể can thiệp chéo).
/// </summary>
public class CandidateInterviewServiceTests
{
    private static ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"AtsCandidateInterviewDb_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static async Task<(Interview interview, Candidate candidate, Application app, User interviewer)> SeedInterviewDataAsync(
        ApplicationDbContext context,
        InterviewStatus status = InterviewStatus.SCHEDULED,
        CandidateConfirmStatus confirmStatus = CandidateConfirmStatus.PENDING,
        string? notes = "Vui lòng chuẩn bị slide thuyết trình kiến trúc hệ thống và kiểm tra micro trước 10 phút.")
    {
        var interviewer = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Ngô Quang Huy",
            Email = $"techlead_{Guid.NewGuid():N}@noveratech.vn",
            PasswordHash = "hashedPassword"
        };
        await context.Users.AddAsync(interviewer);

        var candidate = new Candidate
        {
            Id = Guid.NewGuid(),
            UserId = interviewer.Id,
            FirstName = "Lê",
            LastName = "Tuấn Anh",
            Email = $"ungvien_{Guid.NewGuid():N}@example.com",
            Phone = "0901234567"
        };
        await context.Candidates.AddAsync(candidate);

        var dept = new Department
        {
            Id = Guid.NewGuid(),
            Code = $"DEP_{Guid.NewGuid():N}"[..8],
            Name = "Khối Công nghệ Thông tin"
        };
        await context.Departments.AddAsync(dept);

        var req = new JobRequisition
        {
            Id = Guid.NewGuid(),
            Code = $"REQ_{Guid.NewGuid():N}"[..8],
            DepartmentId = dept.Id,
            Department = dept,
            HiringManagerId = interviewer.Id,
            Status = RequisitionStatus.APPROVED
        };
        await context.JobRequisitions.AddAsync(req);

        var jobPosting = new JobPosting
        {
            Id = Guid.NewGuid(),
            RequisitionId = req.Id,
            Requisition = req,
            Title = "Senior .NET Core Backend Engineer",
            WorkLocation = "Tòa nhà NoveraTech, Hà Nội",
            Status = JobPostingStatus.PUBLISHED,
            PublishedAt = DateTimeOffset.UtcNow.AddDays(-10)
        };
        await context.JobPostings.AddAsync(jobPosting);

        var stage = new PipelineStage
        {
            Id = Guid.NewGuid(),
            Name = "Phỏng vấn Chuyên môn",
            StageOrder = 3
        };
        await context.PipelineStages.AddAsync(stage);

        var app = new Application
        {
            Id = Guid.NewGuid(),
            CandidateId = candidate.Id,
            Candidate = candidate,
            JobPostingId = jobPosting.Id,
            JobPosting = jobPosting,
            CurrentStageId = stage.Id,
            Status = ApplicationStatus.IN_PROCESS,
            AppliedAt = DateTimeOffset.UtcNow.AddDays(-5)
        };
        await context.Applications.AddAsync(app);

        var interview = new Interview
        {
            Id = Guid.NewGuid(),
            ApplicationId = app.Id,
            Application = app,
            Title = "Phỏng vấn Chuyên môn Vòng 2",
            InterviewType = InterviewType.ONLINE_MEET,
            LocationOrLink = "https://meet.google.com/nov-tech-2026",
            StartTime = DateTimeOffset.UtcNow.AddDays(2),
            EndTime = DateTimeOffset.UtcNow.AddDays(2).AddHours(1),
            Status = status,
            CandidateConfirmed = confirmStatus,
            CandidateNotes = notes
        };
        await context.Interviews.AddAsync(interview);

        var panelist = new InterviewPanelist
        {
            Id = Guid.NewGuid(),
            InterviewId = interview.Id,
            Interview = interview,
            InterviewerId = interviewer.Id,
            Interviewer = interviewer,
            IsLead = true
        };
        await context.InterviewPanelists.AddAsync(panelist);

        await context.SaveChangesAsync();

        return (interview, candidate, app, interviewer);
    }

    #region 1. Token Generation & Validation Tests (Tiêu chí 4: Liên kết chỉ dùng cho đúng buổi phỏng vấn đó)

    [Fact]
    public void GenerateInvitationToken_ReturnsConsistentAndWellFormattedToken()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var service = new CandidateInterviewService(context, NullLogger<CandidateInterviewService>.Instance);
        var interviewId = Guid.NewGuid();
        var candidateEmail = "Candidate@Example.Com ";

        // Act
        var token1 = service.GenerateInvitationToken(interviewId, candidateEmail);
        var token2 = service.GenerateInvitationToken(interviewId, "candidate@example.com");

        // Assert
        Assert.NotNull(token1);
        Assert.NotEmpty(token1);
        Assert.Contains(interviewId.ToString("D"), token1);
        Assert.Equal(token1, token2); // Case and whitespace insensitive for email
    }

    [Fact]
    public async Task ValidateInvitationToken_ValidToken_ReturnsTrueAndExtractsInterviewId()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (interview, candidate, _, _) = await SeedInterviewDataAsync(context);
        var service = new CandidateInterviewService(context, NullLogger<CandidateInterviewService>.Instance);

        var token = service.GenerateInvitationToken(interview.Id, candidate.Email);

        // Act
        var isValid = service.ValidateInvitationToken(token, out var extractedId);

        // Assert
        Assert.True(isValid);
        Assert.Equal(interview.Id, extractedId);
    }

    [Fact]
    public async Task ValidateInvitationToken_MismatchedEmail_ReturnsFalse()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (interview, _, _, _) = await SeedInterviewDataAsync(context);
        var service = new CandidateInterviewService(context, NullLogger<CandidateInterviewService>.Instance);

        // Sinh token với email khác hoàn toàn
        var forgedToken = service.GenerateInvitationToken(interview.Id, "attacker@example.com");

        // Act
        var isValid = service.ValidateInvitationToken(forgedToken, out var extractedId);

        // Assert: Bắt buộc từ chối vì email không khớp với ứng viên trong DB
        Assert.False(isValid);
        Assert.Equal(Guid.Empty, extractedId);
    }

    [Fact]
    public async Task ValidateInvitationToken_TamperedToken_ReturnsFalse()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (interview, candidate, _, _) = await SeedInterviewDataAsync(context);
        var service = new CandidateInterviewService(context, NullLogger<CandidateInterviewService>.Instance);

        var token = service.GenerateInvitationToken(interview.Id, candidate.Email);

        // Làm giả signature bằng cách thay đổi ký tự cuối
        var tamperedToken = token[..^1] + (token[^1] == 'a' ? 'b' : 'a');

        // Act
        var isValid = service.ValidateInvitationToken(tamperedToken, out var extractedId);

        // Assert
        Assert.False(isValid);
        Assert.Equal(Guid.Empty, extractedId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid_format")]
    [InlineData("not-a-guid_hash123")]
    public void ValidateInvitationToken_InvalidFormat_ReturnsFalse(string invalidToken)
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var service = new CandidateInterviewService(context, NullLogger<CandidateInterviewService>.Instance);

        // Act
        var isValid = service.ValidateInvitationToken(invalidToken, out var extractedId);

        // Assert
        Assert.False(isValid);
        Assert.Equal(Guid.Empty, extractedId);
    }

    #endregion

    #region 2. Get Interview Details Tests (Tiêu chí 1: Email/Link chứa thời gian, địa điểm, vị trí, hướng dẫn chuẩn bị)

    [Fact]
    public async Task GetInterviewConfirmationAsync_ValidToken_ReturnsCompleteInterviewDetails()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (interview, candidate, app, interviewer) = await SeedInterviewDataAsync(
            context,
            notes: "Vui lòng chuẩn bị laptop có cài đặt .NET 9 SDK.");

        var service = new CandidateInterviewService(context, NullLogger<CandidateInterviewService>.Instance);
        var token = service.GenerateInvitationToken(interview.Id, candidate.Email);

        // Act
        var result = await service.GetInterviewConfirmationAsync(token);

        // Assert: Tiêu chí 1 - Đầy đủ thời gian, địa điểm, vị trí, hướng dẫn chuẩn bị
        Assert.NotNull(result);
        Assert.Equal(interview.Id, result.InterviewId);
        Assert.Equal($"{candidate.FirstName} {candidate.LastName}".Trim(), result.CandidateName);
        Assert.Equal(candidate.Email, result.CandidateEmail);
        Assert.Equal("Senior .NET Core Backend Engineer", result.JobTitle);
        Assert.Equal("Phỏng vấn Chuyên môn Vòng 2", result.RoundTitle);
        Assert.Equal("https://meet.google.com/nov-tech-2026", result.LocationOrLink);
        Assert.Contains(".NET 9 SDK", result.PreparationGuide);
        Assert.Contains("Ngô Quang Huy", result.PanelistNames);
        Assert.NotEmpty(result.TimeDisplay);
        Assert.NotEmpty(result.DateDisplay);
        Assert.True(result.CanRespond);
    }

    [Fact]
    public async Task GetInterviewConfirmationAsync_InvalidOrForgedToken_ReturnsNull()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (interview, _, _, _) = await SeedInterviewDataAsync(context);
        var service = new CandidateInterviewService(context, NullLogger<CandidateInterviewService>.Instance);

        // Token giả mạo
        var forgedToken = $"{interview.Id:D}_0000000000000000";

        // Act
        var result = await service.GetInterviewConfirmationAsync(forgedToken);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetInterviewConfirmationAsync_CancelledInterview_MarksCannotRespond()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (interview, candidate, _, _) = await SeedInterviewDataAsync(context, status: InterviewStatus.CANCELLED);
        var service = new CandidateInterviewService(context, NullLogger<CandidateInterviewService>.Instance);
        var token = service.GenerateInvitationToken(interview.Id, candidate.Email);

        // Act
        var result = await service.GetInterviewConfirmationAsync(token);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.CanRespond);
        Assert.Equal(InterviewStatus.CANCELLED, result.InterviewStatus);
    }

    #endregion

    #region 3. Attendance Confirmation Tests (Tiêu chí 2 & 3: Ứng viên bấm xác nhận, hiển thị cho Recruiter)

    [Fact]
    public async Task ConfirmAttendanceAsync_ValidToken_UpdatesStatusToAcceptedAndLogsHistory()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (interview, candidate, app, _) = await SeedInterviewDataAsync(context);
        var service = new CandidateInterviewService(context, NullLogger<CandidateInterviewService>.Instance);
        var token = service.GenerateInvitationToken(interview.Id, candidate.Email);

        var input = new CandidateInterviewConfirmInputModel
        {
            Token = token,
            Notes = "Tôi xin cam kết tham dự đúng giờ và chuẩn bị đầy đủ thiết bị."
        };

        // Act
        var result = await service.ConfirmAttendanceAsync(input);

        // Assert: Tiêu chí 2 & 3
        Assert.True(result.Success);
        Assert.Contains("xác nhận tham dự", result.Message);

        // Kiểm tra database: Interview đã chuyển sang ACCEPTED
        var updatedInterview = await context.Interviews.FindAsync(interview.Id);
        Assert.NotNull(updatedInterview);
        Assert.Equal(CandidateConfirmStatus.ACCEPTED, updatedInterview.CandidateConfirmed);
        Assert.Contains("Tôi xin cam kết tham dự đúng giờ", updatedInterview.CandidateNotes);

        // Kiểm tra lịch sử giai đoạn và tương tác được ghi nhận tự động
        var histories = await context.ApplicationStageHistories
            .Where(h => h.ApplicationId == app.Id)
            .ToListAsync();
        Assert.Contains(histories, h => h.Comment != null && h.Comment.Contains("xác nhận tham dự"));

        var interactions = await context.CandidateInteractions
            .Where(i => i.CandidateId == candidate.Id)
            .ToListAsync();
        Assert.Contains(interactions, i => i.InteractionType == CandidateInteractionType.MEETING && i.Title.Contains("xác nhận"));
    }

    [Fact]
    public async Task ConfirmAttendanceAsync_CancelledInterview_ReturnsFailure()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (interview, candidate, _, _) = await SeedInterviewDataAsync(context, status: InterviewStatus.CANCELLED);
        var service = new CandidateInterviewService(context, NullLogger<CandidateInterviewService>.Instance);
        var token = service.GenerateInvitationToken(interview.Id, candidate.Email);

        var input = new CandidateInterviewConfirmInputModel
        {
            Token = token
        };

        // Act
        var result = await service.ConfirmAttendanceAsync(input);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("không ở trạng thái", result.Message);
    }

    [Fact]
    public async Task ConfirmAttendanceAsync_InvalidToken_ReturnsFailure()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var service = new CandidateInterviewService(context, NullLogger<CandidateInterviewService>.Instance);

        var input = new CandidateInterviewConfirmInputModel
        {
            Token = "invalid_token_123"
        };

        // Act
        var result = await service.ConfirmAttendanceAsync(input);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("không hợp lệ", result.Message);
    }

    #endregion

    #region 4. Reschedule Request Tests (Tiêu chí 2 & 3: Ứng viên đề nghị đổi lịch, hiển thị lý do cho Recruiter)

    [Fact]
    public async Task RequestRescheduleAsync_ValidReasonAndTimes_UpdatesStatusToDeclinedAndStoresReason()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (interview, candidate, app, _) = await SeedInterviewDataAsync(context);
        var service = new CandidateInterviewService(context, NullLogger<CandidateInterviewService>.Instance);
        var token = service.GenerateInvitationToken(interview.Id, candidate.Email);

        var input = new CandidateInterviewRescheduleInputModel
        {
            Token = token,
            Reason = "Trùng lịch bảo vệ đồ án tốt nghiệp vào chiều thứ Sáu.",
            ProposedTime = "Có thể tham gia vào sáng thứ Hai tuần sau (09:00 - 11:30) hoặc chiều thứ Ba."
        };

        // Act
        var result = await service.RequestRescheduleAsync(input);

        // Assert: Tiêu chí 2 & 3
        Assert.True(result.Success);
        Assert.Contains("đổi lịch", result.Message);

        // Kiểm tra database: Interview đã chuyển sang DECLINED
        var updatedInterview = await context.Interviews.FindAsync(interview.Id);
        Assert.NotNull(updatedInterview);
        Assert.Equal(CandidateConfirmStatus.DECLINED, updatedInterview.CandidateConfirmed);
        Assert.Contains("Trùng lịch bảo vệ đồ án", updatedInterview.CandidateNotes);
        Assert.Contains("Có thể tham gia vào sáng thứ Hai", updatedInterview.CandidateNotes);

        // Kiểm tra interaction log ghi nhận lý do
        var interactions = await context.CandidateInteractions
            .Where(i => i.CandidateId == candidate.Id)
            .ToListAsync();
        Assert.Contains(interactions, i => i.InteractionType == CandidateInteractionType.MEETING && i.Title.Contains("yêu cầu đổi lịch"));
    }

    [Fact]
    public async Task RequestRescheduleAsync_EmptyReason_ReturnsFailure()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (interview, candidate, _, _) = await SeedInterviewDataAsync(context);
        var service = new CandidateInterviewService(context, NullLogger<CandidateInterviewService>.Instance);
        var token = service.GenerateInvitationToken(interview.Id, candidate.Email);

        var input = new CandidateInterviewRescheduleInputModel
        {
            Token = token,
            Reason = "   " // Rỗng lý do
        };

        // Act
        var result = await service.RequestRescheduleAsync(input);

        // Assert: Bắt buộc phải có lý do để recruiter nắm thông tin
        Assert.False(result.Success);
        Assert.Contains("Vui lòng nhập lý do", result.Message);
    }

    [Fact]
    public async Task RequestRescheduleAsync_InvalidToken_ReturnsFailure()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var service = new CandidateInterviewService(context, NullLogger<CandidateInterviewService>.Instance);

        var input = new CandidateInterviewRescheduleInputModel
        {
            Token = "bogus_token",
            Reason = "Bận việc gia đình"
        };

        // Act
        var result = await service.RequestRescheduleAsync(input);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("không hợp lệ", result.Message);
    }

    #endregion

    #region 5. URL Generation Tests (Tiêu chí 4: Custom kebab-case route)

    [Fact]
    public void BuildConfirmationUrl_CreatesCorrectKebabCaseUrl()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var service = new CandidateInterviewService(context, NullLogger<CandidateInterviewService>.Instance);
        var interviewId = Guid.NewGuid();
        var email = "candidate@example.com";
        var baseUrl = "https://ats.noveratech.vn";

        // Act
        var url = service.BuildConfirmationUrl(interviewId, email, baseUrl);

        // Assert
        Assert.StartsWith("https://ats.noveratech.vn/xac-nhan-phong-van/", url);
        Assert.Contains(interviewId.ToString("D"), url);
    }

    #endregion
}
