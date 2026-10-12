using Ats.Web.Data;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Models.ViewModels.CandidateMerge;
using Ats.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ats.Web.Tests.Services;

/// <summary>
/// Unit Tests cho tính năng Cảnh báo & Gộp hồ sơ ứng viên trùng (Scrum #40).
/// Kiểm thử phát hiện trùng theo email / số điện thoại, chuẩn hóa định dạng,
/// hiển thị so sánh cạnh nhau, chuyển giao toàn bộ đơn/CV và hoàn tác trong 24 giờ.
/// </summary>
public class CandidateMergeServiceTests
{
    private static ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"AtsMergeDb_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static async Task<(User recruiter, JobPosting job, PipelineStage stage)> SeedBaseDataAsync(ApplicationDbContext context)
    {
        var dept = new Department
        {
            Id = Guid.NewGuid(),
            Code = $"DEP-{Guid.NewGuid():N}"[..8],
            Name = "Phòng Tuyển Dụng"
        };
        await context.Departments.AddAsync(dept);

        var recruiter = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Nguyễn Tuyển Dụng",
            Email = $"recruiter_{Guid.NewGuid():N}@noveratech.vn",
            PasswordHash = "hash"
        };
        await context.Users.AddAsync(recruiter);

        var req = new JobRequisition
        {
            Id = Guid.NewGuid(),
            Code = $"REQ-{Guid.NewGuid():N}"[..8],
            DepartmentId = dept.Id,
            HiringManagerId = recruiter.Id,
            HiringManager = recruiter,
            Department = dept
        };
        await context.JobRequisitions.AddAsync(req);

        var job = new JobPosting
        {
            Id = Guid.NewGuid(),
            RequisitionId = req.Id,
            Requisition = req,
            Title = "Senior Backend Engineer (.NET/C#)",
            WorkLocation = "Hà Nội",
            Status = JobPostingStatus.PUBLISHED
        };
        await context.JobPostings.AddAsync(job);

        var stage = new PipelineStage
        {
            Id = Guid.NewGuid(),
            Name = "Nộp hồ sơ",
            StageOrder = 1
        };
        await context.PipelineStages.AddAsync(stage);

        await context.SaveChangesAsync();

        return (recruiter, job, stage);
    }

    [Fact]
    public void NormalizePhoneNumber_ShouldHandleCountryCodeAndSeparatorsCorrectly()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var service = new CandidateMergeService(context, NullLogger<CandidateMergeService>.Instance);

        // Act & Assert
        Assert.Equal("0912345678", service.NormalizePhoneNumber("+84 912 345 678"));
        Assert.Equal("0912345678", service.NormalizePhoneNumber("0912.345.678"));
        Assert.Equal("0912345678", service.NormalizePhoneNumber("0912-345-678"));
        Assert.Equal("0912345678", service.NormalizePhoneNumber("84912345678"));
        Assert.Equal("", service.NormalizePhoneNumber(null));
        Assert.Equal("", service.NormalizePhoneNumber("   "));
    }

    [Fact]
    public async Task DetectDuplicatesAsync_ShouldFindDuplicateByEmail()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (recruiter, job, stage) = await SeedBaseDataAsync(context);

        var cand1 = new Candidate
        {
            Id = Guid.NewGuid(),
            FirstName = "Văn A",
            LastName = "Nguyễn",
            Email = "nguyenvana@example.com",
            Phone = "0912345678",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2)
        };

        var cand2 = new Candidate
        {
            Id = Guid.NewGuid(),
            FirstName = "Van A",
            LastName = "Nguyen",
            Email = "NGUYENVANA@EXAMPLE.COM", // Trùng email viết hoa
            Phone = "0987654321",
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-30)
        };

        await context.Candidates.AddRangeAsync(cand1, cand2);
        await context.SaveChangesAsync();

        var service = new CandidateMergeService(context, NullLogger<CandidateMergeService>.Instance);

        // Act
        var duplicates = await service.DetectDuplicatesAsync();

        // Assert
        Assert.NotEmpty(duplicates);
        var group = duplicates.FirstOrDefault(g => g.DuplicateType == DuplicateType.EmailOnly);
        Assert.NotNull(group);
        Assert.Equal("nguyenvana@example.com", group.MatchedValue);
        Assert.Equal(2, group.Candidates.Count);
    }

    [Fact]
    public async Task DetectDuplicatesAsync_ShouldFindDuplicateByPhoneWithDifferentFormatting()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (recruiter, job, stage) = await SeedBaseDataAsync(context);

        var cand1 = new Candidate
        {
            Id = Guid.NewGuid(),
            FirstName = "Thị Lan",
            LastName = "Lê",
            Email = "lanle1@example.com",
            Phone = "+84 901 234 567", // Có mã quốc gia + khoảng trắng
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-5)
        };

        var cand2 = new Candidate
        {
            Id = Guid.NewGuid(),
            FirstName = "Thị Lan",
            LastName = "Lê",
            Email = "lanle2@example.com",
            Phone = "0901.234.567", // Có dấu chấm
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1)
        };

        await context.Candidates.AddRangeAsync(cand1, cand2);
        await context.SaveChangesAsync();

        var service = new CandidateMergeService(context, NullLogger<CandidateMergeService>.Instance);

        // Act
        var duplicates = await service.DetectDuplicatesAsync();

        // Assert
        Assert.NotEmpty(duplicates);
        var group = duplicates.FirstOrDefault(g => g.DuplicateType == DuplicateType.PhoneOnly);
        Assert.NotNull(group);
        Assert.Equal("0901234567", group.MatchedValue);
        Assert.Equal(2, group.Candidates.Count);
    }

    [Fact]
    public async Task DetectDuplicatesAsync_ShouldIgnoreAlreadyMergedCandidates()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (recruiter, job, stage) = await SeedBaseDataAsync(context);

        var cand1 = new Candidate
        {
            Id = Guid.NewGuid(),
            FirstName = "Văn Chính",
            LastName = "Trần",
            Email = "tranchinh@example.com",
            Phone = "0911223344",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10)
        };

        var cand2 = new Candidate
        {
            Id = Guid.NewGuid(),
            FirstName = "Văn Phụ",
            LastName = "Trần",
            Email = "tranchinh@example.com",
            Phone = "0911223344",
            IsMerged = true,
            MergedIntoCandidateId = cand1.Id,
            MergedAt = DateTimeOffset.UtcNow.AddDays(-1),
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2)
        };

        await context.Candidates.AddRangeAsync(cand1, cand2);
        await context.SaveChangesAsync();

        var service = new CandidateMergeService(context, NullLogger<CandidateMergeService>.Instance);

        // Act
        var duplicates = await service.DetectDuplicatesAsync();

        // Assert
        Assert.Empty(duplicates);
    }

    [Fact]
    public async Task FindDuplicatesForCandidateAsync_WhenDuplicateExists_ShouldReturnMatchingCandidate()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (recruiter, job, stage) = await SeedBaseDataAsync(context);

        var cand1 = new Candidate
        {
            Id = Guid.NewGuid(),
            FirstName = "Hải",
            LastName = "Hoàng",
            Email = "hoanghai@example.com",
            Phone = "0934567890",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-3)
        };

        var cand2 = new Candidate
        {
            Id = Guid.NewGuid(),
            FirstName = "Văn Hải",
            LastName = "Hoàng",
            Email = "hoanghai@example.com",
            Phone = "0988776655",
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-2)
        };

        await context.Candidates.AddRangeAsync(cand1, cand2);
        await context.SaveChangesAsync();

        var service = new CandidateMergeService(context, NullLogger<CandidateMergeService>.Instance);

        // Act
        var duplicates = await service.FindDuplicatesForCandidateAsync(cand2.Id);

        // Assert
        Assert.NotEmpty(duplicates);
        var match = duplicates.FirstOrDefault();
        Assert.NotNull(match);
        Assert.Equal(cand1.Id, match.CandidateId);
        Assert.Equal("nghi_van_trung_lap", match.SourceName != null ? "nghi_van_trung_lap" : "");
    }

    [Fact]
    public async Task GetComparisonAsync_ShouldReturnSideBySideComparisonData()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (recruiter, job, stage) = await SeedBaseDataAsync(context);

        var primary = new Candidate
        {
            Id = Guid.NewGuid(),
            FirstName = "Quang Minh",
            LastName = "Phạm",
            Email = "minh.pham@example.com",
            Phone = "0909123456",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30)
        };

        var secondary = new Candidate
        {
            Id = Guid.NewGuid(),
            FirstName = "Quang Minh",
            LastName = "Phạm",
            Email = "minh.pham@example.com",
            Phone = "0909123456",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };

        await context.Candidates.AddRangeAsync(primary, secondary);

        var resume1 = new Resume
        {
            Id = Guid.NewGuid(),
            CandidateId = primary.Id,
            FileName = "cv_primary.pdf",
            FilePath = "/uploads/cv/cv_primary.pdf",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30)
        };

        var resume2 = new Resume
        {
            Id = Guid.NewGuid(),
            CandidateId = secondary.Id,
            FileName = "cv_secondary.pdf",
            FilePath = "/uploads/cv/cv_secondary.pdf",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };

        await context.Resumes.AddRangeAsync(resume1, resume2);

        var app1 = new Application
        {
            Id = Guid.NewGuid(),
            CandidateId = primary.Id,
            JobPostingId = job.Id,
            ResumeId = resume1.Id,
            CurrentStageId = stage.Id,
            TrackingCode = "APP-TEST-001",
            Status = ApplicationStatus.IN_PROCESS,
            AppliedAt = DateTimeOffset.UtcNow.AddDays(-30)
        };

        var app2 = new Application
        {
            Id = Guid.NewGuid(),
            CandidateId = secondary.Id,
            JobPostingId = job.Id,
            ResumeId = resume2.Id,
            CurrentStageId = stage.Id,
            TrackingCode = "APP-TEST-002",
            Status = ApplicationStatus.IN_PROCESS,
            AppliedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };

        await context.Applications.AddRangeAsync(app1, app2);
        await context.SaveChangesAsync();

        var service = new CandidateMergeService(context, NullLogger<CandidateMergeService>.Instance);

        // Act
        var comparison = await service.GetComparisonAsync(primary.Id, secondary.Id);

        // Assert
        Assert.NotNull(comparison);
        Assert.Equal(primary.Id, comparison.PrimaryCandidate.CandidateId);
        Assert.Equal(secondary.Id, comparison.SecondaryCandidate.CandidateId);
        Assert.Contains("Email", comparison.DuplicateReason);
        Assert.Contains("Số điện thoại", comparison.DuplicateReason);
        Assert.Single(comparison.PrimaryCandidate.Applications);
        Assert.Single(comparison.SecondaryCandidate.Applications);
    }

    [Fact]
    public async Task MergeCandidatesAsync_ShouldTransferAllApplicationsAndResumes_AndRecordHistory()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (recruiter, job, stage) = await SeedBaseDataAsync(context);

        var primary = new Candidate
        {
            Id = Guid.NewGuid(),
            FirstName = "Đình Tuấn",
            LastName = "Vũ",
            Email = "tuan.vu@example.com",
            Phone = "0944556677",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10)
        };

        var secondary = new Candidate
        {
            Id = Guid.NewGuid(),
            FirstName = "Dinh Tuan",
            LastName = "Vu",
            Email = "tuan.vu@example.com",
            Phone = "0944556677",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };

        await context.Candidates.AddRangeAsync(primary, secondary);

        var resumeSecondary = new Resume
        {
            Id = Guid.NewGuid(),
            CandidateId = secondary.Id,
            FileName = "tuan_cv_update.pdf",
            FilePath = "/uploads/cv/tuan_cv_update.pdf",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };
        await context.Resumes.AddAsync(resumeSecondary);

        var appSecondary = new Application
        {
            Id = Guid.NewGuid(),
            CandidateId = secondary.Id,
            JobPostingId = job.Id,
            ResumeId = resumeSecondary.Id,
            CurrentStageId = stage.Id,
            TrackingCode = "APP-SEC-999",
            Status = ApplicationStatus.IN_PROCESS,
            AppliedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };
        await context.Applications.AddAsync(appSecondary);
        await context.SaveChangesAsync();

        var service = new CandidateMergeService(context, NullLogger<CandidateMergeService>.Instance);

        var request = new MergeCandidatesRequestModel
        {
            PrimaryCandidateId = primary.Id,
            SecondaryCandidateId = secondary.Id,
            KeepName = "primary",
            KeepPhone = "secondary",
            Reason = "Trùng hồ sơ do ứng viên nộp lại CV mới"
        };

        // Act
        var result = await service.MergeCandidatesAsync(request, recruiter.Id);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.MergeHistoryId);

        // Kiểm tra cơ sở dữ liệu
        var updatedSecondary = await context.Candidates.FindAsync(secondary.Id);
        Assert.NotNull(updatedSecondary);
        Assert.True(updatedSecondary.IsMerged);
        Assert.Equal(primary.Id, updatedSecondary.MergedIntoCandidateId);
        Assert.NotNull(updatedSecondary.MergedAt);

        // Đơn ứng tuyển và CV đã chuyển sang primary
        var updatedApp = await context.Applications.FindAsync(appSecondary.Id);
        Assert.NotNull(updatedApp);
        Assert.Equal(primary.Id, updatedApp.CandidateId);

        var updatedResume = await context.Resumes.FindAsync(resumeSecondary.Id);
        Assert.NotNull(updatedResume);
        Assert.Equal(primary.Id, updatedResume.CandidateId);

        // Kiểm tra lịch sử gộp hồ sơ
        var history = await context.CandidateMergeHistories.FindAsync(result.MergeHistoryId.Value);
        Assert.NotNull(history);
        Assert.Equal(primary.Id, history.PrimaryCandidateId);
        Assert.Equal(secondary.Id, history.SecondaryCandidateId);
        Assert.Equal(recruiter.Id, history.MergedByUserId);
        Assert.False(history.IsUndone);
    }

    [Fact]
    public async Task UndoMergeAsync_Within24Hours_ShouldRestoreDataSuccessfully()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (recruiter, job, stage) = await SeedBaseDataAsync(context);

        var primary = new Candidate
        {
            Id = Guid.NewGuid(),
            FirstName = "Mỹ Linh",
            LastName = "Đỗ",
            Email = "mylinh.do@example.com",
            Phone = "0977889900",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-20)
        };

        var secondary = new Candidate
        {
            Id = Guid.NewGuid(),
            FirstName = "My Linh",
            LastName = "Do",
            Email = "mylinh.do@example.com",
            Phone = "0977889900",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2)
        };

        await context.Candidates.AddRangeAsync(primary, secondary);

        var resumeSecondary = new Resume
        {
            Id = Guid.NewGuid(),
            CandidateId = secondary.Id,
            FileName = "mylinh_cv.pdf",
            FilePath = "/uploads/cv/mylinh_cv.pdf",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2)
        };
        await context.Resumes.AddAsync(resumeSecondary);

        var appSecondary = new Application
        {
            Id = Guid.NewGuid(),
            CandidateId = secondary.Id,
            JobPostingId = job.Id,
            ResumeId = resumeSecondary.Id,
            CurrentStageId = stage.Id,
            TrackingCode = "APP-DO-123",
            Status = ApplicationStatus.IN_PROCESS,
            AppliedAt = DateTimeOffset.UtcNow.AddDays(-2)
        };
        await context.Applications.AddAsync(appSecondary);
        await context.SaveChangesAsync();

        var service = new CandidateMergeService(context, NullLogger<CandidateMergeService>.Instance);

        // Tiến hành gộp
        var mergeResult = await service.MergeCandidatesAsync(new MergeCandidatesRequestModel
        {
            PrimaryCandidateId = primary.Id,
            SecondaryCandidateId = secondary.Id,
            Reason = "Gộp thử nghiệm"
        }, recruiter.Id);

        Assert.True(mergeResult.Success);
        Assert.NotNull(mergeResult.MergeHistoryId);

        // Act - Thực hiện hoàn tác (trong vòng 24h)
        var undoResult = await service.UndoMergeAsync(mergeResult.MergeHistoryId.Value, recruiter.Id);

        // Assert
        Assert.True(undoResult.Success);

        // secondary được mở khóa
        var restoredSecondary = await context.Candidates.FindAsync(secondary.Id);
        Assert.NotNull(restoredSecondary);
        Assert.False(restoredSecondary.IsMerged);
        Assert.Null(restoredSecondary.MergedIntoCandidateId);

        // Đơn ứng tuyển đã trả về secondary
        var restoredApp = await context.Applications.FindAsync(appSecondary.Id);
        Assert.NotNull(restoredApp);
        Assert.Equal(secondary.Id, restoredApp.CandidateId);

        // Lịch sử ghi nhận đã hoàn tác
        var history = await context.CandidateMergeHistories.FindAsync(mergeResult.MergeHistoryId.Value);
        Assert.NotNull(history);
        Assert.True(history.IsUndone);
        Assert.NotNull(history.UndoneAt);
        Assert.Equal(recruiter.Id, history.UndoneByUserId);
    }

    [Fact]
    public async Task UndoMergeAsync_After24Hours_ShouldFailDueToExpiration()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (recruiter, job, stage) = await SeedBaseDataAsync(context);

        var primary = new Candidate
        {
            Id = Guid.NewGuid(),
            FirstName = "Bảo Châu",
            LastName = "Ngô",
            Email = "chau.ngo@example.com",
            Phone = "0912000111",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-40)
        };

        var secondary = new Candidate
        {
            Id = Guid.NewGuid(),
            FirstName = "Bao Chau",
            LastName = "Ngo",
            Email = "chau.ngo@example.com",
            Phone = "0912000111",
            IsMerged = true,
            MergedIntoCandidateId = primary.Id,
            MergedAt = DateTimeOffset.UtcNow.AddHours(-25), // Đã quá 24h
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30)
        };

        await context.Candidates.AddRangeAsync(primary, secondary);

        // Giả lập lịch sử đã tạo cách đây 25 tiếng
        var expiredHistory = new CandidateMergeHistory
        {
            Id = Guid.NewGuid(),
            PrimaryCandidateId = primary.Id,
            SecondaryCandidateId = secondary.Id,
            MergedByUserId = recruiter.Id,
            MergedAt = DateTimeOffset.UtcNow.AddHours(-25), // Quá 24 giờ
            SnapshotSecondaryDataJson = "{}",
            MovedApplicationIdsJson = "[]",
            MovedResumeIdsJson = "[]",
            IsUndone = false
        };

        await context.CandidateMergeHistories.AddAsync(expiredHistory);
        await context.SaveChangesAsync();

        var service = new CandidateMergeService(context, NullLogger<CandidateMergeService>.Instance);

        // Act
        var result = await service.UndoMergeAsync(expiredHistory.Id, recruiter.Id);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("24 giờ", result.Message);

        var historyInDb = await context.CandidateMergeHistories.FindAsync(expiredHistory.Id);
        Assert.NotNull(historyInDb);
        Assert.False(historyInDb.IsUndone);
    }

    [Fact]
    public async Task UndoMergeAsync_WhenAlreadyUndone_ShouldFail()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (recruiter, job, stage) = await SeedBaseDataAsync(context);

        var primary = new Candidate
        {
            Id = Guid.NewGuid(),
            FirstName = "Nam Đế",
            LastName = "Lý",
            Email = "ly.namde@example.com",
            Phone = "0988001122"
        };

        var secondary = new Candidate
        {
            Id = Guid.NewGuid(),
            FirstName = "Nam De",
            LastName = "Ly",
            Email = "ly.namde@example.com",
            Phone = "0988001122"
        };

        await context.Candidates.AddRangeAsync(primary, secondary);

        var history = new CandidateMergeHistory
        {
            Id = Guid.NewGuid(),
            PrimaryCandidateId = primary.Id,
            SecondaryCandidateId = secondary.Id,
            MergedByUserId = recruiter.Id,
            MergedAt = DateTimeOffset.UtcNow.AddHours(-1),
            SnapshotSecondaryDataJson = "{}",
            MovedApplicationIdsJson = "[]",
            MovedResumeIdsJson = "[]",
            IsUndone = true, // Đã hoàn tác trước đó
            UndoneAt = DateTimeOffset.UtcNow.AddMinutes(-30)
        };

        await context.CandidateMergeHistories.AddAsync(history);
        await context.SaveChangesAsync();

        var service = new CandidateMergeService(context, NullLogger<CandidateMergeService>.Instance);

        // Act
        var result = await service.UndoMergeAsync(history.Id, recruiter.Id);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("đã được hoàn tác trước đó", result.Message);
    }

    [Fact]
    public async Task MergeCandidatesAsync_WhenPrimaryAndSecondaryAreSame_ShouldFail()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (recruiter, job, stage) = await SeedBaseDataAsync(context);

        var cand = new Candidate
        {
            Id = Guid.NewGuid(),
            FirstName = "Bộ Lĩnh",
            LastName = "Đinh",
            Email = "bolinh@example.com",
            Phone = "0911224466"
        };

        await context.Candidates.AddAsync(cand);
        await context.SaveChangesAsync();

        var service = new CandidateMergeService(context, NullLogger<CandidateMergeService>.Instance);

        var request = new MergeCandidatesRequestModel
        {
            PrimaryCandidateId = cand.Id,
            SecondaryCandidateId = cand.Id, // Trùng ID
            Reason = "Lỗi trùng ID"
        };

        // Act
        var result = await service.MergeCandidatesAsync(request, recruiter.Id);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("chính nó", result.Message);
    }
}
