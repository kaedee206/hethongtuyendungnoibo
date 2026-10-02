using Ats.Web.Data;
using Ats.Web.Models.DTOs;
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

        // Lọc theo keyword (tìm trong Email, FullName, Department - case insensitive, partial match)
        if (!string.IsNullOrWhiteSpace(keyword))
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

        // Cập nhật các trường được phép
        currentUser.FullName = request.FullName.Trim();
        currentUser.PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim();
        currentUser.JobTitle = string.IsNullOrWhiteSpace(request.JobTitle) ? null : request.JobTitle.Trim();

        // Ghi nhận audit
        currentUser.UpdatedAt = DateTimeOffset.UtcNow;
        currentUser.UpdatedBy = currentUserId;

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
