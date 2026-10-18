using System.Security.Claims;
using Ats.Web.Constants;
using Ats.Web.Models.ViewModels.Evaluations;
using Ats.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

/// <summary>
/// Controller xử lý giao diện và nghiệp vụ Phiếu đánh giá tự động sinh theo khung năng lực chức danh (SCRUM-56).
/// Tuân thủ quy chuẩn: Primary Constructor, kebab-case routing, Clean Controller, ValidateAntiForgeryToken.
/// </summary>
[Authorize]
public class EvaluationsController(
    IEvaluationService evaluationService,
    ILogger<EvaluationsController> logger) : Controller
{
    private readonly IEvaluationService _evaluationService = evaluationService;
    private readonly ILogger<EvaluationsController> _logger = logger;

    /// <summary>
    /// GET: /danh-gia/{interviewId} hoặc /cham-diem/{applicationId}
    /// Mở form hoặc modal chấm điểm phiếu đánh giá tự động nạp theo khung năng lực.
    /// </summary>
    [HttpGet("danh-gia/{interviewId:guid}")]
    [HttpGet("cham-diem/{applicationId:guid}")]
    [HttpGet("danh-gia")]
    public async Task<IActionResult> Evaluate(
        [FromRoute] Guid? interviewId,
        [FromRoute] Guid? applicationId,
        [FromQuery] bool isModal = false,
        CancellationToken cancellationToken = default)
    {
        var currentUserId = GetCurrentUserId();
        var effectiveInterviewId = interviewId ?? Guid.Empty;
        var effectiveApplicationId = applicationId ?? Guid.Empty;

        if (effectiveInterviewId == Guid.Empty && effectiveApplicationId == Guid.Empty)
        {
            TempData["ErrorMessage"] = "Không tìm thấy thông tin phỏng vấn hoặc hồ sơ ứng tuyển cần đánh giá.";
            return RedirectToAction("Index", "Home");
        }

        try
        {
            var model = await _evaluationService.GenerateEvaluationFormAsync(
                effectiveApplicationId,
                effectiveInterviewId,
                currentUserId,
                cancellationToken);

            var isAjax = Request.Headers.XRequestedWith == "XMLHttpRequest" || isModal;
            if (isAjax)
            {
                return PartialView("_EvaluationFormModalPartial", model);
            }

            return View("Evaluate", model);
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Không tìm thấy dữ liệu đánh giá: InterviewId={InterviewId}, ApplicationId={ApplicationId}", effectiveInterviewId, effectiveApplicationId);
            TempData["ErrorMessage"] = ex.Message;
            return RedirectToAction("Index", "Home");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi xảy ra khi tạo phiếu đánh giá cho Interview {InterviewId}", effectiveInterviewId);
            TempData["ErrorMessage"] = "Đã xảy ra sự cố khi tải phiếu đánh giá. Vui lòng thử lại sau.";
            return RedirectToAction("Index", "Home");
        }
    }

    /// <summary>
    /// POST: /nop-danh-gia
    /// Nộp phiếu đánh giá hoặc lưu bản nháp theo khung năng lực trong transaction an toàn.
    /// </summary>
    [HttpPost("nop-danh-gia")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(
        [FromForm] EvaluationFormSubmitInputModel input,
        CancellationToken cancellationToken = default)
    {
        var isAjax = Request.Headers.XRequestedWith == "XMLHttpRequest";
        var currentUserId = GetCurrentUserId();

        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .ToList();

            if (isAjax)
            {
                return Json(new { success = false, message = "Dữ liệu chấm điểm chưa hợp lệ. Vui lòng kiểm tra lại các trường thông tin.", errors });
            }

            TempData["ErrorMessage"] = "Dữ liệu chấm điểm chưa hợp lệ. Vui lòng kiểm tra lại.";
            return RedirectToAction(nameof(Evaluate), new { interviewId = input.InterviewId });
        }

        // Nếu nộp chính thức (IsDraft == false), bắt buộc toàn bộ tiêu chí phải được chấm từ 1 đến 5
        if (!input.IsDraft)
        {
            var unratedItems = input.Items.Where(i => i.Score < 1 || i.Score > 5).ToList();
            if (unratedItems.Count > 0)
            {
                var errorMsg = $"Vui lòng chọn điểm đánh giá (từ 1 đến 5) cho tiêu chí: {unratedItems[0].CriteriaName}";
                if (isAjax)
                {
                    return Json(new { success = false, message = errorMsg });
                }

                TempData["ErrorMessage"] = errorMsg;
                return RedirectToAction(nameof(Evaluate), new { interviewId = input.InterviewId });
            }
        }

        try
        {
            var formId = await _evaluationService.SubmitEvaluationAsync(input, currentUserId, cancellationToken);
            var successMsg = input.IsDraft
                ? "Đã lưu bản nháp phiếu đánh giá thành công! Bạn có thể quay lại cập nhật bất cứ lúc nào."
                : "Đã nộp phiếu đánh giá chính thức thành công! Phiếu đã được khóa an toàn bảo đảm tính bất biến.";

            if (isAjax)
            {
                return Json(new
                {
                    success = true,
                    formId,
                    isDraft = input.IsDraft,
                    message = successMsg,
                    redirectUrl = Url?.Action(nameof(Evaluate), "Evaluations", new { interviewId = input.InterviewId }) ?? $"/danh-gia/{input.InterviewId}"
                });
            }

            TempData["SuccessMessage"] = successMsg;
            return RedirectToAction(nameof(Evaluate), new { interviewId = input.InterviewId });
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Người dùng {UserId} không có quyền nộp đánh giá cho Interview {InterviewId}", currentUserId, input.InterviewId);
            if (isAjax) return Json(new { success = false, message = ex.Message });
            TempData["ErrorMessage"] = ex.Message;
            return RedirectToAction(nameof(Evaluate), new { interviewId = input.InterviewId });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Thao tác nộp đánh giá không hợp lệ: {Message}", ex.Message);
            if (isAjax) return Json(new { success = false, message = ex.Message });
            TempData["ErrorMessage"] = ex.Message;
            return RedirectToAction(nameof(Evaluate), new { interviewId = input.InterviewId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi hệ thống khi nộp phiếu đánh giá cho Interview {InterviewId}", input.InterviewId);
            var errorMsg = "Đã xảy ra sự cố khi lưu phiếu đánh giá. Vui lòng thử lại sau.";
            if (isAjax) return Json(new { success = false, message = errorMsg });
            TempData["ErrorMessage"] = errorMsg;
            return RedirectToAction(nameof(Evaluate), new { interviewId = input.InterviewId });
        }
    }

    /// <summary>
    /// GET: /so-sanh-ung-vien/{jobPostingId}
    /// Màn hình Bảng so sánh ứng viên trên cùng một thước đo khung năng lực (dành riêng cho HR Manager & Admin).
    /// </summary>
    [HttpGet("so-sanh-ung-vien/{jobPostingId:guid}")]
    [Authorize(Roles = $"{UserRoles.HRManager},{UserRoles.Admin},Trưởng phòng nhân sự,Quản trị viên,HiringManager,Quản lý tuyển dụng")]
    public async Task<IActionResult> Comparison(Guid jobPostingId, CancellationToken cancellationToken = default)
    {
        var currentUserId = GetCurrentUserId();

        try
        {
            var model = await _evaluationService.GetComparisonByJobAsync(jobPostingId, currentUserId, cancellationToken);
            return View("Comparison", model);
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Không tìm thấy tin tuyển dụng: JobPostingId={JobPostingId}", jobPostingId);
            TempData["ErrorMessage"] = ex.Message;
            return RedirectToAction("Index", "Jobs");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Từ chối truy cập bảng so sánh ứng viên cho UserId={UserId}", currentUserId);
            TempData["ErrorMessage"] = ex.Message;
            return RedirectToAction("Index", "Home");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi xảy ra khi tải bảng so sánh ứng viên cho JobPosting {JobPostingId}", jobPostingId);
            TempData["ErrorMessage"] = "Không thể tải bảng so sánh ứng viên. Vui lòng thử lại sau.";
            return RedirectToAction("Index", "Jobs");
        }
    }

    /// <summary>
    /// GET: /chi-tiet-danh-gia/{evaluationFormId}
    /// Xem chi tiết một phiếu đánh giá đã hoàn thiện.
    /// </summary>
    [HttpGet("chi-tiet-danh-gia/{evaluationFormId:guid}")]
    public async Task<IActionResult> Detail(Guid evaluationFormId, CancellationToken cancellationToken = default)
    {
        var currentUserId = GetCurrentUserId();

        try
        {
            var model = await _evaluationService.GetEvaluationDetailAsync(evaluationFormId, currentUserId, cancellationToken);
            if (model == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy phiếu đánh giá yêu cầu.";
                return RedirectToAction("Index", "Home");
            }

            return View("Detail", model);
        }
        catch (UnauthorizedAccessException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
            return RedirectToAction("Index", "Home");
        }
    }

    private Guid GetCurrentUserId()
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userIdStr, out var id) ? id : Guid.Empty;
    }
}
