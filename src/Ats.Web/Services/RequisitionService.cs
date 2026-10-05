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

            if (existing.Status != RequisitionStatus.DRAFT)
            {
                return (false, "Chỉ có thể chỉnh sửa yêu cầu tuyển dụng đang ở trạng thái Bản nháp.", null, null);
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
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted && r.Status == RequisitionStatus.DRAFT, cancellationToken);

        if (requisition == null) return null;

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == currentUserId, cancellationToken);
        var userRole = user?.Role ?? string.Empty;
        var isAdminOrHR = userRole.Equals(UserRoles.Admin, StringComparison.OrdinalIgnoreCase) ||
                          userRole.Equals(UserRoles.HRManager, StringComparison.OrdinalIgnoreCase);

        if (!isAdminOrHR && requisition.HiringManagerId != currentUserId)
        {
            return null;
        }

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
            TargetHireDate = requisition.TargetHireDate,
            JobDescription = requisition.JobDescription,
            Requirements = requisition.Requirements,
            IsDraft = true
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
