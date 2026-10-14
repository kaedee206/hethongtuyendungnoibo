namespace Ats.Web.Models.Entities;

/// <summary>
/// Thực thể danh mục chuẩn hóa lý do loại ứng viên (SCRUM-42).
/// Phục vụ phân tích tỷ lệ rơi rụng (drop-off rate) ở từng vòng tuyển dụng.
/// </summary>
public class RejectionReason : BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Mã định danh chuẩn hóa của lý do loại (ví dụ: REJ_EXP, REJ_SALARY, REJ_CULTURE, REJ_SKILL)
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Tên lý do loại hồ sơ hiển thị cho người dùng
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Mô tả chi tiết hoặc ngữ cảnh áp dụng lý do này
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Thứ tự ưu tiên hiển thị trên danh sách lựa chọn
    /// </summary>
    public int DisplayOrder { get; set; } = 0;

    /// <summary>
    /// Trạng thái kích hoạt. Chỉ các lý do có IsActive == true mới được phép chọn khi loại hồ sơ.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Danh sách các bản ghi lịch sử chuyển giai đoạn sử dụng lý do này
    /// </summary>
    public ICollection<CandidateStageHistory> StageHistories { get; set; } = new List<CandidateStageHistory>();
}
