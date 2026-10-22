using System.ComponentModel.DataAnnotations.Schema;

namespace Ats.Web.Models.Entities;

public class CandidateTag : BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CandidateId { get; set; }
    public Guid CatalogId { get; set; }
    public Guid? CreatedByUserId { get; set; }

    public Candidate Candidate { get; set; } = null!;
    public RecruitmentCatalog Catalog { get; set; } = null!;
    public User? CreatedByUser { get; set; }

    [NotMapped]
    public string TagName => Catalog.Name;

    [NotMapped]
    public string? ColorCode => Catalog.ColorCode;
}
