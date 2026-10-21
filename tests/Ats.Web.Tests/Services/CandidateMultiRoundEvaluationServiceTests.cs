using Ats.Web.Constants;
using Ats.Web.Data;
using Ats.Web.Models.DTOs;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Models.ViewModels.Candidates;
using Ats.Web.Services;
using Ats.Web.Services.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ats.Web.Tests.Services;

/// <summary>
/// Bộ kiểm thử đơn vị cho CandidateMultiRoundEvaluationService (SP - 7):
/// 1. Tổng hợp toàn bộ phiếu đánh giá của mọi vòng phỏng vấn cho một ứng viên.
/// 2. Tính toán điểm trung bình từng tiêu chí và tổng hợp các kết luận / khuyến nghị (STRONG_HIRE, HIRE, REJECT...).
/// 3. Ra quyết định tuyển (Chuyển sang đề xuất Offer) -> Ghi nhận chuyển stage và ghi nhật ký AuditLog kèm người quyết định.
/// 4. Ra quyết định loại (Loại kèm lý do) -> Kiểm tra bắt buộc chọn lý do loại và ghi nhật ký AuditLog.
/// 5. Phân quyền: Người dùng không có quyền (User thường) bị từ chối ra quyết định.
/// </summary>
public class CandidateMultiRoundEvaluationServiceTests
{
    private readonly Mock<ICandidateStageService> _stageServiceMock;
    private readonly NullLogger<CandidateMultiRoundEvaluationService> _logger;

    public CandidateMultiRoundEvaluationServiceTests()
    {
        _stageServiceMock = new Mock<ICandidateStageService>();
        _logger = NullLogger<CandidateMultiRoundEvaluationService>.Instance;
    }

    [Fact]
    public async Task GetMultiRoundSummaryAsync_ShouldAggregateAllRoundsAndCalculateAverageCriteriaScores()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemoryDbContext();

        var dept = new Department { Id = Guid.NewGuid(), Name = "IT", Code = "IT", CreatedAt = DateTimeOffset.UtcNow };
        await context.Departments.AddAsync(dept);

        var req = new JobRequisition
        {
            Id = Guid.NewGuid(),
            Code = "REQ-01",
            DepartmentId = dept.Id,
            Department = dept,
            Status = RequisitionStatus.APPROVED,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.JobRequisitions.AddAsync(req);

        var job = new JobPosting
        {
            Id = Guid.NewGuid(),
            RequisitionId = req.Id,
            Requisition = req,
            Title = "Tech Lead Backend",
            Status = JobPostingStatus.PUBLISHED,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.JobPostings.AddAsync(job);

        var stage = new PipelineStage
        {
            Id = Guid.NewGuid(),
            Name = "Phỏng vấn",
            StageOrder = 3,
            ColorCode = "#059669"
        };
        await context.PipelineStages.AddAsync(stage);

        var candidate = new Candidate
        {
            Id = Guid.NewGuid(),
            FirstName = "Trần",
            LastName = "Văn A",
            Email = "vana.tran@example.com",
            Phone = "0901234567",
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.Candidates.AddAsync(candidate);

        var resume = new Resume
        {
            Id = Guid.NewGuid(),
            CandidateId = candidate.Id,
            FileName = "cv.pdf",
            FilePath = "/cv.pdf",
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.Resumes.AddAsync(resume);

        var application = new Application
        {
            Id = Guid.NewGuid(),
            CandidateId = candidate.Id,
            Candidate = candidate,
            JobPostingId = job.Id,
            JobPosting = job,
            ResumeId = resume.Id,
            Resume = resume,
            CurrentStageId = stage.Id,
            CurrentStage = stage,
            Stage = CandidateStage.INTERVIEW,
            Status = ApplicationStatus.IN_PROCESS,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.Applications.AddAsync(application);

        var interviewer1 = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Nguyễn Văn Phỏng Vấn 1",
            Email = "pv1@example.com",
            PasswordHash = "hash",
            Role = UserRoles.HiringManager,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var interviewer2 = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Lê Thị Phỏng Vấn 2",
            Email = "pv2@example.com",
            PasswordHash = "hash",
            Role = UserRoles.Interviewer,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.Users.AddRangeAsync(interviewer1, interviewer2);

        // Vòng 1: Technical Round
        var round1 = new Interview
        {
            Id = Guid.NewGuid(),
            ApplicationId = application.Id,
            Application = application,
            RoundNumber = 1,
            Title = "Vòng 1: Đánh giá Kỹ thuật Chuyên môn",
            InterviewType = InterviewType.ONLINE_MEET,
            Status = InterviewStatus.COMPLETED,
            StartTime = DateTimeOffset.UtcNow.AddDays(-3),
            EndTime = DateTimeOffset.UtcNow.AddDays(-3).AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-4)
        };

        // Vòng 2: Culture Fit & System Design
        var round2 = new Interview
        {
            Id = Guid.NewGuid(),
            ApplicationId = application.Id,
            Application = application,
            RoundNumber = 2,
            Title = "Vòng 2: Đánh giá Kiến trúc & Culture Fit",
            InterviewType = InterviewType.OFFLINE_OFFICE,
            Status = InterviewStatus.COMPLETED,
            StartTime = DateTimeOffset.UtcNow.AddDays(-1),
            EndTime = DateTimeOffset.UtcNow.AddDays(-1).AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2)
        };
        await context.Interviews.AddRangeAsync(round1, round2);

        // Tiêu chí đánh giá
        var critArchitecture = new EvaluationCriteria
        {
            Id = Guid.NewGuid(),
            Name = "System Architecture",
            CriteriaType = "HARD_SKILL",
            CreatedAt = DateTimeOffset.UtcNow
        };
        var critTeamwork = new EvaluationCriteria
        {
            Id = Guid.NewGuid(),
            Name = "Teamwork & Culture Fit",
            CriteriaType = "SOFT_SKILL",
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.EvaluationCriterias.AddRangeAsync(critArchitecture, critTeamwork);

        // Phiếu đánh giá Vòng 1 của PV1
        var evalR1 = new InterviewEvaluation
        {
            Id = Guid.NewGuid(),
            InterviewId = round1.Id,
            Interview = round1,
            InterviewerId = interviewer1.Id,
            Interviewer = interviewer1,
            OverallScore = 4.0m,
            Recommendation = RecommendationType.HIRE,
            Strengths = "Nền tảng kiến trúc vững chắc, trả lời tốt.",
            IsSubmitted = true,
            SubmittedAt = DateTimeOffset.UtcNow.AddDays(-3),
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-3),
            Scores = new List<EvaluationScore>
            {
                new() { Id = Guid.NewGuid(), CriteriaId = critArchitecture.Id, Criteria = critArchitecture, Score = 4, Comment = "Tốt" },
                new() { Id = Guid.NewGuid(), CriteriaId = critTeamwork.Id, Criteria = critTeamwork, Score = 4, Comment = "Khá" }
            }
        };

        // Phiếu đánh giá Vòng 2 của PV2
        var evalR2 = new InterviewEvaluation
        {
            Id = Guid.NewGuid(),
            InterviewId = round2.Id,
            Interview = round2,
            InterviewerId = interviewer2.Id,
            Interviewer = interviewer2,
            OverallScore = 5.0m,
            Recommendation = RecommendationType.STRONG_HIRE,
            Strengths = "Rất hợp văn hóa công ty, tư duy mở.",
            IsSubmitted = true,
            SubmittedAt = DateTimeOffset.UtcNow.AddDays(-1),
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            Scores = new List<EvaluationScore>
            {
                new() { Id = Guid.NewGuid(), CriteriaId = critArchitecture.Id, Criteria = critArchitecture, Score = 5, Comment = "Rất sâu" },
                new() { Id = Guid.NewGuid(), CriteriaId = critTeamwork.Id, Criteria = critTeamwork, Score = 4, Comment = "Năng động" }
            }
        };
        await context.InterviewEvaluations.AddRangeAsync(evalR1, evalR2);
        await context.SaveChangesAsync();

        var service = new CandidateMultiRoundEvaluationService(
            context,
            _stageServiceMock.Object,
            _logger
        );

        // Act
        var result = await service.GetMultiRoundSummaryAsync(application.Id, interviewer1.Id, UserRoles.HiringManager);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(application.Id, result.ApplicationId);
        Assert.Equal("Trần Văn A", result.CandidateName);
        Assert.Equal(2, result.TotalRoundsCount);
        Assert.Equal(2, result.TotalSubmittedEvaluationsCount);
        Assert.Equal(1, result.StrongHireCount);
        Assert.Equal(1, result.HireCount);

        // Kiểm tra điểm trung bình tiêu chí:
        // System Architecture: (4 + 5) / 2 = 4.5
        var archSummary = result.CriteriaAverages.FirstOrDefault(c => c.CriteriaName == critArchitecture.Name);
        Assert.NotNull(archSummary);
        Assert.Equal(4.5, archSummary.AverageScore);
        Assert.Equal(2, archSummary.EvaluationCount);

        // Teamwork: (4 + 4) / 2 = 4.0
        var teamSummary = result.CriteriaAverages.FirstOrDefault(c => c.CriteriaName == critTeamwork.Name);
        Assert.NotNull(teamSummary);
        Assert.Equal(4.0, teamSummary.AverageScore);
        Assert.Equal(2, teamSummary.EvaluationCount);

        // Điểm trung bình toàn diện: (4.5 + 4.0) / 2 = 4.25
        Assert.True(result.OverallAverageScore > 4.2 && result.OverallAverageScore < 4.3);
    }

    [Fact]
    public async Task MakeHiringDecisionAsync_OfferDecision_TransitionsStageAndLogsAudit()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemoryDbContext();

        var hiringManagerId = Guid.NewGuid();
        var req = new JobRequisition
        {
            Id = Guid.NewGuid(),
            Code = "REQ-02",
            HiringManagerId = hiringManagerId,
            Status = RequisitionStatus.APPROVED,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.JobRequisitions.AddAsync(req);

        var job = new JobPosting
        {
            Id = Guid.NewGuid(),
            RequisitionId = req.Id,
            Requisition = req,
            Title = "Senior Engineer",
            Status = JobPostingStatus.PUBLISHED,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.JobPostings.AddAsync(job);

        var stage = new PipelineStage
        {
            Id = Guid.NewGuid(),
            Name = "Phỏng vấn",
            StageOrder = 3,
            ColorCode = "#059669"
        };
        await context.PipelineStages.AddAsync(stage);

        var candidate = new Candidate
        {
            Id = Guid.NewGuid(),
            FirstName = "Nguyễn",
            LastName = "Văn B",
            Email = "vanb.nguyen@example.com",
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.Candidates.AddAsync(candidate);

        var resume = new Resume
        {
            Id = Guid.NewGuid(),
            CandidateId = candidate.Id,
            FileName = "cv.pdf",
            FilePath = "/cv.pdf",
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.Resumes.AddAsync(resume);

        var application = new Application
        {
            Id = Guid.NewGuid(),
            CandidateId = candidate.Id,
            Candidate = candidate,
            JobPostingId = job.Id,
            JobPosting = job,
            ResumeId = resume.Id,
            Resume = resume,
            CurrentStageId = stage.Id,
            CurrentStage = stage,
            Stage = CandidateStage.INTERVIEW,
            Status = ApplicationStatus.IN_PROCESS,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.Applications.AddAsync(application);

        var hiringManager = new User
        {
            Id = hiringManagerId,
            FullName = "Trần Trưởng Bộ Phận",
            Email = "hm@example.com",
            PasswordHash = "hash",
            Role = UserRoles.HiringManager,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.Users.AddAsync(hiringManager);
        await context.SaveChangesAsync();

        _stageServiceMock
            .Setup(s => s.TransitionStageAsync(
                It.Is<TransitionCandidateStageInputModel>(m => m.ApplicationId == application.Id && m.ToStage == CandidateStage.OFFER),
                hiringManager.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CandidateStageHistoryDto
            {
                Id = Guid.NewGuid(),
                ApplicationId = application.Id,
                FromStage = CandidateStage.INTERVIEW,
                ToStage = CandidateStage.OFFER,
                CreatedAt = DateTimeOffset.UtcNow
            });

        var service = new CandidateMultiRoundEvaluationService(
            context,
            _stageServiceMock.Object,
            _logger
        );

        var input = new CandidateHiringDecisionInputModel
        {
            ApplicationId = application.Id,
            Decision = CandidateStage.OFFER,
            Note = "Ứng viên xuất sắc toàn diện qua cả 2 vòng. Đề xuất offer level Senior 2."
        };

        // Act
        var result = await service.MakeHiringDecisionAsync(input, hiringManager.Id, UserRoles.HiringManager);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("OFFER", result.Action);
        Assert.Equal("Trần Trưởng Bộ Phận", result.DecidedByUserName);

        _stageServiceMock.Verify(s => s.TransitionStageAsync(
            It.Is<TransitionCandidateStageInputModel>(m => m.ApplicationId == application.Id && m.ToStage == CandidateStage.OFFER),
            hiringManager.Id,
            It.IsAny<CancellationToken>()), Times.Once);

        // Kiểm tra AuditLog được ghi lại
        var audit = context.AuditLogs.FirstOrDefault(a => a.Action == "QUYET_DINH_DE_XUAT_OFFER");
        Assert.NotNull(audit);
        Assert.Equal(hiringManager.Id, audit.UserId);
        Assert.Equal(application.Id.ToString(), audit.EntityId);

        // Kiểm tra CandidateInteraction được ghi lại
        var interaction = context.CandidateInteractions.FirstOrDefault(ci => ci.CandidateId == candidate.Id);
        Assert.NotNull(interaction);
        Assert.Equal(CandidateInteractionType.NOTE, interaction.InteractionType);
        Assert.Contains("Đề xuất tuyển (Offer)", interaction.Title);
    }

    [Fact]
    public async Task MakeHiringDecisionAsync_RejectDecision_TransitionsStageAndLogsReason()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemoryDbContext();

        var hiringManagerId = Guid.NewGuid();
        var req = new JobRequisition
        {
            Id = Guid.NewGuid(),
            Code = "REQ-03",
            HiringManagerId = hiringManagerId,
            Status = RequisitionStatus.APPROVED,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.JobRequisitions.AddAsync(req);

        var job = new JobPosting
        {
            Id = Guid.NewGuid(),
            RequisitionId = req.Id,
            Requisition = req,
            Title = "Senior Engineer",
            Status = JobPostingStatus.PUBLISHED,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.JobPostings.AddAsync(job);

        var stage = new PipelineStage
        {
            Id = Guid.NewGuid(),
            Name = "Phỏng vấn",
            StageOrder = 3,
            ColorCode = "#059669"
        };
        await context.PipelineStages.AddAsync(stage);

        var candidate = new Candidate
        {
            Id = Guid.NewGuid(),
            FirstName = "Lê",
            LastName = "Văn C",
            Email = "vanc.le@example.com",
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.Candidates.AddAsync(candidate);

        var resume = new Resume
        {
            Id = Guid.NewGuid(),
            CandidateId = candidate.Id,
            FileName = "cv.pdf",
            FilePath = "/cv.pdf",
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.Resumes.AddAsync(resume);

        var application = new Application
        {
            Id = Guid.NewGuid(),
            CandidateId = candidate.Id,
            Candidate = candidate,
            JobPostingId = job.Id,
            JobPosting = job,
            ResumeId = resume.Id,
            Resume = resume,
            CurrentStageId = stage.Id,
            CurrentStage = stage,
            Stage = CandidateStage.INTERVIEW,
            Status = ApplicationStatus.IN_PROCESS,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.Applications.AddAsync(application);

        var hiringManager = new User
        {
            Id = hiringManagerId,
            FullName = "Trưởng Bộ Phận D",
            Email = "hmd@example.com",
            PasswordHash = "hash",
            Role = UserRoles.HiringManager,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.Users.AddAsync(hiringManager);

        var reason = new RejectionReason
        {
            Id = Guid.NewGuid(),
            Code = "REJ_SKILL",
            Name = "Kỹ năng chuyên môn chưa phù hợp",
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.RejectionReasons.AddAsync(reason);
        await context.SaveChangesAsync();

        _stageServiceMock
            .Setup(s => s.TransitionStageAsync(
                It.Is<TransitionCandidateStageInputModel>(m => m.ApplicationId == application.Id && m.ToStage == CandidateStage.REJECTED && m.RejectionReasonId == reason.Id),
                hiringManager.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CandidateStageHistoryDto
            {
                Id = Guid.NewGuid(),
                ApplicationId = application.Id,
                FromStage = CandidateStage.INTERVIEW,
                ToStage = CandidateStage.REJECTED,
                RejectionReasonId = reason.Id,
                RejectionReasonName = reason.Name,
                CreatedAt = DateTimeOffset.UtcNow
            });

        var service = new CandidateMultiRoundEvaluationService(
            context,
            _stageServiceMock.Object,
            _logger
        );

        var inputWithReason = new CandidateHiringDecisionInputModel
        {
            ApplicationId = application.Id,
            Decision = CandidateStage.REJECTED,
            RejectionReasonId = reason.Id,
            Note = "Chưa đáp ứng được yêu cầu về Distributed System."
        };

        // Act
        var resultWithReason = await service.MakeHiringDecisionAsync(inputWithReason, hiringManager.Id, UserRoles.HiringManager);

        // Assert
        Assert.NotNull(resultWithReason);
        Assert.Equal("REJECTED", resultWithReason.Action);
        Assert.Equal(reason.Name, resultWithReason.Reason);

        _stageServiceMock.Verify(s => s.TransitionStageAsync(
            It.Is<TransitionCandidateStageInputModel>(m => m.ApplicationId == application.Id && m.ToStage == CandidateStage.REJECTED && m.RejectionReasonId == reason.Id),
            hiringManager.Id,
            It.IsAny<CancellationToken>()), Times.Once);

        var audit = context.AuditLogs.FirstOrDefault(a => a.Action == "QUYET_DINH_LOAI_UNG_VIEN");
        Assert.NotNull(audit);
        Assert.Equal(hiringManager.Id, audit.UserId);
        Assert.Equal(application.Id.ToString(), audit.EntityId);
    }

    [Fact]
    public async Task MakeHiringDecisionAsync_UnauthorizedUser_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemoryDbContext();

        var req = new JobRequisition
        {
            Id = Guid.NewGuid(),
            Code = "REQ-04",
            HiringManagerId = Guid.NewGuid(),
            Status = RequisitionStatus.APPROVED,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.JobRequisitions.AddAsync(req);

        var job = new JobPosting
        {
            Id = Guid.NewGuid(),
            RequisitionId = req.Id,
            Requisition = req,
            Title = "Senior Engineer",
            Status = JobPostingStatus.PUBLISHED,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.JobPostings.AddAsync(job);

        var stage = new PipelineStage
        {
            Id = Guid.NewGuid(),
            Name = "Phỏng vấn",
            StageOrder = 3,
            ColorCode = "#059669"
        };
        await context.PipelineStages.AddAsync(stage);

        var candidate = new Candidate
        {
            Id = Guid.NewGuid(),
            FirstName = "Hoàng",
            LastName = "Văn E",
            Email = "vane.hoang@example.com",
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.Candidates.AddAsync(candidate);

        var resume = new Resume
        {
            Id = Guid.NewGuid(),
            CandidateId = candidate.Id,
            FileName = "cv.pdf",
            FilePath = "/cv.pdf",
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.Resumes.AddAsync(resume);

        var application = new Application
        {
            Id = Guid.NewGuid(),
            CandidateId = candidate.Id,
            Candidate = candidate,
            JobPostingId = job.Id,
            JobPosting = job,
            ResumeId = resume.Id,
            Resume = resume,
            CurrentStageId = stage.Id,
            CurrentStage = stage,
            Stage = CandidateStage.INTERVIEW,
            Status = ApplicationStatus.IN_PROCESS,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.Applications.AddAsync(application);

        var standardUser = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Nhân viên bình thường",
            Email = "staff@example.com",
            PasswordHash = "hash",
            Role = "Employee",
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.Users.AddAsync(standardUser);
        await context.SaveChangesAsync();

        var service = new CandidateMultiRoundEvaluationService(
            context,
            _stageServiceMock.Object,
            _logger
        );

        var input = new CandidateHiringDecisionInputModel
        {
            ApplicationId = application.Id,
            Decision = CandidateStage.OFFER,
            Note = "Đề xuất tuyển dụng."
        };

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.MakeHiringDecisionAsync(input, standardUser.Id, "Employee"));
    }
}
