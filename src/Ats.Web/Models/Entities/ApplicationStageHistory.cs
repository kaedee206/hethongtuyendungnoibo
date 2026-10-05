namespace Ats.Web.Models.Entities;

public class ApplicationStageHistory : BaseEntity
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid? FromStageId { get; set; }
    public Guid ToStageId { get; set; }
    public Guid ChangedByUserId { get; set; }
    public string? Comment { get; set; }
    public int? DurationHours { get; set; }

    public Application Application { get; set; } = null!;
    public PipelineStage? FromStage { get; set; }
    public PipelineStage ToStage { get; set; } = null!;
    public User ChangedByUser { get; set; } = null!;
}
