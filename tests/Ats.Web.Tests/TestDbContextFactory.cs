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
        var adminRole = new Role { Id = Guid.NewGuid(), Name = "Admin", Code = Ats.Web.Models.Enums.RoleCode.ADMIN, Description = "Quản trị viên", IsSystem = true };
        var hrRole = new Role { Id = Guid.NewGuid(), Name = "HRManager", Code = Ats.Web.Models.Enums.RoleCode.HR_MANAGER, Description = "Quản lý nhân sự", IsSystem = true };
        var recruiterRole = new Role { Id = Guid.NewGuid(), Name = "Recruiter", Code = Ats.Web.Models.Enums.RoleCode.RECRUITER, Description = "Chuyên viên tuyển dụng", IsSystem = true };
        var hmRole = new Role { Id = Guid.NewGuid(), Name = "HiringManager", Code = Ats.Web.Models.Enums.RoleCode.HIRING_MANAGER, Description = "Quản lý tuyển dụng", IsSystem = true };
        var interviewerRole = new Role { Id = Guid.NewGuid(), Name = "Interviewer", Code = Ats.Web.Models.Enums.RoleCode.INTERVIEWER, Description = "Người phỏng vấn", IsSystem = true };
        var approverRole = new Role { Id = Guid.NewGuid(), Name = "Approver", Code = Ats.Web.Models.Enums.RoleCode.APPROVER, Description = "Người phê duyệt", IsSystem = true };

        context.Roles.AddRange(adminRole, hrRole, recruiterRole, hmRole, interviewerRole, approverRole);
        context.SaveChanges();

        return context;
    }
}
