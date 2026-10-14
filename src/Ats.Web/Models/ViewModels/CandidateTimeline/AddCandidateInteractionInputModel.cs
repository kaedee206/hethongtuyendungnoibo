using System.ComponentModel.DataAnnotations;
using Ats.Web.Models.Enums;

namespace Ats.Web.Models.ViewModels.CandidateTimeline;

public class AddCandidateInteractionInputModel
{
    [Required]
    public Guid CandidateId { get; set; }

    public Guid? ApplicationId { get; set; }

    public CandidateInteractionType InteractionType { get; set; } = CandidateInteractionType.CALL;

    [Required(ErrorMessage = "Vui lòng nhập tiêu đề cuộc gọi hoặc tương tác")]
    [StringLength(200, ErrorMessage = "Tiêu đề không được vượt quá 200 ký tự")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập chi tiết nội dung đã trao đổi / nói gì với ứng viên")]
    public string Content { get; set; } = string.Empty;

    [StringLength(200, ErrorMessage = "Kết quả trao đổi không được vượt quá 200 ký tự")]
    public string? Outcome { get; set; }

    public DateTimeOffset? InteractionDate { get; set; }
}
