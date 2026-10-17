using System.ComponentModel.DataAnnotations;
using Ats.Web.Models.Enums;
using Microsoft.AspNetCore.Http;

namespace Ats.Web.Models.ViewModels.Candidates;

/// <summary>
/// Model tiếp nhận dữ liệu thêm ứng viên thủ công vào một vị trí tuyển dụng (SCRUM-47).
/// Áp dụng chặt chẽ DataAnnotations tiếng Việt và IValidatableObject.
/// </summary>
public class CandidateManualCreateInputModel : IValidatableObject
{
    /// <summary>
    /// Vị trí tuyển dụng mục tiêu (Job Posting)
    /// </summary>
    [Required(ErrorMessage = "Vui lòng chọn vị trí tuyển dụng cho ứng viên.")]
    [Display(Name = "Vị trí tuyển dụng")]
    public Guid JobPostingId { get; set; }

    /// <summary>
    /// Họ và tên đầy đủ của ứng viên
    /// </summary>
    [Required(ErrorMessage = "Họ và tên ứng viên không được để trống.")]
    [StringLength(150, ErrorMessage = "Họ và tên không được vượt quá 150 ký tự.")]
    [Display(Name = "Họ và tên ứng viên")]
    public string FullName { get; set; } = string.Empty;

    /// <summary>
    /// Địa chỉ email liên hệ
    /// </summary>
    [Required(ErrorMessage = "Địa chỉ email không được để trống.")]
    [EmailAddress(ErrorMessage = "Địa chỉ email không đúng định dạng.")]
    [StringLength(256, ErrorMessage = "Email không được vượt quá 256 ký tự.")]
    [Display(Name = "Địa chỉ email")]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Số điện thoại (chuẩn định dạng Việt Nam: 10 chữ số)
    /// </summary>
    [RegularExpression(@"^(0|\+84|84)[3|5|7|8|9][0-9]{8}$", ErrorMessage = "Số điện thoại không hợp lệ (định dạng Việt Nam gồm 10 chữ số, ví dụ: 0912345678 hoặc +84912345678).")]
    [StringLength(20, ErrorMessage = "Số điện thoại không được vượt quá 20 ký tự.")]
    [Display(Name = "Số điện thoại")]
    public string? PhoneNumber { get; set; }

    /// <summary>
    /// Đường dẫn hồ sơ LinkedIn
    /// </summary>
    [Url(ErrorMessage = "Đường dẫn LinkedIn không đúng định dạng URL.")]
    [StringLength(500, ErrorMessage = "Đường dẫn LinkedIn không được vượt quá 500 ký tự.")]
    [Display(Name = "Đường dẫn LinkedIn")]
    public string? LinkedInUrl { get; set; }

    /// <summary>
    /// Nguồn tiếp nhận ứng viên (mặc định là LINKEDIN)
    /// </summary>
    [Required(ErrorMessage = "Vui lòng chọn nguồn ứng viên.")]
    [Display(Name = "Nguồn tiếp nhận ứng viên")]
    public CandidateSource Source { get; set; } = CandidateSource.LINKEDIN;

    /// <summary>
    /// Tệp hồ sơ đính kèm (CV) - Giới hạn định dạng .pdf, .doc, .docx và tối đa 10MB
    /// </summary>
    [Display(Name = "Tệp hồ sơ CV")]
    public IFormFile? CvFile { get; set; }

    /// <summary>
    /// Ghi chú sơ bộ hoặc nhận xét ban đầu của Recruiter
    /// </summary>
    [StringLength(2000, ErrorMessage = "Ghi chú ban đầu không được vượt quá 2000 ký tự.")]
    [Display(Name = "Ghi chú / Đánh giá sơ bộ")]
    public string? Note { get; set; }

    /// <summary>
    /// Mã đơn ứng tuyển được tạo ra sau khi lưu thành công (phục vụ điều hướng/truy vấn)
    /// </summary>
    public Guid? CreatedApplicationId { get; set; }

    /// <summary>
    /// Kiểm tra hợp lệ nghiệp vụ bổ sung cho tệp đính kèm CV
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (CvFile != null && CvFile.Length > 0)
        {
            // Kiểm tra dung lượng tối đa 10MB
            const long maxFileSize = 10 * 1024 * 1024;
            if (CvFile.Length > maxFileSize)
            {
                yield return new ValidationResult(
                    "Dung lượng tệp CV không được vượt quá 10MB.",
                    [nameof(CvFile)]);
            }

            // Kiểm tra định dạng đuôi tệp cho phép
            var extension = Path.GetExtension(CvFile.FileName).ToLowerInvariant();
            var allowedExtensions = new[] { ".pdf", ".doc", ".docx" };
            if (string.IsNullOrEmpty(extension) || !allowedExtensions.Contains(extension))
            {
                yield return new ValidationResult(
                    "Tệp CV chỉ chấp nhận các định dạng văn bản: .pdf, .doc, .docx.",
                    [nameof(CvFile)]);
            }
        }
    }
}
