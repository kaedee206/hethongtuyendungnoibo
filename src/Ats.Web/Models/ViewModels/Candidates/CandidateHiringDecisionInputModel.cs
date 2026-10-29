using System.ComponentModel.DataAnnotations;
using Ats.Web.Models.Enums;

namespace Ats.Web.Models.ViewModels.Candidates;

/// <summary>
/// Model tiếp nhận quyết định tuyển dụng của Trưởng bộ phận (Hiring Manager) cho ứng viên (SP - 7).
/// </summary>
public class CandidateHiringDecisionInputModel : IValidatableObject
{
    [Required(ErrorMessage = "Mã hồ sơ ứng tuyển (ApplicationId) là bắt buộc.")]
    public Guid ApplicationId { get; set; }

    /// <summary>
    /// Loại quyết định: OFFER (Chuyển sang đề xuất offer) hoặc REJECTED (Loại hồ sơ)
    /// </summary>
    [Required(ErrorMessage = "Vui lòng chọn quyết định tuyển dụng.")]
    public CandidateStage Decision { get; set; }

    /// <summary>
    /// Mã lý do loại từ danh mục (Bắt buộc nếu Decision == REJECTED)
    /// </summary>
    public Guid? RejectionReasonId { get; set; }

    /// <summary>
    /// Nhận xét, giải trình quyết định hoặc ghi chú bổ sung
    /// </summary>
    [StringLength(2000, ErrorMessage = "Ghi chú không được vượt quá 2000 ký tự.")]
    public string? Note { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ApplicationId == Guid.Empty)
        {
            yield return new ValidationResult("Mã hồ sơ ứng tuyển không hợp lệ.", [nameof(ApplicationId)]);
        }

        if (Decision != CandidateStage.OFFER && Decision != CandidateStage.REJECTED)
        {
            yield return new ValidationResult("Quyết định không hợp lệ. Chỉ chấp nhận Chuyển sang đề xuất offer (OFFER) hoặc Loại (REJECTED).", [nameof(Decision)]);
        }

        if (Decision == CandidateStage.REJECTED)
        {
            if (!RejectionReasonId.HasValue || RejectionReasonId.Value == Guid.Empty)
            {
                yield return new ValidationResult("Khi quyết định loại hồ sơ, bắt buộc phải chọn lý do loại từ danh mục.", [nameof(RejectionReasonId)]);
            }
        }
    }
}
