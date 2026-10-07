using Ats.Web.Controllers;
using Ats.Web.Data;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Services;
using Ats.Web.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using Xunit;

namespace Ats.Web.Tests;

public class SearchMinimumLengthTests
{
    private readonly Mock<ILogger<JobService>> _mockJobLogger = new();

    [Fact]
    public async Task JobService_SearchWithLessThanThreeCharacters_DoesNotFilter()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var jobService = new JobService(db, _mockJobLogger.Object);

        // Act
        var allJobs = await jobService.GetJobListAsync(search: null);
        var searchTwoChars = await jobService.GetJobListAsync(search: "ab");

        // Assert
        Assert.True(allJobs.TotalRecords > 0);
        Assert.Equal(allJobs.TotalRecords, searchTwoChars.TotalRecords);
    }

    [Fact]
    public async Task JobService_SearchWithThreeOrMoreCharacters_FiltersProperly()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var jobService = new JobService(db, _mockJobLogger.Object);

        // Act
        var allJobs = await jobService.GetJobListAsync(search: null);
        var searchFrontend = await jobService.GetJobListAsync(search: "Frontend");

        // Assert
        Assert.True(searchFrontend.TotalRecords > 0);
        Assert.True(searchFrontend.TotalRecords <= allJobs.TotalRecords);
        Assert.All(searchFrontend.Jobs, j => 
            Assert.True(j.Title.Contains("Frontend", StringComparison.OrdinalIgnoreCase) ||
                        j.ShortSummary.Contains("Frontend", StringComparison.OrdinalIgnoreCase) ||
                        j.TechStack.Any(t => t.Contains("Frontend", StringComparison.OrdinalIgnoreCase)) ||
                        j.Requirements.Any(r => r.Contains("Frontend", StringComparison.OrdinalIgnoreCase)) ||
                        j.Department.Contains("Frontend", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task HomeController_GetCandidatePipeline_SearchLessThanThreeCharacters_DoesNotFilter()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var dept = new Department { Id = Guid.NewGuid(), Name = "Bộ phận Kỹ thuật", Code = "ENG", IsActive = true };
        var pos = new JobPosition { Id = Guid.NewGuid(), Title = "Software Engineer", Code = "SWE", DepartmentId = dept.Id };
        var creator = new User { Id = Guid.NewGuid(), Email = "creator@novera.vn", FullName = "Hiring Manager" };
        var req = new JobRequisition
        {
            Id = Guid.NewGuid(),
            DepartmentId = dept.Id,
            JobPositionId = pos.Id,
            HiringManagerId = creator.Id,
            Code = "REQ-SRCH-01",
            Quantity = 2,
            Status = RequisitionStatus.APPROVED
        };
        var posting = new JobPosting
        {
            Id = Guid.NewGuid(),
            RequisitionId = req.Id,
            Title = "Senior Backend Engineer",
            Slug = "senior-backend-engineer",
            WorkLocation = "Hà Nội",
            Status = JobPostingStatus.PUBLISHED
        };
        var stage = new PipelineStage { Id = Guid.NewGuid(), Name = "Sàng lọc CV", StageOrder = 1, ColorCode = "#059669" };

        db.Departments.Add(dept);
        db.JobPositions.Add(pos);
        db.Users.Add(creator);
        db.JobRequisitions.Add(req);
        db.JobPostings.Add(posting);
        db.PipelineStages.Add(stage);

        var cand1 = new Candidate { Id = Guid.NewGuid(), FirstName = "Nguyen", LastName = "Van A", Email = "nguyen.vana@example.com" };
        var cand2 = new Candidate { Id = Guid.NewGuid(), FirstName = "Tran", LastName = "Thi B", Email = "tran.thib@example.com" };
        var resume1 = new Resume { Id = Guid.NewGuid(), CandidateId = cand1.Id, FileName = "cv1.pdf", FilePath = "/cv1.pdf", IsPrimary = true };
        var resume2 = new Resume { Id = Guid.NewGuid(), CandidateId = cand2.Id, FileName = "cv2.pdf", FilePath = "/cv2.pdf", IsPrimary = true };

        db.Candidates.AddRange(cand1, cand2);
        db.Resumes.AddRange(resume1, resume2);

        db.Applications.Add(new Application
        {
            Id = Guid.NewGuid(),
            CandidateId = cand1.Id,
            ResumeId = resume1.Id,
            JobPostingId = posting.Id,
            CurrentStageId = stage.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            AppliedAt = DateTimeOffset.UtcNow
        });
        db.Applications.Add(new Application
        {
            Id = Guid.NewGuid(),
            CandidateId = cand2.Id,
            ResumeId = resume2.Id,
            JobPostingId = posting.Id,
            CurrentStageId = stage.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            AppliedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var mockLogger = new Mock<ILogger<HomeController>>();
        var mockJobService = new Mock<IJobService>();
        var controller = new HomeController(db, mockJobService.Object, mockLogger.Object);

        // Act: search with 2 chars "ng" (should not filter, returns all 2)
        var result = await controller.GetCandidatePipeline(search: "ng", stageOrder: "ALL", page: 1, pageSize: 10);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);
        var totalRecords = doc.RootElement.GetProperty("totalRecords").GetInt32();

        Assert.Equal(2, totalRecords);
    }

    [Fact]
    public async Task HomeController_GetCandidatePipeline_SearchThreeCharactersOrMore_FiltersCorrectly()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var dept = new Department { Id = Guid.NewGuid(), Name = "Bộ phận Kỹ thuật", Code = "ENG", IsActive = true };
        var pos = new JobPosition { Id = Guid.NewGuid(), Title = "Software Engineer", Code = "SWE", DepartmentId = dept.Id };
        var creator = new User { Id = Guid.NewGuid(), Email = "creator@novera.vn", FullName = "Hiring Manager" };
        var req = new JobRequisition
        {
            Id = Guid.NewGuid(),
            DepartmentId = dept.Id,
            JobPositionId = pos.Id,
            HiringManagerId = creator.Id,
            Code = "REQ-SRCH-02",
            Quantity = 2,
            Status = RequisitionStatus.APPROVED
        };
        var posting = new JobPosting
        {
            Id = Guid.NewGuid(),
            RequisitionId = req.Id,
            Title = "Senior Backend Engineer",
            Slug = "senior-backend-engineer-2",
            WorkLocation = "Hà Nội",
            Status = JobPostingStatus.PUBLISHED
        };
        var stage = new PipelineStage { Id = Guid.NewGuid(), Name = "Sàng lọc CV", StageOrder = 1, ColorCode = "#059669" };

        db.Departments.Add(dept);
        db.JobPositions.Add(pos);
        db.Users.Add(creator);
        db.JobRequisitions.Add(req);
        db.JobPostings.Add(posting);
        db.PipelineStages.Add(stage);

        var cand1 = new Candidate { Id = Guid.NewGuid(), FirstName = "Nguyen", LastName = "Van A", Email = "nguyen.vana@example.com" };
        var cand2 = new Candidate { Id = Guid.NewGuid(), FirstName = "Tran", LastName = "Thi B", Email = "tran.thib@example.com" };
        var resume1 = new Resume { Id = Guid.NewGuid(), CandidateId = cand1.Id, FileName = "cv1.pdf", FilePath = "/cv1.pdf", IsPrimary = true };
        var resume2 = new Resume { Id = Guid.NewGuid(), CandidateId = cand2.Id, FileName = "cv2.pdf", FilePath = "/cv2.pdf", IsPrimary = true };

        db.Candidates.AddRange(cand1, cand2);
        db.Resumes.AddRange(resume1, resume2);

        db.Applications.Add(new Application
        {
            Id = Guid.NewGuid(),
            CandidateId = cand1.Id,
            ResumeId = resume1.Id,
            JobPostingId = posting.Id,
            CurrentStageId = stage.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            AppliedAt = DateTimeOffset.UtcNow
        });
        db.Applications.Add(new Application
        {
            Id = Guid.NewGuid(),
            CandidateId = cand2.Id,
            ResumeId = resume2.Id,
            JobPostingId = posting.Id,
            CurrentStageId = stage.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            AppliedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var mockLogger = new Mock<ILogger<HomeController>>();
        var mockJobService = new Mock<IJobService>();
        var controller = new HomeController(db, mockJobService.Object, mockLogger.Object);

        // Act: search with 6 chars "Nguyen" (should filter, returns 1)
        var result = await controller.GetCandidatePipeline(search: "Nguyen", stageOrder: "ALL", page: 1, pageSize: 10);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);
        var totalRecords = doc.RootElement.GetProperty("totalRecords").GetInt32();

        Assert.Equal(1, totalRecords);
    }

    [Fact]
    public async Task QuestionBanksController_SearchLessThanThreeCharacters_DoesNotFilter()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var q1 = new InterviewQuestionBank { Id = Guid.NewGuid(), Content = "Java polymorphism basics", Competency = "Kỹ năng chuyên môn", Difficulty = "Cơ bản", IsActive = true };
        var q2 = new InterviewQuestionBank { Id = Guid.NewGuid(), Content = "React hooks dependency array", Competency = "Kỹ năng chuyên môn", Difficulty = "Trung bình", IsActive = true };
        db.InterviewQuestionBanks.AddRange(q1, q2);
        await db.SaveChangesAsync();

        var controller = new QuestionBanksController(db);

        // Act: keyword with 2 chars "Ja" (should not filter, returns 2)
        var result = await controller.GetQuestions(keyword: "Ja", competency: null, difficulty: null, page: 1, pageSize: 10);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);
        var totalRecords = doc.RootElement.GetProperty("totalRecords").GetInt32();

        Assert.Equal(2, totalRecords);
    }

    [Fact]
    public async Task QuestionBanksController_SearchThreeCharactersOrMore_FiltersCorrectly()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var q1 = new InterviewQuestionBank { Id = Guid.NewGuid(), Content = "Java polymorphism basics", Competency = "Kỹ năng chuyên môn", Difficulty = "Cơ bản", IsActive = true };
        var q2 = new InterviewQuestionBank { Id = Guid.NewGuid(), Content = "React hooks dependency array", Competency = "Kỹ năng chuyên môn", Difficulty = "Trung bình", IsActive = true };
        db.InterviewQuestionBanks.AddRange(q1, q2);
        await db.SaveChangesAsync();

        var controller = new QuestionBanksController(db);

        // Act: keyword with 4 chars "Java" (should filter, returns 1)
        var result = await controller.GetQuestions(keyword: "Java", competency: null, difficulty: null, page: 1, pageSize: 10);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);
        var totalRecords = doc.RootElement.GetProperty("totalRecords").GetInt32();

        Assert.Equal(1, totalRecords);
    }

    [Fact]
    public async Task UserService_GetUsersAsync_SearchLessThanThreeCharacters_DoesNotFilter()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var u1 = new User { Id = Guid.NewGuid(), FullName = "Le Thi Dao", Email = "le.dao@noveratech.digital", Status = "ACTIVE", CreatedAt = DateTime.UtcNow };
        var u2 = new User { Id = Guid.NewGuid(), FullName = "Hoang Van Em", Email = "hoang.em@noveratech.digital", Status = "ACTIVE", CreatedAt = DateTime.UtcNow };
        db.Users.AddRange(u1, u2);
        await db.SaveChangesAsync();

        var userService = new UserService(db, Mock.Of<IEmailService>());

        // Act: keyword with 2 chars "le"
        var result = await userService.GetUsersAsync(keyword: "le", role: null, status: null, page: 1, pageSize: 10);

        // Assert: both returned
        Assert.Equal(2, result.TotalRecords);
    }

    [Fact]
    public async Task UserService_GetUsersAsync_SearchThreeCharactersOrMore_FiltersCorrectly()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var u1 = new User { Id = Guid.NewGuid(), FullName = "Le Thi Dao", Email = "le.dao@noveratech.digital", Status = "ACTIVE", CreatedAt = DateTime.UtcNow };
        var u2 = new User { Id = Guid.NewGuid(), FullName = "Hoang Van Em", Email = "hoang.em@noveratech.digital", Status = "ACTIVE", CreatedAt = DateTime.UtcNow };
        db.Users.AddRange(u1, u2);
        await db.SaveChangesAsync();

        var userService = new UserService(db, Mock.Of<IEmailService>());

        // Act: keyword with 3 chars "Dao"
        var result = await userService.GetUsersAsync(keyword: "Dao", role: null, status: null, page: 1, pageSize: 10);

        // Assert: only u1 returned
        Assert.Equal(1, result.TotalRecords);
        Assert.Equal("Le Thi Dao", result.Users[0].FullName);
    }
}
