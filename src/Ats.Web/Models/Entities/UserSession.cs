namespace Ats.Web.Models.Entities;

public class UserSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public Guid UserId { get; set; }
    public string SessionId { get; set; } = string.Empty;
    
    public string IpAddress { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastActiveAt { get; set; } = DateTimeOffset.UtcNow;
    
    public bool IsRevoked { get; set; } = false;
}
