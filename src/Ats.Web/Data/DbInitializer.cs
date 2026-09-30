using Ats.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;
using BCrypt.Net;

namespace Ats.Web.Data;

public static class DbInitializer
{
    public static async Task SeedAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Tự động chạy Migration nếu cần (chỉ dành cho Development hoặc môi trường kiểm thử)
        // await context.Database.MigrateAsync();

        if (await context.Users.AnyAsync())
        {
            return; // DB đã có dữ liệu, không cần tạo Admin
        }

        var adminEmail = Environment.GetEnvironmentVariable("DEFAULT_ADMIN_EMAIL") ?? "admin@yourcompany.com";
        var adminPassword = Environment.GetEnvironmentVariable("DEFAULT_ADMIN_PASSWORD") ?? "Admin@123456";
        var adminFullName = Environment.GetEnvironmentVariable("DEFAULT_ADMIN_FULLNAME") ?? "Super Admin";

        var adminUser = new User
        {
            Id = Guid.NewGuid(),
            Email = adminEmail,
            FullName = adminFullName,
            Role = "Admin",
            Status = "ACTIVE",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(adminPassword),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        context.Users.Add(adminUser);
        
        // Ghi log audit việc tạo tài khoản ban đầu
        context.AuthAuditLogs.Add(new AuthAuditLog
        {
            UserId = adminUser.Id,
            Email = adminUser.Email,
            IsSuccess = true,
            EventType = "AccountCreated",
            Reason = "Hệ thống tự động tạo tài khoản Admin mặc định",
            IpAddress = "127.0.0.1",
            UserAgent = "SystemSeeder",
            Timestamp = DateTimeOffset.UtcNow,
            NewValues = System.Text.Json.JsonSerializer.Serialize(new { adminUser.Email, adminUser.FullName, adminUser.Role, adminUser.Status })
        });

        await context.SaveChangesAsync();
    }
}
