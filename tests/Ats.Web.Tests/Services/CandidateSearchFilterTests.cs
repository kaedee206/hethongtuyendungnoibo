using Ats.Web.Constants;
using Ats.Web.Data;
using Ats.Web.Models.DTOs;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Services;
using Ats.Web.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ats.Web.Tests.Services;

/// <summary>
/// Unit Tests cho tính năng Tìm kiếm & Lọc hồ sơ ứng viên theo nhiều điều kiện (Scrum #44).
/// Kiểm thử lọc theo vị trí, giai đoạn, nguồn, nhãn, khoảng thời gian nộp (preset tháng trước),
/// mức đánh giá, tìm kiếm tên/email/sđt/từ khóa thư giới thiệu và lưu/xóa bộ lọc hay dùng.
/// </summary>
public class CandidateSearchFilterTests
{
    private class FakeSecurityAuditService : ISecurityAuditService
    {
        public void LogSecurityEvent(SecurityAuditEvent auditEvent) { }
    }

    private class FakeFileStorageService : IFileStorageService
    {
        public Task<string> SaveCvAsync(IFormFile file, string? prefix = null, CancellationToken cancellationToken = default) => Task.FromResult("/uploads/cv/test.pdf");
        public Task<string> SaveMediaAsync(IFormFile file, string? prefix = null, CancellationToken cancellationToken = default) => Task.FromResult("/uploads/media/test.jpg");
        public Task<bool> DeleteFileAsync(string? relativePath) => Task.FromResult(true);
        public string GetPhysicalPath(string relativePath) => relativePath;
        public bool FileExists(string? relativePath) => true;
        public void EnsureStorageDirectories() { }
    }

    private class FakeApplicationTrackingService : IApplicationTrackingService
    {
        public Task<Ats.Web.Models.ViewModels.Tracking.ApplicationTrackingResultViewModel> TrackApplicationAsync(
            Ats.Web.Models.ViewModels.Tracking.ApplicationTrackingInputModel input, CancellationToken cancellationToken = default) =>
            Task.FromResult(new Ats.Web.Models.ViewModels.Tracking.ApplicationTrackingResultViewModel());

        public Task<string> GenerateUniqueTrackingCodeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult("NOV-APP-TEST");
    }

    private static ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"AtsFilterTestDb_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static async Task<(Guid jobNetId, Guid jobReactId, Guid recruiterId)> SeedCandidateDatasetAsync(ApplicationDbContext context)
    {
        var dept = new Department
        {
            Id = Guid.NewGuid(),
            Code = "DEP-IT",
            Name = "Phòng Công Nghệ Thông Tin"
        };
        await context.Departments.AddAsync(dept);

        var recruiter = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Nguyễn Tuyển Dụng",
            Email = "recruiter@noveratech.vn",
            PasswordHash = "hash"
        };
        await context.Users.AddAsync(recruiter);

        var req1 = new JobRequisition
        {
            Id = Guid.NewGuid(),
            Code = "REQ-NET",
            DepartmentId = dept.Id,
            HiringManagerId = recruiter.Id,
            Department = dept
        };
        var req2 = new JobRequisition
        {
            Id = Guid.NewGuid(),
            Code = "REQ-FE",
            DepartmentId = dept.Id,
            HiringManagerId = recruiter.Id,
            Department = dept
        };
        await context.JobRequisitions.AddRangeAsync(req1, req2);

        var jobNet = new JobPosting
        {
            Id = Guid.NewGuid(),
            Code = "JOB-NET",
            Title = "Senior .NET Core Engineer",
            RequisitionId = req1.Id,
            Requisition = req1,
            Status = JobPostingStatus.PUBLISHED,
            PublishedAt = DateTimeOffset.UtcNow.AddMonths(-2)
        };
        var jobReact = new JobPosting
        {
            Id = Guid.NewGuid(),
            Code = "JOB-REACT",
            Title = "Frontend React Developer",
            RequisitionId = req2.Id,
            Requisition = req2,
            Status = JobPostingStatus.PUBLISHED,
            PublishedAt = DateTimeOffset.UtcNow.AddMonths(-1)
        };
        await context.JobPostings.AddRangeAsync(jobNet, jobReact);

        var stageScreening = new PipelineStage
        {
            Id = Guid.NewGuid(),
            Name = "Sàng lọc hồ sơ",
            StageOrder = 1,
            ColorCode = "#059669"
        };
        var stageInterview = new PipelineStage
        {
            Id = Guid.NewGuid(),
            Name = "Phỏng vấn kỹ thuật",
            StageOrder = 2,
            ColorCode = "#2563EB"
        };
        var stageOffer = new PipelineStage
        {
            Id = Guid.NewGuid(),
            Name = "Đề nghị tuyển dụng",
            StageOrder = 3,
            ColorCode = "#D97706"
        };
        await context.PipelineStages.AddRangeAsync(stageScreening, stageInterview, stageOffer);

        var seniorTag = new RecruitmentCatalog { Id = Guid.NewGuid(), CatalogType = "CANDIDATE_TAG", Code = "SENIOR", Name = "Senior", ColorCode = "#059669" };
        var dotnetTag = new RecruitmentCatalog { Id = Guid.NewGuid(), CatalogType = "CANDIDATE_TAG", Code = "DOTNET", Name = "DotNet", ColorCode = "#2563EB" };
        var reactTag = new RecruitmentCatalog { Id = Guid.NewGuid(), CatalogType = "CANDIDATE_TAG", Code = "REACT", Name = "React", ColorCode = "#0284C7" };
        var devopsTag = new RecruitmentCatalog { Id = Guid.NewGuid(), CatalogType = "CANDIDATE_TAG", Code = "DEVOPS", Name = "DevOps", ColorCode = "#D97706" };
        await context.RecruitmentCatalogs.AddRangeAsync(seniorTag, dotnetTag, reactTag, devopsTag);

        // Mốc thời gian tháng trước (UTC+7)
        var nowVn = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7));
        var currentMonthFirstDay = new DateTime(nowVn.Year, nowVn.Month, 1);
        var lastMonthFirstDay = currentMonthFirstDay.AddMonths(-1);
        var lastMonthSampleDate = new DateTimeOffset(lastMonthFirstDay.AddDays(5), TimeSpan.FromHours(7)).ToUniversalTime();

        // Ứng viên 1: Nộp tháng trước, Vị trí .NET, Stage Screening, Source Portal, Tag Senior/.NET, Rating 5, CoverLetter Microservices
        var cand1 = new Candidate
        {
            Id = Guid.NewGuid(),
            FirstName = "Văn An",
            LastName = "Nguyễn",
            Email = "an.nguyen@noveratech.vn",
            Phone = "0911223344",
            CurrentTitle = "Senior Backend",
            Source = CandidateSource.PORTAL,
            CreatedAt = lastMonthSampleDate,
            UpdatedAt = lastMonthSampleDate
        };
        cand1.Tags.Add(new CandidateTag { Id = Guid.NewGuid(), CatalogId = seniorTag.Id, Catalog = seniorTag });
        cand1.Tags.Add(new CandidateTag { Id = Guid.NewGuid(), CatalogId = dotnetTag.Id, Catalog = dotnetTag });
        var app1 = new Application
        {
            Id = Guid.NewGuid(),
            CandidateId = cand1.Id,
            JobPostingId = jobNet.Id,
            CurrentStageId = stageScreening.Id,
            Stage = CandidateStage.SCREENING,
            Rating = 5,
            CoverLetter = "Tôi có hơn 6 năm kinh nghiệm thiết kế kiến trúc Microservices và tối ưu C# .NET",
            AppliedAt = lastMonthSampleDate,
            CreatedAt = lastMonthSampleDate
        };
        cand1.Applications.Add(app1);

        // Ứng viên 2: Nộp hôm nay (tháng này), Vị trí React, Stage Interview, Source LinkedIn, Tag React/UI, Rating 4, CoverLetter Tailwind
        var cand2 = new Candidate
        {
            Id = Guid.NewGuid(),
            FirstName = "Thị Bình",
            LastName = "Trần",
            Email = "binh.tran@noveratech.vn",
            Phone = "0988776655",
            CurrentTitle = "Frontend Developer",
            Source = CandidateSource.LINKEDIN,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        cand2.Tags.Add(new CandidateTag { Id = Guid.NewGuid(), CatalogId = reactTag.Id, Catalog = reactTag });
        var app2 = new Application
        {
            Id = Guid.NewGuid(),
            CandidateId = cand2.Id,
            JobPostingId = jobReact.Id,
            CurrentStageId = stageInterview.Id,
            Stage = CandidateStage.INTERVIEW,
            Rating = 4,
            CoverLetter = "Tôi am hiểu sâu sắc về Redux, Tailwind CSS và tối ưu trải nghiệm người dùng",
            AppliedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
        cand2.Applications.Add(app2);

        // Ứng viên 3: Nộp 60 ngày trước, Vị trí .NET, Stage Offer, Source Referral, Tag DevOps, Rating 2
        var cand3 = new Candidate
        {
            Id = Guid.NewGuid(),
            FirstName = "Hoàng Cường",
            LastName = "Lê",
            Email = "cuong.le@noveratech.vn",
            Phone = "0900112233",
            CurrentTitle = "DevOps Specialist",
            Source = CandidateSource.REFERRAL,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-60),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-60)
        };
        cand3.Tags.Add(new CandidateTag { Id = Guid.NewGuid(), CatalogId = devopsTag.Id, Catalog = devopsTag });
        var app3 = new Application
        {
            Id = Guid.NewGuid(),
            CandidateId = cand3.Id,
            JobPostingId = jobNet.Id,
            CurrentStageId = stageOffer.Id,
            Stage = CandidateStage.OFFER,
            Rating = 2,
            CoverLetter = "Em được anh Tuấn trưởng phòng giới thiệu ứng tuyển hệ thống CI/CD",
            AppliedAt = DateTimeOffset.UtcNow.AddDays(-60),
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-60)
        };
        cand3.Applications.Add(app3);

        await context.Candidates.AddRangeAsync(cand1, cand2, cand3);
        await context.SaveChangesAsync();

        return (jobNet.Id, jobReact.Id, recruiter.Id);
    }

    private static CandidateService CreateService(ApplicationDbContext context)
    {
        return new CandidateService(
            context,
            new FakeFileStorageService(),
            new FakeSecurityAuditService(),
            new FakeApplicationTrackingService(),
            NullLogger<CandidateService>.Instance);
    }

    [Fact]
    public async Task FilterByJobPosting_ReturnsMatchingCandidates()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (jobNetId, jobReactId, recruiterId) = await SeedCandidateDatasetAsync(context);
        var service = CreateService(context);

        var criteria = new CandidateFilterCriteria
        {
            JobPostingId = jobNetId
        };

        // Act
        var result = await service.GetFilteredCandidatesAsync(criteria, recruiterId, UserRoles.Recruiter);

        // Assert: Job .NET có ứng viên 1 và ứng viên 3
        Assert.Equal(2, result.PagedResult.TotalCount);
        Assert.Contains(result.Candidates, c => c.Email == "an.nguyen@noveratech.vn");
        Assert.Contains(result.Candidates, c => c.Email == "cuong.le@noveratech.vn");
        Assert.DoesNotContain(result.Candidates, c => c.Email == "binh.tran@noveratech.vn");
    }

    [Fact]
    public async Task FilterByStage_ReturnsMatchingCandidates()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (_, _, recruiterId) = await SeedCandidateDatasetAsync(context);
        var service = CreateService(context);

        var criteria = new CandidateFilterCriteria
        {
            Stage = CandidateStage.INTERVIEW
        };

        // Act
        var result = await service.GetFilteredCandidatesAsync(criteria, recruiterId, UserRoles.Recruiter);

        // Assert: Chỉ có ứng viên 2 ở giai đoạn INTERVIEW
        Assert.Single(result.Candidates);
        Assert.Equal("binh.tran@noveratech.vn", result.Candidates[0].Email);
    }

    [Fact]
    public async Task FilterBySource_ReturnsMatchingCandidates()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (_, _, recruiterId) = await SeedCandidateDatasetAsync(context);
        var service = CreateService(context);

        var criteria = new CandidateFilterCriteria
        {
            Source = CandidateSource.REFERRAL
        };

        // Act
        var result = await service.GetFilteredCandidatesAsync(criteria, recruiterId, UserRoles.Recruiter);

        // Assert: Chỉ có ứng viên 3 nguồn REFERRAL
        Assert.Single(result.Candidates);
        Assert.Equal("cuong.le@noveratech.vn", result.Candidates[0].Email);
    }

    [Fact]
    public async Task FilterByTag_ReturnsMatchingCandidates()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (_, _, recruiterId) = await SeedCandidateDatasetAsync(context);
        var service = CreateService(context);

        var criteria = new CandidateFilterCriteria
        {
            Tag = "React"
        };

        // Act
        var result = await service.GetFilteredCandidatesAsync(criteria, recruiterId, UserRoles.Recruiter);

        // Assert: Chỉ có ứng viên 2 có nhãn React
        Assert.Single(result.Candidates);
        Assert.Equal("binh.tran@noveratech.vn", result.Candidates[0].Email);
    }

    [Fact]
    public async Task FilterByDatePreset_LastMonth_ReturnsCandidatesFromPreviousMonth()
    {
        // Arrange (Đáp ứng trực tiếp User Story Scrum #44: tìm lại hồ sơ đã xem tháng trước khi mở vị trí tương tự)
        using var context = CreateInMemoryDbContext();
        var (_, _, recruiterId) = await SeedCandidateDatasetAsync(context);
        var service = CreateService(context);

        var criteria = new CandidateFilterCriteria
        {
            DatePreset = "last_month"
        };

        // Act
        var result = await service.GetFilteredCandidatesAsync(criteria, recruiterId, UserRoles.Recruiter);

        // Assert: Chỉ có ứng viên 1 nộp vào tháng trước
        Assert.Single(result.Candidates);
        Assert.Equal("an.nguyen@noveratech.vn", result.Candidates[0].Email);
        Assert.True(result.LastMonthCandidatesCount >= 1);
    }

    [Fact]
    public async Task AddTag_UsesCatalogAndDoesNotCreateDuplicateAssignments()
    {
        using var context = CreateInMemoryDbContext();
        var (_, _, recruiterId) = await SeedCandidateDatasetAsync(context);
        var candidate = await context.Candidates.FirstAsync(c => c.Email == "an.nguyen@noveratech.vn");
        var catalog = await context.RecruitmentCatalogs.FirstAsync(c => c.Code == "REACT");
        var service = CreateService(context);

        var first = await service.AddTagAsync(candidate.Id, catalog.Id, recruiterId);
        var second = await service.AddTagAsync(candidate.Id, catalog.Id, recruiterId);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("React", second.TagName);
        Assert.Equal(3, await context.CandidateTags.CountAsync(t => t.CandidateId == candidate.Id && !t.IsDeleted));
    }

    [Fact]
    public async Task UpdateApplicationRating_PersistsOnlyValuesFromOneToFive()
    {
        using var context = CreateInMemoryDbContext();
        var (_, _, recruiterId) = await SeedCandidateDatasetAsync(context);
        var application = await context.Applications.FirstAsync();
        var service = CreateService(context);

        var updated = await service.UpdateApplicationRatingAsync(
            application.Id,
            3,
            recruiterId,
            UserRoles.Recruiter);

        Assert.True(updated);
        Assert.Equal(3, (await context.Applications.FindAsync(application.Id))!.Rating);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.UpdateApplicationRatingAsync(application.Id, 6, recruiterId, UserRoles.Recruiter));
    }

    [Fact]
    public async Task CandidateRoleCannotLoadRecruiterDossierOrInternalNotes()
    {
        using var context = CreateInMemoryDbContext();
        var (_, _, recruiterId) = await SeedCandidateDatasetAsync(context);
        var candidateId = await context.Candidates
            .Where(c => c.Email == "an.nguyen@noveratech.vn")
            .Select(c => c.Id)
            .FirstAsync();
        var service = CreateService(context);

        var profile = await service.GetUnifiedProfileAsync(candidateId, recruiterId, UserRoles.Candidate);

        Assert.Null(profile);
    }

    [Fact]
    public async Task FilterByMinRating_ReturnsCandidatesWithAtLeastRating()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (_, _, recruiterId) = await SeedCandidateDatasetAsync(context);
        var service = CreateService(context);

        var criteria = new CandidateFilterCriteria
        {
            MinRating = 4 // Từ 4 sao trở lên (ứng viên 1 có 5 sao, ứng viên 2 có 4 sao)
        };

        // Act
        var result = await service.GetFilteredCandidatesAsync(criteria, recruiterId, UserRoles.Recruiter);

        // Assert
        Assert.Equal(2, result.PagedResult.TotalCount);
        Assert.Contains(result.Candidates, c => c.Email == "an.nguyen@noveratech.vn");
        Assert.Contains(result.Candidates, c => c.Email == "binh.tran@noveratech.vn");
        Assert.DoesNotContain(result.Candidates, c => c.Email == "cuong.le@noveratech.vn");
    }

    [Fact]
    public async Task SearchByGeneralTerm_MatchesNameEmailPhone()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (_, _, recruiterId) = await SeedCandidateDatasetAsync(context);
        var service = CreateService(context);

        // Tìm theo số điện thoại
        var criteriaPhone = new CandidateFilterCriteria { Search = "0988776655" };
        var resultPhone = await service.GetFilteredCandidatesAsync(criteriaPhone, recruiterId, UserRoles.Recruiter);
        Assert.Single(resultPhone.Candidates);
        Assert.Equal("binh.tran@noveratech.vn", resultPhone.Candidates[0].Email);

        // Tìm theo một phần họ tên
        var criteriaName = new CandidateFilterCriteria { Search = "Văn An" };
        var resultName = await service.GetFilteredCandidatesAsync(criteriaName, recruiterId, UserRoles.Recruiter);
        Assert.Single(resultName.Candidates);
        Assert.Equal("an.nguyen@noveratech.vn", resultName.Candidates[0].Email);
    }

    [Fact]
    public async Task SearchByCoverLetterKeyword_MatchesCoverLetterSnippet()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (_, _, recruiterId) = await SeedCandidateDatasetAsync(context);
        var service = CreateService(context);

        var criteria = new CandidateFilterCriteria
        {
            CoverLetterKeyword = "Microservices"
        };

        // Act
        var result = await service.GetFilteredCandidatesAsync(criteria, recruiterId, UserRoles.Recruiter);

        // Assert: Ứng viên 1 có thư giới thiệu chứa từ khóa Microservices
        Assert.Single(result.Candidates);
        Assert.Equal("an.nguyen@noveratech.vn", result.Candidates[0].Email);
        Assert.True(result.Candidates[0].HasCoverLetter);
        Assert.Contains("Microservices", result.Candidates[0].CoverLetterSnippet);
    }

    [Fact]
    public async Task SaveFilter_And_GetSavedFilters_And_DeleteSavedFilter_WorkCorrectly()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (jobNetId, _, recruiterId) = await SeedCandidateDatasetAsync(context);
        var service = CreateService(context);

        var input = new SaveCandidateFilterInputModel
        {
            Name = "Bộ lọc Senior .NET Tháng Trước",
            Description = "Dành cho việc mở lại đợt tuyển Backend",
            IsDefault = true,
            Criteria = new CandidateFilterCriteria
            {
                JobPostingId = jobNetId,
                DatePreset = "last_month",
                MinRating = 4
            }
        };

        // Act 1: Lưu bộ lọc
        var saved = await service.SaveFilterAsync(recruiterId, input);
        Assert.NotEqual(Guid.Empty, saved.Id);
        Assert.Equal("Bộ lọc Senior .NET Tháng Trước", saved.Name);
        Assert.True(saved.IsDefault);

        // Act 2: Lấy danh sách bộ lọc đã lưu
        var list = await service.GetSavedFiltersAsync(recruiterId);
        Assert.Single(list);
        Assert.Equal(saved.Id, list[0].Id);
        Assert.Equal(jobNetId, list[0].Criteria.JobPostingId);
        Assert.Equal("last_month", list[0].Criteria.DatePreset);
        Assert.Equal(4, list[0].Criteria.MinRating);

        // Act 3: Xóa bộ lọc đã lưu
        var deleteResult = await service.DeleteSavedFilterAsync(saved.Id, recruiterId);
        Assert.True(deleteResult);

        var listAfterDelete = await service.GetSavedFiltersAsync(recruiterId);
        Assert.Empty(listAfterDelete);
    }
}
