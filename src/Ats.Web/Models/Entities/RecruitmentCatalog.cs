namespace Ats.Web.Models.Entities;

public class RecruitmentCatalog : BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string CatalogType { get; set; } = string.Empty; // SOURCE, REJECTION_REASON, LOCATION, WORK_TYPE
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int DisplayOrder { get; set; } = 0;
    public bool IsActive { get; set; } = true;
    public bool IsSystem { get; set; } = false; // Protected system catalog items
}
