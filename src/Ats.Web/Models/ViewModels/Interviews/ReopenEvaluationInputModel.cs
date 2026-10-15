using System.ComponentModel.DataAnnotations;

namespace Ats.Web.Models.ViewModels.Interviews;

/// <summary>
/// InputModel cho Recruiter mở lại phiếu đánh giá trong vòng 24 giờ (Scrum - Số 57)
/// </summary>
public class ReopenEvaluationInputModel
{
    [Required(ErrorMessage = "Mã phiếu đánh giá là bắt buộc.")]
    public Guid EvaluationId { get; set; }

    [MaxLength(500, ErrorMessage = "Lý do mở lại tối đa 500 ký tự.")]
    public string? Reason { get; set; }
}
