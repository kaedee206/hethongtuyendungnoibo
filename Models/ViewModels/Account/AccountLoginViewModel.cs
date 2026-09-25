using System.ComponentModel.DataAnnotations;

namespace Ats.Web.Models.ViewModels.Account;

/// <summary>
/// ViewModel cho màn hình đăng nhập (Views/Account/Login.cshtml).
/// Liên quan Sprint 1 - Story S1-01:
///   "Là nhân sự nội bộ, tôi muốn đăng nhập bằng email công ty và mật khẩu,
///   để truy cập được dữ liệu tuyển dụng mà người ngoài không xem được."
/// AC liên quan:
///   - Đăng nhập đúng -> vào trang chủ tương ứng vai trò (xử lý ở Controller/Service).
///   - Sai thông tin -> hiển thị THÔNG BÁO CHUNG, không tiết lộ email có tồn tại hay không.
///   - Khoá tạm 15 phút sau 5 lần sai liên tiếp (Service xử lý; View chỉ hiển thị message).
/// </summary>
public class AccountLoginViewModel
{
    [Required(ErrorMessage = "Vui lòng nhập email công ty.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    [Display(Name = "Email công ty")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
    [DataType(DataType.Password)]
    [Display(Name = "Mật khẩu")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Ghi nhớ đăng nhập")]
    public bool RememberMe { get; set; }

    /// <summary>
    /// Đường dẫn quay lại sau khi đăng nhập thành công (hỗ trợ deep-link
    /// khi người dùng bị redirect tới trang Login do chưa xác thực).
    /// Được Controller gán từ query string "?ReturnUrl=" khi trả View(GET).
    /// </summary>
    public string? ReturnUrl { get; set; }
}
