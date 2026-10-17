namespace Ats.Web.Models.ViewModels.Candidates;

/// <summary>
/// Kết quả kiểm tra trùng lặp thông tin ứng viên (SCRUM-47).
/// Phục vụ cơ chế cảnh báo sớm và ngăn chặn nộp trùng vị trí.
/// </summary>
public class CandidateDuplicateCheckResultViewModel
{
    /// <summary>
    /// Đánh dấu phát hiện trùng lặp hồ sơ ứng viên trong hệ thống (qua Email hoặc Số điện thoại).
    /// </summary>
    public bool IsDuplicate { get; set; }

    /// <summary>
    /// Tiêu chí trùng khớp ("Email", "Số điện thoại", hoặc "Email và Số điện thoại").
    /// </summary>
    public string? MatchedBy { get; set; }

    /// <summary>
    /// Mã ID của hồ sơ ứng viên đã tồn tại trước đó trong hệ thống (nếu có).
    /// </summary>
    public Guid? ExistingCandidateId { get; set; }

    /// <summary>
    /// Mã ID đơn ứng tuyển đã có của ứng viên (nếu có).
    /// </summary>
    public Guid? ExistingApplicationId { get; set; }

    /// <summary>
    /// Đánh dấu ứng viên đã có hồ sơ đang xử lý (IN_PROCESS) ở chính vị trí tuyển dụng được chọn hay chưa.
    /// </summary>
    public bool HasActiveApplicationInSameJob { get; set; }

    /// <summary>
    /// Họ và tên của ứng viên trùng khớp đã có trong cơ sở dữ liệu.
    /// </summary>
    public string? CandidateFullName { get; set; }

    /// <summary>
    /// Email của ứng viên trùng khớp đã có.
    /// </summary>
    public string? CandidateEmail { get; set; }

    /// <summary>
    /// Số điện thoại của ứng viên trùng khớp đã có.
    /// </summary>
    public string? CandidatePhone { get; set; }

    /// <summary>
    /// Thông báo chi tiết hoặc cảnh báo trả về cho giao diện người dùng.
    /// </summary>
    public string? Message { get; set; }
}
