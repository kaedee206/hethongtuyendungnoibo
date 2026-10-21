using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Ats.Web.Constants;
using Ats.Web.Data;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Models.ViewModels.Onboarding;
using Ats.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ats.Web.Services;

/// <summary>
/// Triển khai dịch vụ quản lý Checklist Onboarding cho người đã nhận việc (SP-7).
/// Tuân thủ: Primary Constructor, clean service layer, explicit transactions, UTC timestamps và audit logging.
/// </summary>
public class OnboardingChecklistService(
    ApplicationDbContext dbContext,
    ILogger<OnboardingChecklistService> logger) : IOnboardingChecklistService
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly ILogger<OnboardingChecklistService> _logger = logger;

    /// <inheritdoc />
    public async Task<CandidateOnboardingChecklistViewModel?> GetChecklistByApplicationIdAsync(
        Guid applicationId,
        Guid currentUserId,
        string currentUserRole,
        CancellationToken cancellationToken = default)
    {
        if (applicationId == Guid.Empty)
        {
            return null;
        }

        var checklist = await _dbContext.CandidateOnboardingChecklists
            .Include(c => c.Application)
                .ThenInclude(a => a.Candidate)
            .Include(c => c.Application)
                .ThenInclude(a => a.JobPosting)
            .Include(c => c.Department)
            .Include(c => c.Items)
                .ThenInclude(i => i.AssignedToUser)
            .Include(c => c.Items)
                .ThenInclude(i => i.CompletedByUser)
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ApplicationId == applicationId && !c.IsDeleted, cancellationToken);

        if (checklist == null)
        {
            return null;
        }

        var normalizedRole = UserRoles.NormalizeRole(currentUserRole);
        var isHrManager = normalizedRole == UserRoles.HRManager || normalizedRole == UserRoles.Admin;

        return MapToChecklistViewModel(checklist, currentUserId, isHrManager);
    }

    /// <inheritdoc />
    public async Task<OnboardingTemplateLookupDto> GetTemplateForDepartmentAsync(
        Guid departmentId,
        DateOnly? firstWorkingDate,
        CancellationToken cancellationToken = default)
    {
        var dept = await _dbContext.Departments
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == departmentId && !d.IsDeleted, cancellationToken);

        var deptName = dept?.Name ?? "Phòng ban";
        var effectiveJoinDate = firstWorkingDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));

        // 1. Tìm mẫu theo phòng ban cụ thể
        var template = await _dbContext.OnboardingTemplates
            .Include(t => t.Items.Where(i => !i.IsDeleted))
                .ThenInclude(i => i.DefaultAssignedUser)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.DepartmentId == departmentId && t.IsActive && !t.IsDeleted, cancellationToken);

        // 2. Fallback: Nếu phòng ban chưa có mẫu riêng, lấy mẫu chung toàn công ty
        if (template == null || !template.Items.Any())
        {
            template = await _dbContext.OnboardingTemplates
                .Include(t => t.Items.Where(i => !i.IsDeleted))
                    .ThenInclude(i => i.DefaultAssignedUser)
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.DepartmentId == null && t.IsActive && !t.IsDeleted, cancellationToken);
        }

        // 3. Fallback: Nếu cơ sở dữ liệu chưa có mẫu nào, tạo danh sách mẫu mặc định chuẩn 4 nhóm
        var itemsDto = new List<OnboardingTemplateItemDto>();

        if (template != null && template.Items.Any())
        {
            itemsDto = template.Items
                .OrderBy(i => i.Category)
                .ThenBy(i => i.DisplayOrder)
                .Select(i => new OnboardingTemplateItemDto
                {
                    Id = i.Id,
                    Title = i.Title,
                    Category = i.Category,
                    DefaultAssignedUserId = i.DefaultAssignedUserId,
                    DefaultAssignedUserName = i.DefaultAssignedUser?.FullName,
                    DaysBeforeJoinDateOffset = i.DaysBeforeJoinDateOffset,
                    DisplayOrder = i.DisplayOrder
                }).ToList();
        }
        else
        {
            itemsDto = GetDefaultStandardTemplateItems();
        }

        return new OnboardingTemplateLookupDto
        {
            TemplateId = template?.Id ?? Guid.Empty,
            DepartmentId = departmentId,
            DepartmentName = deptName,
            TemplateName = template?.Name ?? $"Mẫu Onboarding chuẩn - {deptName}",
            Items = itemsDto
        };
    }

    /// <inheritdoc />
    public async Task<CandidateOnboardingChecklistViewModel> InitializeChecklistAsync(
        CreateOnboardingChecklistInputModel model,
        Guid currentUserId,
        string currentUserRole,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (currentUserId == Guid.Empty)
        {
            throw new ArgumentException("Mã người dùng không hợp lệ.", nameof(currentUserId));
        }

        // 1. Kiểm tra phân quyền: Chỉ duy nhất [HR Manager] được khởi tạo checklist (và Admin)
        var normalizedRole = UserRoles.NormalizeRole(currentUserRole);
        if (normalizedRole != UserRoles.HRManager && normalizedRole != UserRoles.Admin)
        {
            throw new UnauthorizedAccessException("Chỉ Trưởng phòng Nhân sự (HR Manager) mới có quyền khởi tạo checklist onboarding.");
        }

        // 2. Kiểm tra tính hợp lệ của dữ liệu đầu vào
        var validationContext = new ValidationContext(model);
        var validationResults = new List<ValidationResult>();
        if (!Validator.TryValidateObject(model, validationContext, validationResults, validateAllProperties: true))
        {
            var firstError = validationResults.First().ErrorMessage ?? "Dữ liệu khởi tạo checklist không hợp lệ.";
            throw new ArgumentException(firstError);
        }

        // 3. Tìm nạp hồ sơ ứng tuyển
        var application = await _dbContext.Applications
            .Include(a => a.Candidate)
            .Include(a => a.JobPosting)
                .ThenInclude(jp => jp.Requisition)
            .FirstOrDefaultAsync(a => a.Id == model.ApplicationId && !a.IsDeleted, cancellationToken)
            ?? throw new KeyNotFoundException($"Không tìm thấy hồ sơ ứng tuyển với Id: {model.ApplicationId}");

        // 4. Kiểm tra điều kiện khởi tạo: Chỉ ứng viên ở trạng thái đã nhận việc (HIRED) mới được tạo checklist
        if (application.Stage != CandidateStage.HIRED && application.Status != ApplicationStatus.HIRED)
        {
            throw new InvalidOperationException(
                $"Chỉ ứng viên ở trạng thái đã nhận việc mới được tạo checklist onboarding. Trạng thái hiện tại: {application.Stage.GetDisplayName()}.");
        }

        // 5. Chặn tạo trùng: mỗi ứng viên chỉ có 1 checklist đang hoạt động
        var hasActive = await _dbContext.CandidateOnboardingChecklists
            .AnyAsync(c => c.ApplicationId == model.ApplicationId && !c.IsDeleted, cancellationToken);
        if (hasActive)
        {
            throw new InvalidOperationException("Hồ sơ này đã có một checklist onboarding đang hoạt động. Không thể tạo trùng.");
        }

        var department = await _dbContext.Departments.FindAsync(new object[] { model.DepartmentId }, cancellationToken)
            ?? throw new KeyNotFoundException($"Không tìm thấy phòng ban với Id: {model.DepartmentId}");

        var hrUser = await _dbContext.Users.FindAsync(new object[] { currentUserId }, cancellationToken);

        // 6. Thực thi khởi tạo trong transaction tường minh
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var nowUtc = DateTimeOffset.UtcNow;

            var checklist = new CandidateOnboardingChecklist
            {
                Id = Guid.NewGuid(),
                ApplicationId = application.Id,
                CandidateId = application.CandidateId,
                DepartmentId = model.DepartmentId,
                FirstWorkingDate = model.FirstWorkingDate,
                ProgressPercentage = 0,
                IsCompleted = false,
                CompletedAt = null,
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc,
                CreatedById = currentUserId,
                UpdatedById = currentUserId
            };

            await _dbContext.CandidateOnboardingChecklists.AddAsync(checklist, cancellationToken);

            // Sao chép các mục từ form (sinh từ mẫu) thành checklist riêng biệt của ứng viên
            var checklistItems = new List<CandidateOnboardingChecklistItem>();
            foreach (var itemInput in model.Items)
            {
                var item = new CandidateOnboardingChecklistItem
                {
                    Id = Guid.NewGuid(),
                    ChecklistId = checklist.Id,
                    Title = itemInput.Title.Trim(),
                    Category = itemInput.Category,
                    AssignedToUserId = itemInput.AssignedToUserId,
                    DueDate = itemInput.DueDate,
                    IsCompleted = false,
                    CompletedAt = null,
                    CompletedByUserId = null,
                    CreatedAt = nowUtc,
                    UpdatedAt = nowUtc,
                    CreatedById = currentUserId,
                    UpdatedById = currentUserId
                };
                checklistItems.Add(item);
            }

            await _dbContext.CandidateOnboardingChecklistItems.AddRangeAsync(checklistItems, cancellationToken);

            // Ghi nhật ký kiểm toán (AuditLog)
            var candidateFullName = $"{application.Candidate.FirstName} {application.Candidate.LastName}".Trim();
            var auditLog = new AuditLog
            {
                Id = Guid.NewGuid(),
                UserId = currentUserId,
                Action = "KHOI_TAO_CHECKLIST_ONBOARDING",
                EntityName = nameof(CandidateOnboardingChecklist),
                EntityId = checklist.Id.ToString(),
                NewValues = JsonSerializer.Serialize(new
                {
                    ApplicationId = application.Id,
                    CandidateName = candidateFullName,
                    Department = department.Name,
                    FirstWorkingDate = model.FirstWorkingDate.ToString("yyyy-MM-dd"),
                    TotalItems = checklistItems.Count,
                    CreatedBy = hrUser?.FullName ?? "HR Manager",
                    CreatedAt = nowUtc
                }),
                CreatedAt = nowUtc
            };
            await _dbContext.AuditLogs.AddAsync(auditLog, cancellationToken);

            // Ghi thêm một bản ghi tương tác CandidateInteraction để hiển thị đồng bộ trong timeline hồ sơ
            var interaction = new CandidateInteraction
            {
                Id = Guid.NewGuid(),
                CandidateId = application.CandidateId,
                ApplicationId = application.Id,
                CreatedByUserId = currentUserId,
                InteractionType = CandidateInteractionType.NOTE,
                Title = "Khởi tạo Checklist Onboarding",
                Content = $"Trưởng phòng Nhân sự đã khởi tạo checklist tiếp nhận nhân sự mới gồm {checklistItems.Count} mục cho phòng ban {department.Name}. Ngày đi làm đầu tiên: {model.FirstWorkingDate:dd/MM/yyyy}.",
                Outcome = "ONBOARDING_INITIALIZED",
                InteractionDate = nowUtc,
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc,
                CreatedById = currentUserId,
                UpdatedById = currentUserId
            };
            await _dbContext.CandidateInteractions.AddAsync(interaction, cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation("Khởi tạo checklist onboarding thành công cho ứng viên {CandId}, hồ sơ {AppId} với {Count} mục",
                application.CandidateId, application.Id, checklistItems.Count);

            // Load lại đầy đủ thông tin để trả về ViewModel
            return (await GetChecklistByApplicationIdAsync(application.Id, currentUserId, currentUserRole, cancellationToken))!;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Lỗi xảy ra khi khởi tạo checklist onboarding cho hồ sơ {AppId}", model.ApplicationId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<CandidateOnboardingChecklistViewModel> ToggleChecklistItemAsync(
        ToggleOnboardingItemInputModel model,
        Guid currentUserId,
        string currentUserRole,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (model.ItemId == Guid.Empty)
        {
            throw new ArgumentException("Mã mục checklist không hợp lệ.", nameof(model));
        }

        var item = await _dbContext.CandidateOnboardingChecklistItems
            .Include(i => i.Checklist)
                .ThenInclude(c => c.Items)
            .Include(i => i.Checklist)
                .ThenInclude(c => c.Application)
                    .ThenInclude(a => a.Candidate)
            .FirstOrDefaultAsync(i => i.Id == model.ItemId && !i.IsDeleted, cancellationToken)
            ?? throw new KeyNotFoundException($"Không tìm thấy mục checklist với Id: {model.ItemId}");

        var checklist = item.Checklist;
        var application = checklist.Application;

        // 1. Phân quyền cập nhật tiến độ mục: HR Manager hoặc Người được phân công phụ trách mục đó (Lựa chọn A3)
        var normalizedRole = UserRoles.NormalizeRole(currentUserRole);
        var isHrManager = normalizedRole == UserRoles.HRManager || normalizedRole == UserRoles.Admin;
        var isAssignedUser = item.AssignedToUserId == currentUserId;

        if (!isHrManager && !isAssignedUser)
        {
            throw new UnauthorizedAccessException(
                "Bạn không có quyền cập nhật mục này. Chỉ Trưởng phòng Nhân sự hoặc người được phân công phụ trách mới được cập nhật.");
        }

        // Nếu trạng thái không thay đổi thì bỏ qua
        if (item.IsCompleted == model.IsCompleted)
        {
            return (await GetChecklistByApplicationIdAsync(application.Id, currentUserId, currentUserRole, cancellationToken))!;
        }

        var user = await _dbContext.Users.FindAsync(new object[] { currentUserId }, cancellationToken);

        // 2. Thực thi cập nhật trong transaction tường minh
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var nowUtc = DateTimeOffset.UtcNow;

            item.IsCompleted = model.IsCompleted;
            item.CompletedAt = model.IsCompleted ? nowUtc : null;
            item.CompletedByUserId = model.IsCompleted ? currentUserId : null;
            item.UpdatedAt = nowUtc;
            item.UpdatedById = currentUserId;

            // 3. Tính toán lại tiến độ % tập trung tại Service (Acceptance Criteria 3: Tính ở 1 chỗ duy nhất)
            var allItems = checklist.Items.Where(i => !i.IsDeleted).ToList();
            var totalCount = allItems.Count;
            var completedCount = allItems.Count(i => i.IsCompleted);

            var newProgressPercentage = totalCount == 0
                ? 0
                : (int)Math.Round((double)completedCount * 100 / totalCount, MidpointRounding.AwayFromZero);

            checklist.ProgressPercentage = newProgressPercentage;
            var wasCompleted = checklist.IsCompleted;
            var isNowCompleted = (newProgressPercentage == 100);

            checklist.IsCompleted = isNowCompleted;
            checklist.CompletedAt = isNowCompleted ? nowUtc : null;
            checklist.UpdatedAt = nowUtc;
            checklist.UpdatedById = currentUserId;

            // 4. Acceptance Criteria 4: Khi 100% mục hoàn thành -> tự động chuyển ứng viên sang "Đã onboard" trong cùng transaction
            if (isNowCompleted && !wasCompleted)
            {
                application.Stage = CandidateStage.ONBOARDED;
                application.UpdatedAt = nowUtc;
                application.UpdatedById = currentUserId;

                // Đồng bộ PipelineStage tương ứng nếu có
                var onboardPipelineStage = await _dbContext.PipelineStages
                    .FirstOrDefaultAsync(s => s.Name.Contains("onboard", StringComparison.OrdinalIgnoreCase) || s.StageOrder == 8, cancellationToken);
                if (onboardPipelineStage != null)
                {
                    application.CurrentStageId = onboardPipelineStage.Id;
                }

                // Ghi nhật ký hoàn thành Onboarding
                var auditLog = new AuditLog
                {
                    Id = Guid.NewGuid(),
                    UserId = currentUserId,
                    Action = "HOAN_THANH_ONBOARDING",
                    EntityName = nameof(Application),
                    EntityId = application.Id.ToString(),
                    NewValues = JsonSerializer.Serialize(new
                    {
                        Stage = CandidateStage.ONBOARDED.ToString(),
                        StageDisplay = CandidateStage.ONBOARDED.GetDisplayName(),
                        ChecklistId = checklist.Id,
                        CompletedAt = nowUtc,
                        CompletedBy = user?.FullName ?? "Hệ thống"
                    }),
                    CreatedAt = nowUtc
                };
                await _dbContext.AuditLogs.AddAsync(auditLog, cancellationToken);

                var interaction = new CandidateInteraction
                {
                    Id = Guid.NewGuid(),
                    CandidateId = application.CandidateId,
                    ApplicationId = application.Id,
                    CreatedByUserId = currentUserId,
                    InteractionType = CandidateInteractionType.NOTE,
                    Title = "Hoàn tất tiếp nhận nhân sự (Onboarded)",
                    Content = $"Ứng viên đã hoàn thành 100% các hạng mục trong checklist tiếp nhận nhân sự. Trạng thái hồ sơ đã tự động chuyển sang '{CandidateStage.ONBOARDED.GetDisplayName()}'.",
                    Outcome = "STAGE_ONBOARDED",
                    InteractionDate = nowUtc,
                    CreatedAt = nowUtc,
                    UpdatedAt = nowUtc,
                    CreatedById = currentUserId,
                    UpdatedById = currentUserId
                };
                await _dbContext.CandidateInteractions.AddAsync(interaction, cancellationToken);

                _logger.LogInformation("Checklist đạt 100%. Tự động chuyển ứng viên {CandId} sang trạng thái Đã onboard", application.CandidateId);
            }
            // 5. Xử lý khi có mục bị bỏ tick lại (< 100%): Hoàn tác trạng thái ứng viên về "Tuyển dụng thành công" (Lựa chọn A2)
            else if (wasCompleted && !isNowCompleted)
            {
                application.Stage = CandidateStage.HIRED;
                application.UpdatedAt = nowUtc;
                application.UpdatedById = currentUserId;

                var hiredPipelineStage = await _dbContext.PipelineStages
                    .FirstOrDefaultAsync(s => s.StageOrder == 6 || s.Name.Contains("thành công"), cancellationToken);
                if (hiredPipelineStage != null)
                {
                    application.CurrentStageId = hiredPipelineStage.Id;
                }

                var revertAuditLog = new AuditLog
                {
                    Id = Guid.NewGuid(),
                    UserId = currentUserId,
                    Action = "HOAN_TAC_TRANG_THAI_ONBOARDING",
                    EntityName = nameof(Application),
                    EntityId = application.Id.ToString(),
                    NewValues = JsonSerializer.Serialize(new
                    {
                        Stage = CandidateStage.HIRED.ToString(),
                        StageDisplay = CandidateStage.HIRED.GetDisplayName(),
                        CurrentProgress = newProgressPercentage,
                        RevertedAt = nowUtc,
                        RevertedBy = user?.FullName ?? "Hệ thống",
                        Reason = $"Mục '{item.Title}' bị bỏ đánh dấu hoàn thành."
                    }),
                    CreatedAt = nowUtc
                };
                await _dbContext.AuditLogs.AddAsync(revertAuditLog, cancellationToken);

                var revertInteraction = new CandidateInteraction
                {
                    Id = Guid.NewGuid(),
                    CandidateId = application.CandidateId,
                    ApplicationId = application.Id,
                    CreatedByUserId = currentUserId,
                    InteractionType = CandidateInteractionType.NOTE,
                    Title = "Hoàn tác trạng thái tiếp nhận nhân sự",
                    Content = $"Mục '{item.Title}' trong checklist tiếp nhận đã bị bỏ đánh dấu hoàn thành. Tiến độ checklist giảm xuống {newProgressPercentage}%. Trạng thái hồ sơ đã tự động hoàn tác về '{CandidateStage.HIRED.GetDisplayName()}'.",
                    Outcome = "STAGE_REVERTED_HIRED",
                    InteractionDate = nowUtc,
                    CreatedAt = nowUtc,
                    UpdatedAt = nowUtc,
                    CreatedById = currentUserId,
                    UpdatedById = currentUserId
                };
                await _dbContext.CandidateInteractions.AddAsync(revertInteraction, cancellationToken);

                _logger.LogInformation("Checklist bị bỏ tick (< 100%: {Pct}%). Tự động hoàn tác ứng viên {CandId} về Tuyển dụng thành công",
                    newProgressPercentage, application.CandidateId);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return (await GetChecklistByApplicationIdAsync(application.Id, currentUserId, currentUserRole, cancellationToken))!;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Lỗi xảy ra khi cập nhật mục checklist {ItemId}", model.ItemId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> HasActiveChecklistAsync(Guid applicationId, CancellationToken cancellationToken = default)
    {
        if (applicationId == Guid.Empty) return false;

        return await _dbContext.CandidateOnboardingChecklists
            .AnyAsync(c => c.ApplicationId == applicationId && !c.IsDeleted, cancellationToken);
    }

    private static CandidateOnboardingChecklistViewModel MapToChecklistViewModel(
        CandidateOnboardingChecklist checklist,
        Guid currentUserId,
        bool isHrManager)
    {
        var app = checklist.Application;
        var cand = app?.Candidate;
        var candName = cand != null ? $"{cand.FirstName} {cand.LastName}".Trim() : "Ứng viên";

        var allItems = checklist.Items.Where(i => !i.IsDeleted).ToList();
        var totalCount = allItems.Count;
        var completedCount = allItems.Count(i => i.IsCompleted);

        // Tính tiến độ % tập trung
        var progressPercentage = totalCount == 0
            ? 0
            : (int)Math.Round((double)completedCount * 100 / totalCount, MidpointRounding.AwayFromZero);

        var itemDtos = allItems
            .OrderBy(i => i.Category)
            .ThenBy(i => i.DueDate)
            .ThenBy(i => i.Title)
            .Select(i => new OnboardingItemDto
            {
                Id = i.Id,
                ChecklistId = i.ChecklistId,
                Title = i.Title,
                Category = i.Category,
                AssignedToUserId = i.AssignedToUserId,
                AssignedToUserName = i.AssignedToUser?.FullName ?? "Chưa phân công",
                AssignedToUserEmail = i.AssignedToUser?.Email ?? string.Empty,
                DueDate = i.DueDate,
                IsCompleted = i.IsCompleted,
                CompletedAt = i.CompletedAt,
                CompletedByUserId = i.CompletedByUserId,
                CompletedByUserName = i.CompletedByUser?.FullName,
                CanToggle = isHrManager || i.AssignedToUserId == currentUserId
            }).ToList();

        // Phân theo 4 nhóm chuẩn hóa
        var categoryGroups = Enum.GetValues<OnboardingCategory>()
            .Select(cat =>
            {
                var groupItems = itemDtos.Where(i => i.Category == cat).ToList();
                return new OnboardingCategoryGroupDto
                {
                    Category = cat,
                    Items = groupItems
                };
            })
            .Where(g => g.Items.Any())
            .ToList();

        return new CandidateOnboardingChecklistViewModel
        {
            Id = checklist.Id,
            ApplicationId = checklist.ApplicationId,
            CandidateId = checklist.CandidateId,
            CandidateName = candName,
            CandidateEmail = cand?.Email ?? string.Empty,
            CandidatePhone = cand?.Phone,
            JobTitle = app?.JobPosting?.Title ?? "Vị trí tuyển dụng",
            DepartmentId = checklist.DepartmentId,
            DepartmentName = checklist.Department?.Name ?? "Phòng ban",
            FirstWorkingDate = checklist.FirstWorkingDate,
            ProgressPercentage = progressPercentage,
            IsCompleted = checklist.IsCompleted,
            CompletedAt = checklist.CompletedAt,
            TotalCount = totalCount,
            CompletedCount = completedCount,
            CanInitialize = false, // Đã có checklist rồi nên không thể khởi tạo lại
            CanUpdateItem = isHrManager,
            Items = itemDtos,
            CategoryGroups = categoryGroups
        };
    }

    private static List<OnboardingTemplateItemDto> GetDefaultStandardTemplateItems()
    {
        return
        [
            // Nhóm 1: Tài khoản
            new() { Title = "Tạo tài khoản Email doanh nghiệp (@noveratech.digital)", Category = OnboardingCategory.ACCOUNT, DaysBeforeJoinDateOffset = 2, DisplayOrder = 1 },
            new() { Title = "Cấp quyền truy cập hệ thống nội bộ (GitLab, Jira, Slack, Wiki)", Category = OnboardingCategory.ACCOUNT, DaysBeforeJoinDateOffset = 1, DisplayOrder = 2 },
            new() { Title = "Cấu hình tài khoản mạng riêng ảo VPN bảo mật nội bộ", Category = OnboardingCategory.ACCOUNT, DaysBeforeJoinDateOffset = 1, DisplayOrder = 3 },

            // Nhóm 2: Thiết bị
            new() { Title = "Chuẩn bị máy tính xách tay (Laptop) cài sẵn hệ điều hành và công cụ", Category = OnboardingCategory.EQUIPMENT, DaysBeforeJoinDateOffset = 2, DisplayOrder = 1 },
            new() { Title = "Chuẩn bị màn hình phụ, bàn phím, chuột và cáp kết nối", Category = OnboardingCategory.EQUIPMENT, DaysBeforeJoinDateOffset = 1, DisplayOrder = 2 },

            // Nhóm 3: Chỗ ngồi
            new() { Title = "Sắp xếp vị trí bàn làm việc cố định tại văn phòng", Category = OnboardingCategory.SEATING, DaysBeforeJoinDateOffset = 1, DisplayOrder = 1 },
            new() { Title = "In và kích hoạt thẻ nhân viên ra vào tòa nhà", Category = OnboardingCategory.SEATING, DaysBeforeJoinDateOffset = 0, DisplayOrder = 2 },

            // Nhóm 4: Hồ sơ giấy tờ
            new() { Title = "Ký kết Hợp đồng thử việc / Hợp đồng lao động chính thức", Category = OnboardingCategory.DOCUMENT, DaysBeforeJoinDateOffset = 0, DisplayOrder = 1 },
            new() { Title = "Ký Thỏa thuận bảo mật thông tin & tài sản trí tuệ (NDA)", Category = OnboardingCategory.DOCUMENT, DaysBeforeJoinDateOffset = 0, DisplayOrder = 2 },
            new() { Title = "Thu nhận hồ sơ nhân sự (Bằng cấp công chứng, CCCD, Ảnh thẻ, Sổ BHXH)", Category = OnboardingCategory.DOCUMENT, DaysBeforeJoinDateOffset = 0, DisplayOrder = 3 }
        ];
    }
}
