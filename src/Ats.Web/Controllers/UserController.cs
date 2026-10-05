using Ats.Web.Data;
using Ats.Web.Models.DTOs;
using Ats.Web.Models.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ats.Web.Controllers;

[ApiController]
[Route("api/tai-khoan")]
public class UserController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public UserController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// API tìm kiếm và lọc tài khoản nội bộ (GET: /api/tai-khoan/tim-kiem)
    /// </summary>
    [HttpGet("tim-kiem")]
    public async Task<IActionResult> SearchUsers([FromQuery] string? keyword, [FromQuery] string? role, [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        // Yêu cầu đăng nhập (SCRUM-94)
        var userIdString = HttpContext.Session.GetString("UserId");
        if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out _))
        {
            return Unauthorized(new { isSuccess = false, message = "Vui lòng đăng nhập để thực hiện chức năng này." });
        }

        var query = _dbContext.Users.AsQueryable();

        // Lọc theo keyword (chỉ lọc khi từ khóa có từ 3 ký tự trở lên)
        if (!string.IsNullOrWhiteSpace(keyword) && keyword.Trim().Length >= 3)
        {
            var keywordLower = keyword.Trim().ToLower();
            query = query.Where(u => 
                u.Email.ToLower().Contains(keywordLower) || 
                u.FullName.ToLower().Contains(keywordLower) || 
                (u.Department != null && u.Department.ToLower().Contains(keywordLower))
            );
        }

        // Lọc theo Role
        if (!string.IsNullOrWhiteSpace(role))
        {
            query = query.Where(u => u.Role == role);
        }

        // Lọc theo Status (ACTIVE, INACTIVE, etc.)
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(u => u.Status == status);
        }

        // Sắp xếp mặc định theo ngày tạo mới nhất
        query = query.OrderByDescending(u => u.CreatedAt);

        // SCRUM-130: Đếm tổng số bản ghi và áp dụng phân trang
        var totalRecords = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalRecords / (double)pageSize);

        query = query.Skip((page - 1) * pageSize).Take(pageSize);

        var users = await query.Select(u => new UserSearchResponseDto(
            u.Id,
            u.Email,
            u.FullName,
            u.Role,
            u.Department,
            u.Status,
            u.CreatedAt
        )).ToListAsync();

        return Ok(new
        {
            isSuccess = true,
            totalRecords = totalRecords,
            totalPages = totalPages,
            currentPage = page,
            pageSize = pageSize,
            data = users
        });
    }

    /// <summary>
    /// API Cập nhật thông tin tài khoản (PUT: /api/tai-khoan/{id})
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateUser(Guid id, [FromBody] UpdateUserRequestDto request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        // Kiểm tra quyền Admin (SCRUM-132)
        var currentUserIdString = HttpContext.Session.GetString("UserId");
        if (string.IsNullOrEmpty(currentUserIdString) || !Guid.TryParse(currentUserIdString, out var currentUserId))
        {
            return Unauthorized(new { isSuccess = false, message = "Vui lòng đăng nhập để thực hiện chức năng này." });
        }

        var currentUser = await _dbContext.Users.FindAsync(currentUserId);
        if (currentUser == null || currentUser.Role != "Admin")
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { isSuccess = false, message = "Bạn không có quyền thực hiện chức năng này." });
        }

        // Validate dữ liệu đầu vào
        var validRoles = new[] { "Admin", "HR", "Interviewer", "Candidate" };
        if (!validRoles.Contains(request.Role))
        {
            return BadRequest(new { isSuccess = false, message = "Vai trò không hợp lệ." });
        }

        var validStatuses = new[] { "ACTIVE", "INACTIVE", "PENDING" };
        if (!validStatuses.Contains(request.Status))
        {
            return BadRequest(new { isSuccess = false, message = "Trạng thái không hợp lệ." });
        }

        var targetUser = await _dbContext.Users.FindAsync(id);
        if (targetUser == null)
        {
            return NotFound(new { isSuccess = false, message = "Không tìm thấy tài khoản." });
        }

        // Cập nhật thông tin
        targetUser.FullName = request.FullName.Trim();
        targetUser.Department = string.IsNullOrWhiteSpace(request.Department) ? null : request.Department.Trim();
        targetUser.Role = request.Role;
        targetUser.Status = request.Status;
        
        // Ghi nhận audit
        targetUser.UpdatedAt = DateTimeOffset.UtcNow;
        targetUser.UpdatedBy = currentUserId;

        await _dbContext.SaveChangesAsync();

        return Ok(new { isSuccess = true, message = "Cập nhật thông tin tài khoản thành công." });
    }

    /// <summary>
    /// SCRUM-193: API lấy thông tin hồ sơ cá nhân
    /// GET: /api/tai-khoan/ho-so
    /// </summary>
    [HttpGet("ho-so")]
    public async Task<IActionResult> GetProfile()
    {
        var currentUserIdString = HttpContext.Session.GetString("UserId");
        if (string.IsNullOrEmpty(currentUserIdString) || !Guid.TryParse(currentUserIdString, out var currentUserId))
        {
            return Unauthorized(new { isSuccess = false, message = "Vui lòng đăng nhập để thực hiện chức năng này." });
        }

        var currentUser = await _dbContext.Users.FindAsync(currentUserId);
        if (currentUser == null)
        {
            return NotFound(new { isSuccess = false, message = "Không tìm thấy tài khoản người dùng." });
        }

        var profile = new UserProfileResponseDto
        {
            FullName = currentUser.FullName,
            Email = currentUser.Email,
            PhoneNumber = currentUser.PhoneNumber,
            JobTitle = currentUser.JobTitle,
            Department = currentUser.Department,
            Role = currentUser.Role
        };

        return Ok(new { isSuccess = true, data = profile });
    }

    /// <summary>
    /// SCRUM-187: API cập nhật thông tin cá nhân (hồ sơ) của người dùng đang đăng nhập
    /// PUT: /api/tai-khoan/ho-so
    /// </summary>
    [HttpPut("ho-so")]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequestDto request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new { isSuccess = false, message = "Dữ liệu đầu vào không hợp lệ.", errors = ModelState });
        }

        // SCRUM-189: Từ chối thay đổi các trường bị khóa
        if (!string.IsNullOrEmpty(request.Email) || !string.IsNullOrEmpty(request.Department) || !string.IsNullOrEmpty(request.Role))
        {
            return BadRequest(new { isSuccess = false, message = "Bạn không có quyền thay đổi Email, Phòng ban hoặc Vai trò. Vui lòng liên hệ Admin để được hỗ trợ." });
        }

        var currentUserIdString = HttpContext.Session.GetString("UserId");
        if (string.IsNullOrEmpty(currentUserIdString) || !Guid.TryParse(currentUserIdString, out var currentUserId))
        {
            return Unauthorized(new { isSuccess = false, message = "Vui lòng đăng nhập để thực hiện chức năng này." });
        }

        var currentUser = await _dbContext.Users.FindAsync(currentUserId);
        if (currentUser == null)
        {
            return NotFound(new { isSuccess = false, message = "Không tìm thấy tài khoản người dùng." });
        }

        // Tạo bản ghi theo dõi thay đổi (Audit log)
        var oldValues = new Dictionary<string, string?>();
        var newValues = new Dictionary<string, string?>();

        var newFullName = request.FullName.Trim();
        if (currentUser.FullName != newFullName)
        {
            oldValues["FullName"] = currentUser.FullName;
            newValues["FullName"] = newFullName;
            currentUser.FullName = newFullName;
        }

        var newPhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim();
        if (currentUser.PhoneNumber != newPhoneNumber)
        {
            oldValues["PhoneNumber"] = currentUser.PhoneNumber;
            newValues["PhoneNumber"] = newPhoneNumber;
            currentUser.PhoneNumber = newPhoneNumber;
        }

        var newJobTitle = string.IsNullOrWhiteSpace(request.JobTitle) ? null : request.JobTitle.Trim();
        if (currentUser.JobTitle != newJobTitle)
        {
            oldValues["JobTitle"] = currentUser.JobTitle;
            newValues["JobTitle"] = newJobTitle;
            currentUser.JobTitle = newJobTitle;
        }

        // Nếu không có gì thay đổi
        if (oldValues.Count == 0)
        {
            return Ok(new { isSuccess = true, message = "Không có thông tin nào được thay đổi." });
        }

        // Ghi nhận audit
        currentUser.UpdatedAt = DateTimeOffset.UtcNow;
        currentUser.UpdatedBy = currentUserId;

        // Lưu vào bảng audit_logs
        var auditLog = new AuditLog
        {
            UserId = currentUserId,
            Action = "UPDATE_PROFILE",
            EntityName = "User",
            EntityId = currentUserId.ToString(),
            OldValues = System.Text.Json.JsonSerializer.Serialize(oldValues),
            NewValues = System.Text.Json.JsonSerializer.Serialize(newValues),
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = Request.Headers["User-Agent"].ToString()
        };
        _dbContext.AuditLogs.Add(auditLog);

        try
        {
            await _dbContext.SaveChangesAsync();
            return Ok(new { isSuccess = true, message = "Cập nhật hồ sơ thành công." });
        }
        catch (Exception)
        {
            return StatusCode(500, new { isSuccess = false, message = "Lỗi hệ thống khi cập nhật hồ sơ." });
        }
    }
}
