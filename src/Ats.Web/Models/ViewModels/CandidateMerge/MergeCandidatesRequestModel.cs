using System.ComponentModel.DataAnnotations;

namespace Ats.Web.Models.ViewModels.CandidateMerge;

/// <summary>
/// Model gửi yêu cầu gộp 2 hồ sơ ứng viên trùng lặp
/// </summary>
public class MergeCandidatesRequestModel
{
    [Required(ErrorMessage = "Vui lòng chọn hồ sơ chính")]
    public Guid PrimaryCandidateId { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn hồ sơ phụ cần gộp")]
    public Guid SecondaryCandidateId { get; set; }

    public string? Reason { get; set; }

    // Tùy chọn ưu tiên giữ lại thông tin
    public string KeepPhone { get; set; } = "primary"; // primary hoặc secondary
    public string KeepEmail { get; set; } = "primary";
    public string KeepName { get; set; } = "primary";
}
