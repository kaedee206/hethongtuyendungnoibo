using System.Security.Claims;
using Ats.Web.Models.DTOs.InterviewQuestions;
using Ats.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

/// <summary>
/// Controller cung cấp API gợi ý bộ câu hỏi phỏng vấn theo tiêu chí & chức danh (User Story SP-7).
/// Tuân thủ quy chuẩn: Primary Constructor, kebab-case routing, Clean Controller, Exception handling & Logging.
/// </summary>
[Authorize]
[Route("api/interview-questions")]
public class InterviewQuestionsController(
    IInterviewQuestionSuggestionService questionService,
    ILogger<InterviewQuestionsController> logger) : Controller
{
    private readonly IInterviewQuestionSuggestionService _questionService = questionService;
    private readonly ILogger<InterviewQuestionsController> _logger = logger;

    /// <summary>
    /// GET: /api/interview-questions/suggestions
    /// Lấy danh sách câu hỏi gợi ý, tự động load theo chức danh ứng tuyển và tiêu chí của vòng hiện tại.
    /// Kèm trạng thái hỏi trùng lặp qua các vòng và Good Answer Rubric (AC1, AC2, AC3).
    /// </summary>
    [HttpGet("suggestions")]
    public async Task<IActionResult> GetSuggestions(
        [FromQuery] Guid candidateId,
        [FromQuery] Guid interviewId,
        [FromQuery] Guid? positionId,
        [FromQuery] Guid? criteriaId,
        [FromQuery] string? criteriaName,
        CancellationToken cancellationToken = default)
    {
        if (candidateId == Guid.Empty || interviewId == Guid.Empty)
        {
            return BadRequest(new
            {
                success = false,
                message = "Vui lòng cung cấp đầy đủ thông tin CandidateId và InterviewId."
            });
        }

        try
        {
            var data = await _questionService.GetSuggestedQuestionsAsync(
                candidateId,
                interviewId,
                positionId,
                criteriaId,
                criteriaName,
                cancellationToken);

            return Ok(new
            {
                success = true,
                data
            });
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Không tìm thấy tài nguyên khi lấy câu hỏi gợi ý: CandidateId={CandidateId}, InterviewId={InterviewId}", candidateId, interviewId);
            return NotFound(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi lấy danh sách câu hỏi gợi ý cho ứng viên {CandidateId}", candidateId);
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                message = "Đã xảy ra sự cố khi tải danh sách câu hỏi gợi ý. Vui lòng thử lại sau."
            });
        }
    }

    /// <summary>
    /// POST: /api/interview-questions/toggle-asked
    /// Đánh dấu hoặc bỏ đánh dấu câu hỏi đã hỏi trong phiên phỏng vấn hiện tại (AC2).
    /// </summary>
    [HttpPost("toggle-asked")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> ToggleAsked(
        [FromBody] ToggleQuestionAskedInputModel input,
        CancellationToken cancellationToken = default)
    {
        if (input == null || input.CandidateId == Guid.Empty || input.InterviewId == Guid.Empty || input.QuestionId == Guid.Empty)
        {
            return BadRequest(new
            {
                success = false,
                message = "Dữ liệu yêu cầu không hợp lệ."
            });
        }

        var currentUserId = GetCurrentUserId();
        if (currentUserId == Guid.Empty)
        {
            return Unauthorized(new
            {
                success = false,
                message = "Vui lòng đăng nhập để thực hiện thao tác này."
            });
        }

        try
        {
            var result = await _questionService.ToggleQuestionAskedAsync(input, currentUserId, cancellationToken);
            return Ok(new
            {
                success = result.Success,
                message = result.Message,
                questionId = result.QuestionId,
                isAskedInCurrentRound = result.IsAskedInCurrentRound,
                currentRoundAskedCount = result.CurrentRoundAskedCount
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi toggle trạng thái câu hỏi {QuestionId} cho ứng viên {CandidateId}", input.QuestionId, input.CandidateId);
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                message = "Không thể cập nhật trạng thái câu hỏi lúc này. Vui lòng thử lại."
            });
        }
    }

    private Guid GetCurrentUserId()
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userIdStr, out var id) ? id : Guid.Empty;
    }
}
