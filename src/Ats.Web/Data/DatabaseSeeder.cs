using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ats.Web.Data;

public static class DatabaseSeeder
{
    public static async Task SeedAsync(ApplicationDbContext context)
    {
        // 1. Seed Roles
        if (!await context.Roles.AnyAsync())
        {
            var roles = new List<Role>
            {
                new() { Id = Guid.NewGuid(), Code = RoleCode.ADMIN, Name = "Quản trị viên hệ thống", Description = "Toàn quyền trên hệ thống", IsSystem = true },
                new() { Id = Guid.NewGuid(), Code = RoleCode.HR_MANAGER, Name = "Trưởng phòng nhân sự", Description = "Quản lý tuyển dụng, duyệt yêu cầu và offer", IsSystem = true },
                new() { Id = Guid.NewGuid(), Code = RoleCode.RECRUITER, Name = "Chuyên viên tuyển dụng", Description = "Đăng tin, lọc hồ sơ, xếp lịch phỏng vấn", IsSystem = true },
                new() { Id = Guid.NewGuid(), Code = RoleCode.HIRING_MANAGER, Name = "Quản lý chuyên môn", Description = "Tạo yêu cầu tuyển dụng, tham gia phỏng vấn", IsSystem = true },
                new() { Id = Guid.NewGuid(), Code = RoleCode.INTERVIEWER, Name = "Người phỏng vấn", Description = "Tham gia phỏng vấn và đánh giá ứng viên", IsSystem = true },
                new() { Id = Guid.NewGuid(), Code = RoleCode.APPROVER, Name = "Người phê duyệt", Description = "Phê duyệt yêu cầu tuyển dụng và offer", IsSystem = true },
                new() { Id = Guid.NewGuid(), Code = RoleCode.CANDIDATE, Name = "Ứng viên nội bộ", Description = "Xem và ứng tuyển các vị trí nội bộ", IsSystem = true }
            };
            await context.Roles.AddRangeAsync(roles);
            await context.SaveChangesAsync();
        }

        // 2. Seed Departments
        if (!await context.Departments.AnyAsync())
        {
            var bod = new Department { Id = Guid.NewGuid(), Code = "BOD", Name = "Ban Giám Đốc", IsActive = true };
            var it = new Department { Id = Guid.NewGuid(), Code = "IT", Name = "Phòng Công Nghệ Thông Tin", IsActive = true, ParentId = bod.Id };
            var hr = new Department { Id = Guid.NewGuid(), Code = "HR", Name = "Phòng Nhân Sự", IsActive = true, ParentId = bod.Id };
            var sales = new Department { Id = Guid.NewGuid(), Code = "SALES", Name = "Phòng Kinh Doanh", IsActive = true, ParentId = bod.Id };

            await context.Departments.AddRangeAsync(bod, it, hr, sales);
            await context.SaveChangesAsync();
        }

        // 3. Seed JobPositions
        if (!await context.JobPositions.AnyAsync())
        {
            var itDept = await context.Departments.FirstOrDefaultAsync(d => d.Code == "IT");
            var hrDept = await context.Departments.FirstOrDefaultAsync(d => d.Code == "HR");

            if (itDept != null && hrDept != null)
            {
                var positions = new List<JobPosition>
                {
                    new() { Id = Guid.NewGuid(), Code = "SWE-SENIOR", Title = "Senior Software Engineer", DepartmentId = itDept.Id, JobLevel = "SENIOR", IsActive = true },
                    new() { Id = Guid.NewGuid(), Code = "QA-MID", Title = "Middle QA Engineer", DepartmentId = itDept.Id, JobLevel = "MIDDLE", IsActive = true },
                    new() { Id = Guid.NewGuid(), Code = "REC-SENIOR", Title = "Senior Recruiter", DepartmentId = hrDept.Id, JobLevel = "SENIOR", IsActive = true },
                    new() { Id = Guid.NewGuid(), Code = "HR-MANAGER", Title = "HR Manager", DepartmentId = hrDept.Id, JobLevel = "LEAD", IsActive = true }
                };
                await context.JobPositions.AddRangeAsync(positions);
                await context.SaveChangesAsync();
            }
        }

        // 4. Seed PipelineStages
        if (!await context.PipelineStages.AnyAsync())
        {
            var stages = new List<PipelineStage>
            {
                new() { Id = Guid.NewGuid(), Name = "Ứng tuyển mới", StageOrder = 1, ColorCode = "#0d6efd" },
                new() { Id = Guid.NewGuid(), Name = "Sàng lọc CV", StageOrder = 2, ColorCode = "#6c757d" },
                new() { Id = Guid.NewGuid(), Name = "Phỏng vấn sơ loại", StageOrder = 3, ColorCode = "#0dcaf0" },
                new() { Id = Guid.NewGuid(), Name = "Phỏng vấn chuyên môn", StageOrder = 4, ColorCode = "#ffc107" },
                new() { Id = Guid.NewGuid(), Name = "Đề nghị tuyển dụng (Offer)", StageOrder = 5, ColorCode = "#fd7e14" },
                new() { Id = Guid.NewGuid(), Name = "Tuyển dụng thành công", StageOrder = 6, ColorCode = "#198754" },
                new() { Id = Guid.NewGuid(), Name = "Từ chối", StageOrder = 7, ColorCode = "#dc3545" }
            };
            await context.PipelineStages.AddRangeAsync(stages);
            await context.SaveChangesAsync();
        }

        // 5. Seed Permissions (Epics 01 -> 09)
        if (!await context.Permissions.AnyAsync())
        {
            var permissions = new List<Permission>
            {
                new() { Id = Guid.NewGuid(), Code = "user.view", Module = "EP-01_UserRole", Description = "Xem danh sách người dùng" },
                new() { Id = Guid.NewGuid(), Code = "user.manage", Module = "EP-01_UserRole", Description = "Quản lý tài khoản và phân quyền" },
                new() { Id = Guid.NewGuid(), Code = "org.manage", Module = "EP-02_Organization", Description = "Quản lý cơ cấu phòng ban và vị trí" },
                new() { Id = Guid.NewGuid(), Code = "requisition.create", Module = "EP-03_Requisition", Description = "Tạo yêu cầu tuyển dụng" },
                new() { Id = Guid.NewGuid(), Code = "requisition.approve", Module = "EP-03_Requisition", Description = "Phê duyệt yêu cầu tuyển dụng" },
                new() { Id = Guid.NewGuid(), Code = "job.post", Module = "EP-04_JobPosting", Description = "Đăng tin tuyển dụng" },
                new() { Id = Guid.NewGuid(), Code = "candidate.manage", Module = "EP-05_CandidatePipeline", Description = "Quản lý hồ sơ và pipeline ứng viên" },
                new() { Id = Guid.NewGuid(), Code = "interview.manage", Module = "EP-06_InterviewEvaluation", Description = "Xếp lịch và đánh giá phỏng vấn" },
                new() { Id = Guid.NewGuid(), Code = "offer.manage", Module = "EP-07_OfferOnboarding", Description = "Tạo và duyệt thư mời nhận việc" },
                new() { Id = Guid.NewGuid(), Code = "analytics.view", Module = "EP-09_AnalyticsDashboard", Description = "Xem báo cáo thống kê tuyển dụng" }
            };
            await context.Permissions.AddRangeAsync(permissions);
            await context.SaveChangesAsync();

            // Gán tất cả quyền cho role ADMIN
            var adminRole = await context.Roles.FirstOrDefaultAsync(r => r.Code == RoleCode.ADMIN);
            if (adminRole != null)
            {
                var rolePermissions = permissions.Select(p => new RolePermission
                {
                    RoleId = adminRole.Id,
                    PermissionId = p.Id
                });
                await context.RolePermissions.AddRangeAsync(rolePermissions);
                await context.SaveChangesAsync();
            }
        }

        // 6. Xóa bỏ dữ liệu người dùng mẫu cũ để làm mới dữ liệu
        var deptsWithManager = await context.Departments.Where(d => d.ManagerId != null).ToListAsync();
        foreach (var d in deptsWithManager)
        {
            d.ManagerId = null;
        }
        await context.SaveChangesAsync();

        var oldUserRoles = await context.UserRoles.ToListAsync();
        if (oldUserRoles.Count != 0)
        {
            context.UserRoles.RemoveRange(oldUserRoles);
            await context.SaveChangesAsync();
        }

        var oldUsers = await context.Users.ToListAsync();
        if (oldUsers.Count != 0)
        {
            context.Users.RemoveRange(oldUsers);
            await context.SaveChangesAsync();
        }

        // 7. Seed danh sách tài khoản người dùng với tên thật chuẩn người Việt
        var rolesMap = await context.Roles.ToDictionaryAsync(r => r.Code, r => r.Id);
        var deptsMap = await context.Departments.ToDictionaryAsync(d => d.Code, d => d.Id);
        var posMap = await context.JobPositions.ToDictionaryAsync(p => p.Code, p => p.Id);

        var seedUsers = new List<(string Email, string FullName, string Password, RoleCode Role, string? DeptCode, string? PosCode)>
        {
            // Tài khoản theo phòng ban (phong_ban@noveratech.vn)
            ("admin@noveratech.vn", "Đặng Hoàng Nam", "123456@@", RoleCode.ADMIN, "IT", "SWE-SENIOR"),
            ("bod@noveratech.vn", "Trần Đức Minh", "123456@@", RoleCode.APPROVER, "BOD", null),
            ("hr@noveratech.vn", "Nguyễn Mai Phương", "123456@@", RoleCode.HR_MANAGER, "HR", "HR-MANAGER"),
            ("it@noveratech.vn", "Vũ Thành Long", "123456@@", RoleCode.HIRING_MANAGER, "IT", "SWE-SENIOR"),
            ("sales@noveratech.vn", "Hoàng Gia Bảo", "123456@@", RoleCode.HIRING_MANAGER, "SALES", null),

            // Nhân viên tuyển dụng (Email cá nhân)
            ("recruiter.thuydung@gmail.com", "Lê Thùy Dung", "123456@@", RoleCode.RECRUITER, "HR", "REC-SENIOR"),
            ("recruiter.quocanh@gmail.com", "Phạm Quốc Anh", "123456@@", RoleCode.RECRUITER, "HR", "REC-SENIOR"),

            // Người phỏng vấn & Ứng viên nội bộ
            ("interviewer.quanghuy@gmail.com", "Ngô Quang Huy", "123456@@", RoleCode.INTERVIEWER, "IT", "SWE-SENIOR"),
            ("candidate.minhtuan@gmail.com", "Bùi Minh Tuấn", "123456@@", RoleCode.CANDIDATE, "IT", "QA-MID")
        };

        foreach (var item in seedUsers)
        {
            var user = new User
            {
                Id = Guid.NewGuid(),
                Email = item.Email,
                FullName = item.FullName,
                PasswordHash = item.Password,
                Status = "ACTIVE",
                UserType = UserType.INTERNAL,
                DepartmentId = item.DeptCode != null && deptsMap.TryGetValue(item.DeptCode, out var dId) ? dId : null,
                JobPositionId = item.PosCode != null && posMap.TryGetValue(item.PosCode, out var pId) ? pId : null,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            await context.Users.AddAsync(user);
            await context.SaveChangesAsync();

            if (rolesMap.TryGetValue(item.Role, out var roleId))
            {
                await context.UserRoles.AddAsync(new UserRole
                {
                    UserId = user.Id,
                    RoleId = roleId
                });
                await context.SaveChangesAsync();
            }

            // Gán Manager cho phòng ban tương ứng
            if (item.Role == RoleCode.APPROVER && item.DeptCode == "BOD")
            {
                var bodDept = await context.Departments.FirstOrDefaultAsync(d => d.Code == "BOD");
                if (bodDept != null) { bodDept.ManagerId = user.Id; }
            }
            else if (item.Role == RoleCode.HR_MANAGER && item.DeptCode == "HR")
            {
                var hrDept = await context.Departments.FirstOrDefaultAsync(d => d.Code == "HR");
                if (hrDept != null) { hrDept.ManagerId = user.Id; }
            }
            else if (item.Role == RoleCode.HIRING_MANAGER && item.DeptCode == "IT")
            {
                var itDept = await context.Departments.FirstOrDefaultAsync(d => d.Code == "IT");
                if (itDept != null) { itDept.ManagerId = user.Id; }
            }
            else if (item.Role == RoleCode.HIRING_MANAGER && item.DeptCode == "SALES")
            {
                var salesDept = await context.Departments.FirstOrDefaultAsync(d => d.Code == "SALES");
                if (salesDept != null) { salesDept.ManagerId = user.Id; }
            }
        }

        await context.SaveChangesAsync();
    }
}
