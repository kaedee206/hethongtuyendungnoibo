namespace Ats.Web.Models.Entities;

public class Notification : BaseEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? Link { get; set; }
    public bool IsRead { get; set; } = false;

    public User User { get; set; } = null!;
}
