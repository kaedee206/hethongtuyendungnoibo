namespace Ats.Web.Models.Entities;

public class PipelineStage : BaseEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty; // Ví dụ: Applied, Screening, Interview, Offer, Hired, Rejected
    public int StageOrder { get; set; }
    public string? ColorCode { get; set; }

    /// <summary>
    /// Ngưỡng thời gian tối đa cho phép xử lý hồ sơ ở giai đoạn này tính theo ngày (SLA threshold in days - SCRUM-327).
    /// </summary>
    public int SlaDays { get; set; } = 3;

    /// <summary>
    /// Trạng thái bật/tắt theo dõi và cảnh báo quá hạn SLA cho giai đoạn này (SCRUM-327).
    /// </summary>
    public bool IsSlaEnabled { get; set; } = true;
}
