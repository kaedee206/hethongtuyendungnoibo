using System.Security.Claims;
using Ats.Web.Constants;
using Ats.Web.Models.DTOs.Offers;
using Ats.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

/// <summary>
/// Controller điều phối xử lý Thư mời nhận việc (Offers), Thương lượng lại & Từ chối Offer (User Story SP-7).
/// Tuân thủ quy tắc Clean Controller: Chỉ điều phối, xác thực quyền và gọi OfferService.
/// </summary>
[Route("offers")]
[Authorize(Roles = $"{UserRoles.Recruiter},{UserRoles.HRManager},{UserRoles.Admin},{UserRoles.HiringManager}")]
public class OffersController(
    IOfferService offerService,
    IRequisitionAssignmentService? assignmentService,
    ILogger<OffersController> logger) : Controller
{
    private readonly IOfferService _offerService = offerService;
    private readonly IRequisitionAssignmentService? _assignmentService = assignmentService;
    private readonly ILogger<OffersController> _logger = logger;

    /// <summary>
    /// GET: /offers/rejection-reasons
    /// Trả về danh sách danh mục lý do từ chối chuẩn hóa.
    /// </summary>
    [HttpGet("rejection-reasons")]
    public async Task<IActionResult> GetRejectionReasons(CancellationToken cancellationToken)
    {
        var reasons = await _offerService.GetRejectionReasonsAsync(cancellationToken);
        return Json(new { success = true, data = reasons });
    }

    /// <summary>
    /// GET: /offers/{id}
    /// Lấy chi tiết thông tin Offer phục vụ binding lên Modal.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetDetails(Guid id, CancellationToken cancellationToken)
    {
        var offer = await _offerService.GetOfferDetailsAsync(id, cancellationToken);
        if (offer == null)
        {
            return Json(new { success = false, message = "Không tìm thấy thông tin Thư mời nhận việc." });
        }

        // Kiểm tra phân quyền Recruiter nếu có
        var currentUserId = GetCurrentUserId();
        if (IsRecruiterOnly() && _assignmentService != null && currentUserId != Guid.Empty)
        {
            var canAccess = await _assignmentService.CanRecruiterAccessApplicationAsync(currentUserId, offer.ApplicationId);
            if (!canAccess)
            {
                return Json(new { success = false, message = "Bạn không có quyền thao tác trên hồ sơ này do không phụ trách vị trí tương ứng." });
            }
        }

        return Json(new { success = true, data = offer });
    }

    /// <summary>
    /// GET: /offers/application/{applicationId}/history
    /// Lấy toàn bộ lịch sử các phiên bản Offer của hồ sơ ứng tuyển.
    /// </summary>
    [HttpGet("application/{applicationId:guid}/history")]
    public async Task<IActionResult> GetHistory(Guid applicationId, CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        if (IsRecruiterOnly() && _assignmentService != null && currentUserId != Guid.Empty)
        {
            var canAccess = await _assignmentService.CanRecruiterAccessApplicationAsync(currentUserId, applicationId);
            if (!canAccess)
            {
                return Json(new { success = false, message = "Bạn không có quyền truy cập lịch sử offer của ứng viên này." });
            }
        }

        var history = await _offerService.GetOfferHistoryByApplicationAsync(applicationId, cancellationToken);
        return Json(new { success = true, data = history });
    }

    /// <summary>
    /// POST: /offers/reject
    /// Xử lý ghi nhận ứng viên từ chối Thư mời nhận việc, lưu lý do và tùy chọn đưa vào Kho tiềm năng.
    /// </summary>
    [HttpPost("reject")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject([FromForm] RejectOfferDto dto, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Json(new { success = false, message = "Dữ liệu gửi lên không hợp lệ." });
        }

        var offer = await _offerService.GetOfferByIdAsync(dto.OfferId, cancellationToken);
        if (offer == null)
        {
            return Json(new { success = false, message = "Không tìm thấy Thư mời nhận việc cần từ chối." });
        }

        var currentUserId = GetCurrentUserId();
        if (IsRecruiterOnly() && _assignmentService != null && currentUserId != Guid.Empty)
        {
            var canAccess = await _assignmentService.CanRecruiterAccessApplicationAsync(currentUserId, offer.ApplicationId);
            if (!canAccess)
            {
                return Json(new { success = false, message = "Bạn không có quyền từ chối Offer cho hồ sơ này." });
            }
        }

        var (success, message, updatedOffer) = await _offerService.RejectOfferAsync(dto, currentUserId, cancellationToken);
        return Json(new { success, message, data = updatedOffer != null ? new { updatedOffer.Id, updatedOffer.Status } : null });
    }

    /// <summary>
    /// POST: /offers/revise
    /// Xử lý tạo Thư mời sửa đổi khi ứng viên thương lượng lại (Offer Revision Flow).
    /// </summary>
    [HttpPost("revise")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Revise([FromForm] ReviseOfferDto dto, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Json(new { success = false, message = "Dữ liệu sửa đổi Thư mời nhận việc không hợp lệ." });
        }

        var originalOffer = await _offerService.GetOfferByIdAsync(dto.OriginalOfferId, cancellationToken);
        if (originalOffer == null)
        {
            return Json(new { success = false, message = "Không tìm thấy Thư mời nhận việc gốc để tạo phiên bản sửa đổi." });
        }

        var currentUserId = GetCurrentUserId();
        if (IsRecruiterOnly() && _assignmentService != null && currentUserId != Guid.Empty)
        {
            var canAccess = await _assignmentService.CanRecruiterAccessApplicationAsync(currentUserId, originalOffer.ApplicationId);
            if (!canAccess)
            {
                return Json(new { success = false, message = "Bạn không có quyền sửa đổi Offer cho hồ sơ này." });
            }
        }

        var (success, message, newOffer) = await _offerService.ReviseOfferAsync(dto, currentUserId, cancellationToken);
        return Json(new
        {
            success,
            message,
            data = newOffer != null ? new { newOffer.Id, newOffer.Version, newOffer.Status } : null
        });
    }

    private Guid GetCurrentUserId()
    {
        var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        _ = Guid.TryParse(idStr, out var id);
        return id;
    }

    private bool IsRecruiterOnly()
    {
        return User?.Identity?.IsAuthenticated == true &&
               User.IsInRole(UserRoles.Recruiter) &&
               !User.IsInRole(UserRoles.Admin) &&
               !User.IsInRole(UserRoles.HRManager);
    }
}
