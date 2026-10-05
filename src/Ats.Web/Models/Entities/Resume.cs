namespace Ats.Web.Models.Entities;

public class Resume : BaseEntity
{
    public Guid Id { get; set; }
    public Guid CandidateId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string MimeType { get; set; } = string.Empty;
    public string? ParsedText { get; set; }
    public bool IsPrimary { get; set; } = false;

    public Candidate Candidate { get; set; } = null!;
}
