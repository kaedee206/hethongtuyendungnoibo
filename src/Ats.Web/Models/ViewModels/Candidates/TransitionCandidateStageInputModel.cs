using System.ComponentModel.DataAnnotations;
using Ats.Web.Models.Enums;

namespace Ats.Web.Models.ViewModels.Candidates;

/// <summary>
/// Model tiếp nhận dữ liệu yêu cầu chuyển giai đoạn ứng viên (SCRUM-42).
/// Thực thi kiểm tra nghiệp vụ tầng Model bằng IValidatableObject kèm thông báo tiếng Việt.
/// </summary>
public class TransitionCandidateStageInputModel : IValidatableObject
{
    /// <summary>
    /// Mã hồ sơ ứng tuyển cần chuyển giai đoạn
    /// </summary>
    [Required(ErrorMessage = "Mã hồ sơ ứng tuyển (ApplicationId) là bắt buộc.")]
    public Guid ApplicationId { get; set; }

    /// <summary>
    /// Giai đoạn tuyển dụng đích muốn chuyển tới
    /// </summary>
    [Required(ErrorMessage = "Giai đoạn đích (ToStage) là bắt buộc.")]
    public CandidateStage ToStage { get; set; }

    /// <summary>
    /// Mã lý do loại từ danh mục (Bắt buộc phải chọn khi ToStage == REJECTED)
    /// </summary>
    public Guid? RejectionReasonId { get; set; }

    /// <summary>
    /// Ghi chú, giải trình hoặc phản hồi bổ sung của người thực hiện
    /// </summary>
    [StringLength(2000, ErrorMessage = "Ghi chú không được vượt quá 2000 ký tự.")]
    public string? Note { get; set; }

    /// <summary>
    /// Cờ xác nhận của Trưởng phòng Nhân sự (HRManager) khi muốn duyệt thẳng ứng viên lên OFFER
    /// mà không cần trải qua giai đoạn Phỏng vấn (INTERVIEW).
    /// </summary>
    public bool SkipInterviewConfirmedByHrManager { get; set; } = false;

    /// <summary>
    /// Kiểm tra tính hợp lệ của dữ liệu đầu vào theo các quy tắc nghiệp vụ.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ApplicationId == Guid.Empty)
        {
            yield return new ValidationResult(
                "Mã hồ sơ ứng tuyển không hợp lệ.",
                [nameof(ApplicationId)]);
        }

        // Quy tắc 1: Khi chuyển sang REJECTED, bắt buộc phải có RejectionReasonId
        if (ToStage == CandidateStage.REJECTED)
        {
            if (!RejectionReasonId.HasValue || RejectionReasonId.Value == Guid.Empty)
            {
                yield return new ValidationResult(
                    "Khi loại hồ sơ (REJECTED), bắt buộc phải chọn một lý do hợp lệ từ danh mục lý do loại.",
                    [nameof(RejectionReasonId)]);
            }
        }
    }
}
