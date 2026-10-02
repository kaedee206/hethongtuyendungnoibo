using System.Security.Claims;
using Ats.Web.Constants;
using Ats.Web.Models.ViewModels.Users;
using Ats.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

/// <summary>
/// Controller Quản trị người dùng & Phân quyền nội bộ dành cho Admin (S1-06, S1-07, S1-08, S1-09, S1-10).
/// </summary>
[Authorize(Roles = UserRoles.Admin)]
[Route("users")]
public class UsersController(
    IUserService userService,
    ILogger<UsersController> logger) : Controller
{
    private readonly IUserService _userService = userService;
    private readonly ILogger<UsersController> _logger = logger;

    private Guid GetCurrentAdminId()
    {
        var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(idStr, out var id) ? id : Guid.Empty;
    }

    /// <summary>
    /// S1-06: Danh sách tài khoản với bộ lọc, tìm kiếm và phân trang 20 dòng/trang.
    /// </summary>
    [HttpGet("")]
    public async Task<IActionResult> Index(
        [FromQuery] string? keyword,
        [FromQuery] string? role,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var model = await _userService.GetUsersAsync(keyword, role, status, page, pageSize, cancellationToken);
        return View(model);
    }

    /// <summary>
    /// S1-07: Form tạo tài khoản nội bộ mới.
    /// </summary>
    [HttpGet("create")]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        var model = await _userService.GetUserCreateViewModelAsync(cancellationToken);
        return View(model);
    }

    /// <summary>
    /// S1-07: Xử lý tạo tài khoản, sinh mật khẩu tạm và gửi email chào mừng.
    /// </summary>
    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(UserCreateViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            var freshModel = await _userService.GetUserCreateViewModelAsync(cancellationToken);
            model.AvailableRoles = freshModel.AvailableRoles;
            return View(model);
        }

        var adminId = GetCurrentAdminId();
        var baseUrl = $"{Request.Scheme}://{Request.Host}";

        var (isSuccess, message, _) = await _userService.CreateUserAsync(model, adminId, baseUrl, cancellationToken);

        if (!isSuccess)
        {
            ModelState.AddModelError(string.Empty, message);
            var freshModel = await _userService.GetUserCreateViewModelAsync(cancellationToken);
            model.AvailableRoles = freshModel.AvailableRoles;
            return View(model);
        }

        TempData["SuccessMessage"] = message;
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// S1-08, S1-09: Form chỉnh sửa thông tin và phân quyền vai trò.
    /// </summary>
    [HttpGet("edit/{id:guid}")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var adminId = GetCurrentAdminId();
        var model = await _userService.GetUserEditViewModelAsync(id, adminId, cancellationToken);

        if (model == null)
        {
            TempData["ErrorMessage"] = "Không tìm thấy tài khoản người dùng yêu cầu.";
            return RedirectToAction(nameof(Index));
        }

        return View(model);
    }

    /// <summary>
    /// S1-08, S1-09: Cập nhật thông tin và danh sách vai trò (chặn tự tước quyền Admin).
    /// </summary>
    [HttpPost("edit/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, UserEditViewModel model, CancellationToken cancellationToken)
    {
        if (id != model.Id)
        {
            return BadRequest();
        }

        var adminId = GetCurrentAdminId();

        if (!ModelState.IsValid)
        {
            var fresh = await _userService.GetUserEditViewModelAsync(id, adminId, cancellationToken);
            if (fresh != null)
            {
                model.Email = fresh.Email;
                model.IsSelf = fresh.IsSelf;
                model.AvailableRoles = fresh.AvailableRoles;
            }
            return View(model);
        }

        var (isSuccess, message) = await _userService.UpdateUserAsync(model, adminId, cancellationToken);

        if (!isSuccess)
        {
            ModelState.AddModelError(string.Empty, message);
            var fresh = await _userService.GetUserEditViewModelAsync(id, adminId, cancellationToken);
            if (fresh != null)
            {
                model.Email = fresh.Email;
                model.IsSelf = fresh.IsSelf;
                model.AvailableRoles = fresh.AvailableRoles;
            }
            return View(model);
        }

        TempData["SuccessMessage"] = message;
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// S1-10: Khóa tài khoản nội bộ kèm lý do bắt buộc, thu hồi session ngay lập tức.
    /// </summary>
    [HttpPost("lock/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Lock(Guid id, [FromForm] string lockReason, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(lockReason))
        {
            TempData["ErrorMessage"] = "Lý do khóa tài khoản là bắt buộc.";
            return RedirectToAction(nameof(Index));
        }

        var adminId = GetCurrentAdminId();
        var (isSuccess, message, handoverWarning) = await _userService.LockUserAsync(id, lockReason, adminId, cancellationToken);

        if (!isSuccess)
        {
            TempData["ErrorMessage"] = message;
        }
        else
        {
            if (!string.IsNullOrEmpty(handoverWarning))
            {
                TempData["WarningMessage"] = handoverWarning;
            }
            TempData["SuccessMessage"] = message;
        }

        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// S1-10: Mở khóa tài khoản nội bộ.
    /// </summary>
    [HttpPost("unlock/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unlock(Guid id, CancellationToken cancellationToken)
    {
        var adminId = GetCurrentAdminId();
        var (isSuccess, message) = await _userService.UnlockUserAsync(id, adminId, cancellationToken);

        if (!isSuccess)
        {
            TempData["ErrorMessage"] = message;
        }
        else
        {
            TempData["SuccessMessage"] = message;
        }

        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// SCRUM-176: Tải tệp mẫu Excel nhập nhân sự
    /// </summary>
    [HttpGet("import-template")]
    public async Task<IActionResult> DownloadImportTemplate(CancellationToken cancellationToken)
    {
        var fileContents = await _userService.GenerateExcelTemplateAsync(cancellationToken);
        return File(fileContents, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Mau_Nhap_Nhan_Su.xlsx");
    }

    /// <summary>
    /// SCRUM-178: Xử lý đọc và validate dữ liệu từng dòng trong tệp Excel
    /// </summary>
    [HttpPost("import-excel")]
    public async Task<IActionResult> ImportExcel(IFormFile file, CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest("Vui lòng tải lên một tệp Excel hợp lệ.");
        }

        var ext = Path.GetExtension(file.FileName).ToLower();
        if (ext != ".xlsx" && ext != ".xls")
        {
            return BadRequest("Chỉ hỗ trợ định dạng tệp Excel (.xlsx, .xls).");
        }

        using var stream = file.OpenReadStream();
        try
        {
            var result = await _userService.ValidateExcelImportAsync(stream, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi xử lý tệp Excel nhập nhân sự");
            return BadRequest("Tệp Excel không đúng định dạng hoặc bị lỗi.");
        }
    }
}
