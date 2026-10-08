using Ats.Web.Data;
using Ats.Web.Models.DTOs;
using Ats.Web.Models.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ats.Web.Controllers;

[ApiController]
[Route("api/phong-ban")]
public class DepartmentsController : Controller
{
    private readonly ApplicationDbContext _dbContext;

    public DepartmentsController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet("/phong-ban")]
    [HttpGet("/departments")]
    public async Task<IActionResult> Index()
    {
        var users = await _dbContext.Users
            .Where(u => u.Status == "ACTIVE")
            .OrderBy(u => u.FullName)
            .Select(u => new { u.Id, u.FullName, u.Email, u.Role })
            .ToListAsync();
            
        ViewBag.ActiveUsers = users;
        return View("~/Views/Departments/Index.cshtml");
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] DepartmentCreateRequestDto request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        // check duplicate code
        if (await _dbContext.Departments.AnyAsync(d => d.Code == request.Code && !d.IsDeleted))
        {
            return BadRequest(new { isSuccess = false, message = "Mã phòng ban đã tồn tại." });
        }

        // Validate tên phòng ban không trùng trong cùng cấp cha
        if (await _dbContext.Departments.AnyAsync(d => d.Name == request.Name && d.ParentId == request.ParentId && !d.IsDeleted))
        {
            return BadRequest(new { isSuccess = false, message = "Tên phòng ban đã tồn tại trong cùng cấp." });
        }

        var newDept = new Department
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Code = request.Code,
            ParentId = request.ParentId,
            ManagerId = request.ManagerId,
            IsActive = true
        };

        if (request.ParentId.HasValue)
        {
            var parent = await _dbContext.Departments.FindAsync(request.ParentId.Value);
            if (parent == null)
            {
                return BadRequest(new { isSuccess = false, message = "Phòng ban cha không tồn tại." });
            }
            newDept.Level = parent.Level + 1;
            newDept.Path = $"{parent.Path}{newDept.Id}/";
        }
        else
        {
            newDept.Level = 1;
            newDept.Path = $"/{newDept.Id}/";
        }

        _dbContext.Departments.Add(newDept);
        await _dbContext.SaveChangesAsync();

        return Ok(new { isSuccess = true, message = "Tạo phòng ban thành công.", data = newDept.Id });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] DepartmentUpdateRequestDto request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var dept = await _dbContext.Departments.FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted);
        if (dept == null)
        {
            return NotFound(new { isSuccess = false, message = "Không tìm thấy phòng ban." });
        }

        // check duplicate code
        if (await _dbContext.Departments.AnyAsync(d => d.Code == request.Code && d.Id != id && !d.IsDeleted))
        {
            return BadRequest(new { isSuccess = false, message = "Mã phòng ban đã tồn tại." });
        }

        // Validate tên phòng ban không trùng trong cùng cấp cha
        if (await _dbContext.Departments.AnyAsync(d => d.Name == request.Name && d.ParentId == request.ParentId && d.Id != id && !d.IsDeleted))
        {
            return BadRequest(new { isSuccess = false, message = "Tên phòng ban đã tồn tại trong cùng cấp." });
        }

        // Validate vòng lặp (cyclic loop)
        if (request.ParentId.HasValue)
        {
            if (request.ParentId.Value == id)
            {
                return BadRequest(new { isSuccess = false, message = "Phòng ban cha không thể là chính nó." });
            }

            var parent = await _dbContext.Departments.FindAsync(request.ParentId.Value);
            if (parent == null)
            {
                return BadRequest(new { isSuccess = false, message = "Phòng ban cha không tồn tại." });
            }

            // Check if new parent is a descendant of current department
            if (parent.Path.Contains($"/{id}/"))
            {
                return BadRequest(new { isSuccess = false, message = "Không thể chọn phòng ban con làm phòng ban cha (tạo vòng lặp)." });
            }
            
            // Nếu đổi phòng ban cha, cập nhật Path và Level cho chính nó và toàn bộ phòng ban con
            if (dept.ParentId != request.ParentId.Value)
            {
                var oldPath = dept.Path;
                dept.Level = parent.Level + 1;
                dept.Path = $"{parent.Path}{id}/";
                
                var newPath = dept.Path;
                
                var descendants = await _dbContext.Departments
                    .Where(d => d.Path.StartsWith(oldPath) && d.Id != id)
                    .ToListAsync();
                    
                foreach (var child in descendants)
                {
                    child.Path = child.Path.Replace(oldPath, newPath);
                    child.Level = child.Path.Split('/', StringSplitOptions.RemoveEmptyEntries).Length;
                }
            }
        }
        else
        {
            if (dept.ParentId != null)
            {
                // Chuyển thành thư mục gốc
                var oldPath = dept.Path;
                dept.Level = 1;
                dept.Path = $"/{id}/";
                var newPath = dept.Path;
                
                var descendants = await _dbContext.Departments
                    .Where(d => d.Path.StartsWith(oldPath) && d.Id != id)
                    .ToListAsync();
                    
                foreach (var child in descendants)
                {
                    child.Path = child.Path.Replace(oldPath, newPath);
                    child.Level = child.Path.Split('/', StringSplitOptions.RemoveEmptyEntries).Length;
                }
            }
        }

        dept.Name = request.Name;
        dept.Code = request.Code;
        dept.ParentId = request.ParentId;
        dept.ManagerId = request.ManagerId;
        dept.IsActive = request.IsActive;
        dept.UpdatedAt = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync();

        return Ok(new { isSuccess = true, message = "Cập nhật phòng ban thành công." });
    }

    [HttpGet("tree")]
    public async Task<IActionResult> GetTree([FromQuery] bool? isActive = null)
    {
        var query = _dbContext.Departments
            .Include(d => d.Manager)
            .Where(d => !d.IsDeleted);

        if (isActive.HasValue)
        {
            query = query.Where(d => d.IsActive == isActive.Value);
        }

        var allDepts = await query
            .OrderBy(d => d.Level)
            .ThenBy(d => d.Name)
            .Select(d => new DepartmentResponseDto
            {
                Id = d.Id,
                Code = d.Code,
                Name = d.Name,
                ParentId = d.ParentId,
                ManagerId = d.ManagerId,
                ManagerName = d.Manager != null ? d.Manager.FullName : null,
                ManagerEmail = d.Manager != null ? d.Manager.Email : null,
                ManagerTitle = d.Manager != null && d.Manager.JobPosition != null ? d.Manager.JobPosition.Title : null,
                IsActive = d.IsActive,
                Level = d.Level,
                Path = d.Path,
                CreatedAt = d.CreatedAt
            })
            .ToListAsync();

        var dict = allDepts.ToDictionary(d => d.Id);
        var roots = new List<DepartmentResponseDto>();

        foreach (var dept in allDepts)
        {
            if (dept.ParentId.HasValue && dict.TryGetValue(dept.ParentId.Value, out var parent))
            {
                parent.Children.Add(dept);
            }
            else
            {
                roots.Add(dept);
            }
        }

        return Ok(new { isSuccess = true, data = roots });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetDetail(Guid id)
    {
        var dept = await _dbContext.Departments
            .Include(d => d.Manager)
            .Where(d => d.Id == id && !d.IsDeleted)
            .Select(d => new DepartmentResponseDto
            {
                Id = d.Id,
                Code = d.Code,
                Name = d.Name,
                ParentId = d.ParentId,
                ManagerId = d.ManagerId,
                ManagerName = d.Manager != null ? d.Manager.FullName : null,
                ManagerEmail = d.Manager != null ? d.Manager.Email : null,
                ManagerTitle = d.Manager != null && d.Manager.JobPosition != null ? d.Manager.JobPosition.Title : null,
                IsActive = d.IsActive,
                Level = d.Level,
                Path = d.Path,
                CreatedAt = d.CreatedAt
            })
            .FirstOrDefaultAsync();

        if (dept == null)
            return NotFound(new { isSuccess = false, message = "Không tìm thấy phòng ban." });

        // Get direct children
        var children = await _dbContext.Departments
            .Include(d => d.Manager)
            .Where(d => d.ParentId == id && !d.IsDeleted)
            .OrderBy(d => d.Name)
            .Select(d => new DepartmentResponseDto
            {
                Id = d.Id,
                Code = d.Code,
                Name = d.Name,
                ParentId = d.ParentId,
                ManagerId = d.ManagerId,
                ManagerName = d.Manager != null ? d.Manager.FullName : null,
                ManagerEmail = d.Manager != null ? d.Manager.Email : null,
                ManagerTitle = d.Manager != null && d.Manager.JobPosition != null ? d.Manager.JobPosition.Title : null,
                IsActive = d.IsActive,
                Level = d.Level,
                Path = d.Path,
                CreatedAt = d.CreatedAt
            })
            .ToListAsync();

        dept.Children = children;

        return Ok(new { isSuccess = true, data = dept });
    }

    [HttpPut("{id}/manager")]
    public async Task<IActionResult> AssignManager(Guid id, [FromBody] AssignManagerRequestDto request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var dept = await _dbContext.Departments.FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted);
        if (dept == null)
            return NotFound(new { isSuccess = false, message = "Không tìm thấy phòng ban." });

        var manager = await _dbContext.Users.FindAsync(request.ManagerId);
        if (manager == null)
            return BadRequest(new { isSuccess = false, message = "Người phụ trách không tồn tại." });

        if (manager.Status != "ACTIVE")
            return BadRequest(new { isSuccess = false, message = "Người phụ trách phải ở trạng thái ACTIVE." });

        var oldManagerId = dept.ManagerId;
        
        if (oldManagerId == request.ManagerId)
            return Ok(new { isSuccess = true, message = "Người phụ trách không thay đổi." });

        dept.ManagerId = request.ManagerId;
        dept.UpdatedAt = DateTimeOffset.UtcNow;
        
        var currentUserIdString = HttpContext?.Session?.GetString("UserId");
        Guid? currentUserId = null;
        if (!string.IsNullOrEmpty(currentUserIdString) && Guid.TryParse(currentUserIdString, out var parsedId))
        {
            currentUserId = parsedId;
            dept.UpdatedById = currentUserId;
        }

        // Write Audit Log
        var auditLog = new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = currentUserId,
            Action = "ASSIGN_MANAGER",
            EntityName = "Department",
            EntityId = dept.Id.ToString(),
            OldValues = oldManagerId.HasValue ? $"{{\"ManagerId\": \"{oldManagerId.Value}\"}}" : null,
            NewValues = $"{{\"ManagerId\": \"{request.ManagerId}\"}}",
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.AuditLogs.Add(auditLog);

        await _dbContext.SaveChangesAsync();

        return Ok(new { isSuccess = true, message = "Gán người phụ trách thành công." });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var dept = await _dbContext.Departments.FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted);
        if (dept == null)
            return NotFound(new { isSuccess = false, message = "Không tìm thấy phòng ban." });

        // Kiểm tra có yêu cầu tuyển dụng đang mở (status APPROVED / IN_PROGRESS)
        var activeReqsCount = await _dbContext.JobRequisitions
            .Include(r => r.Department)
            .Where(r => r.Department != null && r.Department.Path.StartsWith(dept.Path) && !r.Department.IsDeleted && !r.IsDeleted 
                     && (r.Status == Ats.Web.Models.Enums.RequisitionStatus.APPROVED || r.Status == Ats.Web.Models.Enums.RequisitionStatus.IN_PROGRESS))
            .CountAsync();

        if (activeReqsCount > 0)
        {
            return StatusCode(StatusCodes.Status409Conflict, new { isSuccess = false, message = $"Phòng ban (hoặc phòng ban con) đang có {activeReqsCount} yêu cầu tuyển dụng mở, không thể xoá. Vui lòng chọn ngừng áp dụng." });
        }

        // Soft delete department and all descendants
        var descendants = await _dbContext.Departments
            .Where(d => d.Path.StartsWith(dept.Path) && !d.IsDeleted)
            .ToListAsync();

        var currentUserIdString = HttpContext?.Session?.GetString("UserId");
        Guid? currentUserId = null;
        if (!string.IsNullOrEmpty(currentUserIdString) && Guid.TryParse(currentUserIdString, out var parsedId))
            currentUserId = parsedId;

        foreach (var child in descendants)
        {
            child.IsDeleted = true;
            child.DeletedAt = DateTimeOffset.UtcNow;
            child.UpdatedById = currentUserId;
        }

        await _dbContext.SaveChangesAsync();

        return Ok(new { isSuccess = true, message = "Xóa phòng ban thành công." });
    }

    [HttpPut("{id}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id)
    {
        var dept = await _dbContext.Departments.FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted);
        if (dept == null)
            return NotFound(new { isSuccess = false, message = "Không tìm thấy phòng ban." });

        // Ngừng áp dụng: cascade cho các phòng ban con
        var descendants = await _dbContext.Departments
            .Where(d => d.Path.StartsWith(dept.Path) && !d.IsDeleted && d.IsActive)
            .ToListAsync();

        var currentUserIdString = HttpContext?.Session?.GetString("UserId");
        Guid? currentUserId = null;
        if (!string.IsNullOrEmpty(currentUserIdString) && Guid.TryParse(currentUserIdString, out var parsedId))
            currentUserId = parsedId;

        foreach (var child in descendants)
        {
            child.IsActive = false;
            child.UpdatedAt = DateTimeOffset.UtcNow;
            child.UpdatedById = currentUserId;
        }

        await _dbContext.SaveChangesAsync();

        return Ok(new { isSuccess = true, message = "Ngừng áp dụng phòng ban thành công." });
    }

    [HttpPut("{id}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id)
    {
        var dept = await _dbContext.Departments.FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted);
        if (dept == null)
            return NotFound(new { isSuccess = false, message = "Không tìm thấy phòng ban." });

        if (dept.ParentId.HasValue)
        {
            var parent = await _dbContext.Departments.FirstOrDefaultAsync(p => p.Id == dept.ParentId.Value && !p.IsDeleted);
            if (parent != null && !parent.IsActive)
            {
                return BadRequest(new { isSuccess = false, message = "Không thể kích hoạt phòng ban con khi phòng ban cha đang ngừng áp dụng." });
            }
        }

        dept.IsActive = true;
        dept.UpdatedAt = DateTimeOffset.UtcNow;
        
        var currentUserIdString = HttpContext.Session.GetString("UserId");
        if (!string.IsNullOrEmpty(currentUserIdString) && Guid.TryParse(currentUserIdString, out var parsedId))
            dept.UpdatedById = parsedId;

        await _dbContext.SaveChangesAsync();

        return Ok(new { isSuccess = true, message = "Kích hoạt phòng ban thành công." });
    }
}
