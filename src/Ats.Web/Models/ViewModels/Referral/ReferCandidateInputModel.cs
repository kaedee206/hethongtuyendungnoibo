using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace Ats.Web.Models.ViewModels.Referral;

/// <summary>
/// Model nhận dữ liệu form giới thiệu ứng viên nội bộ
/// </summary>
public class ReferCandidateInputModel
{
    [Required(ErrorMessage = "Vui lòng chọn vị trí tuyển dụng.")]
    [Display(Name = "Vị trí tuyển dụng")]
    public Guid JobPostingId { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập họ và tên ứng viên.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Họ và tên ứng viên phải từ 2 đến 100 ký tự.")]
    [Display(Name = "Họ và tên ứng viên")]
    public string CandidateName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập email ứng viên.")]
    [EmailAddress(ErrorMessage = "Địa chỉ email ứng viên không đúng định dạng.")]
    [StringLength(150, ErrorMessage = "Email tối đa 150 ký tự.")]
    [Display(Name = "Email ứng viên")]
    public string CandidateEmail { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập số điện thoại ứng viên.")]
    [RegularExpression(@"^(0|\+84)[3|5|7|8|9][0-9]{8}$", ErrorMessage = "Số điện thoại không hợp lệ (định dạng Việt Nam 10 chữ số).")]
    [Display(Name = "Số điện thoại")]
    public string CandidatePhone { get; set; } = string.Empty;

    [Url(ErrorMessage = "Đường dẫn LinkedIn không hợp lệ.")]
    [Display(Name = "LinkedIn / Portfolio")]
    public string? LinkedinUrl { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập ghi chú giới thiệu để ban tuyển dụng có cơ sở đánh giá.")]
    [StringLength(2000, MinimumLength = 10, ErrorMessage = "Ghi chú giới thiệu tối thiểu 10 ký tự và tối đa 2000 ký tự.")]
    [Display(Name = "Ghi chú giới thiệu")]
    public string ReferralNote { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng đính kèm file CV của ứng viên.")]
    [Display(Name = "File CV (PDF, DOC, DOCX)")]
    public IFormFile? CvFile { get; set; }

    [Range(typeof(bool), "true", "true", ErrorMessage = "Bạn cần cam kết thông tin người quen được giới thiệu là trung thực và đã có sự đồng ý của ứng viên.")]
    [Display(Name = "Cam kết tính xác thực")]
    public bool AgreeTerms { get; set; }
}
