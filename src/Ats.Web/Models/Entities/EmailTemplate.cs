using Ats.Web.Models.Enums;

namespace Ats.Web.Models.Entities;

public class EmailTemplate : BaseEntity
{
    public Guid Id { get; set; }
    public EmailTemplateType TemplateType { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public bool IsHtml { get; set; } = true;
}
