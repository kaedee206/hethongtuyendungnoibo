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
        // 1. Kiểm tra tồn tại của chức danh
        var position = await _dbContext.JobPositions
            .FirstOrDefaultAsync(p => p.Id == model.JobPositionId && p.IsActive && !p.IsDeleted, cancellationToken);

        if (position == null)
        {
            return (false, "Chức danh được chọn không tồn tại hoặc đã ngừng hoạt động.", null);
        }

        // 2. Kiểm tra tồn tại của phòng ban
        var department = await _dbContext.Departments
            .FirstOrDefaultAsync(d => d.Id == model.DepartmentId && d.IsActive, cancellationToken);

        if (department == null)
        {
            return (false, "Phòng ban được chọn không tồn tại hoặc đã ngừng hoạt động.", null);
        }

        // 3. Kiểm tra số lượng
        if (model.Quantity < 1)
        {
            return (false, "Số lượng tuyển dụng phải từ 1 người trở lên.", null);
        }

        // 4. Kiểm tra dải lương
        if (!model.MinSalary.HasValue || !model.MaxSalary.HasValue)
        {
            return (false, "Vui lòng nhập đầy đủ dải lương đề xuất (lương tối thiểu và tối đa).", null);
        }

        if (model.MinSalary.Value <= 0 || model.MaxSalary.Value <= 0)
        {
            return (false, "Mức lương đề xuất phải là số dương lớn hơn 0.", null);
        }

        if (model.MaxSalary.Value < model.MinSalary.Value)
        {
            return (false, "Mức lương tối đa đề xuất không được nhỏ hơn mức lương tối thiểu.", null);
        }

        // 5. Kiểm tra ngày cần người
        if (!model.TargetHireDate.HasValue || model.TargetHireDate.Value < DateOnly.FromDateTime(DateTime.Today))
        {
            return (false, "Ngày cần nhân sự có mặt phải từ ngày hôm nay trở đi.", null);
        }

        // 6. Kiểm tra bắt buộc mô tả công việc và yêu cầu ứng viên khi gửi duyệt
        if (!model.IsDraft)
        {
            if (string.IsNullOrWhiteSpace(StripHtml(model.JobDescription)))
            {
                return (false, "Vui lòng nhập mô tả công việc (trách nhiệm, nhiệm vụ chính, KPI...) khi gửi duyệt.", null);
            }

            if (string.IsNullOrWhiteSpace(StripHtml(model.Requirements)))
            {
                return (false, "Vui lòng nhập yêu cầu ứng viên (trình độ học vấn, kinh nghiệm, kỹ năng, chứng chỉ...) khi gửi duyệt.", null);
            }
        }

        // 7. Sinh mã yêu cầu tuyển dụng chuẩn REQ-yyyyMMdd-XXXX
        var datePrefix = DateTime.UtcNow.ToString("yyyyMMdd");
        var randomSuffix = Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
        var requisitionCode = $"REQ-{datePrefix}-{randomSuffix}";

        // Xử lý lý do tuyển dụng
        var reasonText = string.IsNullOrWhiteSpace(model.ReasonDetail)
            ? (model.HeadcountType == HeadcountType.REPLACEMENT ? "Thay thế nhân sự nghỉ việc" : "Tăng mới headcount mở rộng dự án")
            : model.ReasonDetail.Trim();

        var initialStatus = model.IsDraft ? RequisitionStatus.DRAFT : RequisitionStatus.PENDING_APPROVAL;

        var requisition = new JobRequisition
        {
            Id = Guid.NewGuid(),
            Code = requisitionCode,
            JobPositionId = position.Id,
            DepartmentId = department.Id,
            HiringManagerId = currentUserId,
            Quantity = model.Quantity,
            HeadcountType = model.HeadcountType,
            Reason = reasonText,
            MinSalary = model.MinSalary.Value,
            MaxSalary = model.MaxSalary.Value,
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

        _logger.LogInformation("Người dùng {UserId} đã tạo thành công yêu cầu tuyển dụng {Code} (Trạng thái: {Status}) cho vị trí {Position} tại phòng {Department}",
            currentUserId, requisitionCode, initialStatus, position.Title, department.Name);

        var responseMessage = model.IsDraft
            ? $"Bản nháp yêu cầu tuyển dụng '{requisitionCode}' đã được lưu thành công."
            : $"Yêu cầu tuyển dụng '{requisitionCode}' đã được khởi tạo thành công và chuyển sang trạng thái Chờ duyệt.";

        return (true, responseMessage, requisition.Id);
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
