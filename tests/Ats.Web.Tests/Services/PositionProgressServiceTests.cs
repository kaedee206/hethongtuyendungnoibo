using Ats.Web.Data;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ats.Web.Tests.Services;

/// <summary>
/// Unit Tests kiểm thử nghiệp vụ theo dõi tiến độ tuyển dụng vị trí sở hữu của Trưởng bộ phận (SCRUM-351).
/// </summary>
public class PositionProgressServiceTests
{
    private static ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"AtsPositionProgressDb_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new ApplicationDbContext(options);
    }

    [Fact]
    public void CalculateDaysRemaining_WhenNullDate_ReturnsNotSet()
    {
        // Act
        var result = PositionProgressService.CalculateDaysRemaining(null);

        // Assert
        Assert.Null(result.DaysRemaining);
        Assert.Equal("NOT_SET", result.UrgencyLevel);
        Assert.Contains("Chưa thiết lập", result.DaysRemainingDisplay);
    }

    [Fact]
    public void CalculateDaysRemaining_WhenDateInPast_ReturnsDangerOverdue()
    {
        // Arrange
        var pastDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-5));

        // Act
        var result = PositionProgressService.CalculateDaysRemaining(pastDate);

        // Assert
        Assert.NotNull(result.DaysRemaining);
        Assert.True(result.DaysRemaining < 0);
        Assert.Equal("DANGER", result.UrgencyLevel);
        Assert.Contains("Quá hạn", result.DaysRemainingDisplay);
    }

    [Fact]
    public void CalculateDaysRemaining_WhenDateIsToday_ReturnsWarningToday()
    {
        // Arrange
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Act
        var result = PositionProgressService.CalculateDaysRemaining(today);

        // Assert
        Assert.Equal(0, result.DaysRemaining);
        Assert.Equal("WARNING", result.UrgencyLevel);
        Assert.Contains("Hôm nay là hạn chót", result.DaysRemainingDisplay);
    }

    [Fact]
    public void CalculateDaysRemaining_WhenDateWithin14Days_ReturnsWarning()
    {
        // Arrange
        var soonDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));

        // Act
        var result = PositionProgressService.CalculateDaysRemaining(soonDate);

        // Assert
        Assert.NotNull(result.DaysRemaining);
        Assert.InRange(result.DaysRemaining.Value, 1, 14);
        Assert.Equal("WARNING", result.UrgencyLevel);
        Assert.Contains("Còn", result.DaysRemainingDisplay);
    }

    [Fact]
    public void CalculateDaysRemaining_WhenDateBeyond14Days_ReturnsNormal()
    {
        // Arrange
        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30));

        // Act
        var result = PositionProgressService.CalculateDaysRemaining(futureDate);

        // Assert
        Assert.NotNull(result.DaysRemaining);
        Assert.True(result.DaysRemaining > 14);
        Assert.Equal("NORMAL", result.UrgencyLevel);
        Assert.Contains("Còn", result.DaysRemainingDisplay);
    }

    [Fact]
    public async Task GetOwnedPositionsSummaryAsync_ReturnsOnlyPositionsOwnedByHiringManager()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var hiringManagerId = Guid.NewGuid();
        var otherHiringManagerId = Guid.NewGuid();

        var dept = new Department { Id = Guid.NewGuid(), Name = "Ky thuat", Code = "ENG" };
        var pos = new JobPosition { Id = Guid.NewGuid(), Title = "Senior Backend", Code = "BE-SR", DepartmentId = dept.Id };
        context.Departments.Add(dept);
        context.JobPositions.Add(pos);

        var ownedReq = new JobRequisition
        {
            Id = Guid.NewGuid(),
            Code = "REQ-ENG-001",
            JobPositionId = pos.Id,
            DepartmentId = dept.Id,
            HiringManagerId = hiringManagerId,
            Quantity = 3,
            Status = RequisitionStatus.APPROVED,
            TargetHireDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(20))
        };

        var otherReq = new JobRequisition
        {
            Id = Guid.NewGuid(),
            Code = "REQ-ENG-002",
            JobPositionId = pos.Id,
            DepartmentId = dept.Id,
            HiringManagerId = otherHiringManagerId,
            Quantity = 1,
            Status = RequisitionStatus.APPROVED
        };

        context.JobRequisitions.AddRange(ownedReq, otherReq);
        await context.SaveChangesAsync();

        var service = new PositionProgressService(context, NullLogger<PositionProgressService>.Instance);

        // Act
        var result = await service.GetOwnedPositionsSummaryAsync(hiringManagerId);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(ownedReq.Id, result[0].RequisitionId);
        Assert.Equal("REQ-ENG-001", result[0].Code);
        Assert.Equal(3, result[0].Quantity);
    }

    [Fact]
    public async Task GetPositionProgressDetailAsync_UnauthorizedAccess_ReturnsEmptyViewModel()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var hiringManagerId = Guid.NewGuid();
        var unauthorizedUserId = Guid.NewGuid();

        var dept = new Department { Id = Guid.NewGuid(), Name = "San pham", Code = "PROD" };
        var pos = new JobPosition { Id = Guid.NewGuid(), Title = "Product Owner", Code = "PO-01", DepartmentId = dept.Id };
        var req = new JobRequisition
        {
            Id = Guid.NewGuid(),
            Code = "REQ-PO-01",
            JobPositionId = pos.Id,
            DepartmentId = dept.Id,
            HiringManagerId = hiringManagerId,
            Quantity = 1
        };

        context.Departments.Add(dept);
        context.JobPositions.Add(pos);
        context.JobRequisitions.Add(req);
        await context.SaveChangesAsync();

        var service = new PositionProgressService(context, NullLogger<PositionProgressService>.Instance);

        // Act
        var result = await service.GetPositionProgressDetailAsync(req.Id, unauthorizedUserId, isElevatedRole: false);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(Guid.Empty, result.SelectedRequisitionId);
    }

    [Fact]
    public async Task GetPositionProgressDetailAsync_WhenAuthorized_CalculatesFunnelAndBossReportCorrectly()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var hmId = Guid.NewGuid();

        var dept = new Department { Id = Guid.NewGuid(), Name = "Cong nghe", Code = "TECH" };
        var pos = new JobPosition { Id = Guid.NewGuid(), Title = "Lead Architect", Code = "ARC-01", DepartmentId = dept.Id };
        var req = new JobRequisition
        {
            Id = Guid.NewGuid(),
            Code = "REQ-ARC-01",
            JobPositionId = pos.Id,
            DepartmentId = dept.Id,
            HiringManagerId = hmId,
            Quantity = 2,
            TargetHireDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10))
        };

        var jobPosting = new JobPosting
        {
            Id = Guid.NewGuid(),
            RequisitionId = req.Id,
            Requisition = req,
            Title = "Lead Architect Tuyển dụng",
            Status = JobPostingStatus.PUBLISHED
        };

        var stage1 = new PipelineStage { Id = Guid.NewGuid(), Name = "Ứng tuyển", StageOrder = 1 };
        var stage4 = new PipelineStage { Id = Guid.NewGuid(), Name = "Phỏng vấn chuyên môn", StageOrder = 4 };
        var stage6 = new PipelineStage { Id = Guid.NewGuid(), Name = "Đã tuyển", StageOrder = 6 };

        var cand1 = new Candidate { Id = Guid.NewGuid(), FirstName = "Nguyen", LastName = "Van A", Email = "a@test.com" };
        var cand2 = new Candidate { Id = Guid.NewGuid(), FirstName = "Tran", LastName = "Thi B", Email = "b@test.com" };

        var res1 = new Resume { Id = Guid.NewGuid(), CandidateId = cand1.Id, FilePath = "resume1.pdf" };
        var res2 = new Resume { Id = Guid.NewGuid(), CandidateId = cand2.Id, FilePath = "resume2.pdf" };

        var app1 = new Application
        {
            Id = Guid.NewGuid(),
            JobPostingId = jobPosting.Id,
            JobPosting = jobPosting,
            CandidateId = cand1.Id,
            Candidate = cand1,
            ResumeId = res1.Id,
            Resume = res1,
            CurrentStageId = stage4.Id,
            CurrentStage = stage4,
            Status = ApplicationStatus.IN_PROCESS
        };

        var app2 = new Application
        {
            Id = Guid.NewGuid(),
            JobPostingId = jobPosting.Id,
            JobPosting = jobPosting,
            CandidateId = cand2.Id,
            Candidate = cand2,
            ResumeId = res2.Id,
            Resume = res2,
            CurrentStageId = stage6.Id,
            CurrentStage = stage6,
            Status = ApplicationStatus.HIRED
        };

        context.Departments.Add(dept);
        context.JobPositions.Add(pos);
        context.JobRequisitions.Add(req);
        context.JobPostings.Add(jobPosting);
        context.PipelineStages.AddRange(stage1, stage4, stage6);
        context.Candidates.AddRange(cand1, cand2);
        context.Resumes.AddRange(res1, res2);
        context.Applications.AddRange(app1, app2);

        // Thêm 1 buổi phỏng vấn sắp tới
        var interview = new Interview
        {
            Id = Guid.NewGuid(),
            ApplicationId = app1.Id,
            Application = app1,
            StartTime = DateTime.UtcNow.AddDays(2),
            EndTime = DateTime.UtcNow.AddDays(2).AddHours(1),
            InterviewType = InterviewType.ONLINE_MEET,
            Status = InterviewStatus.SCHEDULED
        };
        interview.Panelists.Add(new InterviewPanelist { InterviewerId = hmId });
        context.Interviews.Add(interview);

        await context.SaveChangesAsync();

        var service = new PositionProgressService(context, NullLogger<PositionProgressService>.Instance);

        // Act
        var result = await service.GetPositionProgressDetailAsync(req.Id, hmId, isElevatedRole: false);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(req.Id, result.SelectedRequisitionId);
        Assert.Equal(2, result.Quantity);
        Assert.Equal(1, result.HiredCount);
        Assert.Equal(1, result.RemainingHeadcountNeeded);
        Assert.Equal(50, result.HeadcountFulfillmentPercentage);
        Assert.Equal("WARNING", result.UrgencyLevel);
        Assert.NotEmpty(result.ExecutiveBossReportText);
        Assert.Contains("REQ-ARC-01", result.ExecutiveBossReportText);

        // Kiểm tra phỏng vấn sắp tới
        Assert.Single(result.UpcomingInterviews);
        Assert.Contains("Nguyen", result.UpcomingInterviews[0].CandidateName);

        // Kiểm tra ứng viên chờ ý kiến
        Assert.Single(result.PendingFeedbackCandidates);
        Assert.Contains("Nguyen", result.PendingFeedbackCandidates[0].CandidateName);
    }
}
