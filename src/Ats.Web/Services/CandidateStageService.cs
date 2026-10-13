using System.ComponentModel.DataAnnotations;
using Ats.Web.Constants;
using Ats.Web.Data;
using Ats.Web.Models.DTOs;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Models.ViewModels.Candidates;
using Ats.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ats.Web.Services;

/// <summary>
/// Triển khai dịch vụ quản lý chuyển giai đoạn ứng viên & ghi nhận lý do khi loại hồ sơ (SCRUM-42).
/// Tuân thủ kiến trúc C# 12 Primary Constructors, transaction tường minh EF Core và chuẩn hóa thời gian UTC.
/// </summary>
public class CandidateStageService(
    ApplicationDbContext dbContext,
    ILogger<CandidateStageService> logger) : ICandidateStageService
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly ILogger<CandidateStageService> _logger = logger;

    /// <inheritdoc />
    public async Task<IReadOnlyList<RejectionReasonLookupDto>> GetActiveRejectionReasonsAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.RejectionReasons
            .AsNoTracking()
            .Where(r => r.IsActive && !r.IsDeleted)
            .OrderBy(r => r.DisplayOrder)
            .ThenBy(r => r.Name)
            .Select(r => new RejectionReasonLookupDto
            {
                Id = r.Id,
                Code = r.Code,
                Name = r.Name,
                Description = r.Description,
                DisplayOrder = r.DisplayOrder,
                IsActive = r.IsActive
            })
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CandidateStageHistoryDto>> GetStageHistoryAsync(Guid applicationId, CancellationToken cancellationToken = default)
    {
        if (applicationId == Guid.Empty)
        {
            return Array.Empty<CandidateStageHistoryDto>();
        }

        return await _dbContext.CandidateStageHistories
            .AsNoTracking()
            .Include(h => h.RejectionReason)
            .Include(h => h.ChangedByUser)
            .Where(h => h.ApplicationId == applicationId)
            .OrderBy(h => h.CreatedAt)
            .Select(h => new CandidateStageHistoryDto
            {
                Id = h.Id,
                ApplicationId = h.ApplicationId,
                FromStage = h.FromStage,
                FromStageDisplayName = h.FromStage.HasValue ? h.FromStage.Value.GetDisplayName() : null,
                ToStage = h.ToStage,
                ToStageDisplayName = h.ToStage.GetDisplayName(),
                RejectionReasonId = h.RejectionReasonId,
                RejectionReasonCode = h.RejectionReason != null ? h.RejectionReason.Code : null,
                RejectionReasonName = h.RejectionReason != null ? h.RejectionReason.Name : null,
                Note = h.Note,
                ChangedByUserId = h.ChangedByUserId,
                ChangedByUserName = h.ChangedByUser != null ? h.ChangedByUser.FullName : null,
                ChangedByUserEmail = h.ChangedByUser != null ? h.ChangedByUser.Email : null,
                CreatedAt = h.CreatedAt
            })
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CandidateStageHistoryDto> TransitionStageAsync(
        TransitionCandidateStageInputModel model,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (currentUserId == Guid.Empty)
        {
            throw new ArgumentException("Mã người dùng thực hiện không hợp lệ.", nameof(currentUserId));
        }

        // 1. Thực thi kiểm tra nghiệp vụ Model qua IValidatableObject
        var validationContext = new ValidationContext(model);
        var validationResults = new List<ValidationResult>();
        if (!Validator.TryValidateObject(model, validationContext, validationResults, validateAllProperties: true))
        {
            var firstError = validationResults.First().ErrorMessage ?? "Dữ liệu đầu vào không hợp lệ.";
            throw new ArgumentException(firstError);
        }

        // 2. Tìm nạp hồ sơ ứng tuyển
        var application = await _dbContext.Applications
            .Include(a => a.CandidateStageHistories)
            .Include(a => a.Interviews)
            .FirstOrDefaultAsync(a => a.Id == model.ApplicationId, cancellationToken);

        if (application == null)
        {
            throw new KeyNotFoundException($"Không tìm thấy hồ sơ ứng tuyển với mã: {model.ApplicationId}");
        }

        // 3. Tìm nạp thông tin người dùng và xác định vai trò
        var currentUser = await _dbContext.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == currentUserId, cancellationToken);

        if (currentUser == null)
        {
            throw new KeyNotFoundException($"Không tìm thấy người dùng thực hiện với mã: {currentUserId}");
        }

        var isHrManager = IsUserHrManager(currentUser);
        var fromStage = application.Stage;

        // 4. QUY TẮC 1: Kiểm tra loại hồ sơ (REJECTED)
        RejectionReason? rejectionReason = null;
        if (model.ToStage == CandidateStage.REJECTED)
        {
            if (!model.RejectionReasonId.HasValue || model.RejectionReasonId.Value == Guid.Empty)
            {
                throw new InvalidOperationException("Khi loại hồ sơ (REJECTED), bắt buộc phải chọn một lý do hợp lệ từ danh mục lý do loại.");
            }

            rejectionReason = await _dbContext.RejectionReasons
                .FirstOrDefaultAsync(r => r.Id == model.RejectionReasonId.Value, cancellationToken);

            if (rejectionReason == null)
            {
                throw new InvalidOperationException("Lý do loại hồ sơ đã chọn không tồn tại trong danh mục hệ thống.");
            }

            if (!rejectionReason.IsActive)
            {
                throw new InvalidOperationException($"Lý do loại hồ sơ '{rejectionReason.Name}' hiện đang bị vô hiệu hóa.");
            }
        }

        // 5. QUY TẮC 3: Rào chắn đề xuất Offer (OFFER)
        if (model.ToStage == CandidateStage.OFFER)
        {
            var hasPassedInterview = CheckHasPassedInterview(application, fromStage);

            if (!hasPassedInterview)
            {
                // Nhảy cóc từ các vòng trước (như APPLIED, SCREENING) thẳng lên OFFER
                if (!isHrManager)
                {
                    throw new InvalidOperationException(
                        "Ứng viên chưa trải qua giai đoạn Phỏng vấn (INTERVIEW). Nghiêm cấm nhảy cóc từ các vòng trước thẳng lên OFFER trừ khi được thực hiện bởi Trưởng phòng Nhân sự (HRManager).");
                }

                if (!model.SkipInterviewConfirmedByHrManager)
                {
                    throw new InvalidOperationException(
                        "Ứng viên chưa trải qua giai đoạn Phỏng vấn (INTERVIEW). Trưởng phòng Nhân sự bắt buộc phải bật cờ xác nhận SkipInterviewConfirmedByHrManager để chuyển thẳng lên OFFER.");
                }
            }
        }

        // 6. QUY TẮC 2: Thực thi cập nhật trạng thái & lưu nhật ký trong transaction tường minh theo chuẩn UTC
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var nowUtc = DateTimeOffset.UtcNow;

            // Tạo bản ghi lịch sử CandidateStageHistory
            var history = new CandidateStageHistory
            {
                Id = Guid.NewGuid(),
                ApplicationId = application.Id,
                FromStage = fromStage,
                ToStage = model.ToStage,
                RejectionReasonId = model.ToStage == CandidateStage.REJECTED ? model.RejectionReasonId : null,
                Note = string.IsNullOrWhiteSpace(model.Note) ? null : model.Note.Trim(),
                ChangedByUserId = currentUserId,
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc,
                CreatedById = currentUserId,
                UpdatedById = currentUserId
            };

            await _dbContext.CandidateStageHistories.AddAsync(history, cancellationToken);

            // Cập nhật thực thể Application
            application.Stage = model.ToStage;
            application.UpdatedAt = nowUtc;
            application.UpdatedById = currentUserId;

            if (model.ToStage == CandidateStage.REJECTED)
            {
                application.Status = ApplicationStatus.REJECTED;
                application.RejectionReasonId = model.RejectionReasonId;
                application.RejectionNote = !string.IsNullOrWhiteSpace(model.Note) ? model.Note.Trim() : rejectionReason?.Name;
            }
            else if (model.ToStage == CandidateStage.HIRED)
            {
                application.Status = ApplicationStatus.HIRED;
                application.RejectionReasonId = null;
                application.RejectionNote = null;
            }
            else
            {
                application.Status = ApplicationStatus.IN_PROCESS;
                application.RejectionReasonId = null;
                application.RejectionNote = null;
            }

            // Đồng bộ PipelineStage tương ứng nếu có để bảo toàn tính tương thích với hệ thống cũ
            var matchingPipelineStage = await FindMatchingPipelineStageAsync(model.ToStage, cancellationToken);
            if (matchingPipelineStage != null)
            {
                application.CurrentStageId = matchingPipelineStage.Id;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "Chuyển giai đoạn ứng viên thành công: ApplicationId={AppId}, FromStage={From}, ToStage={To}, ChangedByUserId={UserId}",
                application.Id, fromStage, model.ToStage, currentUserId);

            return new CandidateStageHistoryDto
            {
                Id = history.Id,
                ApplicationId = history.ApplicationId,
                FromStage = history.FromStage,
                FromStageDisplayName = history.FromStage?.GetDisplayName(),
                ToStage = history.ToStage,
                ToStageDisplayName = history.ToStage.GetDisplayName(),
                RejectionReasonId = history.RejectionReasonId,
                RejectionReasonCode = rejectionReason?.Code,
                RejectionReasonName = rejectionReason?.Name,
                Note = history.Note,
                ChangedByUserId = history.ChangedByUserId,
                ChangedByUserName = currentUser.FullName,
                ChangedByUserEmail = currentUser.Email,
                CreatedAt = history.CreatedAt
            };
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Lỗi xảy ra trong quá trình chuyển giai đoạn hồ sơ ứng viên {AppId}", model.ApplicationId);
            throw;
        }
    }

    /// <summary>
    /// Kiểm tra người dùng có vai trò Trưởng phòng Nhân sự (HRManager) hoặc Quản trị viên (Admin) hay không.
    /// </summary>
    private static bool IsUserHrManager(User user)
    {
        var role = user.Role;
        if (role.Equals(UserRoles.HRManager, StringComparison.OrdinalIgnoreCase) ||
            role.Equals(UserRoles.Admin, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return user.UserRoles.Any(ur => ur.Role != null &&
            (ur.Role.Name.Equals(UserRoles.HRManager, StringComparison.OrdinalIgnoreCase) ||
             ur.Role.Name.Equals(UserRoles.Admin, StringComparison.OrdinalIgnoreCase) ||
             ur.Role.Code == RoleCode.HR_MANAGER ||
             ur.Role.Code == RoleCode.ADMIN));
    }

    /// <summary>
    /// Kiểm tra ứng viên đã từng trải qua giai đoạn Phỏng vấn (INTERVIEW) hay chưa.
    /// </summary>
    private static bool CheckHasPassedInterview(Application application, CandidateStage currentStage)
    {
        // 1. Giai đoạn hiện tại là INTERVIEW
        if (currentStage == CandidateStage.INTERVIEW)
        {
            return true;
        }

        // 2. Lịch sử các lần chuyển giai đoạn trước đó đã từng có INTERVIEW
        if (application.CandidateStageHistories != null &&
            application.CandidateStageHistories.Any(h => h.ToStage == CandidateStage.INTERVIEW || h.FromStage == CandidateStage.INTERVIEW))
        {
            return true;
        }

        // 3. Đã có lịch phỏng vấn được tạo cho đơn ứng tuyển này
        if (application.Interviews != null && application.Interviews.Count != 0)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Tìm PipelineStage cũ tương ứng để đồng bộ ngược với CSDL.
    /// </summary>
    private async Task<PipelineStage?> FindMatchingPipelineStageAsync(CandidateStage stage, CancellationToken cancellationToken)
    {
        return stage switch
        {
            CandidateStage.APPLIED => await _dbContext.PipelineStages.FirstOrDefaultAsync(s => s.StageOrder == 1 || s.Name.Contains("Ứng tuyển"), cancellationToken),
            CandidateStage.SCREENING => await _dbContext.PipelineStages.FirstOrDefaultAsync(s => s.StageOrder == 2 || s.Name.Contains("Sàng lọc"), cancellationToken),
            CandidateStage.INTERVIEW => await _dbContext.PipelineStages.FirstOrDefaultAsync(s => s.StageOrder == 3 || s.StageOrder == 4 || s.Name.Contains("Phỏng vấn"), cancellationToken),
            CandidateStage.OFFER => await _dbContext.PipelineStages.FirstOrDefaultAsync(s => s.StageOrder == 5 || s.Name.Contains("Offer"), cancellationToken),
            CandidateStage.HIRED => await _dbContext.PipelineStages.FirstOrDefaultAsync(s => s.StageOrder == 6 || s.Name.Contains("Tuyển dụng thành công"), cancellationToken),
            CandidateStage.REJECTED => await _dbContext.PipelineStages.FirstOrDefaultAsync(s => s.StageOrder == 7 || s.Name.Contains("Từ chối"), cancellationToken),
            _ => null
        };
    }
}
