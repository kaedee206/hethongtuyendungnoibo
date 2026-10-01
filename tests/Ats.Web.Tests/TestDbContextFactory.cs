using Ats.Web.Data;
using Ats.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ats.Web.Tests;

public static class TestDbContextFactory
{
    public static ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var context = new ApplicationDbContext(options);

        // Seed Roles
        var adminRole = new Role { Id = Guid.NewGuid(), Name = "Admin", Code = 1, Description = "Quản trị viên", IsSystem = true };
        var hrRole = new Role { Id = Guid.NewGuid(), Name = "HRManager", Code = 2, Description = "Quản lý nhân sự", IsSystem = true };
        var recruiterRole = new Role { Id = Guid.NewGuid(), Name = "Recruiter", Code = 3, Description = "Chuyên viên tuyển dụng", IsSystem = true };
        var hmRole = new Role { Id = Guid.NewGuid(), Name = "HiringManager", Code = 4, Description = "Quản lý tuyển dụng", IsSystem = true };
        var interviewerRole = new Role { Id = Guid.NewGuid(), Name = "Interviewer", Code = 5, Description = "Người phỏng vấn", IsSystem = true };
        var approverRole = new Role { Id = Guid.NewGuid(), Name = "Approver", Code = 6, Description = "Người phê duyệt", IsSystem = true };

        context.Roles.AddRange(adminRole, hrRole, recruiterRole, hmRole, interviewerRole, approverRole);
        context.SaveChanges();

        return context;
    }
}
