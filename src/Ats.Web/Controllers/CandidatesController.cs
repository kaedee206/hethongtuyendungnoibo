using System.Security.Claims;
using Ats.Web.Constants;
using Ats.Web.Models.ViewModels.Candidates;
using Ats.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Ats.Web.Controllers;

/// <summary>
/// Controller điều phối nghiệp vụ ứng viên & quản lý giai đoạn tuyển dụng (SCRUM-42).
/// Tuân thủ quy chuẩn C# 12 Primary Constructors, kebab-case routing và bảo mật Anti-Forgery Token.
/// </summary>
[Authorize(Roles = $"{UserRoles.Recruiter},{UserRoles.Admin},{UserRoles.HRManager}")]
[Route("ung-vien")]
[Route("candidates")]
public class CandidatesController(
    ICandidateStageService candidateStageService,
    ILogger<CandidatesController> logger) : Controller
{
    private readonly ICandidateStageService _candidateStageService = candidateStageService;
    private readonly ILogger<CandidatesController> _logger = logger;

    /// <summary>
    /// Lấy ID người dùng hiện tại an toàn từ ClaimTypes.NameIdentifier.
    /// </summary>
    private Guid GetCurrentUserId()
    {
        var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(idClaim, out var id) ? id : Guid.Empty;
    }

    /// <summary>
    /// GET: /ung-vien/danh-muc-ly-do-loai hoặc /candidates/rejection-reasons
    /// Lấy danh mục các lý do loại hồ sơ đang kích hoạt để hiển thị trong dropdown modal loại.
    /// </summary>
    [HttpGet("danh-muc-ly-do-loai")]
    [HttpGet("rejection-reasons")]
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
}
