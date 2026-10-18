using Ats.Web.Constants;
using Ats.Web.Data;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Models.ViewModels.Interviews;
using Ats.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ats.Web.Tests.Services;

/// <summary>
/// Unit Tests kiểm thử nghiệp vụ nộp phiếu đánh giá sau phỏng vấn (Scrum - Số 57).
/// 4 Tiêu chí cốt lõi:
/// 1. Chỉ người phỏng vấn được mời mới mở được phiếu của buổi đó.
/// 2. Bắt buộc chọn kết luận: Nên tuyển, Cân nhắc, Không nên tuyển.
/// 3. Đã nộp thì không sửa được, trừ khi recruiter mở lại trong 24 giờ.
/// 4. Không nhìn thấy đánh giá của người khác trước khi nộp phiếu của mình (Blind Evaluation).
/// </summary>
public class InterviewEvaluationServiceTests
{
    private static ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"AtsInterviewEvalDb_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static async Task<(Interview interview, User panelist1, User panelist2, User uninvitedUser, User recruiter)> SeedInterviewTestDataAsync(
        ApplicationDbContext context,
        bool panelist1Submitted = false,
        DateTimeOffset? panelist1SubmittedAt = null)
    {
        // 1. Tạo Users
        var panelist1 = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Nguyễn Văn Giám Khảo 1",
            Email = $"panelist1_{Guid.NewGuid():N}@noveratech.vn",
            PasswordHash = "hash",
            Status = "ACTIVE",
            Role = UserRoles.Interviewer,
            UserType = UserType.INTERNAL
        };

        var panelist2 = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Trần Thị Giám Khảo 2",
            Email = $"panelist2_{Guid.NewGuid():N}@noveratech.vn",
            PasswordHash = "hash",
            Status = "ACTIVE",
            Role = UserRoles.Interviewer,
            UserType = UserType.INTERNAL
        };

        var uninvitedUser = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Lê Người Ngoài Cuộc",
            Email = $"outsider_{Guid.NewGuid():N}@noveratech.vn",
            PasswordHash = "hash",
            Status = "ACTIVE",
            Role = UserRoles.Interviewer,
            UserType = UserType.INTERNAL
        };

        var recruiter = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Phạm Chuyên Viên Tuyển Dụng",
            Email = $"recruiter_{Guid.NewGuid():N}@noveratech.vn",
            PasswordHash = "hash",
            Status = "ACTIVE",
            Role = UserRoles.Recruiter,
            UserType = UserType.INTERNAL
        };

        await context.Users.AddRangeAsync(panelist1, panelist2, uninvitedUser, recruiter);

        // 2. Tạo Dept, Job & Application
        var dept = new Department
        {
            Id = Guid.NewGuid(),
            Code = $"DEP-{Guid.NewGuid():N}"[..8],
            Name = "Khối Công Nghệ Thông Tin"
        };
        await context.Departments.AddAsync(dept);

        var req = new JobRequisition
        {
            Id = Guid.NewGuid(),
            Code = $"REQ-{Guid.NewGuid():N}"[..8],
            DepartmentId = dept.Id,
            HiringManagerId = recruiter.Id
        };
        await context.JobRequisitions.AddAsync(req);

        var job = new JobPosting
        {
            Id = Guid.NewGuid(),
            RequisitionId = req.Id,
            Title = "Senior Backend .NET Engineer",
            Slug = "senior-backend-net-engineer",
            EmploymentType = EmploymentType.FULL_TIME,
            Status = JobPostingStatus.PUBLISHED
        };
        await context.JobPostings.AddAsync(job);

        var candidate = new Candidate
        {
            Id = Guid.NewGuid(),
            FirstName = "Hoàng",
            LastName = "Văn Ứng Viên",
            Email = $"candidate_{Guid.NewGuid():N}@gmail.com"
        };
        await context.Candidates.AddAsync(candidate);

        var app = new Application
        {
            Id = Guid.NewGuid(),
            JobPostingId = job.Id,
            CandidateId = candidate.Id,
            Status = ApplicationStatus.IN_PROCESS
        };
        await context.Applications.AddAsync(app);

        // 3. Tạo Interview
        var interview = new Interview
        {
            Id = Guid.NewGuid(),
            ApplicationId = app.Id,
            RoundNumber = 1,
            Title = "Phỏng vấn Chuyên môn Kỹ thuật",
            StartTime = DateTimeOffset.UtcNow.AddHours(2),
            EndTime = DateTimeOffset.UtcNow.AddHours(3),
            LocationOrLink = "https://meet.google.com/test-ats-scrum57",
            Status = InterviewStatus.SCHEDULED
        };
        await context.Interviews.AddAsync(interview);

        // 4. Mời panelist1 và panelist2 vào hội đồng
        var p1 = new InterviewPanelist
        {
            Id = Guid.NewGuid(),
            InterviewId = interview.Id,
            InterviewerId = panelist1.Id,
            IsLead = true,
            Attended = true
        };
        var p2 = new InterviewPanelist
        {
            Id = Guid.NewGuid(),
            InterviewId = interview.Id,
            InterviewerId = panelist2.Id,
            IsLead = false,
            Attended = true
        };
        await context.InterviewPanelists.AddRangeAsync(p1, p2);

        // 5. Nếu panelist 1 đã nộp phiếu
        if (panelist1Submitted)
        {
            var eval1 = new InterviewEvaluation
            {
                Id = Guid.NewGuid(),
                InterviewId = interview.Id,
                InterviewerId = panelist1.Id,
                OverallScore = 4.5m,
                Recommendation = RecommendationType.HIRE,
                Strengths = "Kiến thức microservices tốt, xử lý tốt bài toán phân tán.",
                Weaknesses = "Cần thêm kinh nghiệm CI/CD GitOps.",
                DetailedFeedback = "Đánh giá ứng viên rất tiềm năng, đồng ý tuyển dụng.",
                IsSubmitted = true,
                SubmittedAt = panelist1SubmittedAt ?? DateTimeOffset.UtcNow.AddHours(-1),
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-2)
            };
            await context.InterviewEvaluations.AddAsync(eval1);
        }

        await context.SaveChangesAsync();

        return (interview, panelist1, panelist2, uninvitedUser, recruiter);
    }

    #region Tiêu chí 1: Chỉ người phỏng vấn được mời mới mở được phiếu

    [Fact]
    public async Task GetEvaluationSheet_WhenUserIsNotInvited_ShouldReturnFalseWithForbiddenMessage()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (interview, _, _, uninvitedUser, _) = await SeedInterviewTestDataAsync(context);
        var service = new InterviewEvaluationService(context, NullLogger<InterviewEvaluationService>.Instance);

        // Act
        var (canOpen, errorMessage, model) = await service.GetEvaluationSheetAsync(
            interview.Id, uninvitedUser.Id, isStaffManager: false);

        // Assert
        Assert.False(canOpen);
        Assert.Contains("Chỉ người phỏng vấn được mời", errorMessage);
        Assert.Null(model);
    }

    [Fact]
    public async Task GetEvaluationSheet_WhenUserIsInvited_ShouldReturnTrueWithSheetModel()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (interview, panelist1, _, _, _) = await SeedInterviewTestDataAsync(context);
        var service = new InterviewEvaluationService(context, NullLogger<InterviewEvaluationService>.Instance);

        // Act
        var (canOpen, errorMessage, model) = await service.GetEvaluationSheetAsync(
            interview.Id, panelist1.Id, isStaffManager: false);

        // Assert
        Assert.True(canOpen);
        Assert.Null(errorMessage);
        Assert.NotNull(model);
        Assert.True(model.IsInvitedPanelist);
        Assert.Equal("Senior Backend .NET Engineer", model.JobTitle);
        Assert.Contains("Hoàng", model.CandidateName);
    }

    [Fact]
    public async Task SubmitEvaluation_WhenUserIsNotInvited_ShouldFail()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (interview, _, _, uninvitedUser, _) = await SeedInterviewTestDataAsync(context);
        var service = new InterviewEvaluationService(context, NullLogger<InterviewEvaluationService>.Instance);

        var input = new InterviewEvaluationSubmitInputModel
        {
            InterviewId = interview.Id,
            Recommendation = "Nên tuyển",
            OverallScore = 4.0m,
            DetailedFeedback = "Đánh giá trái phép từ người không được mời."
        };

        // Act
        var (success, message, evalId) = await service.SubmitEvaluationAsync(input, uninvitedUser.Id);

        // Assert
        Assert.False(success);
        Assert.Contains("Chỉ người phỏng vấn được mời", message);
        Assert.Null(evalId);
    }

    #endregion

    #region Tiêu chí 2: Bắt buộc chọn kết luận (Nên tuyển, Cân nhắc, Không nên tuyển)

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("KHONG_XAC_DINH")]
    [InlineData("MAYBE")]
    public async Task SubmitEvaluation_WithMissingOrInvalidRecommendation_ShouldFail(string invalidRec)
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (interview, panelist1, _, _, _) = await SeedInterviewTestDataAsync(context);
        var service = new InterviewEvaluationService(context, NullLogger<InterviewEvaluationService>.Instance);

        var input = new InterviewEvaluationSubmitInputModel
        {
            InterviewId = interview.Id,
            Recommendation = invalidRec,
            OverallScore = 4.0m,
            DetailedFeedback = "Nhận xét phỏng vấn."
        };

        // Act
        var (success, message, evalId) = await service.SubmitEvaluationAsync(input, panelist1.Id);

        // Assert
        Assert.False(success);
        Assert.Contains("Bắt buộc chọn kết luận", message);
        Assert.Null(evalId);
    }

    [Theory]
    [InlineData("Nên tuyển", RecommendationType.HIRE)]
    [InlineData("HIRE", RecommendationType.HIRE)]
    [InlineData("Cân nhắc", RecommendationType.CONSIDER)]
    [InlineData("CONSIDER", RecommendationType.CONSIDER)]
    [InlineData("Không nên tuyển", RecommendationType.STRONG_NO_HIRE)]
    [InlineData("STRONG_NO_HIRE", RecommendationType.STRONG_NO_HIRE)]
    public async Task SubmitEvaluation_WithValidConclusions_ShouldSucceedAndNormalize(
        string rawInput, RecommendationType expectedType)
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (interview, panelist1, _, _, _) = await SeedInterviewTestDataAsync(context);
        var service = new InterviewEvaluationService(context, NullLogger<InterviewEvaluationService>.Instance);

        var input = new InterviewEvaluationSubmitInputModel
        {
            InterviewId = interview.Id,
            Recommendation = rawInput,
            OverallScore = 4.2m,
            Strengths = "Tư duy hệ thống sắc bén",
            Weaknesses = "Cần trau dồi tiếng Anh",
            DetailedFeedback = "Ghi nhận đánh giá đầy đủ sau buổi phỏng vấn."
        };

        // Act
        var (success, message, evalId) = await service.SubmitEvaluationAsync(input, panelist1.Id);

        // Assert
        Assert.True(success);
        Assert.NotNull(evalId);

        var savedEval = await context.InterviewEvaluations.FindAsync(evalId.Value);
        Assert.NotNull(savedEval);
        Assert.Equal(expectedType, savedEval.Recommendation);
        Assert.True(savedEval.IsSubmitted);
        Assert.NotNull(savedEval.SubmittedAt);
    }

    #endregion

    #region Tiêu chí 3: Đã nộp thì không sửa được, trừ khi recruiter mở lại trong 24 giờ

    [Fact]
    public async Task SubmitEvaluation_WhenAlreadySubmittedAndNotReopened_ShouldBeLocked()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (interview, panelist1, _, _, _) = await SeedInterviewTestDataAsync(
            context, panelist1Submitted: true, panelist1SubmittedAt: DateTimeOffset.UtcNow.AddHours(-2));
        var service = new InterviewEvaluationService(context, NullLogger<InterviewEvaluationService>.Instance);

        var input = new InterviewEvaluationSubmitInputModel
        {
            InterviewId = interview.Id,
            Recommendation = "Cân nhắc",
            OverallScore = 3.5m,
            DetailedFeedback = "Cố gắng sửa đổi sau khi đã nộp."
        };

        // Act - Giám khảo cố gắng nộp đè lên phiếu đã khóa
        var (success, message, _) = await service.SubmitEvaluationAsync(input, panelist1.Id);

        // Assert
        Assert.False(success);
        Assert.Contains("Phiếu đánh giá đã được nộp và không thể chỉnh sửa", message);
    }

    [Fact]
    public async Task ReopenEvaluation_ByRecruiterWithin24Hours_ShouldUnlockEvaluationForEditing()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (interview, panelist1, _, _, recruiter) = await SeedInterviewTestDataAsync(
            context, panelist1Submitted: true, panelist1SubmittedAt: DateTimeOffset.UtcNow.AddHours(-3));
        var service = new InterviewEvaluationService(context, NullLogger<InterviewEvaluationService>.Instance);

        var eval = await context.InterviewEvaluations.FirstAsync(e => e.InterviewerId == panelist1.Id);

        // Act 1: Recruiter mở lại phiếu trong vòng 24 giờ
        var (reopenSuccess, reopenMessage) = await service.ReopenEvaluationAsync(
            eval.Id, recruiter.Id, "Giám khảo cần bổ sung thêm nhận định về phần giải thuật");

        // Assert 1
        Assert.True(reopenSuccess);
        Assert.Contains("mở lại phiếu đánh giá thành công", reopenMessage, StringComparison.OrdinalIgnoreCase);

        var reloadedEval = await context.InterviewEvaluations.FindAsync(eval.Id);
        Assert.True(reloadedEval!.IsReopened);
        Assert.NotNull(reloadedEval.ReopenedAt);
        Assert.Equal(recruiter.Id, reloadedEval.ReopenedByUserId);

        // Act 2: Giám khảo cập nhật lại và nộp lại phiếu
        var updateInput = new InterviewEvaluationSubmitInputModel
        {
            InterviewId = interview.Id,
            Recommendation = "Nên tuyển",
            OverallScore = 4.8m,
            Strengths = "Đã bổ sung: Giải thuật tối ưu O(n log n).",
            DetailedFeedback = "Đã hoàn thành giải trình chi tiết theo yêu cầu của Recruiter."
        };

        var (submitSuccess, submitMessage, _) = await service.SubmitEvaluationAsync(updateInput, panelist1.Id);

        // Assert 2
        Assert.True(submitSuccess);
        Assert.Contains("thành công", submitMessage);

        var finalEval = await context.InterviewEvaluations.FindAsync(eval.Id);
        Assert.False(finalEval!.IsReopened); // Sau khi nộp lại, phiếu khóa lại
        Assert.Equal(4.8m, finalEval.OverallScore);
        Assert.Contains("Giải thuật tối ưu", finalEval.Strengths);
    }

    [Fact]
    public async Task ReopenEvaluation_WhenSubmittedOver24HoursAgo_ShouldReject()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        // Nộp cách đây 25 giờ (vượt quá hạn định mức 24h)
        var (interview, panelist1, _, _, recruiter) = await SeedInterviewTestDataAsync(
            context, panelist1Submitted: true, panelist1SubmittedAt: DateTimeOffset.UtcNow.AddHours(-25));
        var service = new InterviewEvaluationService(context, NullLogger<InterviewEvaluationService>.Instance);

        var eval = await context.InterviewEvaluations.FirstAsync(e => e.InterviewerId == panelist1.Id);

        // Act
        var (reopenSuccess, reopenMessage) = await service.ReopenEvaluationAsync(
            eval.Id, recruiter.Id, "Yêu cầu mở lại sau 25 giờ");

        // Assert
        Assert.False(reopenSuccess);
        Assert.Contains("quá thời hạn 24 giờ", reopenMessage, StringComparison.OrdinalIgnoreCase);

        var reloadedEval = await context.InterviewEvaluations.FindAsync(eval.Id);
        Assert.False(reloadedEval!.IsReopened);
    }

    #endregion

    #region Tiêu chí 4: Không nhìn thấy đánh giá của người khác trước khi nộp phiếu của mình (Blind Evaluation)

    [Fact]
    public async Task GetEvaluationSheet_WhenCurrentUserHasNotSubmitted_ShouldHideOtherPanelistsEvaluations()
    {
        // Arrange
        // Panelist 1 đã nộp phiếu. Panelist 2 chưa nộp.
        using var context = CreateInMemoryDbContext();
        var (interview, _, panelist2, _, _) = await SeedInterviewTestDataAsync(
            context, panelist1Submitted: true);
        var service = new InterviewEvaluationService(context, NullLogger<InterviewEvaluationService>.Instance);

        // Act - Panelist 2 mở phiếu của mình trước khi nộp
        var (canOpen, _, model) = await service.GetEvaluationSheetAsync(
            interview.Id, panelist2.Id, isStaffManager: false);

        // Assert
        Assert.True(canOpen);
        Assert.NotNull(model);
        Assert.False(model.IsSubmitted); // Chưa nộp
        Assert.False(model.CanViewOtherEvaluations); // Bị ẩn theo quy tắc Blind Review
        Assert.NotNull(model.BlindReviewNotice);
        Assert.Contains("Đánh giá độc lập", model.BlindReviewNotice);
        Assert.Empty(model.PanelEvaluations); // Danh sách đánh giá của giám khảo khác phải rỗng!
    }

    [Fact]
    public async Task GetEvaluationSheet_WhenCurrentUserHasSubmitted_ShouldRevealOtherPanelistsEvaluations()
    {
        // Arrange
        // Panelist 1 đã nộp phiếu. Bây giờ Panelist 2 cũng nộp phiếu của mình.
        using var context = CreateInMemoryDbContext();
        var (interview, panelist1, panelist2, _, _) = await SeedInterviewTestDataAsync(
            context, panelist1Submitted: true);
        var service = new InterviewEvaluationService(context, NullLogger<InterviewEvaluationService>.Instance);

        // Panelist 2 nộp phiếu
        var p2Input = new InterviewEvaluationSubmitInputModel
        {
            InterviewId = interview.Id,
            Recommendation = "Cân nhắc",
            OverallScore = 3.8m,
            DetailedFeedback = "Đánh giá độc lập từ Panelist 2."
        };
        var (submitSuccess, _, _) = await service.SubmitEvaluationAsync(p2Input, panelist2.Id);
        Assert.True(submitSuccess);

        // Act - Panelist 2 mở lại phiếu sau khi đã nộp
        var (canOpen, _, model) = await service.GetEvaluationSheetAsync(
            interview.Id, panelist2.Id, isStaffManager: false);

        // Assert
        Assert.True(canOpen);
        Assert.NotNull(model);
        Assert.True(model.IsSubmitted);
        Assert.True(model.CanViewOtherEvaluations); // Đã mở khóa Blind Review!
        Assert.NotEmpty(model.PanelEvaluations);

        // Panelist 2 nhìn thấy đánh giá của Panelist 1
        var p1Eval = model.PanelEvaluations.FirstOrDefault(e => e.InterviewerId == panelist1.Id);
        Assert.NotNull(p1Eval);
        Assert.Equal("Nên tuyển", p1Eval.RecommendationDisplay);
        Assert.Equal(4.5m, p1Eval.OverallScore);
        Assert.Contains("microservices", p1Eval.Strengths);
    }

    [Fact]
    public async Task GetEvaluationSheet_WhenStaffManagerViews_ShouldAlwaysSeeAllSubmittedEvaluations()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (interview, panelist1, _, _, recruiter) = await SeedInterviewTestDataAsync(
            context, panelist1Submitted: true);
        var service = new InterviewEvaluationService(context, NullLogger<InterviewEvaluationService>.Instance);

        // Act - Recruiter xem phiếu của buổi phỏng vấn
        var (canOpen, _, model) = await service.GetEvaluationSheetAsync(
            interview.Id, recruiter.Id, isStaffManager: true);

        // Assert
        Assert.True(canOpen);
        Assert.NotNull(model);
        Assert.True(model.CanViewOtherEvaluations); // Recruiter luôn được xem
        Assert.NotEmpty(model.PanelEvaluations);

        var p1Eval = model.PanelEvaluations.First();
        Assert.Equal(panelist1.Id, p1Eval.InterviewerId);
        Assert.True(p1Eval.CanRecruiterReopen); // Còn trong 24h nên Recruiter có quyền mở lại
        Assert.True(p1Eval.HoursRemainingToReopen > 0);
    }

    [Fact]
    public async Task GetEvaluationSheet_WhenCandidateAttemptsToOpen_ShouldReturnSecurityViolation_Rule12()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (interview, _, _, _, _) = await SeedInterviewTestDataAsync(context);

        var candidateUser = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Ứng Viên Đang Thi Tuyển",
            Email = "candidate_hacker@gmail.com",
            PasswordHash = "hash",
            Status = "ACTIVE",
            Role = UserRoles.Candidate,
            UserType = UserType.EXTERNAL_CANDIDATE
        };
        await context.Users.AddAsync(candidateUser);
        await context.SaveChangesAsync();

        var service = new InterviewEvaluationService(context, NullLogger<InterviewEvaluationService>.Instance);

        // Act
        var (canOpen, message, model) = await service.GetEvaluationSheetAsync(interview.Id, candidateUser.Id, isStaffManager: false);

        // Assert
        Assert.False(canOpen);
        Assert.Null(model);
        Assert.Contains("Rule 12", message);
        Assert.Contains("Ứng viên (Candidate) không có quyền mở phiếu đánh giá", message);
    }

    [Fact]
    public async Task SubmitEvaluation_WhenCandidateAttemptsToSubmit_ShouldFailWithSecurityViolation_Rule12()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (interview, _, _, _, _) = await SeedInterviewTestDataAsync(context);

        var candidateUser = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Ứng Viên Cố Nộp Đánh Giá",
            Email = "candidate_submitting@gmail.com",
            PasswordHash = "hash",
            Status = "ACTIVE",
            Role = UserRoles.Candidate,
            UserType = UserType.EXTERNAL_CANDIDATE
        };
        await context.Users.AddAsync(candidateUser);
        await context.SaveChangesAsync();

        var service = new InterviewEvaluationService(context, NullLogger<InterviewEvaluationService>.Instance);

        var input = new InterviewEvaluationSubmitInputModel
        {
            InterviewId = interview.Id,
            Recommendation = "HIRE",
            DetailedFeedback = "Tự đánh giá chính mình"
        };

        // Act
        var (success, message, evalId) = await service.SubmitEvaluationAsync(input, candidateUser.Id);

        // Assert
        Assert.False(success);
        Assert.Null(evalId);
        Assert.Contains("Rule 12", message);
        Assert.Contains("Ứng viên (Candidate) tuyệt đối không có quyền nộp phiếu đánh giá", message);
    }

    #endregion
}
