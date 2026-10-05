namespace Ats.Web.Models.Entities;

public class PipelineStage : BaseEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty; // Ví dụ: Applied, Screening, Interview, Offer, Hired, Rejected
    public int StageOrder { get; set; }
    public string? ColorCode { get; set; }
}
