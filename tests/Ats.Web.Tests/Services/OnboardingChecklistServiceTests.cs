using System.ComponentModel.DataAnnotations;
using Ats.Web.Constants;
using Ats.Web.Data;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Models.ViewModels.Onboarding;
using Ats.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ats.Web.Tests.Services;

/// <summary>
/// Bộ kiểm thử đơn vị cho OnboardingChecklistService (User story SP-7):
/// 1. Khởi tạo checklist từ mẫu theo phòng ban gồm 4 nhóm: ACCOUNT, EQUIPMENT, SEATING, DOCUMENT (AC1).
/// 2. Mỗi mục có người phụ trách và hạn hoàn thành DueDate <= FirstWorkingDate (AC2).
/// 3. Tiến độ hoàn thành hiển thị theo phần trăm tính tập trung tại Service (AC3).
/// 4. Tự động chuyển ứng viên sang "Đã onboard" khi checklist đạt 100% (AC4).
/// 5. Tự động hoàn tác về "Tuyển dụng thành công" khi checklist bị giảm dưới 100% (Revert).
/// 6. Kiểm soát phân quyền: Chỉ HR Manager mới được khởi tạo; chỉ HR Manager và Assignee mới được tick.
/// </summary>
public class OnboardingChecklistServiceTests
{
    private readonly NullLogger<OnboardingChecklistService> _logger = NullLogger<OnboardingChecklistService>.Instance;

    #region Helper Methods

    private static async Task<(Department dept, User hrManager, User itStaff, Candidate candidate, Application application)> SeedBaseDataAsync(
        ApplicationDbContext context,
        CandidateStage candidateStage = CandidateStage.HIRED)
    {
        var dept = new Department
        {
            Id = Guid.NewGuid(),
            Name = "Phòng Kỹ thuật & Công nghệ",
            Code = "TECH",
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.Departments.AddAsync(dept);

        var hrManager = new User
        {
            Id = Guid.NewGuid(),
            Email = "hrmanager@ats.local",
            FullName = "Trần Văn Quản Lý",
            Role = UserRoles.HRManager,
            Status = "ACTIVE",
            DepartmentId = dept.Id,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.Users.AddAsync(hrManager);

        var itStaff = new User
        {
            Id = Guid.NewGuid(),
            Email = "itadmin@ats.local",
            FullName = "Lê Quản Trị IT",
            Role = UserRoles.Interviewer,
            Status = "ACTIVE",
            DepartmentId = dept.Id,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.Users.AddAsync(itStaff);

        var req = new JobRequisition
        {
            Id = Guid.NewGuid(),
            Code = "REQ-TECH-01",
            DepartmentId = dept.Id,
            HiringManagerId = hrManager.Id,
            Status = RequisitionStatus.APPROVED,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.JobRequisitions.AddAsync(req);

        var job = new JobPosting
        {
            Id = Guid.NewGuid(),
            RequisitionId = req.Id,
            Requisition = req,
            Title = "Senior Fullstack Developer",
            Status = JobPostingStatus.PUBLISHED,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.JobPostings.AddAsync(job);

        var hiredStage = new PipelineStage
        {
            Id = Guid.NewGuid(),
            Name = "Tuyển dụng thành công",
            StageOrder = 6,
            ColorCode = "#10B981"
        };
        await context.PipelineStages.AddAsync(hiredStage);

        var onboardedStage = new PipelineStage
        {
            Id = Guid.NewGuid(),
            Name = "Đã onboard",
            StageOrder = 8,
            ColorCode = "#059669"
        };
        await context.PipelineStages.AddAsync(onboardedStage);

        var candidate = new Candidate
        {
            Id = Guid.NewGuid(),
            FirstName = "Văn Toàn",
            LastName = "Nguyễn",
            Email = "toan.nguyen@example.com",
            Phone = "0987654321",
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.Candidates.AddAsync(candidate);

        var application = new Application
        {
            Id = Guid.NewGuid(),
            CandidateId = candidate.Id,
            JobPostingId = job.Id,
            Stage = candidateStage,
            Status = candidateStage == CandidateStage.HIRED ? ApplicationStatus.HIRED : ApplicationStatus.IN_PROCESS,
            CurrentStageId = hiredStage.Id,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.Applications.AddAsync(application);

        await context.SaveChangesAsync();

        return (dept, hrManager, itStaff, candidate, application);
    }

    private static List<CreateOnboardingChecklistItemInputModel> CreateStandardFourCategoryItems(Guid assigneeId, DateOnly joinDate)
    {
        return
        [
            new CreateOnboardingChecklistItemInputModel
            {
                Category = OnboardingCategory.ACCOUNT,
                Title = "Tạo tài khoản Google Workspace và Slack",
                AssignedToUserId = assigneeId,
                DueDate = joinDate.AddDays(-1)
            },
            new CreateOnboardingChecklistItemInputModel
            {
                Category = OnboardingCategory.EQUIPMENT,
                Title = "Cấp phát laptop MacBook Pro M3 và màn hình 27 inch",
                AssignedToUserId = assigneeId,
                DueDate = joinDate.AddDays(-1)
            },
            new CreateOnboardingChecklistItemInputModel
            {
                Category = OnboardingCategory.SEATING,
                Title = "Bố trí bàn làm việc tầng 3 khu vực Kỹ thuật",
                AssignedToUserId = assigneeId,
                DueDate = joinDate.AddDays(-1)
            },
            new CreateOnboardingChecklistItemInputModel
            {
                Category = OnboardingCategory.DOCUMENT,
                Title = "Ký kết hợp đồng lao động chính thức và cam kết bảo mật",
                AssignedToUserId = assigneeId,
                DueDate = joinDate
            }
        ];
    }

    #endregion

    #region AC1: Checklist sinh từ mẫu theo phòng ban gồm 4 nhóm

    [Fact]
    public async Task InitializeChecklistAsync_WithValidFourCategories_ShouldCreateChecklistSuccessfully()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (dept, hrManager, itStaff, _, application) = await SeedBaseDataAsync(context);

        var joinDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));
        var inputModel = new CreateOnboardingChecklistInputModel
        {
            ApplicationId = application.Id,
            DepartmentId = dept.Id,
            FirstWorkingDate = joinDate,
            Items = CreateStandardFourCategoryItems(itStaff.Id, joinDate)
        };

        var service = new OnboardingChecklistService(context, _logger);

        // Act
        var result = await service.InitializeChecklistAsync(inputModel, hrManager.Id, UserRoles.HRManager);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(application.Id, result.ApplicationId);
        Assert.Equal(0, result.ProgressPercentage);
        Assert.False(result.IsCompleted);
        Assert.Equal(4, result.TotalCount);
        Assert.Equal(0, result.CompletedCount);

        // Đảm bảo đủ 4 nhóm nghiệp vụ
        var categories = result.CategoryGroups.Select(g => g.Category).ToHashSet();
        Assert.Contains(OnboardingCategory.ACCOUNT, categories);
        Assert.Contains(OnboardingCategory.EQUIPMENT, categories);
        Assert.Contains(OnboardingCategory.SEATING, categories);
        Assert.Contains(OnboardingCategory.DOCUMENT, categories);

        // Kiểm tra cơ sở dữ liệu đã lưu checklist và items riêng biệt
        var savedChecklist = await context.CandidateOnboardingChecklists
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.ApplicationId == application.Id);

        Assert.NotNull(savedChecklist);
        Assert.Equal(4, savedChecklist.Items.Count);
        Assert.All(savedChecklist.Items, item =>
        {
            Assert.False(item.IsCompleted);
            Assert.Equal(itStaff.Id, item.AssignedToUserId);
            Assert.True(item.DueDate <= joinDate);
        });

        // Kiểm tra có tạo AuditLog và CandidateInteraction
        var audit = await context.AuditLogs.FirstOrDefaultAsync(a => a.EntityId == savedChecklist.Id.ToString());
        Assert.NotNull(audit);
        Assert.Equal("KHOI_TAO_CHECKLIST_ONBOARDING", audit.Action);

        var interaction = await context.CandidateInteractions.FirstOrDefaultAsync(i => i.ApplicationId == application.Id);
        Assert.NotNull(interaction);
        Assert.Equal("ONBOARDING_INITIALIZED", interaction.Outcome);
    }

    [Fact]
    public async Task GetTemplateForDepartmentAsync_WhenDepartmentTemplateExists_ShouldReturnDepartmentTemplate()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (dept, _, itStaff, _, _) = await SeedBaseDataAsync(context);

        var template = new OnboardingTemplate
        {
            Id = Guid.NewGuid(),
            Name = "Mẫu tiếp nhận IT",
            DepartmentId = dept.Id,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.OnboardingTemplates.AddAsync(template);

        var templateItem = new OnboardingTemplateItem
        {
            Id = Guid.NewGuid(),
            TemplateId = template.Id,
            Category = OnboardingCategory.ACCOUNT,
            Title = "Cấp quyền tài khoản GitHub Enterprise",
            DefaultAssignedUserId = itStaff.Id,
            DaysBeforeJoinDateOffset = 2,
            DisplayOrder = 1,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.OnboardingTemplateItems.AddAsync(templateItem);
        await context.SaveChangesAsync();

        var service = new OnboardingChecklistService(context, _logger);

        // Act
        var result = await service.GetTemplateForDepartmentAsync(dept.Id, null);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(template.Id, result.TemplateId);
        Assert.Equal(dept.Id, result.DepartmentId);
        Assert.Single(result.Items);
        Assert.Equal("Cấp quyền tài khoản GitHub Enterprise", result.Items[0].Title);
        Assert.Equal(OnboardingCategory.ACCOUNT, result.Items[0].Category);
    }

    [Fact]
    public async Task GetTemplateForDepartmentAsync_WhenNoDepartmentTemplate_ShouldFallbackToGeneralTemplate()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (dept, _, itStaff, _, _) = await SeedBaseDataAsync(context);

        var generalTemplate = new OnboardingTemplate
        {
            Id = Guid.NewGuid(),
            Name = "Mẫu chuẩn chung toàn công ty",
            DepartmentId = null, // Chung toàn công ty
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.OnboardingTemplates.AddAsync(generalTemplate);

        var generalItem = new OnboardingTemplateItem
        {
            Id = Guid.NewGuid(),
            TemplateId = generalTemplate.Id,
            Category = OnboardingCategory.DOCUMENT,
            Title = "Nộp sổ hộ chiếu / CCCD công chứng",
            DefaultAssignedUserId = itStaff.Id,
            DaysBeforeJoinDateOffset = 1,
            DisplayOrder = 1,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.OnboardingTemplateItems.AddAsync(generalItem);
        await context.SaveChangesAsync();

        var service = new OnboardingChecklistService(context, _logger);

        // Act
        var result = await service.GetTemplateForDepartmentAsync(dept.Id, null);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(generalTemplate.Id, result.TemplateId);
        Assert.Single(result.Items);
        Assert.Equal(OnboardingCategory.DOCUMENT, result.Items[0].Category);
    }

    #endregion

    #region AC2: Mỗi mục có người phụ trách và hạn hoàn thành (DueDate <= FirstWorkingDate)

    [Fact]
    public void CreateOnboardingChecklistInputModel_Validation_WhenDueDateAfterFirstWorkingDate_ShouldFail()
    {
        // Arrange
        var joinDate = new DateOnly(2026, 11, 1);
        var model = new CreateOnboardingChecklistInputModel
        {
            ApplicationId = Guid.NewGuid(),
            DepartmentId = Guid.NewGuid(),
            FirstWorkingDate = joinDate,
            Items =
            [
                new CreateOnboardingChecklistItemInputModel
                {
                    Category = OnboardingCategory.ACCOUNT,
                    Title = "Cấp tài khoản email",
                    AssignedToUserId = Guid.NewGuid(),
                    DueDate = new DateOnly(2026, 11, 2) // Hạn sau ngày nhận việc -> Không hợp lệ!
                }
            ]
        };

        // Act
        var validationContext = new ValidationContext(model);
        var validationResults = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(model, validationContext, validationResults, validateAllProperties: true);

        // Assert
        Assert.False(isValid);
        Assert.Contains(validationResults, v => v.ErrorMessage!.Contains("không được sau ngày đi làm đầu tiên"));
    }

    [Fact]
    public async Task InitializeChecklistAsync_WhenItemDueDateAfterFirstWorkingDate_ShouldThrowArgumentException()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (dept, hrManager, itStaff, _, application) = await SeedBaseDataAsync(context);

        var joinDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));
        var inputModel = new CreateOnboardingChecklistInputModel
        {
            ApplicationId = application.Id,
            DepartmentId = dept.Id,
            FirstWorkingDate = joinDate,
            Items =
            [
                new CreateOnboardingChecklistItemInputModel
                {
                    Category = OnboardingCategory.EQUIPMENT,
                    Title = "Chuẩn bị màn hình",
                    AssignedToUserId = itStaff.Id,
                    DueDate = joinDate.AddDays(1) // Sau FirstWorkingDate
                }
            ]
        };

        var service = new OnboardingChecklistService(context, _logger);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.InitializeChecklistAsync(inputModel, hrManager.Id, UserRoles.HRManager));
    }

    #endregion

    #region AC3: Tiến độ hoàn thành hiển thị theo phần trăm (tính tập trung tại Service)

    [Fact]
    public async Task ToggleChecklistItemAsync_ShouldCalculateProgressPercentageAccurately()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (dept, hrManager, itStaff, _, application) = await SeedBaseDataAsync(context);

        var joinDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));
        var inputModel = new CreateOnboardingChecklistInputModel
        {
            ApplicationId = application.Id,
            DepartmentId = dept.Id,
            FirstWorkingDate = joinDate,
            Items = CreateStandardFourCategoryItems(itStaff.Id, joinDate)
        };

        var service = new OnboardingChecklistService(context, _logger);
        var checklistVm = await service.InitializeChecklistAsync(inputModel, hrManager.Id, UserRoles.HRManager);

        var items = await context.CandidateOnboardingChecklistItems
            .Where(i => i.ChecklistId == checklistVm.Id)
            .OrderBy(i => i.Category)
            .ToListAsync();

        Assert.Equal(4, items.Count);

        // Act & Assert 1: Hoàn thành 1/4 mục -> 25%
        var res1 = await service.ToggleChecklistItemAsync(
            new ToggleOnboardingItemInputModel { ItemId = items[0].Id, IsCompleted = true },
            hrManager.Id,
            UserRoles.HRManager);

        Assert.Equal(25, res1.ProgressPercentage);
        Assert.Equal(1, res1.CompletedCount);
        Assert.False(res1.IsCompleted);

        // Act & Assert 2: Hoàn thành 2/4 mục -> 50%
        var res2 = await service.ToggleChecklistItemAsync(
            new ToggleOnboardingItemInputModel { ItemId = items[1].Id, IsCompleted = true },
            hrManager.Id,
            UserRoles.HRManager);

        Assert.Equal(50, res2.ProgressPercentage);
        Assert.Equal(2, res2.CompletedCount);
        Assert.False(res2.IsCompleted);

        // Act & Assert 3: Hoàn thành 3/4 mục -> 75%
        var res3 = await service.ToggleChecklistItemAsync(
            new ToggleOnboardingItemInputModel { ItemId = items[2].Id, IsCompleted = true },
            hrManager.Id,
            UserRoles.HRManager);

        Assert.Equal(75, res3.ProgressPercentage);
        Assert.Equal(3, res3.CompletedCount);
        Assert.False(res3.IsCompleted);
    }

    #endregion

    #region AC4: Tự động chuyển ứng viên sang "Đã onboard" khi checklist 100%

    [Fact]
    public async Task ToggleChecklistItemAsync_WhenChecklistReaches100Percent_ShouldTransitionCandidateToOnboarded()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (dept, hrManager, itStaff, _, application) = await SeedBaseDataAsync(context);

        var joinDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));
        var inputModel = new CreateOnboardingChecklistInputModel
        {
            ApplicationId = application.Id,
            DepartmentId = dept.Id,
            FirstWorkingDate = joinDate,
            Items = CreateStandardFourCategoryItems(itStaff.Id, joinDate)
        };

        var service = new OnboardingChecklistService(context, _logger);
        var checklistVm = await service.InitializeChecklistAsync(inputModel, hrManager.Id, UserRoles.HRManager);

        var items = await context.CandidateOnboardingChecklistItems
            .Where(i => i.ChecklistId == checklistVm.Id)
            .ToListAsync();

        // Hoàn thành trước 3 mục đầu
        for (int i = 0; i < 3; i++)
        {
            await service.ToggleChecklistItemAsync(
                new ToggleOnboardingItemInputModel { ItemId = items[i].Id, IsCompleted = true },
                hrManager.Id,
                UserRoles.HRManager);
        }

        // Act: Hoàn thành mục thứ 4 cuối cùng -> đạt 100%
        var finalResult = await service.ToggleChecklistItemAsync(
            new ToggleOnboardingItemInputModel { ItemId = items[3].Id, IsCompleted = true },
            hrManager.Id,
            UserRoles.HRManager);

        // Assert
        Assert.Equal(100, finalResult.ProgressPercentage);
        Assert.True(finalResult.IsCompleted);
        Assert.NotNull(finalResult.CompletedAt);

        // Kiểm tra database: Ứng viên và Hồ sơ ứng tuyển tự động chuyển sang CandidateStage.ONBOARDED
        var updatedApp = await context.Applications.FindAsync(application.Id);
        Assert.NotNull(updatedApp);
        Assert.Equal(CandidateStage.ONBOARDED, updatedApp.Stage);

        // Kiểm tra ghi AuditLog HOAN_THANH_ONBOARDING
        var completionAudit = await context.AuditLogs
            .FirstOrDefaultAsync(a => a.Action == "HOAN_THANH_ONBOARDING" && a.EntityId == application.Id.ToString());
        Assert.NotNull(completionAudit);

        // Kiểm tra ghi CandidateInteraction STAGE_ONBOARDED
        var completionInteraction = await context.CandidateInteractions
            .FirstOrDefaultAsync(i => i.ApplicationId == application.Id && i.Outcome == "STAGE_ONBOARDED");
        Assert.NotNull(completionInteraction);
    }

    #endregion

    #region Revert: Khi bỏ tick dưới 100% thì tự động hoàn tác về "Tuyển dụng thành công" (HIRED)

    [Fact]
    public async Task ToggleChecklistItemAsync_WhenUncheckedFrom100Percent_ShouldRevertCandidateToHired()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (dept, hrManager, itStaff, _, application) = await SeedBaseDataAsync(context);

        var joinDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));
        var inputModel = new CreateOnboardingChecklistInputModel
        {
            ApplicationId = application.Id,
            DepartmentId = dept.Id,
            FirstWorkingDate = joinDate,
            Items = CreateStandardFourCategoryItems(itStaff.Id, joinDate)
        };

        var service = new OnboardingChecklistService(context, _logger);
        var checklistVm = await service.InitializeChecklistAsync(inputModel, hrManager.Id, UserRoles.HRManager);

        var items = await context.CandidateOnboardingChecklistItems
            .Where(i => i.ChecklistId == checklistVm.Id)
            .ToListAsync();

        // Hoàn thành cả 4 mục -> Đạt 100% và ONBOARDED
        foreach (var item in items)
        {
            await service.ToggleChecklistItemAsync(
                new ToggleOnboardingItemInputModel { ItemId = item.Id, IsCompleted = true },
                hrManager.Id,
                UserRoles.HRManager);
        }

        var appAfter100 = await context.Applications.FindAsync(application.Id);
        Assert.Equal(CandidateStage.ONBOARDED, appAfter100!.Stage);

        // Act: Bỏ chọn 1 mục -> Tiến độ giảm xuống 75%
        var revertResult = await service.ToggleChecklistItemAsync(
            new ToggleOnboardingItemInputModel { ItemId = items[0].Id, IsCompleted = false },
            hrManager.Id,
            UserRoles.HRManager);

        // Assert
        Assert.Equal(75, revertResult.ProgressPercentage);
        Assert.False(revertResult.IsCompleted);
        Assert.Null(revertResult.CompletedAt);

        // Kiểm tra database: Ứng viên tự động hoàn tác về CandidateStage.HIRED
        var revertedApp = await context.Applications.FindAsync(application.Id);
        Assert.NotNull(revertedApp);
        Assert.Equal(CandidateStage.HIRED, revertedApp.Stage);

        // Kiểm tra ghi CandidateInteraction STAGE_REVERTED_HIRED
        var revertInteraction = await context.CandidateInteractions
            .FirstOrDefaultAsync(i => i.ApplicationId == application.Id && i.Outcome == "STAGE_REVERTED_HIRED");
        Assert.NotNull(revertInteraction);
    }

    #endregion

    #region Security & RBAC: Phân quyền khởi tạo và tick hoàn thành

    [Fact]
    public async Task InitializeChecklistAsync_WhenCallerNotHrManager_ShouldThrowUnauthorizedAccessException()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (dept, _, itStaff, _, application) = await SeedBaseDataAsync(context);

        var joinDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));
        var inputModel = new CreateOnboardingChecklistInputModel
        {
            ApplicationId = application.Id,
            DepartmentId = dept.Id,
            FirstWorkingDate = joinDate,
            Items = CreateStandardFourCategoryItems(itStaff.Id, joinDate)
        };

        var service = new OnboardingChecklistService(context, _logger);

        // Act & Assert: User có role Interviewer không được phép khởi tạo
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.InitializeChecklistAsync(inputModel, itStaff.Id, UserRoles.Interviewer));
    }

    [Fact]
    public async Task InitializeChecklistAsync_WhenApplicationNotHired_ShouldThrowInvalidOperationException()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        // Hồ sơ đang ở vòng phỏng vấn (INTERVIEW), chưa nhận việc
        var (dept, hrManager, itStaff, _, application) = await SeedBaseDataAsync(context, candidateStage: CandidateStage.INTERVIEW);
        application.Status = ApplicationStatus.IN_PROCESS;
        await context.SaveChangesAsync();

        var joinDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));
        var inputModel = new CreateOnboardingChecklistInputModel
        {
            ApplicationId = application.Id,
            DepartmentId = dept.Id,
            FirstWorkingDate = joinDate,
            Items = CreateStandardFourCategoryItems(itStaff.Id, joinDate)
        };

        var service = new OnboardingChecklistService(context, _logger);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.InitializeChecklistAsync(inputModel, hrManager.Id, UserRoles.HRManager));
    }

    [Fact]
    public async Task InitializeChecklistAsync_WhenChecklistAlreadyExists_ShouldThrowInvalidOperationException()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (dept, hrManager, itStaff, _, application) = await SeedBaseDataAsync(context);

        var joinDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));
        var inputModel = new CreateOnboardingChecklistInputModel
        {
            ApplicationId = application.Id,
            DepartmentId = dept.Id,
            FirstWorkingDate = joinDate,
            Items = CreateStandardFourCategoryItems(itStaff.Id, joinDate)
        };

        var service = new OnboardingChecklistService(context, _logger);
        await service.InitializeChecklistAsync(inputModel, hrManager.Id, UserRoles.HRManager);

        // Act & Assert: Khởi tạo lần thứ hai cho cùng application -> bị chặn tạo trùng
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.InitializeChecklistAsync(inputModel, hrManager.Id, UserRoles.HRManager));
    }

    [Fact]
    public async Task ToggleChecklistItemAsync_WhenUserIsNotHrManagerAndNotAssignee_ShouldThrowUnauthorizedAccessException()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (dept, hrManager, itStaff, _, application) = await SeedBaseDataAsync(context);

        // Tạo thêm một user khác không liên quan
        var otherUser = new User
        {
            Id = Guid.NewGuid(),
            Email = "other@ats.local",
            FullName = "Nguyễn Nhân Viên Khác",
            Role = UserRoles.Interviewer,
            Status = "ACTIVE",
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.Users.AddAsync(otherUser);
        await context.SaveChangesAsync();

        var joinDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));
        var inputModel = new CreateOnboardingChecklistInputModel
        {
            ApplicationId = application.Id,
            DepartmentId = dept.Id,
            FirstWorkingDate = joinDate,
            Items = CreateStandardFourCategoryItems(itStaff.Id, joinDate)
        };

        var service = new OnboardingChecklistService(context, _logger);
        var checklistVm = await service.InitializeChecklistAsync(inputModel, hrManager.Id, UserRoles.HRManager);

        var item = await context.CandidateOnboardingChecklistItems
            .FirstOrDefaultAsync(i => i.ChecklistId == checklistVm.Id);

        // Act & Assert: Người không phải HR Manager và không phải itStaff không được quyền cập nhật
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.ToggleChecklistItemAsync(
                new ToggleOnboardingItemInputModel { ItemId = item!.Id, IsCompleted = true },
                otherUser.Id,
                UserRoles.Interviewer));
    }

    [Fact]
    public async Task ToggleChecklistItemAsync_WhenUserIsAssignee_ShouldSucceedEvenIfNotHrManager()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (dept, hrManager, itStaff, _, application) = await SeedBaseDataAsync(context);

        var joinDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));
        var inputModel = new CreateOnboardingChecklistInputModel
        {
            ApplicationId = application.Id,
            DepartmentId = dept.Id,
            FirstWorkingDate = joinDate,
            Items = CreateStandardFourCategoryItems(itStaff.Id, joinDate)
        };

        var service = new OnboardingChecklistService(context, _logger);
        var checklistVm = await service.InitializeChecklistAsync(inputModel, hrManager.Id, UserRoles.HRManager);

        var item = await context.CandidateOnboardingChecklistItems
            .FirstOrDefaultAsync(i => i.ChecklistId == checklistVm.Id && i.AssignedToUserId == itStaff.Id);

        // Act: itStaff (role Interviewer) cập nhật mục được giao cho chính mình -> Thành công!
        var result = await service.ToggleChecklistItemAsync(
            new ToggleOnboardingItemInputModel { ItemId = item!.Id, IsCompleted = true },
            itStaff.Id,
            UserRoles.Interviewer);

        // Assert
        Assert.NotNull(result);
        var updatedItem = await context.CandidateOnboardingChecklistItems.FindAsync(item.Id);
        Assert.NotNull(updatedItem);
        Assert.True(updatedItem.IsCompleted);
        Assert.Equal(itStaff.Id, updatedItem.CompletedByUserId);
    }

    #endregion
}
