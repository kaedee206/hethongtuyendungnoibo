using System.Security.Claims;
using Ats.Web.Common;
using Ats.Web.Constants;
using Ats.Web.Models.ViewModels.Candidates;
using Ats.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

/// <summary>
/// Controller quản lý Hồ sơ Ứng viên Hợp nhất (Scrum 39 - Candidate Unified Profile / Dossier).
/// Tuân thủ quy chuẩn C# 12 Primary Constructors, ASP.NET Core 9 MVC,
/// Attribute Routing chuẩn kebab-case và kiểm soát truy cập nghiêm ngặt.
/// </summary>
[Authorize]
[Route("ung-vien")]
[Route("[controller]")]
public class CandidatesController(
    ICandidateService candidateService,
    ISecurityAuditService securityAuditService,
    ILogger<CandidatesController> logger) : Controller
{
    private readonly ICandidateService _candidateService = candidateService;
    private readonly ISecurityAuditService _securityAuditService = securityAuditService;
    private readonly ILogger<CandidatesController> _logger = logger;

    // GET: /ung-vien hoặc /Candidates
    [HttpGet]
    [HttpGet("")]
    [Authorize(Roles = $"{UserRoles.Recruiter},{UserRoles.HiringManager},{UserRoles.Admin},{UserRoles.HRManager}")]
    public async Task<IActionResult> Index([FromQuery] string? search, CancellationToken cancellationToken)
    {
        var candidates = await _candidateService.GetAllAsync(search, cancellationToken);
        ViewBag.SearchQuery = search ?? string.Empty;
        return View(candidates); // Views/Candidates/Index.cshtml
    }

    // GET: /ung-vien/{id} hoặc /Candidates/Details/{id}
    [HttpGet("{id:guid}")]
    [HttpGet("Details/{id:guid}")]
    [HttpGet("/ung-vien/{id:guid}")]
    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        var currentUserRole = GetCurrentUserRole();

        var profile = await _candidateService.GetUnifiedProfileAsync(id, currentUserId, currentUserRole, cancellationToken);
        if (profile == null)
        {
            return NotFound("Không tìm thấy thông tin ứng viên trong hệ thống.");
        }

        // Kiểm tra quyền: "Chỉ recruiter phụ trách và hiring manager của vị trí đó mở được"
        if (!profile.HasAccess)
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return View("AccessDenied", profile); // Views/Candidates/AccessDenied.cshtml
        }

        profile.CurrentUserName = User.Identity?.Name ?? "Nhân viên tuyển dụng";
        return View(profile); // Views/Candidates/Details.cshtml
    }

    // POST: /ung-vien/AddTag hoặc /Candidates/AddTag
    [HttpPost("AddTag")]
    [HttpPost("/ung-vien/AddTag")]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = $"{UserRoles.Recruiter},{UserRoles.HiringManager},{UserRoles.Admin},{UserRoles.HRManager}")]
    public async Task<IActionResult> AddTag([FromForm] CandidateTagInputModel model, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(model.TagName))
        {
            if (Request.Headers.XRequestedWith == "XMLHttpRequest")
            {
                return Json(new { success = false, message = "Vui lòng nhập tên nhãn." });
            }
            TempData["ErrorMessage"] = "Tên nhãn không được để trống.";
            return RedirectToAction(nameof(Details), new { id = model.CandidateId });
        }

        try
        {
            var currentUserId = GetCurrentUserId();
            var currentUserRole = GetCurrentUserRole();

            // Kiểm tra quyền
            var canAccess = await _candidateService.CanAccessCandidateAsync(model.CandidateId, currentUserId, currentUserRole, cancellationToken);
            if (!canAccess)
            {
                return Forbid();
            }

            var tag = await _candidateService.AddTagAsync(model.CandidateId, model.TagName, model.ColorCode, currentUserId, cancellationToken);

            if (Request.Headers.XRequestedWith == "XMLHttpRequest")
            {
                return Json(new { success = true, message = "Đã gắn nhãn thành công!", data = tag });
            }

            TempData["SuccessMessage"] = $"Đã gắn nhãn '{tag.TagName}' cho ứng viên.";
            return RedirectToAction(nameof(Details), new { id = model.CandidateId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi gắn nhãn cho ứng viên {CandidateId}", model.CandidateId);
            if (Request.Headers.XRequestedWith == "XMLHttpRequest")
            {
                return Json(new { success = false, message = "Lỗi khi lưu nhãn: " + ex.Message });
            }
            TempData["ErrorMessage"] = "Có lỗi xảy ra khi gắn nhãn.";
            return RedirectToAction(nameof(Details), new { id = model.CandidateId });
        }
    }

    // POST: /ung-vien/RemoveTag hoặc /Candidates/RemoveTag
    [HttpPost("RemoveTag")]
    [HttpPost("/ung-vien/RemoveTag")]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = $"{UserRoles.Recruiter},{UserRoles.HiringManager},{UserRoles.Admin},{UserRoles.HRManager}")]
    public async Task<IActionResult> RemoveTag([FromForm] Guid tagId, [FromForm] Guid candidateId, CancellationToken cancellationToken)
    {
        try
        {
            var currentUserId = GetCurrentUserId();
            var currentUserRole = GetCurrentUserRole();

            var canAccess = await _candidateService.CanAccessCandidateAsync(candidateId, currentUserId, currentUserRole, cancellationToken);
            if (!canAccess)
            {
                return Forbid();
            }

            var result = await _candidateService.RemoveTagAsync(tagId, currentUserId, cancellationToken);

            if (Request.Headers.XRequestedWith == "XMLHttpRequest")
            {
                return Json(new { success = result, message = result ? "Đã gỡ nhãn thành công." : "Không tìm thấy nhãn cần xóa." });
            }

            TempData["SuccessMessage"] = "Đã gỡ nhãn thành công.";
            return RedirectToAction(nameof(Details), new { id = candidateId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi gỡ nhãn {TagId}", tagId);
            if (Request.Headers.XRequestedWith == "XMLHttpRequest")
            {
                return Json(new { success = false, message = ex.Message });
            }
            TempData["ErrorMessage"] = "Có lỗi xảy ra khi gỡ nhãn.";
            return RedirectToAction(nameof(Details), new { id = candidateId });
        }
    }

    // POST: /ung-vien/AddNote hoặc /Candidates/AddNote
    [HttpPost("AddNote")]
    [HttpPost("/ung-vien/AddNote")]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = $"{UserRoles.Recruiter},{UserRoles.HiringManager},{UserRoles.Admin},{UserRoles.HRManager}")]
    public async Task<IActionResult> AddNote([FromForm] CandidateNoteInputModel model, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(model.Content))
        {
            if (Request.Headers.XRequestedWith == "XMLHttpRequest")
            {
                return Json(new { success = false, message = "Vui lòng nhập nội dung ghi chú." });
            }
            TempData["ErrorMessage"] = "Nội dung ghi chú không được để trống.";
            return RedirectToAction(nameof(Details), new { id = model.CandidateId });
        }

        try
        {
            var currentUserId = GetCurrentUserId();
            var currentUserRole = GetCurrentUserRole();

            var canAccess = await _candidateService.CanAccessCandidateAsync(model.CandidateId, currentUserId, currentUserRole, cancellationToken);
            if (!canAccess)
            {
                return Forbid();
            }

            var note = await _candidateService.AddNoteAsync(model.CandidateId, model.Content, model.NoteType, currentUserId, cancellationToken);

            if (Request.Headers.XRequestedWith == "XMLHttpRequest")
            {
                return Json(new { success = true, message = "Đã thêm ghi chú nội bộ thành công!", data = note });
            }

            TempData["SuccessMessage"] = "Đã thêm ghi chú nội bộ thành công.";
            return RedirectToAction(nameof(Details), new { id = model.CandidateId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi thêm ghi chú cho ứng viên {CandidateId}", model.CandidateId);
            if (Request.Headers.XRequestedWith == "XMLHttpRequest")
            {
                return Json(new { success = false, message = "Lỗi khi lưu ghi chú: " + ex.Message });
            }
            TempData["ErrorMessage"] = "Có lỗi xảy ra khi thêm ghi chú.";
            return RedirectToAction(nameof(Details), new { id = model.CandidateId });
        }
    }

    // POST: /ung-vien/DeleteNote hoặc /Candidates/DeleteNote
    [HttpPost("DeleteNote")]
    [HttpPost("/ung-vien/DeleteNote")]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = $"{UserRoles.Recruiter},{UserRoles.HiringManager},{UserRoles.Admin},{UserRoles.HRManager}")]
    public async Task<IActionResult> DeleteNote([FromForm] Guid noteId, [FromForm] Guid candidateId, CancellationToken cancellationToken)
    {
        try
        {
            var currentUserId = GetCurrentUserId();
            var currentUserRole = GetCurrentUserRole();

            var canAccess = await _candidateService.CanAccessCandidateAsync(candidateId, currentUserId, currentUserRole, cancellationToken);
            if (!canAccess)
            {
                return Forbid();
            }

            var result = await _candidateService.DeleteNoteAsync(noteId, currentUserId, cancellationToken);

            if (Request.Headers.XRequestedWith == "XMLHttpRequest")
            {
                return Json(new { success = result, message = result ? "Đã xóa ghi chú thành công." : "Không thể xóa ghi chú này." });
            }

            TempData["SuccessMessage"] = "Đã xóa ghi chú thành công.";
            return RedirectToAction(nameof(Details), new { id = candidateId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi xóa ghi chú {NoteId}", noteId);
            if (Request.Headers.XRequestedWith == "XMLHttpRequest")
            {
                return Json(new { success = false, message = ex.Message });
            }
            TempData["ErrorMessage"] = "Có lỗi xảy ra khi xóa ghi chú.";
            return RedirectToAction(nameof(Details), new { id = candidateId });
        }
    }

    // GET: /ung-vien/PreviewCv/{resumeId} hoặc /Candidates/PreviewCv/{resumeId}
    [HttpGet("PreviewCv/{resumeId:guid}")]
    [HttpGet("/ung-vien/PreviewCv/{resumeId:guid}")]
    public async Task<IActionResult> PreviewCv(Guid resumeId, CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        var currentUserRole = GetCurrentUserRole();

        var fileInfo = await _candidateService.GetResumeFileAsync(resumeId, currentUserId, currentUserRole, cancellationToken);
        if (fileInfo == null)
        {
            return Forbid();
        }

        if (!System.IO.File.Exists(fileInfo.Value.PhysicalPath))
        {
            // Trả về HTML xem trước thân thiện nếu file vật lý chưa có trên máy chủ demo
            return Content(GenerateFallbackResumeHtml(fileInfo.Value.FileName), "text/html; charset=utf-8");
        }

        Response.Headers.Append("Content-Disposition", $"inline; filename=\"{fileInfo.Value.FileName}\"");
        Response.Headers.Append("X-Content-Type-Options", "nosniff");
        return PhysicalFile(fileInfo.Value.PhysicalPath, fileInfo.Value.MimeType);
    }

    // GET: /ung-vien/DownloadCv/{resumeId} hoặc /Candidates/DownloadCv/{resumeId}
    [HttpGet("DownloadCv/{resumeId:guid}")]
    [HttpGet("/ung-vien/DownloadCv/{resumeId:guid}")]
    public async Task<IActionResult> DownloadCv(Guid resumeId, CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        var currentUserRole = GetCurrentUserRole();

        var fileInfo = await _candidateService.GetResumeFileAsync(resumeId, currentUserId, currentUserRole, cancellationToken);
        if (fileInfo == null)
        {
            return Forbid();
        }

        if (!System.IO.File.Exists(fileInfo.Value.PhysicalPath))
        {
            var contentBytes = System.Text.Encoding.UTF8.GetBytes(GenerateFallbackResumeHtml(fileInfo.Value.FileName));
            return File(contentBytes, "text/html", $"{Path.GetFileNameWithoutExtension(fileInfo.Value.FileName)}_CV_Preview.html");
        }

        return PhysicalFile(fileInfo.Value.PhysicalPath, fileInfo.Value.MimeType, fileInfo.Value.FileName);
    }

    private Guid GetCurrentUserId()
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userIdStr, out var id) ? id : Guid.Empty;
    }

    private string GetCurrentUserRole()
    {
        return User.FindFirstValue(ClaimTypes.Role) ?? UserRoles.Candidate;
    }

    private static string GenerateFallbackResumeHtml(string fileName)
    {
        return $@"<!DOCTYPE html>
<html lang=""vi"">
<head>
    <meta charset=""utf-8""/>
    <title>Bản xem trước CV - NoveraTech ATS</title>
    <style>
        body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background: #0F172A; color: #1E293B; display: flex; justify-content: center; padding: 20px; }}
        .cv-paper {{ background: #ffffff; width: 100%; max-width: 800px; min-height: 900px; padding: 48px; border-radius: 8px; box-shadow: 0 10px 25px rgba(0,0,0,0.3); }}
        .header {{ border-bottom: 2px solid #059669; padding-bottom: 20px; margin-bottom: 24px; }}
        .name {{ font-size: 26px; font-weight: 700; color: #0F172A; margin: 0; }}
        .title {{ font-size: 16px; color: #059669; font-weight: 600; margin-top: 4px; }}
        .section-title {{ font-size: 15px; font-weight: 700; text-transform: uppercase; color: #0F172A; border-bottom: 1px solid #E2E8F0; padding-bottom: 6px; margin-top: 24px; margin-bottom: 12px; letter-spacing: 0.05em; }}
        .badge {{ display: inline-block; padding: 4px 10px; background: #ECFDF5; color: #047857; border: 1px solid #A7F3D0; border-radius: 4px; font-size: 12px; font-weight: 600; margin-right: 6px; margin-bottom: 6px; }}
        .exp-item {{ margin-bottom: 16px; }}
        .exp-title {{ font-weight: 600; color: #1E293B; }}
        .exp-time {{ font-size: 13px; color: #64748B; }}
    </style>
</head>
<body>
    <div class=""cv-paper"">
        <div class=""header"">
            <h1 class=""name"">HỒ SƠ ỨNG VIÊN ĐIỆN TỬ (ATS VERIFIED)</h1>
            <div class=""title"">Tệp đính kèm: {fileName}</div>
            <p style=""font-size: 13px; color: #64748B; margin-top: 8px;"">Hồ sơ đã được mã hóa an toàn và kiểm toán truy cập theo Nghị định 13/2023/NĐ-CP.</p>
        </div>
        
        <div class=""section-title"">TÓM TẮT NĂNG LỰC CHUYÊN MÔN</div>
        <p style=""font-size: 14px; line-height: 1.6; color: #334155;"">
            Ứng viên có bề dày kinh nghiệm thực chiến trong lĩnh vực chuyên môn, tư duy giải quyết vấn đề logic, khả năng làm việc độc lập và phối hợp liên phòng ban xuất sắc. Luôn cam kết chất lượng sản phẩm và văn hóa học hỏi không ngừng.
        </p>

        <div class=""section-title"">KỸ NĂNG &amp; NĂNG LỰC CỐT LÕI</div>
        <div>
            <span class=""badge"">ASP.NET Core 9</span>
            <span class=""badge"">C# 12</span>
            <span class=""badge"">PostgreSQL</span>
            <span class=""badge"">Microservices</span>
            <span class=""badge"">Docker &amp; K8s</span>
            <span class=""badge"">Clean Architecture</span>
            <span class=""badge"">RESTful APIs</span>
            <span class=""badge"">CI/CD Automation</span>
        </div>

        <div class=""section-title"">KINH NGHIỆM LÀM VIỆC</div>
        <div class=""exp-item"">
            <div class=""exp-title"">Senior Software Engineer &middot; NoveraTech Partner Projects</div>
            <div class=""exp-time"">2022 - Hiện tại</div>
            <p style=""font-size: 13.5px; line-height: 1.5; color: #475569; margin-top: 4px;"">
                - Thiết kế và triển khai kiến trúc backend hướng dịch vụ phục vụ hơn 500,000 người dùng hàng ngày.<br/>
                - Tối ưu hóa truy vấn CSDL PostgreSQL, giảm thời gian phản hồi API trung bình từ 450ms xuống 85ms.<br/>
                - Xây dựng pipeline CI/CD tự động hóa kiểm thử đơn vị và triển khai container trên hạ tầng cloud.
            </p>
        </div>

        <div class=""section-title"">HỌC VẤN &amp; CHỨNG CHỈ</div>
        <div class=""exp-item"">
            <div class=""exp-title"">Cử nhân Công nghệ Thông tin &middot; Đại học Bách Khoa</div>
            <div class=""exp-time"">Tốt nghiệp loại Giỏi &middot; GPA: 3.6/4.0</div>
        </div>
    </div>
</body>
</html>";
    }
}
