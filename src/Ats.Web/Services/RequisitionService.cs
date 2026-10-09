using System.Text.Json;
using Ats.Web.Constants;
using Ats.Web.Data;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Models.ViewModels.Requisitions;
using Ats.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ats.Web.Services;

public class RequisitionService(
    ApplicationDbContext dbContext,
    ILogger<RequisitionService> logger) : IRequisitionService
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly ILogger<RequisitionService> _logger = logger;

    public async Task<RequisitionCreateViewModel> PrepareCreateViewModelAsync(Guid currentUserId, CancellationToken cancellationToken = default)
    {
        var model = new RequisitionCreateViewModel
        {
            Quantity = 1,
            HeadcountType = HeadcountType.NEW_HEADCOUNT,
            TargetHireDate = DateOnly.FromDateTime(DateTime.Today.AddDays(30))
        };

        await PopulateOptionsAsync(model, currentUserId, cancellationToken);

        return model;
    }

    public async Task PopulateOptionsAsync(RequisitionCreateViewModel model, Guid currentUserId, CancellationToken cancellationToken = default)
    {
        // 1. Tải danh sách Chức danh đang hoạt động từ module Quản lý chức danh (JobPositions)
        var positions = await _dbContext.JobPositions
            .Include(p => p.Department)
            .Where(p => p.IsActive && !p.IsDeleted)
            .OrderBy(p => p.Title)
            .ToListAsync(cancellationToken);

        model.JobPositionOptions = positions.Select(p => new JobPositionOptionViewModel
        {
            Id = p.Id,
            Code = p.Code,
            Title = p.Title,
            JobLevel = p.JobLevel,
            DepartmentId = p.DepartmentId,
            DepartmentName = p.Department?.Name ?? "Chưa phân bổ",
            MinSalary = p.MinSalary,
            MaxSalary = p.MaxSalary
        }).ToList();

        // 2. Tải thông tin người dùng đang đăng nhập
        var user = await _dbContext.Users
            .Include(u => u.DepartmentEntity)
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == currentUserId, cancellationToken);

        model.CurrentUserName = user?.FullName ?? "Trưởng bộ phận";
        model.CurrentUserDepartmentName = user?.DepartmentEntity?.Name;

        var userRole = user?.Role ?? string.Empty;
        var isAdminOrHR = userRole.Equals(UserRoles.Admin, StringComparison.OrdinalIgnoreCase) ||
                          userRole.Equals(UserRoles.HRManager, StringComparison.OrdinalIgnoreCase) ||
                          user?.UserRoles.Any(r => r.Role != null && (r.Role.Name == UserRoles.Admin || r.Role.Name == UserRoles.HRManager)) == true;

        // 3. Tải danh sách phòng ban mà người dùng có quyền quản lý/chọn
        List<Department> eligibleDepartments;

        if (isAdminOrHR)
        {
            // Admin hoặc HR Manager có quyền chọn bất kỳ phòng ban nào
            eligibleDepartments = await _dbContext.Departments
                .Where(d => d.IsActive)
                .OrderBy(d => d.Name)
                .ToListAsync(cancellationToken);

            model.CanChangeDepartment = true;
        }
        else
        {
            // Hiring Manager: Tìm các phòng ban do người dùng làm Quản lý (ManagerId) hoặc phòng ban chính
            var managedDepartments = await _dbContext.Departments
                .Where(d => d.IsActive && (d.ManagerId == currentUserId || (user != null && d.Id == user.DepartmentId)))
                .OrderBy(d => d.Name)
                .ToListAsync(cancellationToken);

            if (managedDepartments.Count == 0 && user?.DepartmentId != null)
            {
                var userDept = await _dbContext.Departments
                    .FirstOrDefaultAsync(d => d.Id == user.DepartmentId && d.IsActive, cancellationToken);
                if (userDept != null)
                {
                    managedDepartments.Add(userDept);
                }
            }

            // Nếu không tìm thấy phòng ban nào của user, lấy toàn bộ phòng ban active để tránh deadlock nghiệp vụ
            if (managedDepartments.Count == 0)
            {
                managedDepartments = await _dbContext.Departments
                    .Where(d => d.IsActive)
                    .OrderBy(d => d.Name)
                    .ToListAsync(cancellationToken);
            }

            eligibleDepartments = managedDepartments;
            // Cho phép đổi phòng ban nếu quản lý từ 2 phòng ban trở lên
            model.CanChangeDepartment = eligibleDepartments.Count > 1;
        }

        model.DepartmentOptions = eligibleDepartments.Select(d => new DepartmentOptionViewModel
        {
            Id = d.Id,
            Code = d.Code,
            Name = d.Name,
            IsUserDepartment = user != null && d.Id == user.DepartmentId
        }).ToList();

        // 4. Tự động gán phòng ban của Trưởng bộ phận nếu chưa được chọn
        if (!model.DepartmentId.HasValue || model.DepartmentId == Guid.Empty)
        {
            if (user?.DepartmentId.HasValue == true && eligibleDepartments.Any(d => d.Id == user.DepartmentId.Value))
            {
                model.DepartmentId = user.DepartmentId.Value;
            }
            else if (eligibleDepartments.Count > 0)
            {
                model.DepartmentId = eligibleDepartments.First().Id;
            }
        }
    }

    public async Task<(bool Success, string Message, Guid? RequisitionId)> CreateRequisitionAsync(
        RequisitionCreateViewModel model,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var result = await SaveOrUpdateRequisitionAsync(model, currentUserId, cancellationToken);
        return (result.Success, result.Message, result.RequisitionId);
    }

    public async Task<(bool Success, string Message, Guid? RequisitionId, string? Code)> SaveOrUpdateRequisitionAsync(
        RequisitionCreateViewModel model,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == currentUserId, cancellationToken);
        var userRole = user?.Role ?? string.Empty;
        var isAdminOrHR = userRole.Equals(UserRoles.Admin, StringComparison.OrdinalIgnoreCase) ||
                          userRole.Equals(UserRoles.HRManager, StringComparison.OrdinalIgnoreCase);

        // 1. Kiểm tra validation theo ngữ cảnh Lưu nháp (DRAFT) vs Gửi duyệt (PENDING_APPROVAL)
        if (!model.IsDraft)
        {
            // Gửi duyệt: Bắt buộc chức danh
            if (!model.JobPositionId.HasValue || model.JobPositionId.Value == Guid.Empty)
            {
                return (false, "Vui lòng chọn chức danh cần tuyển dụng.", null, null);
            }

            var position = await _dbContext.JobPositions
                .FirstOrDefaultAsync(p => p.Id == model.JobPositionId.Value && p.IsActive && !p.IsDeleted, cancellationToken);

            if (position == null)
            {
                return (false, "Chức danh được chọn không tồn tại hoặc đã ngừng hoạt động.", null, null);
            }

            // Gửi duyệt: Bắt buộc phòng ban
            if (!model.DepartmentId.HasValue || model.DepartmentId.Value == Guid.Empty)
            {
                return (false, "Vui lòng chọn phòng ban phụ trách yêu cầu tuyển dụng.", null, null);
            }

            var department = await _dbContext.Departments
                .FirstOrDefaultAsync(d => d.Id == model.DepartmentId.Value && d.IsActive, cancellationToken);

            if (department == null)
            {
                return (false, "Phòng ban được chọn không tồn tại hoặc đã ngừng hoạt động.", null, null);
            }

            // Gửi duyệt: Bắt buộc số lượng
            if (model.Quantity < 1)
            {
                return (false, "Số lượng tuyển dụng phải từ 1 người trở lên.", null, null);
            }

            // Gửi duyệt: Bắt buộc dải lương
            if (!model.MinSalary.HasValue || !model.MaxSalary.HasValue)
            {
                return (false, "Vui lòng nhập đầy đủ dải lương đề xuất (lương tối thiểu và tối đa).", null, null);
            }

            if (model.MinSalary.Value <= 0 || model.MaxSalary.Value <= 0)
            {
                return (false, "Mức lương đề xuất phải là số dương lớn hơn 0.", null, null);
            }

            if (model.MaxSalary.Value < model.MinSalary.Value)
            {
                return (false, "Mức lương tối đa đề xuất không được nhỏ hơn mức lương tối thiểu.", null, null);
            }

            // Gửi duyệt: Bắt buộc ngày cần người
            if (!model.TargetHireDate.HasValue || model.TargetHireDate.Value < DateOnly.FromDateTime(DateTime.Today))
            {
                return (false, "Ngày cần nhân sự có mặt phải từ ngày hôm nay trở đi.", null, null);
            }

            // Gửi duyệt: Bắt buộc Mô tả công việc và Yêu cầu ứng viên
            if (string.IsNullOrWhiteSpace(StripHtml(model.JobDescription)))
            {
                return (false, "Vui lòng nhập mô tả công việc (trách nhiệm, nhiệm vụ chính, KPI...) khi gửi duyệt.", null, null);
            }

            if (string.IsNullOrWhiteSpace(StripHtml(model.Requirements)))
            {
                return (false, "Vui lòng nhập yêu cầu ứng viên (trình độ học vấn, kinh nghiệm, kỹ năng, chứng chỉ...) khi gửi duyệt.", null, null);
            }
        }
        else
        {
            // Lưu nháp: Nếu người dùng đã chọn Chức danh thì kiểm tra tính hợp lệ
            if (model.JobPositionId.HasValue && model.JobPositionId.Value != Guid.Empty)
            {
                var position = await _dbContext.JobPositions
                    .FirstOrDefaultAsync(p => p.Id == model.JobPositionId.Value && p.IsActive && !p.IsDeleted, cancellationToken);

                if (position == null)
                {
                    return (false, "Chức danh được chọn không tồn tại hoặc đã ngừng hoạt động.", null, null);
                }
            }

            // Lưu nháp: Nếu người dùng đã chọn Phòng ban thì kiểm tra tính hợp lệ
            if (model.DepartmentId.HasValue && model.DepartmentId.Value != Guid.Empty)
            {
                var department = await _dbContext.Departments
                    .FirstOrDefaultAsync(d => d.Id == model.DepartmentId.Value && d.IsActive, cancellationToken);

                if (department == null)
                {
                    return (false, "Phòng ban được chọn không tồn tại hoặc đã ngừng hoạt động.", null, null);
                }
            }

            // Lưu nháp: Nếu nhập cả 2 mức lương thì kiểm tra Max >= Min
            if (model.MinSalary.HasValue && model.MaxSalary.HasValue && model.MaxSalary.Value < model.MinSalary.Value)
            {
                return (false, "Mức lương tối đa đề xuất không được nhỏ hơn mức lương tối thiểu.", null, null);
            }

            // Lưu nháp: Nếu nhập ngày thì kiểm tra ngày không được ở quá khứ
            if (model.TargetHireDate.HasValue && model.TargetHireDate.Value < DateOnly.FromDateTime(DateTime.Today))
            {
                return (false, "Ngày cần nhân sự có mặt phải từ ngày hôm nay trở đi.", null, null);
            }
        }

        var reasonText = string.IsNullOrWhiteSpace(model.ReasonDetail)
            ? (model.HeadcountType == HeadcountType.REPLACEMENT ? "Thay thế nhân sự nghỉ việc" : "Tăng mới headcount mở rộng dự án")
            : model.ReasonDetail.Trim();

        // 2. Phân nhánh Xử lý: Cập nhật bản nháp đã có (UPDATE) hay Tạo mới (INSERT)
        if (model.Id.HasValue && model.Id.Value != Guid.Empty)
        {
            var existing = await _dbContext.JobRequisitions
                .FirstOrDefaultAsync(r => r.Id == model.Id.Value && !r.IsDeleted, cancellationToken);

            if (existing == null)
            {
                return (false, "Không tìm thấy yêu cầu tuyển dụng cần lưu.", null, null);
            }

            if (existing.Status != RequisitionStatus.DRAFT && existing.Status != RequisitionStatus.CHANGES_REQUESTED)
            {
                return (false, "Chỉ có thể chỉnh sửa yêu cầu tuyển dụng đang ở trạng thái Bản nháp hoặc Yêu cầu bổ sung.", null, null);
            }

            if (!isAdminOrHR && existing.HiringManagerId != currentUserId)
            {
                return (false, "Bạn không có quyền chỉnh sửa bản nháp của người dùng khác.", null, null);
            }

            existing.JobPositionId = (model.JobPositionId.HasValue && model.JobPositionId.Value != Guid.Empty) ? model.JobPositionId : null;
            existing.DepartmentId = (model.DepartmentId.HasValue && model.DepartmentId.Value != Guid.Empty) ? model.DepartmentId : null;
            existing.Quantity = model.Quantity < 1 ? 1 : model.Quantity;
            existing.HeadcountType = model.HeadcountType;
            existing.Reason = reasonText;
            existing.MinSalary = model.MinSalary;
            existing.MaxSalary = model.MaxSalary;
            existing.SalaryBandExplanation = model.SalaryBandExplanation;
            existing.TargetHireDate = model.TargetHireDate;
            existing.JobDescription = string.IsNullOrWhiteSpace(model.JobDescription) ? null : model.JobDescription.Trim();
            existing.Requirements = string.IsNullOrWhiteSpace(model.Requirements) ? null : model.Requirements.Trim();
            existing.Status = model.IsDraft ? RequisitionStatus.DRAFT : RequisitionStatus.PENDING_APPROVAL;
            existing.UpdatedAt = DateTimeOffset.UtcNow;

            if (!model.IsDraft)
            {
                // Kiểm tra và sinh luồng phê duyệt 2 cấp nếu chưa có bước pending nào (hoặc nộp lại sau CHANGES_REQUESTED)
                var existingPendingApprovals = await _dbContext.RequisitionApprovals
                    .Where(a => a.RequisitionId == existing.Id && a.Status == ApprovalStatus.PENDING && !a.IsDeleted)
                    .ToListAsync(cancellationToken);

                if (existingPendingApprovals.Count == 0)
                {
                    var (hrApproverId, bodApproverId) = await GetDefaultApproversAsync(currentUserId, cancellationToken);
                    var step1 = new RequisitionApproval
                    {
                        Id = Guid.NewGuid(),
                        RequisitionId = existing.Id,
                        ApproverId = hrApproverId,
                        StepOrder = 1,
                        Status = ApprovalStatus.PENDING,
                        CreatedAt = DateTimeOffset.UtcNow,
                        UpdatedAt = DateTimeOffset.UtcNow
                    };
                    var step2 = new RequisitionApproval
                    {
                        Id = Guid.NewGuid(),
                        RequisitionId = existing.Id,
                        ApproverId = bodApproverId,
                        StepOrder = 2,
                        Status = ApprovalStatus.PENDING,
                        CreatedAt = DateTimeOffset.UtcNow,
                        UpdatedAt = DateTimeOffset.UtcNow
                    };
                    await _dbContext.RequisitionApprovals.AddRangeAsync(new[] { step1, step2 }, cancellationToken);
                }
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Người dùng {UserId} đã cập nhật yêu cầu tuyển dụng {Code} (Trạng thái: {Status})",
                currentUserId, existing.Code, existing.Status);

            var message = model.IsDraft
                ? $"Bản nháp yêu cầu tuyển dụng '{existing.Code}' đã được lưu thành công."
                : $"Yêu cầu tuyển dụng '{existing.Code}' đã được gửi duyệt thành công.";

            return (true, message, existing.Id, existing.Code);
        }
        else
        {
            // Tạo mới Requisition
            var datePrefix = DateTime.UtcNow.ToString("yyyyMMdd");
            var randomSuffix = Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
            var requisitionCode = $"REQ-{datePrefix}-{randomSuffix}";
            var initialStatus = model.IsDraft ? RequisitionStatus.DRAFT : RequisitionStatus.PENDING_APPROVAL;

            var requisition = new JobRequisition
            {
                Id = Guid.NewGuid(),
                Code = requisitionCode,
                JobPositionId = (model.JobPositionId.HasValue && model.JobPositionId.Value != Guid.Empty) ? model.JobPositionId : null,
                DepartmentId = (model.DepartmentId.HasValue && model.DepartmentId.Value != Guid.Empty) ? model.DepartmentId : null,
                HiringManagerId = currentUserId,
                Quantity = model.Quantity < 1 ? 1 : model.Quantity,
                HeadcountType = model.HeadcountType,
                Reason = reasonText,
                MinSalary = model.MinSalary,
                MaxSalary = model.MaxSalary,
                SalaryBandExplanation = model.SalaryBandExplanation,
                Currency = "VND",
                TargetHireDate = model.TargetHireDate,
                JobDescription = string.IsNullOrWhiteSpace(model.JobDescription) ? null : model.JobDescription.Trim(),
                Requirements = string.IsNullOrWhiteSpace(model.Requirements) ? null : model.Requirements.Trim(),
                Status = initialStatus,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            await _dbContext.JobRequisitions.AddAsync(requisition, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            if (!model.IsDraft)
            {
                var (hrApproverId, bodApproverId) = await GetDefaultApproversAsync(currentUserId, cancellationToken);
                var step1 = new RequisitionApproval
                {
                    Id = Guid.NewGuid(),
                    RequisitionId = requisition.Id,
                    ApproverId = hrApproverId,
                    StepOrder = 1,
                    Status = ApprovalStatus.PENDING,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                var step2 = new RequisitionApproval
                {
                    Id = Guid.NewGuid(),
                    RequisitionId = requisition.Id,
                    ApproverId = bodApproverId,
                    StepOrder = 2,
                    Status = ApprovalStatus.PENDING,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                await _dbContext.RequisitionApprovals.AddRangeAsync(new[] { step1, step2 }, cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            _logger.LogInformation("Người dùng {UserId} đã tạo thành công yêu cầu tuyển dụng {Code} (Trạng thái: {Status})",
                currentUserId, requisitionCode, initialStatus);

            var responseMessage = model.IsDraft
                ? $"Bản nháp yêu cầu tuyển dụng '{requisitionCode}' đã được lưu thành công."
                : $"Yêu cầu tuyển dụng '{requisitionCode}' đã được khởi tạo thành công và chuyển sang trạng thái Chờ duyệt.";

            return (true, responseMessage, requisition.Id, requisition.Code);
        }
    }

    public async Task<List<RequisitionDraftItemViewModel>> GetDraftsByManagerAsync(
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == currentUserId, cancellationToken);
        var userRole = user?.Role ?? string.Empty;
        var isAdminOrHR = userRole.Equals(UserRoles.Admin, StringComparison.OrdinalIgnoreCase) ||
                          userRole.Equals(UserRoles.HRManager, StringComparison.OrdinalIgnoreCase);

        var query = _dbContext.JobRequisitions
            .Include(r => r.JobPosition)
            .Include(r => r.Department)
            .Where(r => r.Status == RequisitionStatus.DRAFT && !r.IsDeleted);

        if (!isAdminOrHR)
        {
            query = query.Where(r => r.HiringManagerId == currentUserId);
        }

        return await query
            .OrderByDescending(r => r.UpdatedAt)
            .Select(r => new RequisitionDraftItemViewModel
            {
                Id = r.Id,
                Code = r.Code,
                JobPositionTitle = r.JobPosition != null ? r.JobPosition.Title : null,
                JobPositionCode = r.JobPosition != null ? r.JobPosition.Code : null,
                DepartmentName = r.Department != null ? r.Department.Name : null,
                Quantity = r.Quantity,
                MinSalary = r.MinSalary,
                MaxSalary = r.MaxSalary,
                TargetHireDate = r.TargetHireDate,
                UpdatedAt = r.UpdatedAt,
                CreatedAt = r.CreatedAt,
                HasContent = !string.IsNullOrWhiteSpace(r.JobDescription) || !string.IsNullOrWhiteSpace(r.Requirements)
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<RequisitionCreateViewModel?> GetDraftByIdAsync(
        Guid id,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var requisition = await _dbContext.JobRequisitions
            .Include(r => r.JobPosition)
            .Include(r => r.Department)
            .Include(r => r.Approvals)
                .ThenInclude(a => a.Approver)
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted && (r.Status == RequisitionStatus.DRAFT || r.Status == RequisitionStatus.CHANGES_REQUESTED), cancellationToken);

        if (requisition == null) return null;

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == currentUserId, cancellationToken);
        var userRole = user?.Role ?? string.Empty;
        var isAdminOrHR = userRole.Equals(UserRoles.Admin, StringComparison.OrdinalIgnoreCase) ||
                          userRole.Equals(UserRoles.HRManager, StringComparison.OrdinalIgnoreCase);

        if (!isAdminOrHR && requisition.HiringManagerId != currentUserId)
        {
            return null;
        }

        var latestApprovalFeedback = requisition.Approvals
            .Where(a => a.Status == ApprovalStatus.CHANGES_REQUESTED)
            .OrderByDescending(a => a.DecidedAt ?? a.CreatedAt)
            .FirstOrDefault();

        var model = new RequisitionCreateViewModel
        {
            Id = requisition.Id,
            Code = requisition.Code,
            JobPositionId = requisition.JobPositionId,
            DepartmentId = requisition.DepartmentId,
            Quantity = requisition.Quantity,
            HeadcountType = requisition.HeadcountType,
            ReasonDetail = requisition.Reason,
            MinSalary = requisition.MinSalary,
            MaxSalary = requisition.MaxSalary,
            SalaryBandExplanation = requisition.SalaryBandExplanation,
            TargetHireDate = requisition.TargetHireDate,
            JobDescription = requisition.JobDescription,
            Requirements = requisition.Requirements,
            IsDraft = requisition.Status == RequisitionStatus.DRAFT,
            Status = requisition.Status,
            ReviewerFeedback = latestApprovalFeedback?.Comment
        };

        await PopulateOptionsAsync(model, currentUserId, cancellationToken);
        return model;
    }

    public async Task<(bool Success, string Message)> DeleteDraftAsync(
        Guid id,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var requisition = await _dbContext.JobRequisitions
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted, cancellationToken);

        if (requisition == null)
        {
            return (false, "Không tìm thấy yêu cầu tuyển dụng cần xóa.");
        }

        if (requisition.Status != RequisitionStatus.DRAFT)
        {
            return (false, "Chỉ có thể xóa yêu cầu tuyển dụng đang ở trạng thái Bản nháp.");
        }

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == currentUserId, cancellationToken);
        var userRole = user?.Role ?? string.Empty;
        var isAdminOrHR = userRole.Equals(UserRoles.Admin, StringComparison.OrdinalIgnoreCase) ||
                          userRole.Equals(UserRoles.HRManager, StringComparison.OrdinalIgnoreCase);

        if (!isAdminOrHR && requisition.HiringManagerId != currentUserId)
        {
            return (false, "Bạn không có quyền xóa bản nháp của người dùng khác.");
        }

        requisition.IsDeleted = true;
        requisition.DeletedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Người dùng {UserId} đã xóa bản nháp yêu cầu tuyển dụng {Code}", currentUserId, requisition.Code);
        return (true, $"Bản nháp yêu cầu tuyển dụng '{requisition.Code}' đã được xóa thành công.");
    }

    public async Task<int> GetDraftCountAsync(
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == currentUserId, cancellationToken);
        var userRole = user?.Role ?? string.Empty;
        var isAdminOrHR = userRole.Equals(UserRoles.Admin, StringComparison.OrdinalIgnoreCase) ||
                          userRole.Equals(UserRoles.HRManager, StringComparison.OrdinalIgnoreCase);

        var query = _dbContext.JobRequisitions
            .Where(r => r.Status == RequisitionStatus.DRAFT && !r.IsDeleted);

        if (!isAdminOrHR)
        {
            query = query.Where(r => r.HiringManagerId == currentUserId);
        }

        return await query.CountAsync(cancellationToken);
    }

    /// <summary>
    /// Nhân bản một yêu cầu tuyển dụng đã có thành bản nháp mới (SCRUM-274).
    /// Mã yêu cầu mới được sinh tự động; TargetHireDate được reset về hôm nay + 30 ngày
    /// nếu ngày gốc đã qua hoặc chưa được chọn.
    /// </summary>
    public async Task<(bool Success, string Message, Guid? NewRequisitionId, string? NewCode)> DuplicateRequisitionAsync(
        Guid sourceId,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var source = await _dbContext.JobRequisitions
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == sourceId && !r.IsDeleted, cancellationToken);

        if (source == null)
        {
            return (false, "Không tìm thấy yêu cầu tuyển dụng nguồn để sao chép.", null, null);
        }

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == currentUserId, cancellationToken);
        var userRole = user?.Role ?? string.Empty;
        var isAdminOrHR = userRole.Equals(UserRoles.Admin, StringComparison.OrdinalIgnoreCase) ||
                          userRole.Equals(UserRoles.HRManager, StringComparison.OrdinalIgnoreCase);

        // Chỉ Admin, HRManager hoặc chính Hiring Manager mới được sao chép
        if (!isAdminOrHR && source.HiringManagerId != currentUserId)
        {
            return (false, "Bạn không có quyền sao chép yêu cầu tuyển dụng này.", null, null);
        }

        // Sinh mã mới theo chuẩn REQ-yyyyMMdd-XXXX
        var datePrefix = DateTime.UtcNow.ToString("yyyyMMdd");
        var randomSuffix = Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
        var newCode = $"REQ-{datePrefix}-{randomSuffix}";

        // Reset TargetHireDate nếu đã qua hoặc chưa có
        var today = DateOnly.FromDateTime(DateTime.Today);
        var newTargetHireDate = (source.TargetHireDate.HasValue && source.TargetHireDate.Value >= today)
            ? source.TargetHireDate
            : today.AddDays(30);

        var duplicate = new JobRequisition
        {
            Id = Guid.NewGuid(),
            Code = newCode,
            JobPositionId = source.JobPositionId,
            DepartmentId = source.DepartmentId,
            HiringManagerId = currentUserId,
            Quantity = source.Quantity,
            HeadcountType = source.HeadcountType,
            Reason = source.Reason,
            MinSalary = source.MinSalary,
            MaxSalary = source.MaxSalary,
            SalaryBandExplanation = null,       // Không sao chép giải trình ngoại lệ
            Currency = source.Currency ?? "VND",
            TargetHireDate = newTargetHireDate,
            JobDescription = source.JobDescription,
            Requirements = source.Requirements,
            Status = Models.Enums.RequisitionStatus.DRAFT,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await _dbContext.JobRequisitions.AddAsync(duplicate, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Người dùng {UserId} đã sao chép yêu cầu tuyển dụng {SourceCode} → bản nháp mới {NewCode}",
            currentUserId, source.Code, newCode);

        return (true,
            $"Đã sao chép thành công yêu cầu tuyển dụng '{source.Code}' thành bản nháp mới '{newCode}'.",
            duplicate.Id,
            newCode);
    }

    public async Task<(bool Success, string Message)> ProcessApprovalDecisionAsync(
        Guid requisitionId,
        Guid currentUserId,
        RequisitionApprovalDecisionInputModel input,
        CancellationToken cancellationToken = default)
    {
        var action = input.Action?.Trim().ToUpperInvariant();
        if (action != "APPROVE" && action != "REJECT" && action != "REQUEST_CHANGES")
        {
            return (false, "Hành động phê duyệt không hợp lệ. Vui lòng chọn Duyệt, Từ chối hoặc Yêu cầu bổ sung.");
        }

        if (action == "REJECT" && string.IsNullOrWhiteSpace(input.Comment))
        {
            return (false, "Vui lòng nhập lý do từ chối yêu cầu tuyển dụng.");
        }

        if (action == "REQUEST_CHANGES" && string.IsNullOrWhiteSpace(input.Comment))
        {
            return (false, "Vui lòng nhập ý kiến yêu cầu bổ sung thông tin.");
        }

        var requisition = await _dbContext.JobRequisitions
            .Include(r => r.Approvals)
                .ThenInclude(a => a.Approver)
            .FirstOrDefaultAsync(r => r.Id == requisitionId && !r.IsDeleted, cancellationToken);

        if (requisition == null)
        {
            return (false, "Không tìm thấy yêu cầu tuyển dụng cần phê duyệt.");
        }

        if (requisition.Status != RequisitionStatus.PENDING_APPROVAL)
        {
            return (false, "Yêu cầu tuyển dụng này hiện không ở trạng thái Chờ phê duyệt.");
        }

        // Tìm bước duyệt pending có thứ tự nhỏ nhất (chu kỳ duyệt mới nhất)
        var pendingApprovals = requisition.Approvals
            .Where(a => a.Status == ApprovalStatus.PENDING && !a.IsDeleted)
            .OrderBy(a => a.StepOrder)
            .ThenByDescending(a => a.CreatedAt)
            .ToList();

        var currentStep = pendingApprovals.FirstOrDefault();
        if (currentStep == null)
        {
            return (false, "Không tìm thấy bước phê duyệt đang chờ xử lý cho yêu cầu này.");
        }

        var currentUser = await _dbContext.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == currentUserId, cancellationToken);

        if (currentUser == null)
        {
            return (false, "Người dùng không tồn tại.");
        }

        var userRole = currentUser.Role ?? string.Empty;
        var isAdmin = userRole.Equals(UserRoles.Admin, StringComparison.OrdinalIgnoreCase) ||
                      currentUser.UserRoles.Any(ur => ur.Role != null && ur.Role.Name == UserRoles.Admin);
        var isHrManager = userRole.Equals(UserRoles.HRManager, StringComparison.OrdinalIgnoreCase) ||
                          currentUser.UserRoles.Any(ur => ur.Role != null && ur.Role.Name == UserRoles.HRManager);
        var isApprover = userRole.Equals(UserRoles.Approver, StringComparison.OrdinalIgnoreCase) ||
                         currentUser.UserRoles.Any(ur => ur.Role != null && ur.Role.Name == UserRoles.Approver);

        bool isAuthorized = isAdmin ||
            currentStep.ApproverId == currentUserId ||
            (currentStep.StepOrder == 1 && isHrManager) ||
            (currentStep.StepOrder == 2 && isApprover);

        if (!isAuthorized)
        {
            return (false, "Bạn không có quyền phê duyệt bước này.");
        }

        var now = DateTimeOffset.UtcNow;
        var trimmedComment = string.IsNullOrWhiteSpace(input.Comment) ? null : input.Comment.Trim();

        // Ghi nhận sổ kiểm toán hệ thống AuditLog bảo đảm tính bất biến (Scrum #23)
        var auditLog = new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = currentUserId,
            Action = $"REQUISITION_APPROVAL_{action}",
            EntityName = "JobRequisition",
            EntityId = requisition.Id.ToString(),
            NewValues = JsonSerializer.Serialize(new
            {
                RequisitionCode = requisition.Code,
                StepOrder = currentStep.StepOrder,
                ApproverId = currentUserId,
                ApproverName = currentUser.FullName,
                ApproverEmail = currentUser.Email,
                Action = action,
                Comment = trimmedComment,
                DecidedAt = now
            }, new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }),
            CreatedAt = now
        };
        await _dbContext.AuditLogs.AddAsync(auditLog, cancellationToken);

        switch (action)
        {
            case "APPROVE":
                currentStep.Status = ApprovalStatus.APPROVED;
                currentStep.Comment = trimmedComment;
                currentStep.DecidedAt = now;
                currentStep.UpdatedAt = now;

                // Kiểm tra xem còn bước duyệt kế tiếp chưa hoàn thành không
                var nextStep = pendingApprovals
                    .Where(a => a.StepOrder > currentStep.StepOrder && a.Status == ApprovalStatus.PENDING)
                    .OrderBy(a => a.StepOrder)
                    .FirstOrDefault();

                if (nextStep != null)
                {
                    requisition.Status = RequisitionStatus.PENDING_APPROVAL;
                    requisition.UpdatedAt = now;
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    _logger.LogInformation("Người dùng {UserId} đã duyệt bước {Step} cho yêu cầu {Code}. Tự động chuyển bước {NextStep}.",
                        currentUserId, currentStep.StepOrder, requisition.Code, nextStep.StepOrder);
                    return (true, $"Đã phê duyệt thành công Cấp {currentStep.StepOrder}. Yêu cầu tuyển dụng tự động chuyển sang Cấp {nextStep.StepOrder} để tiếp tục xét duyệt.");
                }
                else
                {
                    // Cấp cuối cùng duyệt -> Yêu cầu chuyển sang Đã duyệt
                    requisition.Status = RequisitionStatus.APPROVED;
                    requisition.UpdatedAt = now;
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    _logger.LogInformation("Người dùng {UserId} đã duyệt bước cuối ({Step}) cho yêu cầu {Code}. Chuyển sang Đã duyệt.",
                        currentUserId, currentStep.StepOrder, requisition.Code);
                    return (true, $"Yêu cầu tuyển dụng '{requisition.Code}' đã được phê duyệt hoàn tất ở cấp cuối cùng!");
                }

            case "REJECT":
                currentStep.Status = ApprovalStatus.REJECTED;
                currentStep.Comment = trimmedComment;
                currentStep.DecidedAt = now;
                currentStep.UpdatedAt = now;

                var otherPendingOnReject = pendingApprovals
                    .Where(a => a.Id != currentStep.Id && a.Status == ApprovalStatus.PENDING)
                    .ToList();
                if (otherPendingOnReject.Count > 0)
                {
                    _dbContext.RequisitionApprovals.RemoveRange(otherPendingOnReject);
                }

                requisition.Status = RequisitionStatus.REJECTED;
                requisition.UpdatedAt = now;
                await _dbContext.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Người dùng {UserId} đã từ chối yêu cầu {Code} tại bước {Step} với lý do: {Comment}",
                    currentUserId, requisition.Code, currentStep.StepOrder, trimmedComment);
                return (true, $"Đã từ chối yêu cầu tuyển dụng '{requisition.Code}'.");

            case "REQUEST_CHANGES":
                currentStep.Status = ApprovalStatus.CHANGES_REQUESTED;
                currentStep.Comment = trimmedComment;
                currentStep.DecidedAt = now;
                currentStep.UpdatedAt = now;

                var otherPendingOnChanges = pendingApprovals
                    .Where(a => a.Id != currentStep.Id && a.Status == ApprovalStatus.PENDING)
                    .ToList();
                if (otherPendingOnChanges.Count > 0)
                {
                    _dbContext.RequisitionApprovals.RemoveRange(otherPendingOnChanges);
                }

                requisition.Status = RequisitionStatus.CHANGES_REQUESTED;
                requisition.UpdatedAt = now;
                await _dbContext.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Người dùng {UserId} đã yêu cầu bổ sung cho yêu cầu {Code} tại bước {Step} với ý kiến: {Comment}",
                    currentUserId, requisition.Code, currentStep.StepOrder, trimmedComment);
                return (true, $"Đã yêu cầu bổ sung thông tin cho yêu cầu tuyển dụng '{requisition.Code}'. Hồ sơ đã được trả về cho người tạo để cập nhật và lịch sử được giữ nguyên.");

            default:
                return (false, "Hành động không xác định.");
        }
    }

    public async Task<RequisitionDetailsViewModel?> GetRequisitionDetailsAsync(
        Guid id,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var requisition = await _dbContext.JobRequisitions
            .Include(r => r.JobPosition)
            .Include(r => r.Department)
            .Include(r => r.HiringManager)
            .Include(r => r.AssignedRecruiter)
            .Include(r => r.Approvals)
                .ThenInclude(a => a.Approver)
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted, cancellationToken);

        if (requisition == null) return null;

        var currentUser = await _dbContext.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == currentUserId, cancellationToken);

        var userRole = currentUser?.Role ?? string.Empty;
        var isAdmin = currentUser != null && (userRole.Equals(UserRoles.Admin, StringComparison.OrdinalIgnoreCase) ||
                      currentUser.UserRoles.Any(ur => ur.Role != null && ur.Role.Name == UserRoles.Admin));
        var isHrManager = currentUser != null && (userRole.Equals(UserRoles.HRManager, StringComparison.OrdinalIgnoreCase) ||
                          currentUser.UserRoles.Any(ur => ur.Role != null && ur.Role.Name == UserRoles.HRManager));
        var isApprover = currentUser != null && (userRole.Equals(UserRoles.Approver, StringComparison.OrdinalIgnoreCase) ||
                         currentUser.UserRoles.Any(ur => ur.Role != null && ur.Role.Name == UserRoles.Approver));

        // Xác định bước pending đang chờ xử lý
        var pendingApprovals = requisition.Approvals
            .Where(a => a.Status == ApprovalStatus.PENDING && !a.IsDeleted)
            .OrderBy(a => a.StepOrder)
            .ThenByDescending(a => a.CreatedAt)
            .ToList();

        var currentPendingStep = pendingApprovals.FirstOrDefault();
        var canCurrentUserApprove = false;
        int? currentPendingStepOrder = null;

        if (requisition.Status == RequisitionStatus.PENDING_APPROVAL && currentPendingStep != null && currentUser != null)
        {
            currentPendingStepOrder = currentPendingStep.StepOrder;
            canCurrentUserApprove = isAdmin ||
                currentPendingStep.ApproverId == currentUserId ||
                (currentPendingStep.StepOrder == 1 && isHrManager) ||
                (currentPendingStep.StepOrder == 2 && isApprover);
        }

        var salaryDisplay = (requisition.MinSalary.HasValue && requisition.MaxSalary.HasValue)
            ? $"{requisition.MinSalary.Value:N0} - {requisition.MaxSalary.Value:N0} {requisition.Currency}"
            : "Thỏa thuận";

        // Xác định thông tin vị trí bàn làm việc hiện tại (Scrum #23)
        var isCurrentUserHiringManager = requisition.HiringManagerId == currentUserId;
        var currentApproverName = "—";
        var currentApproverRole = "—";
        var currentApproverEmail = string.Empty;
        var currentPendingSinceDisplay = "—";
        var currentWaitingDurationDisplay = "—";
        var currentStageSummary = string.Empty;

        if (requisition.Status == RequisitionStatus.PENDING_APPROVAL && currentPendingStep != null)
        {
            var approver = currentPendingStep.Approver;
            currentApproverName = approver?.FullName ?? (currentPendingStep.StepOrder == 1 ? "Nguyễn Mai Phương" : "Trần Đức Minh");
            currentApproverRole = currentPendingStep.StepOrder == 1 ? "Trưởng phòng Nhân sự (HR Manager)" : "Ban Giám Đốc (BOD / Approver)";
            currentApproverEmail = approver?.Email ?? (currentPendingStep.StepOrder == 1 ? "phuong.nguyen@noveratech.digital" : "minh.tran@noveratech.digital");
            currentPendingSinceDisplay = currentPendingStep.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");

            var waitingSpan = DateTimeOffset.UtcNow - currentPendingStep.CreatedAt;
            if (waitingSpan.TotalDays >= 1)
            {
                var days = (int)waitingSpan.TotalDays;
                var hours = waitingSpan.Hours;
                currentWaitingDurationDisplay = hours > 0 ? $"Đang chờ {days} ngày {hours} giờ" : $"Đang chờ {days} ngày";
            }
            else if (waitingSpan.TotalHours >= 1)
            {
                currentWaitingDurationDisplay = $"Đang chờ {(int)waitingSpan.TotalHours} giờ";
            }
            else
            {
                currentWaitingDurationDisplay = $"Đang chờ {(int)Math.Max(1, waitingSpan.TotalMinutes)} phút";
            }

            currentStageSummary = $"Cấp {currentPendingStep.StepOrder} / 2: Chờ {currentApproverName} ({currentApproverRole}) phê duyệt";
        }
        else if (requisition.Status == RequisitionStatus.APPROVED)
        {
            var lastStep = requisition.Approvals.Where(a => a.Status == ApprovalStatus.APPROVED).OrderByDescending(a => a.DecidedAt).FirstOrDefault();
            currentApproverName = lastStep?.Approver?.FullName ?? "Ban Giám Đốc";
            currentApproverRole = "Đã phê duyệt hoàn tất";
            currentApproverEmail = lastStep?.Approver?.Email ?? string.Empty;
            currentPendingSinceDisplay = lastStep?.DecidedAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? "—";
            currentWaitingDurationDisplay = "Hoàn tất";
            currentStageSummary = "Hồ sơ đã được phê duyệt hoàn tất ở tất cả các cấp (2/2 cấp)";
        }
        else if (requisition.Status == RequisitionStatus.CHANGES_REQUESTED)
        {
            currentApproverName = requisition.HiringManager?.FullName ?? "Người tạo yêu cầu";
            currentApproverRole = "Trưởng bộ phận (Cần cập nhật bổ sung)";
            currentApproverEmail = requisition.HiringManager?.Email ?? string.Empty;
            currentPendingSinceDisplay = requisition.UpdatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
            currentWaitingDurationDisplay = "Chờ cập nhật";
            currentStageSummary = "Hồ sơ đang ở bàn Bạn (Trưởng bộ phận) để bổ sung thông tin theo yêu cầu của Người duyệt";
        }
        else if (requisition.Status == RequisitionStatus.REJECTED)
        {
            var rejectedStep = requisition.Approvals.Where(a => a.Status == ApprovalStatus.REJECTED).OrderByDescending(a => a.DecidedAt).FirstOrDefault();
            currentApproverName = rejectedStep?.Approver?.FullName ?? "Người duyệt";
            currentApproverRole = "Đã từ chối";
            currentApproverEmail = rejectedStep?.Approver?.Email ?? string.Empty;
            currentPendingSinceDisplay = rejectedStep?.DecidedAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? "—";
            currentWaitingDurationDisplay = "Đã dừng";
            currentStageSummary = "Yêu cầu tuyển dụng đã bị từ chối phê duyệt";
        }

        var steps = requisition.Approvals
            .Where(a => !a.IsDeleted)
            .OrderBy(a => a.CreatedAt)
            .ThenBy(a => a.StepOrder)
            .Select(a =>
            {
                var durationDisplay = "—";
                if (a.DecidedAt.HasValue)
                {
                    var diff = a.DecidedAt.Value - a.CreatedAt;
                    durationDisplay = diff.TotalDays >= 1
                        ? $"Xử lý sau {(int)diff.TotalDays} ngày {(diff.Hours > 0 ? $"{diff.Hours} giờ" : "")}".Trim()
                        : diff.TotalHours >= 1
                            ? $"Xử lý sau {(int)diff.TotalHours} giờ {(diff.Minutes > 0 ? $"{diff.Minutes} phút" : "")}".Trim()
                            : $"Xử lý sau {(int)Math.Max(1, diff.TotalMinutes)} phút";
                }
                else if (currentPendingStep != null && a.Id == currentPendingStep.Id)
                {
                    durationDisplay = currentWaitingDurationDisplay;
                }

                var round = 1 + requisition.Approvals.Count(prev => prev.Status == ApprovalStatus.CHANGES_REQUESTED && prev.DecidedAt < a.CreatedAt);

                return new RequisitionApprovalStepDto
                {
                    StepOrder = a.StepOrder,
                    StepTitle = a.StepOrder == 1 ? "Cấp 1: Trưởng phòng Nhân sự (HR Manager)" : "Cấp 2: Ban Giám Đốc (BOD / Approver)",
                    ApproverId = a.ApproverId,
                    ApproverName = a.Approver?.FullName ?? "Người phê duyệt",
                    ApproverRole = a.StepOrder == 1 ? "HR Manager" : "BOD / Approver",
                    ApproverEmail = a.Approver?.Email ?? string.Empty,
                    Status = a.Status,
                    Comment = a.Comment,
                    DecidedAt = a.DecidedAt,
                    DecidedAtDisplay = a.DecidedAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss") ?? "—",
                    StepCreatedAtDisplay = a.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
                    DurationDisplay = durationDisplay,
                    RoundNumber = round,
                    IsCurrent = currentPendingStep != null && a.Id == currentPendingStep.Id,
                    IsImmutable = true
                };
            }).ToList();

        return new RequisitionDetailsViewModel
        {
            Id = requisition.Id,
            Code = requisition.Code,
            JobPositionTitle = requisition.JobPosition?.Title ?? "Chưa xác định",
            JobLevel = requisition.JobPosition?.JobLevel,
            DepartmentName = requisition.Department?.Name ?? "Chưa phân bổ",
            HiringManagerName = requisition.HiringManager?.FullName ?? "Chưa gán",
            HiringManagerEmail = requisition.HiringManager?.Email ?? string.Empty,
            AssignedRecruiterName = requisition.AssignedRecruiter?.FullName,
            Quantity = requisition.Quantity,
            HeadcountType = requisition.HeadcountType,
            Reason = requisition.Reason,
            SalaryDisplay = salaryDisplay,
            SalaryBandExplanation = requisition.SalaryBandExplanation,
            TargetHireDateDisplay = requisition.TargetHireDate?.ToString("dd/MM/yyyy") ?? "—",
            Status = requisition.Status,
            JobDescription = requisition.JobDescription,
            Requirements = requisition.Requirements,
            CreatedAtDisplay = requisition.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
            CanCurrentUserApprove = canCurrentUserApprove,
            CurrentPendingStepOrder = currentPendingStepOrder,
            CurrentApproverName = currentApproverName,
            CurrentApproverRole = currentApproverRole,
            CurrentApproverEmail = currentApproverEmail,
            CurrentPendingSinceDisplay = currentPendingSinceDisplay,
            CurrentWaitingDurationDisplay = currentWaitingDurationDisplay,
            CurrentStageSummary = currentStageSummary,
            IsCurrentUserHiringManager = isCurrentUserHiringManager,
            ApprovalSteps = steps
        };
    }

    public async Task<Ats.Web.Models.DTOs.PagedResult<RequisitionApprovalListItemViewModel>> GetRequisitionsForApprovalAsync(
        Guid currentUserId,
        string? tab = null,
        string? search = null,
        Guid? departmentId = null,
        Guid? recruiterId = null,
        DateOnly? fromDate = null,
        DateOnly? toDate = null,
        int page = 1,
        int pageSize = 15,
        CancellationToken cancellationToken = default)
    {
        var currentUser = await _dbContext.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == currentUserId, cancellationToken);

        var userRole = currentUser?.Role ?? string.Empty;
        var isAdmin = currentUser != null && (userRole.Equals(UserRoles.Admin, StringComparison.OrdinalIgnoreCase) ||
                      currentUser.UserRoles.Any(ur => ur.Role != null && ur.Role.Name == UserRoles.Admin));
        var isHrManager = currentUser != null && (userRole.Equals(UserRoles.HRManager, StringComparison.OrdinalIgnoreCase) ||
                          currentUser.UserRoles.Any(ur => ur.Role != null && ur.Role.Name == UserRoles.HRManager));
        var isApprover = currentUser != null && (userRole.Equals(UserRoles.Approver, StringComparison.OrdinalIgnoreCase) ||
                         currentUser.UserRoles.Any(ur => ur.Role != null && ur.Role.Name == UserRoles.Approver));

        var query = _dbContext.JobRequisitions
            .Include(r => r.JobPosition)
            .Include(r => r.Department)
            .Include(r => r.HiringManager)
            .Include(r => r.AssignedRecruiter)
            .Include(r => r.Approvals)
                .ThenInclude(a => a.Approver)
            .Where(r => !r.IsDeleted && r.Status != RequisitionStatus.DRAFT);

        if (!isAdmin && !isHrManager && !isApprover)
        {
            query = query.Where(r => r.HiringManagerId == currentUserId);
        }

        if (departmentId.HasValue)
        {
            query = query.Where(r => r.DepartmentId == departmentId.Value);
        }

        if (recruiterId.HasValue)
        {
            query = query.Where(r => r.AssignedRecruiterId == recruiterId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(r => r.Code.ToLower().Contains(s) ||
                                     (r.JobPosition != null && r.JobPosition.Title.ToLower().Contains(s)) ||
                                     (r.Department != null && r.Department.Name.ToLower().Contains(s)));
        }

        var list = await query
            .OrderByDescending(r => r.UpdatedAt)
            .ToListAsync(cancellationToken);

        var items = new List<RequisitionApprovalListItemViewModel>();
        var today = DateOnly.FromDateTime(DateTime.Today);

        foreach (var r in list)
        {
            if (fromDate.HasValue && DateOnly.FromDateTime(r.CreatedAt.Date) < fromDate.Value) continue;
            if (toDate.HasValue && DateOnly.FromDateTime(r.CreatedAt.Date) > toDate.Value) continue;

            var pendingApprovals = r.Approvals
                .Where(a => a.Status == ApprovalStatus.PENDING && !a.IsDeleted)
                .OrderBy(a => a.StepOrder)
                .ThenByDescending(a => a.CreatedAt)
                .ToList();

            var currentPendingStep = pendingApprovals.FirstOrDefault();
            var canApprove = false;
            var currentStepOrder = currentPendingStep?.StepOrder ?? 0;
            var currentApproverName = currentPendingStep?.Approver?.FullName ?? (r.Status == RequisitionStatus.APPROVED ? "Đã duyệt hoàn tất" : "—");
            var currentApproverRole = string.Empty;
            var currentApproverEmail = string.Empty;
            var waitingSinceDisplay = "—";
            var waitingDurationDisplay = "—";
            var approvalChainProgress = string.Empty;

            if (r.Status == RequisitionStatus.PENDING_APPROVAL && currentPendingStep != null && currentUser != null)
            {
                canApprove = isAdmin ||
                    currentPendingStep.ApproverId == currentUserId ||
                    (currentPendingStep.StepOrder == 1 && isHrManager) ||
                    (currentPendingStep.StepOrder == 2 && isApprover);

                currentApproverRole = currentPendingStep.StepOrder == 1 ? "HR Manager" : "BOD / Approver";
                currentApproverEmail = currentPendingStep.Approver?.Email ?? string.Empty;
                waitingSinceDisplay = currentPendingStep.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");

                var span = DateTimeOffset.UtcNow - currentPendingStep.CreatedAt;
                waitingDurationDisplay = span.TotalDays >= 1
                    ? $"{(int)span.TotalDays} ngày"
                    : span.TotalHours >= 1
                        ? $"{(int)span.TotalHours} giờ"
                        : $"{(int)Math.Max(1, span.TotalMinutes)} phút";

                approvalChainProgress = currentPendingStep.StepOrder == 1
                    ? "Cấp 1: Chờ HR duyệt"
                    : "Cấp 2: Chờ BOD duyệt";
            }
            else if (r.Status == RequisitionStatus.APPROVED)
            {
                approvalChainProgress = "Đã duyệt 2/2 cấp";
                waitingDurationDisplay = "Hoàn tất";
            }
            else if (r.Status == RequisitionStatus.CHANGES_REQUESTED)
            {
                approvalChainProgress = "Cần bổ sung hồ sơ";
                currentApproverName = r.HiringManager?.FullName ?? "Người tạo";
                currentApproverRole = "Trưởng bộ phận";
                waitingDurationDisplay = "Chờ cập nhật";
            }
            else if (r.Status == RequisitionStatus.REJECTED)
            {
                approvalChainProgress = "Từ chối duyệt";
                waitingDurationDisplay = "Đã dừng";
            }

            var latestComment = r.Approvals
                .Where(a => a.Status == ApprovalStatus.CHANGES_REQUESTED || a.Status == ApprovalStatus.REJECTED)
                .OrderByDescending(a => a.DecidedAt ?? a.CreatedAt)
                .Select(a => a.Comment)
                .FirstOrDefault();

            var salaryDisplay = (r.MinSalary.HasValue && r.MaxSalary.HasValue)
                ? $"{r.MinSalary.Value:N0} - {r.MaxSalary.Value:N0} {r.Currency}"
                : "Thỏa thuận";

            var isCreatedByCurrentUser = r.HiringManagerId == currentUserId;
            
            int openDays = (int)(DateTimeOffset.UtcNow - r.CreatedAt).TotalDays;
            int? remainingDays = r.TargetHireDate.HasValue 
                ? r.TargetHireDate.Value.DayNumber - today.DayNumber 
                : null;
            bool isOverdue = remainingDays.HasValue && remainingDays.Value < 0 && r.Status != RequisitionStatus.APPROVED && r.Status != RequisitionStatus.REJECTED;

            var item = new RequisitionApprovalListItemViewModel
            {
                Id = r.Id,
                Code = r.Code,
                JobTitle = r.JobPosition?.Title ?? "Chưa xác định",
                DepartmentName = r.Department?.Name ?? "Chưa phân bổ",
                Quantity = r.Quantity,
                HeadcountType = r.HeadcountType,
                SalaryDisplay = salaryDisplay,
                TargetHireDateDisplay = r.TargetHireDate?.ToString("dd/MM/yyyy") ?? "—",
                HiringManagerName = r.HiringManager?.FullName ?? "Chưa rõ",
                Status = r.Status,
                CurrentStepOrder = currentStepOrder,
                CurrentApproverName = currentApproverName,
                CurrentApproverRole = currentApproverRole,
                CurrentApproverEmail = currentApproverEmail,
                CanCurrentUserApprove = canApprove,
                CreatedAtDisplay = r.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy"),
                LatestComment = latestComment,
                WaitingSinceDisplay = waitingSinceDisplay,
                WaitingDurationDisplay = waitingDurationDisplay,
                ApprovalChainProgress = approvalChainProgress,
                IsCreatedByCurrentUser = isCreatedByCurrentUser,
                OpenDays = openDays,
                RemainingDays = remainingDays,
                IsOverdue = isOverdue,
                AssignedRecruiterName = r.AssignedRecruiter?.FullName,
                CreatedAt = r.CreatedAt,
                DepartmentId = r.DepartmentId,
                AssignedRecruiterId = r.AssignedRecruiterId
            };

            var tabKey = tab?.Trim().ToLowerInvariant() ?? "all";
            bool matchTab = tabKey switch
            {
                "my-requisitions" or "cua-toi" or "yeu-cau-cua-toi" => isCreatedByCurrentUser,
                "my-pending" or "cho-toi-duyet" => canApprove && r.Status == RequisitionStatus.PENDING_APPROVAL,
                "pending" or "cho-duyet" => r.Status == RequisitionStatus.PENDING_APPROVAL,
                "approved" or "da-duyet" => r.Status == RequisitionStatus.APPROVED,
                "changes-requested" or "yeu-cau-bo-sung" => r.Status == RequisitionStatus.CHANGES_REQUESTED,
                "rejected" or "tu-choi" => r.Status == RequisitionStatus.REJECTED,
                _ => true
            };

            if (matchTab)
            {
                items.Add(item);
            }
        }

        var totalCount = items.Count;
        var pagedItems = items.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return new Ats.Web.Models.DTOs.PagedResult<RequisitionApprovalListItemViewModel>
        {
            Items = pagedItems,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<int> GetPendingApprovalCountForUserAsync(
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var items = await GetRequisitionsForApprovalAsync(currentUserId, "my-pending", null, null, null, null, null, 1, 9999, cancellationToken);
        return items.TotalCount;
    }

    public async Task<int> GetMyRequisitionsCountAsync(
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.JobRequisitions
            .Where(r => r.HiringManagerId == currentUserId && !r.IsDeleted && r.Status != RequisitionStatus.DRAFT)
            .CountAsync(cancellationToken);
    }

    private async Task<(Guid HrApproverId, Guid BodApproverId)> GetDefaultApproversAsync(Guid fallbackUserId, CancellationToken cancellationToken)
    {
        // 1. HR Manager (Step 1)
        var hrUser = await _dbContext.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Email == "phuong.nguyen@noveratech.digital" || u.Email == "hr@noveratech.vn", cancellationToken);

        if (hrUser == null)
        {
            hrUser = await _dbContext.Users
                .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
                .FirstOrDefaultAsync(u => u.Role == UserRoles.HRManager || u.UserRoles.Any(ur => ur.Role != null && ur.Role.Name == UserRoles.HRManager), cancellationToken);
        }

        // 2. BOD / Approver (Step 2)
        var bodUser = await _dbContext.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Email == "minh.tran@noveratech.digital" || u.Email == "bod@noveratech.vn", cancellationToken);

        if (bodUser == null)
        {
            bodUser = await _dbContext.Users
                .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
                .FirstOrDefaultAsync(u => u.Role == UserRoles.Approver || u.UserRoles.Any(ur => ur.Role != null && ur.Role.Name == UserRoles.Approver), cancellationToken);
        }

        var defaultAdmin = await _dbContext.Users.FirstOrDefaultAsync(cancellationToken);
        var hrId = hrUser?.Id ?? defaultAdmin?.Id ?? fallbackUserId;
        var bodId = bodUser?.Id ?? defaultAdmin?.Id ?? fallbackUserId;

        return (hrId, bodId);
    }

    private static string StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;

        var decoded = System.Net.WebUtility.HtmlDecode(html)
            .Replace("&nbsp;", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("<br>", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("<br/>", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("<br />", " ", StringComparison.OrdinalIgnoreCase);

        return System.Text.RegularExpressions.Regex.Replace(decoded, "<.*?>", string.Empty).Trim();
    }
}
