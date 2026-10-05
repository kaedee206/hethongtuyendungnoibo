using System.ComponentModel.DataAnnotations.Schema;

namespace Ats.Web.Models.Entities;

public class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? UserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    
    [Column(TypeName = "jsonb")]
    public string? OldValues { get; set; }
    
    [Column(TypeName = "jsonb")]
    public string? NewValues { get; set; }
    
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    
    public User? User { get; set; }
}
