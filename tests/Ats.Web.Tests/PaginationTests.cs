using Ats.Web.Controllers;
using Ats.Web.Data;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Services;
using Ats.Web.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Ats.Web.Tests;

public class PaginationTests
{
    private readonly Mock<ILogger<RecruitmentCatalogsController>> _mockCatalogLogger = new();
    private readonly Mock<ILogger<JobService>> _mockJobServiceLogger = new();

    [Fact]
    public async Task JobService_GetJobListAsync_CalculatesTotalPagesAndPagesCorrectly()
    {
        // Arrange: using in-memory db with fallback mock jobs
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var jobService = new JobService(db, _mockJobServiceLogger.Object);

        // Act: Page 1 with pageSize 3
        var page1 = await jobService.GetJobListAsync(page: 1, pageSize: 3);

        // Assert: Page 1
        Assert.True(page1.TotalRecords > 0);
        Assert.Equal(1, page1.CurrentPage);
        Assert.Equal(3, page1.PageSize);
        Assert.Equal(3, page1.Jobs.Count);
        Assert.True(page1.TotalPages >= 2);

        // Act: Page 2 with pageSize 3
        var page2 = await jobService.GetJobListAsync(page: 2, pageSize: 3);

        // Assert: Page 2
        Assert.Equal(2, page2.CurrentPage);
        Assert.Equal(3, page2.Jobs.Count);
        // Ensure different jobs are served across pages
        Assert.NotEqual(page1.Jobs[0].Id, page2.Jobs[0].Id);
    }

    [Fact]
    public async Task QuestionBanksController_GetQuestions_ReturnsPagedResponse()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();

        for (int i = 1; i <= 15; i++)
        {
            db.InterviewQuestionBanks.Add(new InterviewQuestionBank
            {
                Id = Guid.NewGuid(),
                Content = $"Câu hỏi phỏng vấn số {i}",
                Competency = "Kỹ thuật Backend",
                Difficulty = "Trung bình",
                IsActive = true
            });
        }
        await db.SaveChangesAsync();

        var controller = new QuestionBanksController(db);

        // Act: Page 1 with pageSize 5
        var result = await controller.GetQuestions(page: 1, pageSize: 5);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(okResult.Value);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal(15, doc.RootElement.GetProperty("totalRecords").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("currentPage").GetInt32());
        Assert.Equal(5, doc.RootElement.GetProperty("pageSize").GetInt32());
        Assert.Equal(3, doc.RootElement.GetProperty("totalPages").GetInt32());
    }

    [Fact]
    public async Task RecruitmentCatalogsController_GetItems_WithPagination_ReturnsPagedResponse()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        for (int i = 1; i <= 12; i++)
        {
            db.RecruitmentCatalogs.Add(new RecruitmentCatalog
            {
                Id = Guid.NewGuid(),
                CatalogType = "SOURCE",
                Code = $"SRC_{i:D2}",
                Name = $"Nguồn tuyển dụng {i}",
                DisplayOrder = i,
                IsActive = true
            });
        }
        await db.SaveChangesAsync();

        var controller = new RecruitmentCatalogsController(db, _mockCatalogLogger.Object);

        // Act: Request page 2 with pageSize 5
        var result = await controller.GetItems(catalogType: "SOURCE", page: 2, pageSize: 5);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(okResult.Value);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal(12, doc.RootElement.GetProperty("totalRecords").GetInt32());
        Assert.Equal(2, doc.RootElement.GetProperty("currentPage").GetInt32());
        Assert.Equal(5, doc.RootElement.GetProperty("pageSize").GetInt32());
        Assert.Equal(3, doc.RootElement.GetProperty("totalPages").GetInt32());
    }

    [Fact]
    public async Task HomeController_GetCandidatePipeline_ReturnsPagedData()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var dept = new Department { Id = Guid.NewGuid(), Name = "Kỹ thuật", Code = "TECH" };
        var pos = new JobPosition { Id = Guid.NewGuid(), Title = "Software Engineer", Code = "SWE", DepartmentId = dept.Id };
        var creator = new User { Id = Guid.NewGuid(), Email = "creator@novera.vn", FullName = "Hiring Manager" };
        var req = new JobRequisition
        {
            Id = Guid.NewGuid(),
            DepartmentId = dept.Id,
            JobPositionId = pos.Id,
            HiringManagerId = creator.Id,
            Code = "REQ-PAG-01",
            Quantity = 2,
            Status = RequisitionStatus.APPROVED
        };
        var posting = new JobPosting
        {
            Id = Guid.NewGuid(),
            RequisitionId = req.Id,
            Title = "Senior Backend",
            Slug = "senior-backend",
            WorkLocation = "Hà Nội",
            Status = JobPostingStatus.PUBLISHED
        };
        var stage = new PipelineStage { Id = Guid.NewGuid(), Name = "Sàng lọc CV", StageOrder = 2, ColorCode = "#059669" };

        db.Departments.Add(dept);
        db.JobPositions.Add(pos);
        db.Users.Add(creator);
        db.JobRequisitions.Add(req);
        db.JobPostings.Add(posting);
        db.PipelineStages.Add(stage);

        for (int i = 1; i <= 18; i++)
        {
            var cand = new Candidate
            {
                Id = Guid.NewGuid(),
                FirstName = "Ứng viên",
                LastName = $"Số {i:D2}",
                Email = $"candidate{i}@novera.vn"
            };
            var resume = new Resume
            {
                Id = Guid.NewGuid(),
                CandidateId = cand.Id,
                FileName = $"cv_{i}.pdf",
                FilePath = $"/uploads/cv_{i}.pdf",
                IsPrimary = true
            };
            db.Candidates.Add(cand);
            db.Resumes.Add(resume);

            db.Applications.Add(new Application
            {
                Id = Guid.NewGuid(),
                CandidateId = cand.Id,
                ResumeId = resume.Id,
                JobPostingId = posting.Id,
                CurrentStageId = stage.Id,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(i),
                AppliedAt = DateTimeOffset.UtcNow.AddMinutes(i)
            });
        }
        await db.SaveChangesAsync();

        var mockLogger = new Mock<ILogger<HomeController>>();
        var mockJobService = new Mock<IJobService>();
        var controller = new HomeController(db, mockJobService.Object, mockLogger.Object);

        // Act: Page 2, pageSize 10
        var result = await controller.GetCandidatePipeline(search: null, stageOrder: "2", page: 2, pageSize: 10);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(okResult.Value);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("isSuccess").GetBoolean());
        Assert.Equal(18, doc.RootElement.GetProperty("totalRecords").GetInt32());
        Assert.Equal(2, doc.RootElement.GetProperty("currentPage").GetInt32());
        Assert.Equal(10, doc.RootElement.GetProperty("pageSize").GetInt32());
        Assert.Equal(2, doc.RootElement.GetProperty("totalPages").GetInt32());
    }
}
