using System.ComponentModel.DataAnnotations;

namespace Ats.Web.Models.ViewModels.Interviews;

/// <summary>
/// InputModel xử lý nộp phiếu đánh giá phỏng vấn (Scrum - Số 57)
/// </summary>
public class InterviewEvaluationSubmitInputModel
{
    [Required(ErrorMessage = "Mã buổi phỏng vấn là bắt buộc.")]
    public Guid InterviewId { get; set; }

    /// <summary>
    /// Bắt buộc chọn kết luận: Nên tuyển, Cân nhắc, Không nên tuyển
    /// </summary>
    [Required(ErrorMessage = "Bắt buộc chọn kết luận đánh giá: Nên tuyển, Cân nhắc hoặc Không nên tuyển.")]
    public string Recommendation { get; set; } = string.Empty;

    [Range(1.0, 5.0, ErrorMessage = "Điểm đánh giá phải từ 1.0 đến 5.0.")]
    public decimal? OverallScore { get; set; }

    public string? Strengths { get; set; }

    public string? Weaknesses { get; set; }

    public string? DetailedFeedback { get; set; }
}
