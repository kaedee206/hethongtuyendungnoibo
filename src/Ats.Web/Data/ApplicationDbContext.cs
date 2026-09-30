using Ats.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ats.Web.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<User> Users { get; set; }
    public DbSet<AuthAuditLog> AuthAuditLogs { get; set; }
    public DbSet<UserSession> UserSessions { get; set; }
    public DbSet<Role> Roles { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            // Tối ưu hóa Database (SCRUM-131)
            
            // Email có constraint unique
            entity.HasIndex(e => e.Email).IsUnique();

            // Index trên các trường tìm kiếm
            entity.HasIndex(e => e.FullName);
            entity.HasIndex(e => e.Department);

            // Foreign key đến bảng roles
            entity.HasOne(e => e.RoleEntity)
                  .WithMany()
                  .HasForeignKey(e => e.RoleId)
                  .OnDelete(DeleteBehavior.SetNull);
        });
    }
}