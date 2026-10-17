using System.Security.Claims;
using Ats.Web.Common;
using Ats.Web.Constants;
using Ats.Web.Models.DTOs;
using Ats.Web.Models.Enums;
using Ats.Web.Models.ViewModels.Candidates;
using Ats.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Logging;

namespace Ats.Web.Controllers;

/// <summary>
/// Controller điều phối nghiệp vụ ứng viên, quản lý hồ sơ hợp nhất (SCRUM-39), 
/// giai đoạn tuyển dụng (SCRUM-42) & thêm ứng viên thủ công (SCRUM-47).
/// Tuân thủ quy chuẩn C# 12 Primary Constructors, kebab-case routing và bảo mật Anti-Forgery Token.
/// </summary>
[Authorize]
[Route("ung-vien")]
[Route("candidates")]
public class CandidatesController(
    ICandidateStageService candidateStageService,
    ICandidateService candidateService,
    IJobPostingService jobPostingService,
    ISecurityAuditService securityAuditService,
    ILogger<CandidatesController> logger) : Controller
{
    private readonly ICandidateStageService _candidateStageService = candidateStageService;
    private readonly ICandidateService _candidateService = candidateService;
    private readonly IJobPostingService _jobPostingService = jobPostingService;
    private readonly ISecurityAuditService _securityAuditService = securityAuditService;
    private readonly ILogger<CandidatesController> _logger = logger;

    private Guid GetCurrentUserId()
    {
        var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(idClaim, out var id) ? id : Guid.Empty;
    }

    private string GetCurrentUserRole()
    {
        return User.FindFirstValue(ClaimTypes.Role) ?? UserRoles.Candidate;
    }

    // ==========================================
    // SCRUM-39: HỒ SƠ ỨNG VIÊN HỢP NHẤT & QUẢN LÝ
    // ==========================================

    // ==========================================
    // SCRUM-44: TÌM KIẾM & LỌC ỨNG VIÊN ĐA ĐIỀU KIỆN
    // ==========================================

    // GET: /ung-vien hoặc /Candidates
    [HttpGet("")]
    [Authorize(Roles = $"{UserRoles.Recruiter},{UserRoles.HiringManager},{UserRoles.Admin},{UserRoles.HRManager}")]
    public async Task<IActionResult> Index([FromQuery] CandidateFilterCriteria criteria, CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        var currentUserRole = GetCurrentUserRole();

        var model = await _candidateService.GetFilteredCandidatesAsync(criteria, currentUserId, currentUserRole, cancellationToken);
        return View(model); // Views/Candidates/Index.cshtml
    }

    /// <summary>
    /// POST: /ung-vien/bo-loc/luu hoặc /candidates/saved-filters/save
    /// Lưu bộ lọc tìm kiếm hồ sơ ứng viên hay dùng (SCRUM-44).
    /// </summary>
    [HttpPost("bo-loc/luu")]
    [HttpPost("saved-filters/save")]
    [HttpPost("/ung-vien/bo-loc/luu")]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = $"{UserRoles.Recruiter},{UserRoles.HiringManager},{UserRoles.Admin},{UserRoles.HRManager}")]
    public async Task<IActionResult> SaveFilter(
        [FromBody] SaveCandidateFilterInputModel model,
        CancellationToken cancellationToken = default)
    {
        if (model == null || string.IsNullOrWhiteSpace(model.Name))
        {
            return Json(new { success = false, message = "Vui lòng nhập tên cho bộ lọc." });
        }

        var currentUserId = GetCurrentUserId();
        if (currentUserId == Guid.Empty)
        {
            return Json(new { success = false, message = "Phiên làm việc hết hạn. Vui lòng đăng nhập lại." });
        }

        try
        {
            var savedFilter = await _candidateService.SaveFilterAsync(currentUserId, model, cancellationToken);
            return Json(new
            {
                success = true,
                message = $"Đã lưu bộ lọc '{savedFilter.Name}' thành công!",
                data = savedFilter
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi lưu bộ lọc tìm kiếm cho user {UserId}", currentUserId);
            return Json(new { success = false, message = "Không thể lưu bộ lọc: " + ex.Message });
        }
    }

    /// <summary>
    /// GET: /ung-vien/bo-loc/danh-sach hoặc /candidates/saved-filters/list
    /// Lấy danh sách bộ lọc đã lưu của Recruiter hiện tại (SCRUM-44).
    /// </summary>
    [HttpGet("bo-loc/danh-sach")]
    [HttpGet("saved-filters/list")]
    [HttpGet("/ung-vien/bo-loc/danh-sach")]
    [Authorize(Roles = $"{UserRoles.Recruiter},{UserRoles.HiringManager},{UserRoles.Admin},{UserRoles.HRManager}")]
    public async Task<IActionResult> GetSavedFilters(CancellationToken cancellationToken = default)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == Guid.Empty)
        {
            return Json(new { success = false, message = "Chưa xác thực." });
        }

        try
        {
            var filters = await _candidateService.GetSavedFiltersAsync(currentUserId, cancellationToken);
            return Json(new { success = true, data = filters });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi lấy danh sách bộ lọc đã lưu của user {UserId}", currentUserId);
            return Json(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// POST: /ung-vien/bo-loc/xoa hoặc /candidates/saved-filters/delete
    /// Xóa bộ lọc đã lưu của Recruiter (SCRUM-44).
    /// </summary>
    [HttpPost("bo-loc/xoa")]
    [HttpPost("saved-filters/delete")]
    [HttpPost("/ung-vien/bo-loc/xoa")]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = $"{UserRoles.Recruiter},{UserRoles.HiringManager},{UserRoles.Admin},{UserRoles.HRManager}")]
    public async Task<IActionResult> DeleteSavedFilter(
        [FromForm] Guid filterId,
        CancellationToken cancellationToken = default)
    {
        if (filterId == Guid.Empty)
        {
            return Json(new { success = false, message = "Mã bộ lọc không hợp lệ." });
        }

        var currentUserId = GetCurrentUserId();
        try
        {
            var result = await _candidateService.DeleteSavedFilterAsync(filterId, currentUserId, cancellationToken);
            return Json(new
            {
                success = result,
                message = result ? "Đã xóa bộ lọc thành công." : "Không tìm thấy bộ lọc cần xóa."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi xóa bộ lọc {FilterId}", filterId);
            return Json(new { success = false, message = "Lỗi khi xóa bộ lọc: " + ex.Message });
        }
    }

    // GET: /ung-vien/{id} hoặc /Candidates/Details/{id}
    [HttpGet("{id:guid}")]
    [HttpGet("Details/{id:guid}")]
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

    // ==========================================
    // SCRUM-42: QUẢN LÝ GIAI ĐOẠN & LÝ DO LOẠI
    // ==========================================

    /// <summary>
    /// GET: /ung-vien/danh-muc-ly-do-loai hoặc /candidates/rejection-reasons
    /// Lấy danh mục các lý do loại hồ sơ đang kích hoạt để hiển thị trong dropdown modal loại.
    /// </summary>
    [HttpGet("danh-muc-ly-do-loai")]
    [HttpGet("rejection-reasons")]
    [Authorize(Roles = $"{UserRoles.Recruiter},{UserRoles.Admin},{UserRoles.HRManager}")]
    public async Task<IActionResult> GetRejectionReasons(CancellationToken cancellationToken = default)
    {
        try
        {
            var reasons = await _candidateStageService.GetActiveRejectionReasonsAsync(cancellationToken);
            return Json(new
            {
                success = true,
                data = reasons
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi lấy danh mục lý do loại hồ sơ");
            return Json(new
            {
                success = false,
                message = "Không thể tải danh mục lý do loại hồ sơ. Vui lòng thử lại sau."
            });
        }
    }

    /// <summary>
    /// POST: /ung-vien/chuyen-giai-doan hoặc /candidates/transition-stage
    /// Thực hiện chuyển giai đoạn ứng viên, ghi nhận lý do khi loại và kiểm tra rào chắn đặc cách Offer.
    /// </summary>
    [HttpPost("chuyen-giai-doan")]
    [HttpPost("transition-stage")]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = $"{UserRoles.Recruiter},{UserRoles.Admin},{UserRoles.HRManager}")]
    public async Task<IActionResult> TransitionStage(
        [FromForm] TransitionCandidateStageInputModel model,
        CancellationToken cancellationToken = default)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == Guid.Empty)
        {
            return Json(new
            {
                success = false,
                message = "Phiên làm việc đã hết hạn hoặc không hợp lệ. Vui lòng đăng nhập lại."
            });
        }

        if (!ModelState.IsValid)
        {
            var errorMessage = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .FirstOrDefault() ?? "Dữ liệu yêu cầu chuyển giai đoạn không hợp lệ.";

            return Json(new
            {
                success = false,
                message = errorMessage
            });
        }

        try
        {
            var historyDto = await _candidateStageService.TransitionStageAsync(
                model,
                currentUserId,
                cancellationToken);

            return Json(new
            {
                success = true,
                message = $"Đã chuyển giai đoạn hồ sơ sang '{historyDto.ToStageDisplayName}' thành công.",
                data = historyDto
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Vi phạm quy tắc nghiệp vụ khi chuyển giai đoạn ứng viên {AppId}", model.ApplicationId);
            return Json(new
            {
                success = false,
                message = ex.Message
            });
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Không tìm thấy thực thể khi chuyển giai đoạn ứng viên {AppId}", model.ApplicationId);
            return Json(new
            {
                success = false,
                message = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi không xác định khi chuyển giai đoạn hồ sơ {AppId}", model.ApplicationId);
            return Json(new
            {
                success = false,
                message = "Đã xảy ra lỗi trong quá trình cập nhật trạng thái. Vui lòng liên hệ quản trị viên."
            });
        }
    }

    /// <summary>
    /// GET: /ung-vien/{applicationId}/lich-su-giai-doan hoặc /candidates/{applicationId}/history
    /// Lấy danh sách lịch sử các lần chuyển giai đoạn của ứng viên để render component timeline.
    /// </summary>
    [HttpGet("{applicationId:guid}/lich-su-giai-doan")]
    [HttpGet("{applicationId:guid}/history")]
    [Authorize(Roles = $"{UserRoles.Recruiter},{UserRoles.Admin},{UserRoles.HRManager}")]
    public async Task<IActionResult> GetStageHistory(
        Guid applicationId,
        CancellationToken cancellationToken = default)
    {
        if (applicationId == Guid.Empty)
        {
            return Json(new
            {
                success = false,
                message = "Mã hồ sơ ứng tuyển không hợp lệ."
            });
        }

        try
        {
            var histories = await _candidateStageService.GetStageHistoryAsync(applicationId, cancellationToken);
            return Json(new
            {
                success = true,
                data = histories
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi lấy lịch sử giai đoạn cho hồ sơ {AppId}", applicationId);
            return Json(new
            {
                success = false,
                message = "Không thể tải lịch sử giai đoạn của ứng viên. Vui lòng thử lại sau."
            });
        }
    }
    /// <summary>
    /// SCRUM-34 & Excel: Tải tệp mẫu Excel nhập ứng viên
    /// </summary>
    [HttpGet("import-template")]
    [HttpHead("import-template")]
    [Authorize(Roles = $"{UserRoles.Recruiter},{UserRoles.HRManager},{UserRoles.Admin}")]
    public async Task<IActionResult> DownloadImportTemplate(CancellationToken cancellationToken)
    {
        try
        {
            var fileContents = await _candidateService.GenerateCandidateExcelTemplateAsync(cancellationToken);
            return File(fileContents, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Mau_Nhap_Ung_Vien.xlsx");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không thể sinh template Excel ứng viên động qua ClosedXML, chuyển sang dùng file mẫu tĩnh.");
            var fallbackPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "templates", "Mau_Nhap_Ung_Vien.xlsx");
            if (!System.IO.File.Exists(fallbackPath))
            {
                fallbackPath = Path.Combine(Directory.GetCurrentDirectory(), "src", "Ats.Web", "wwwroot", "templates", "Mau_Nhap_Ung_Vien.xlsx");
            }

            if (System.IO.File.Exists(fallbackPath))
            {
                return PhysicalFile(fallbackPath, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Mau_Nhap_Ung_Vien.xlsx");
            }
            return StatusCode(500, "Không tìm thấy tệp Excel mẫu ứng viên trên hệ thống.");
        }
    }

    /// <summary>
    /// SCRUM-34 & Excel: Xử lý đọc và validate dữ liệu từng dòng trong tệp Excel ứng viên
    /// </summary>
    [HttpPost("import-excel")]
    [IgnoreAntiforgeryToken]
    [Authorize(Roles = $"{UserRoles.Recruiter},{UserRoles.HRManager},{UserRoles.Admin}")]
    public async Task<IActionResult> ImportExcel(IFormFile? file, CancellationToken cancellationToken)
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

        const long maxFileSize = 5 * 1024 * 1024;
        if (file.Length > maxFileSize)
        {
            return BadRequest("Dung lượng tệp vượt quá giới hạn cho phép (tối đa 5MB).");
        }

        using var stream = file.OpenReadStream();
        try
        {
            var result = await _candidateService.ValidateCandidateExcelImportAsync(stream, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi xử lý tệp Excel nhập ứng viên");
            return BadRequest("Tệp Excel không đúng định dạng hoặc bị lỗi.");
        }
    }

    /// <summary>
    /// SCRUM-34 & Excel: Thực thi nhập dữ liệu ứng viên hàng loạt từ danh sách dòng hợp lệ
    /// </summary>
    [HttpPost("execute-import")]
    [IgnoreAntiforgeryToken]
    [Authorize(Roles = $"{UserRoles.Recruiter},{UserRoles.HRManager},{UserRoles.Admin}")]
    public async Task<IActionResult> ExecuteImport([FromBody] ExecuteCandidateImportRequestDto request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        if (request.ValidRows == null || request.ValidRows.Count == 0)
        {
            return BadRequest("Danh sách nhập dữ liệu trống.");
        }

        try
        {
            var currentUserId = GetCurrentUserId();
            var count = await _candidateService.ExecuteCandidateImportAsync(request, currentUserId, cancellationToken);
            return Ok(new { success = true, importedCount = count, message = $"Nhập thành công {count} ứng viên vào hệ thống." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi thực thi nhập dữ liệu ứng viên hàng loạt");
            return StatusCode(500, "Đã xảy ra lỗi hệ thống trong quá trình nhập dữ liệu ứng viên.");
        }
    }

    // ==========================================
    // SCRUM-47: THÊM ỨNG VIÊN THỦ CÔNG VÀO VỊ TRÍ
    // ==========================================

    /// <summary>
    /// API kiểm tra trùng lặp thời gian thực qua Email và Số điện thoại (SCRUM-47).
    /// </summary>
    [HttpGet("kiem-tra-trung")]
    [HttpGet("check-duplicate")]
    [Authorize(Roles = $"{UserRoles.Recruiter},{UserRoles.Admin},{UserRoles.HRManager}")]
    public async Task<IActionResult> CheckDuplicate(
        [FromQuery] string? email,
        [FromQuery] string? phoneNumber,
        [FromQuery] Guid jobPostingId,
        CancellationToken cancellationToken)
    {
        var result = await _candidateService.CheckDuplicateCandidateAsync(
            email ?? string.Empty,
            phoneNumber,
            jobPostingId,
            cancellationToken);

        return Json(result);
    }

    /// <summary>
    /// Giao diện form / modal thêm ứng viên thủ công vào vị trí tuyển dụng (SCRUM-47).
    /// </summary>
    [HttpGet("them-thu-cong/{jobPostingId:guid?}")]
    [HttpGet("them-thu-cong")]
    [HttpGet("create-manual/{jobPostingId:guid?}")]
    [HttpGet("create-manual")]
    [Authorize(Roles = $"{UserRoles.Recruiter},{UserRoles.Admin},{UserRoles.HRManager}")]
    public async Task<IActionResult> CreateManual(
        [FromRoute] Guid? jobPostingId,
        [FromQuery] Guid? queryJobPostingId,
        CancellationToken cancellationToken)
    {
        var targetJobPostingId = (jobPostingId.HasValue && jobPostingId.Value != Guid.Empty)
            ? jobPostingId.Value
            : (queryJobPostingId ?? Guid.Empty);

        var model = new CandidateManualCreateInputModel
        {
            JobPostingId = targetJobPostingId,
            Source = CandidateSource.LINKEDIN
        };

        await PopulateJobPostingsDropdownAsync(targetJobPostingId, cancellationToken);
        return View(model);
    }

    /// <summary>
    /// Xử lý lưu hồ sơ ứng viên thêm thủ công vào vị trí tuyển dụng (SCRUM-47).
    /// Hỗ trợ cả phản hồi AJAX (JSON) và Form submit truyền thống.
    /// </summary>
    [HttpPost("them-thu-cong")]
    [HttpPost("create-manual")]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = $"{UserRoles.Recruiter},{UserRoles.Admin},{UserRoles.HRManager}")]
    public async Task<IActionResult> CreateManual(
        [FromForm] CandidateManualCreateInputModel input,
        CancellationToken cancellationToken)
    {
        var isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";

        if (!ModelState.IsValid)
        {
            if (isAjax)
            {
                var errors = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .ToList();
                return Json(new { success = false, message = "Dữ liệu nhập vào chưa hợp lệ. Vui lòng kiểm tra lại các trường thông tin.", errors });
            }

            await PopulateJobPostingsDropdownAsync(input.JobPostingId, cancellationToken);
            return View(input);
        }

        try
        {
            var currentUserId = GetCurrentUserId().ToString();
            var candidateId = await _candidateService.AddManualCandidateAsync(input, currentUserId, cancellationToken);
            var successMsg = $"Đã thêm ứng viên '{input.FullName}' vào vị trí tuyển dụng thành công!";

            if (isAjax)
            {
                return Json(new
                {
                    success = true,
                    candidateId,
                    applicationId = input.CreatedApplicationId,
                    message = successMsg,
                    redirectUrl = Url.Action(nameof(Details), "Candidates", new { id = candidateId })
                });
            }

            TempData["SuccessMessage"] = successMsg;
            return RedirectToAction(nameof(Details), new { id = candidateId });
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Người dùng không có quyền thêm ứng viên thủ công: UserId={UserId}", GetCurrentUserId());
            if (isAjax) return Json(new { success = false, message = ex.Message });
            ModelState.AddModelError(string.Empty, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Lỗi nghiệp vụ khi thêm ứng viên thủ công: {Message}", ex.Message);
            if (isAjax) return Json(new { success = false, message = ex.Message });
            ModelState.AddModelError(string.Empty, ex.Message);
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Không tìm thấy dữ liệu: {Message}", ex.Message);
            if (isAjax) return Json(new { success = false, message = ex.Message });
            ModelState.AddModelError(string.Empty, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi hệ thống khi thêm ứng viên thủ công");
            var errorMsg = "Đã xảy ra sự cố trong quá trình lưu hồ sơ ứng viên. Vui lòng thử lại sau.";
            if (isAjax) return Json(new { success = false, message = errorMsg });
            ModelState.AddModelError(string.Empty, errorMsg);
        }

        await PopulateJobPostingsDropdownAsync(input.JobPostingId, cancellationToken);
        return View(input);
    }

    private async Task PopulateJobPostingsDropdownAsync(Guid selectedJobId, CancellationToken cancellationToken)
    {
        var publishedJobs = await _jobPostingService.GetPublishedJobPostingsAsync(cancellationToken);
        var items = publishedJobs.Select(j => new SelectListItem
        {
            Value = j.Id.ToString(),
            Text = string.IsNullOrWhiteSpace(j.Code)
                ? j.Title
                : $"[{j.Code}] {j.Title} - {(j.Requisition?.Department?.Name ?? j.WorkLocation)}",
            Selected = j.Id == selectedJobId
        }).ToList();

        ViewBag.JobPostings = items;
        ViewBag.SelectedJob = publishedJobs.FirstOrDefault(j => j.Id == selectedJobId);
    }
}

