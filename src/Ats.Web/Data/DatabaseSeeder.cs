using Ats.Web.Constants;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Ats.Web.Data;

public static class DatabaseSeeder
{
    public static async Task SeedAsync(ApplicationDbContext context)
    {
        // 1. Seed & Đồng bộ 7 Vai trò chuẩn doanh nghiệp (Rule 11)
        var standardRoles = new List<(RoleCode Code, string Name, string Description)>
        {
            (RoleCode.CANDIDATE, UserRoles.NameCandidate, "Người nộp hồ sơ từ bên ngoài, không có tài khoản nội bộ"),
            (RoleCode.RECRUITER, UserRoles.NameRecruiter, "Người vận hành tuyển dụng hằng ngày"),
            (RoleCode.HIRING_MANAGER, UserRoles.NameHiringManager, "Người cần người, sở hữu vị trí tuyển dụng"),
            (RoleCode.INTERVIEWER, UserRoles.NameInterviewer, "Nhân sự được mời tham gia một vòng phỏng vấn"),
            (RoleCode.HR_MANAGER, UserRoles.NameHRManager, "Chủ sở hữu toàn bộ hoạt động tuyển dụng"),
            (RoleCode.APPROVER, UserRoles.NameApprover, "Ban giám đốc hoặc cấp duyệt theo hạn mức"),
            (RoleCode.ADMIN, UserRoles.NameAdmin, "Người vận hành ứng dụng")
        };

        foreach (var sr in standardRoles)
        {
            var existingRole = await context.Roles.FirstOrDefaultAsync(r => r.Code == sr.Code);
            if (existingRole == null)
            {
                await context.Roles.AddAsync(new Role
                {
                    Id = Guid.NewGuid(),
                    Code = sr.Code,
                    Name = sr.Name,
                    Description = sr.Description,
                    IsSystem = true
                });
            }
            else
            {
                existingRole.Name = sr.Name;
                existingRole.Description = sr.Description;
                existingRole.IsSystem = true;
            }
        }
        await context.SaveChangesAsync();

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

        var itDept = await context.Departments.FirstOrDefaultAsync(d => d.Code == "IT") ?? await context.Departments.FirstAsync();
        var hrDept = await context.Departments.FirstOrDefaultAsync(d => d.Code == "HR") ?? await context.Departments.FirstAsync();
        var salesDept = await context.Departments.FirstOrDefaultAsync(d => d.Code == "SALES") ?? await context.Departments.FirstAsync();

        // 3. Seed JobPositions (Standard base positions) with real salary benchmarks
        var initialPositions = new List<(string Code, string Title, Guid DeptId, string Level, decimal MinSal, decimal MaxSal)>
        {
            ("SWE-SENIOR", "Senior Software Engineer (.NET & React)", itDept.Id, "SENIOR", 35000000m, 55000000m),
            ("QA-MID", "Middle QA Engineer", itDept.Id, "MIDDLE", 18000000m, 28000000m),
            ("REC-SENIOR", "Senior Recruiter", hrDept.Id, "SENIOR", 20000000m, 35000000m),
            ("HR-MANAGER", "HR Manager", hrDept.Id, "LEAD", 40000000m, 65000000m),
            ("AI-ENG", "AI / LLM Application Engineer", itDept.Id, "SENIOR", 38000000m, 60000000m),
            ("PO-FINTECH", "Product Owner (Fintech & AI Platforms)", itDept.Id, "SENIOR", 35000000m, 55000000m),
            ("DEVOPS-SR", "Senior DevOps & Cloud Platform Engineer", itDept.Id, "SENIOR", 35000000m, 52000000m),
            ("LEAD-ARCH", "Lead Solution Architect", itDept.Id, "LEAD", 55000000m, 85000000m),
            ("QA-LEAD", "QA / Test Automation Lead", itDept.Id, "LEAD", 40000000m, 60000000m),
            ("MOB-SR", "Senior Mobile Engineer (Flutter & iOS)", itDept.Id, "SENIOR", 32000000m, 48000000m),
            ("DATA-ENG", "Senior Data Engineer & Lakehouse Architect", itDept.Id, "SENIOR", 35000000m, 55000000m),
            ("CYBER-SEC", "Cybersecurity & DevSecOps Specialist", itDept.Id, "SENIOR", 35000000m, 55000000m),
            ("UIUX-SR", "Senior UI/UX Product Designer", itDept.Id, "SENIOR", 28000000m, 45000000m),
            ("SALES-LEAD", "Technical Customer Success Lead", salesDept.Id, "LEAD", 35000000m, 60000000m)
        };

        foreach (var p in initialPositions)
        {
            var existingPos = await context.JobPositions.FirstOrDefaultAsync(jp => jp.Code == p.Code);
            if (existingPos == null)
            {
                await context.JobPositions.AddAsync(new JobPosition
                {
                    Id = Guid.NewGuid(),
                    Code = p.Code,
                    Title = p.Title,
                    DepartmentId = p.DeptId,
                    JobLevel = p.Level,
                    MinSalary = p.MinSal,
                    MaxSalary = p.MaxSal,
                    IsActive = true,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                });
            }
            else if (existingPos.MinSalary == null || existingPos.MaxSalary == null)
            {
                existingPos.MinSalary = p.MinSal;
                existingPos.MaxSalary = p.MaxSal;
            }
        }
        await context.SaveChangesAsync();

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
                new() { Id = Guid.NewGuid(), Name = "Từ chối", StageOrder = 7, ColorCode = "#dc3545" },
                new() { Id = Guid.NewGuid(), Name = "Đã onboard", StageOrder = 8, ColorCode = "#059669" }
            };
            await context.PipelineStages.AddRangeAsync(stages);
            await context.SaveChangesAsync();
        }
        else
        {
            var onboardedStage = await context.PipelineStages.FirstOrDefaultAsync(ps => ps.Name == "Đã onboard");
            if (onboardedStage == null)
            {
                await context.PipelineStages.AddAsync(new PipelineStage
                {
                    Id = Guid.NewGuid(),
                    Name = "Đã onboard",
                    StageOrder = 8,
                    ColorCode = "#059669"
                });
                await context.SaveChangesAsync();
            }
        }

        // 5. Seed Permissions
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

        // 6. Seed Users (Bao gồm cả email @noveratech.digital và @noveratech.vn / alias cá nhân)
        var rolesMap = await context.Roles.ToDictionaryAsync(r => r.Code, r => r.Id);
        var deptsMap = await context.Departments.ToDictionaryAsync(d => d.Code, d => d.Id);
        var posMap = await context.JobPositions.ToDictionaryAsync(p => p.Code, p => p.Id);

        var seedUsers = new List<(string Email, string FullName, string Password, RoleCode Role, string? DeptCode, string? PosCode)>
        {
            // [1] Quản trị viên hệ thống (Admin) / CEO - Lường Minh Hiếu
            ("admin@noveratech.vn", "Lường Minh Hiếu", "123456@@", RoleCode.ADMIN, "BOD", null),
            ("nam.dang@noveratech.digital", "Lường Minh Hiếu", "123456@@", RoleCode.ADMIN, "BOD", null),
            ("hieu.luong@noveratech.digital", "Lường Minh Hiếu", "123456@@", RoleCode.ADMIN, "BOD", null),
            ("luong.hieu@noveratech.digital", "Lường Minh Hiếu", "123456@@", RoleCode.ADMIN, "BOD", null),
            ("security@noveratech.digital", "Lường Minh Hiếu", "123456@@", RoleCode.ADMIN, "BOD", null),
            ("duc.anh@noveratech.digital", "Nguyễn Đức Anh", "123456@@", RoleCode.ADMIN, "IT", null),
            ("duc.hieu@noveratech.digital", "Nguyễn Đức Hiếu", "123456@@", RoleCode.RECRUITER, "HR", null),

            // [2] Người phê duyệt cấp cao (Approver / Ban Giám Đốc) - Trần Đức Minh
            ("bod@noveratech.vn", "Trần Đức Minh", "123456@@", RoleCode.APPROVER, "BOD", null),
            ("minh.tran@noveratech.digital", "Trần Đức Minh", "123456@@", RoleCode.APPROVER, "BOD", null),

            // [3] Trưởng phòng Nhân sự (HR Manager) - Nguyễn Mai Phương
            ("hr@noveratech.vn", "Nguyễn Mai Phương", "123456@@", RoleCode.HR_MANAGER, "HR", "HR-MANAGER"),
            ("phuong.nguyen@noveratech.digital", "Nguyễn Mai Phương", "123456@@", RoleCode.HR_MANAGER, "HR", "HR-MANAGER"),

            // [4] Quản lý chuyên môn IT (Hiring Manager - IT) - Vũ Thành Long
            ("it@noveratech.vn", "Vũ Thành Long", "123456@@", RoleCode.HIRING_MANAGER, "IT", "SWE-SENIOR"),
            ("long.vu@noveratech.digital", "Vũ Thành Long", "123456@@", RoleCode.HIRING_MANAGER, "IT", "SWE-SENIOR"),

            // [5] Quản lý chuyên môn Kinh Doanh (Hiring Manager - Sales) - Hoàng Gia Bảo
            ("sales@noveratech.vn", "Hoàng Gia Bảo", "123456@@", RoleCode.HIRING_MANAGER, "SALES", null),
            ("bao.hoang@noveratech.digital", "Hoàng Gia Bảo", "123456@@", RoleCode.HIRING_MANAGER, "SALES", null),

            // [6] Chuyên viên tuyển dụng 1 (Recruiter 1) - Lê Thùy Dung
            ("recruiter.thuydung@gmail.com", "Lê Thùy Dung", "123456@@", RoleCode.RECRUITER, "HR", "REC-SENIOR"),
            ("dung.le@noveratech.digital", "Lê Thùy Dung", "123456@@", RoleCode.RECRUITER, "HR", "REC-SENIOR"),

            // [7] Chuyên viên tuyển dụng 2 (Recruiter 2) - Phạm Quốc Anh
            ("recruiter.quocanh@gmail.com", "Phạm Quốc Anh", "123456@@", RoleCode.RECRUITER, "HR", "REC-SENIOR"),
            ("anh.pham@noveratech.digital", "Phạm Quốc Anh", "123456@@", RoleCode.RECRUITER, "HR", "REC-SENIOR"),

            // [8] Người phỏng vấn kỹ thuật (Interviewer) - Ngô Quang Huy
            ("interviewer.quanghuy@gmail.com", "Ngô Quang Huy", "123456@@", RoleCode.INTERVIEWER, "IT", "SWE-SENIOR"),
            ("huy.ngo@noveratech.digital", "Ngô Quang Huy", "123456@@", RoleCode.INTERVIEWER, "IT", "SWE-SENIOR"),

            // [9] Ứng viên nội bộ (Candidate) - Bùi Minh Tuấn
            ("candidate.minhtuan@gmail.com", "Bùi Minh Tuấn", "123456@@", RoleCode.CANDIDATE, "IT", "QA-MID"),
            ("tuan.bui@noveratech.digital", "Bùi Minh Tuấn", "123456@@", RoleCode.CANDIDATE, "IT", "QA-MID")
        };

        foreach (var item in seedUsers)
        {
            var user = await context.Users.FirstOrDefaultAsync(u => u.Email == item.Email);
            var canonicalRole = UserRoles.NormalizeRole(item.Role.ToString());
            Guid? assignedRoleId = rolesMap.TryGetValue(item.Role, out var rId) ? rId : null;

            if (user == null)
            {
                user = new User
                {
                    Id = Guid.NewGuid(),
                    Email = item.Email,
                    FullName = item.FullName,
                    PasswordHash = item.Password,
                    Status = "ACTIVE",
                    UserType = item.Role == RoleCode.CANDIDATE ? UserType.EXTERNAL_CANDIDATE : UserType.INTERNAL,
                    Role = canonicalRole,
                    RoleId = assignedRoleId,
                    DepartmentId = item.DeptCode != null && deptsMap.TryGetValue(item.DeptCode, out var dId) ? dId : null,
                    JobPositionId = item.PosCode != null && posMap.TryGetValue(item.PosCode, out var pId) ? pId : null,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };

                await context.Users.AddAsync(user);
                await context.SaveChangesAsync();

                if (assignedRoleId.HasValue)
                {
                    await context.UserRoles.AddAsync(new UserRole
                    {
                        UserId = user.Id,
                        RoleId = assignedRoleId.Value
                    });
                    await context.SaveChangesAsync();
                }

                // Cập nhật Manager của phòng ban
                if (item.Role == RoleCode.APPROVER && item.DeptCode == "BOD")
                {
                    var bodDept = await context.Departments.FirstOrDefaultAsync(d => d.Code == "BOD");
                    if (bodDept != null) bodDept.ManagerId = user.Id;
                }
                else if (item.Role == RoleCode.HR_MANAGER && item.DeptCode == "HR")
                {
                    var hDept = await context.Departments.FirstOrDefaultAsync(d => d.Code == "HR");
                    if (hDept != null) hDept.ManagerId = user.Id;
                }
                else if (item.Role == RoleCode.HIRING_MANAGER && item.DeptCode == "IT")
                {
                    var iDept = await context.Departments.FirstOrDefaultAsync(d => d.Code == "IT");
                    if (iDept != null) iDept.ManagerId = user.Id;
                }
                else if (item.Role == RoleCode.HIRING_MANAGER && item.DeptCode == "SALES")
                {
                    var sDept = await context.Departments.FirstOrDefaultAsync(d => d.Code == "SALES");
                    if (sDept != null) sDept.ManagerId = user.Id;
                }
            }
            else
            {
                if (user.FullName != item.FullName)
                {
                    user.FullName = item.FullName;
                    user.UpdatedAt = DateTimeOffset.UtcNow;
                }
                user.Role = canonicalRole;
                if (assignedRoleId.HasValue)
                {
                    user.RoleId = assignedRoleId.Value;
                }
                user.LockedUntil = null;
                user.FailedLoginAttempts = 0;

                // Đồng bộ quan hệ UserRole
                if (assignedRoleId.HasValue)
                {
                    var hasUr = await context.UserRoles.AnyAsync(ur => ur.UserId == user.Id && ur.RoleId == assignedRoleId.Value);
                    if (!hasUr)
                    {
                        await context.UserRoles.AddAsync(new UserRole
                        {
                            UserId = user.Id,
                            RoleId = assignedRoleId.Value
                        });
                    }
                }
            }
        }
        await context.SaveChangesAsync();

        // Đồng bộ đồng loạt Role và RoleId cho tất cả người dùng trong hệ thống
        var allDbUsers = await context.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .ToListAsync();

        foreach (var u in allDbUsers)
        {
            if (u.UserRoles.Any())
            {
                var primaryUr = u.UserRoles
                    .OrderBy(ur => ur.Role != null ? UserRoles.GetRolePriority(ur.Role.Name) : 99)
                    .First();
                if (primaryUr.Role != null)
                {
                    u.Role = UserRoles.NormalizeRole(primaryUr.Role.Name);
                    u.RoleId = primaryUr.Role.Id;
                }
            }
        }
        await context.SaveChangesAsync();

        // Đồng bộ vai trò chính xác cho các tài khoản đặc thù (SSO & Nội bộ)
        var luongHieuUser = await context.Users.FirstOrDefaultAsync(u => u.Email == "luong.hieu@noveratech.digital");
        if (luongHieuUser != null)
        {
            var targetRoles = new[] { RoleCode.ADMIN, RoleCode.HR_MANAGER, RoleCode.RECRUITER, RoleCode.HIRING_MANAGER, RoleCode.INTERVIEWER, RoleCode.APPROVER };
            var existingRoleIds = await context.UserRoles.Where(ur => ur.UserId == luongHieuUser.Id).Select(ur => ur.RoleId).ToListAsync();
            
            // Đảm bảo không gắn quyền Ứng viên cho luong.hieu
            if (rolesMap.TryGetValue(RoleCode.CANDIDATE, out var candidateRoleId))
            {
                var candidateUr = await context.UserRoles.FirstOrDefaultAsync(ur => ur.UserId == luongHieuUser.Id && ur.RoleId == candidateRoleId);
                if (candidateUr != null)
                {
                    context.UserRoles.Remove(candidateUr);
                }
            }

            foreach (var rCode in targetRoles)
            {
                if (rolesMap.TryGetValue(rCode, out var rId) && !existingRoleIds.Contains(rId))
                {
                    context.UserRoles.Add(new UserRole { UserId = luongHieuUser.Id, RoleId = rId });
                }
            }
        }

        var ducAnhUser = await context.Users.FirstOrDefaultAsync(u => u.Email == "duc.anh@noveratech.digital");
        if (ducAnhUser != null)
        {
            var targetRoles = new[] { RoleCode.ADMIN, RoleCode.RECRUITER };
            var existingRoleIds = await context.UserRoles.Where(ur => ur.UserId == ducAnhUser.Id).Select(ur => ur.RoleId).ToListAsync();
            foreach (var rCode in targetRoles)
            {
                if (rolesMap.TryGetValue(rCode, out var rId) && !existingRoleIds.Contains(rId))
                {
                    context.UserRoles.Add(new UserRole { UserId = ducAnhUser.Id, RoleId = rId });
                }
            }
        }

        var ducHieuUser = await context.Users.FirstOrDefaultAsync(u => u.Email == "duc.hieu@noveratech.digital");
        if (ducHieuUser != null)
        {
            var targetRoles = new[] { RoleCode.RECRUITER, RoleCode.INTERVIEWER };
            var existingRoleIds = await context.UserRoles.Where(ur => ur.UserId == ducHieuUser.Id).Select(ur => ur.RoleId).ToListAsync();
            foreach (var rCode in targetRoles)
            {
                if (rolesMap.TryGetValue(rCode, out var rId) && !existingRoleIds.Contains(rId))
                {
                    context.UserRoles.Add(new UserRole { UserId = ducHieuUser.Id, RoleId = rId });
                }
            }
        }

        var securityUser = await context.Users.FirstOrDefaultAsync(u => u.Email == "security@noveratech.digital");
        if (securityUser != null)
        {
            if (rolesMap.TryGetValue(RoleCode.ADMIN, out var adminRoleId))
            {
                var existingRoleIds = await context.UserRoles.Where(ur => ur.UserId == securityUser.Id).Select(ur => ur.RoleId).ToListAsync();
                if (!existingRoleIds.Contains(adminRoleId))
                {
                    context.UserRoles.Add(new UserRole { UserId = securityUser.Id, RoleId = adminRoleId });
                }
            }
        }
        await context.SaveChangesAsync();

        // 7. Seed 12 JobRequisitions & JobPostings (đầy đủ dữ liệu tương tác thực tế)
        var standardJobs = JobService.GetStandardNoveraTechJobs();
        var itManager = await context.Users.FirstOrDefaultAsync(u => u.Email == "long.vu@noveratech.digital" || u.Email == "it@noveratech.vn")
            ?? await context.Users.FirstAsync();
        var hrRecruiter = await context.Users.FirstOrDefaultAsync(u => u.Email == "dung.le@noveratech.digital" || u.Email == "recruiter.thuydung@gmail.com")
            ?? await context.Users.FirstAsync();

        int jobIndex = 1;
        foreach (var stdJob in standardJobs)
        {
            var reqCode = $"REQ-2026-{jobIndex:D3}";
            var requisition = await context.JobRequisitions.FirstOrDefaultAsync(r => r.Code == reqCode);
            if (requisition == null)
            {
                var targetDept = itDept;
                if (stdJob.DepartmentCategory == "hr") targetDept = hrDept;
                else if (stdJob.DepartmentCategory == "other") targetDept = salesDept;

                var position = await context.JobPositions.FirstOrDefaultAsync(p => p.Title.Contains(stdJob.Title))
                    ?? await context.JobPositions.FirstOrDefaultAsync(p => p.DepartmentId == targetDept.Id)
                    ?? await context.JobPositions.FirstAsync();

                requisition = new JobRequisition
                {
                    Id = Guid.NewGuid(),
                    Code = reqCode,
                    JobPositionId = position.Id,
                    DepartmentId = targetDept.Id,
                    HiringManagerId = itManager.Id,
                    AssignedRecruiterId = hrRecruiter.Id,
                    Quantity = 2,
                    HeadcountType = HeadcountType.NEW_HEADCOUNT,
                    Reason = "Mở rộng năng lực triển khai sản phẩm công nghệ trọng điểm của NoveraTech.",
                    MinSalary = 30000000,
                    MaxSalary = 60000000,
                    Currency = "VND",
                    TargetHireDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
                    Status = RequisitionStatus.APPROVED,
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-15),
                    UpdatedAt = DateTimeOffset.UtcNow.AddDays(-15)
                };
                await context.JobRequisitions.AddAsync(requisition);
                await context.SaveChangesAsync();
            }

            var posting = await context.JobPostings.FirstOrDefaultAsync(p => p.Slug == stdJob.Slug);
            if (posting == null)
            {
                posting = new JobPosting
                {
                    Id = Guid.NewGuid(),
                    RequisitionId = requisition.Id,
                    Code = !string.IsNullOrWhiteSpace(stdJob.Code) ? stdJob.Code : $"NT-2610-JOB{jobIndex:D2}",
                    Title = stdJob.Title,
                    Slug = stdJob.Slug,
                    WorkLocation = stdJob.WorkLocation,
                    EmploymentType = EmploymentType.FULL_TIME,
                    SalaryDisplay = stdJob.SalaryDisplay,
                    JobDescription = stdJob.Overview,
                    Requirements = string.Join("\n", stdJob.Requirements),
                    Benefits = string.Join("\n", stdJob.Benefits),
                    PublishedAt = stdJob.PostedDate,
                    ExpiredAt = stdJob.Deadline,
                    Status = JobPostingStatus.PUBLISHED,
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-10),
                    UpdatedAt = DateTimeOffset.UtcNow.AddDays(-10)
                };
                await context.JobPostings.AddAsync(posting);
                await context.SaveChangesAsync();
            }
            else if (string.IsNullOrWhiteSpace(posting.Code))
            {
                posting.Code = !string.IsNullOrWhiteSpace(stdJob.Code) ? stdJob.Code : $"NT-2610-JOB{jobIndex:D2}";
                posting.UpdatedAt = DateTimeOffset.UtcNow;
            }
            jobIndex++;
        }

        // 8. Seed Candidates & Resumes
        var candidateUser = await context.Users.FirstOrDefaultAsync(u => u.Email == "candidate.minhtuan@gmail.com" || u.Email == "tuan.bui@noveratech.digital");
        var seedCandidates = new List<(string FirstName, string LastName, string Email, string Phone, string Company, string Title, string ResumeFile, Guid? UserId)>
        {
            ("Bùi Minh", "Tuấn", "candidate.minhtuan@gmail.com", "0912 345 678", "NoveraTech / FPT Software", "Middle QA Engineer", "CV_BuiMinhTuan_SeniorDotnet.pdf", candidateUser?.Id),
            ("Trần Minh", "Đức", "duc.tran@gmail.com", "0988 776 655", "Vingroup VinAI", "AI Research Engineer", "CV_TranMinhDuc_AIEngineer.pdf", null),
            ("Lê Thu", "Hà", "ha.le@gmail.com", "0903 221 144", "Shopee Vietnam", "Senior Tech Recruiter", "CV_LeThuHa_Recruiter.pdf", null),
            ("Hoàng Hải", "Đăng", "dang.hoang@gmail.com", "0977 123 456", "VNG Cloud", "DevOps Engineer", "CV_HoangHaiDang_DevOps.pdf", null),
            ("Nguyễn Phương", "Linh", "linh.nguyen@gmail.com", "0934 567 890", "Momo Fintech", "Product Designer", "CV_NguyenPhuongLinh_UIUX.pdf", null),
            ("Đỗ Gia", "Huy", "huy.dogia@gmail.com", "0945 678 123", "Viettel Telecom", "Associate Product Owner", "CV_DoGiaHuy_ProductOwner.pdf", null),
            ("Phan Văn", "Hải", "hai.phan@gmail.com", "0918 234 567", "Tiki", "Senior Backend Engineer", "CV_PhanVanHai_Hired.pdf", null),
            ("Đinh Thúy", "Nga", "nga.dinh@gmail.com", "0923 456 789", "VNPT IT", "QA Automation Engineer", "CV_DinhThuyNga_Hired.pdf", null),
            ("Vũ Đức", "Thắng", "thang.vu@gmail.com", "0987 654 321", "CMC Global", "Frontend Developer", "CV_VuDucThang_Rejected.pdf", null)
        };

        var candidateEntities = new Dictionary<string, Candidate>();
        foreach (var cData in seedCandidates)
        {
            var cand = await context.Candidates.Include(c => c.Resumes).FirstOrDefaultAsync(c => c.Email == cData.Email);
            if (cand == null)
            {
                cand = new Candidate
                {
                    Id = Guid.NewGuid(),
                    UserId = cData.UserId,
                    FirstName = cData.FirstName,
                    LastName = cData.LastName,
                    Email = cData.Email,
                    Phone = cData.Phone,
                    CurrentCompany = cData.Company,
                    CurrentTitle = cData.Title,
                    Source = CandidateSource.PORTAL,
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-10),
                    UpdatedAt = DateTimeOffset.UtcNow.AddDays(-10)
                };
                await context.Candidates.AddAsync(cand);
                await context.SaveChangesAsync();

                var resume = new Resume
                {
                    Id = Guid.NewGuid(),
                    CandidateId = cand.Id,
                    FileName = cData.ResumeFile,
                    FilePath = $"/uploads/resumes/{cData.ResumeFile}",
                    FileSize = 1024 * 1024 + 250000,
                    MimeType = "application/pdf",
                    IsPrimary = true,
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-10),
                    UpdatedAt = DateTimeOffset.UtcNow.AddDays(-10)
                };
                await context.Resumes.AddAsync(resume);
                await context.SaveChangesAsync();
            }
            candidateEntities[cData.Email] = cand;
        }

        // 9. Seed Applications across Pipeline Stages
        var stagesList = await context.PipelineStages.OrderBy(s => s.StageOrder).ToListAsync();
        var stage1 = stagesList.FirstOrDefault(s => s.StageOrder == 1) ?? stagesList[0];
        var stage2 = stagesList.FirstOrDefault(s => s.StageOrder == 2) ?? stagesList[1];
        var stage3 = stagesList.FirstOrDefault(s => s.StageOrder == 3) ?? stagesList[2];
        var stage4 = stagesList.FirstOrDefault(s => s.StageOrder == 4) ?? stagesList[3];
        var stage5 = stagesList.FirstOrDefault(s => s.StageOrder == 5) ?? stagesList[4];
        var stage6 = stagesList.FirstOrDefault(s => s.StageOrder == 6) ?? stagesList[5];
        var stage7 = stagesList.FirstOrDefault(s => s.StageOrder == 7) ?? stagesList[6];

        var dotnetJob = await context.JobPostings.FirstOrDefaultAsync(j => j.Slug.Contains("dotnet")) ?? await context.JobPostings.FirstAsync();
        var aiJob = await context.JobPostings.FirstOrDefaultAsync(j => j.Slug.Contains("ai")) ?? await context.JobPostings.FirstAsync();
        var recruiterJob = await context.JobPostings.FirstOrDefaultAsync(j => j.Slug.Contains("recruiter")) ?? await context.JobPostings.FirstAsync();
        var devopsJob = await context.JobPostings.FirstOrDefaultAsync(j => j.Slug.Contains("devops")) ?? await context.JobPostings.FirstAsync();
        var designJob = await context.JobPostings.FirstOrDefaultAsync(j => j.Slug.Contains("designer")) ?? await context.JobPostings.FirstAsync();
        var poJob = await context.JobPostings.FirstOrDefaultAsync(j => j.Slug.Contains("product-owner")) ?? await context.JobPostings.FirstAsync();

        // Ứng dụng mẫu cho Bùi Minh Tuấn (Senior .NET - Đang ở Vòng 4: Phỏng vấn chuyên môn / Chuẩn bị Offer)
        Application? appTuan = null;
        if (candidateEntities.TryGetValue("candidate.minhtuan@gmail.com", out var candTuan))
        {
            appTuan = await context.Applications.FirstOrDefaultAsync(a => a.CandidateId == candTuan.Id && a.JobPostingId == dotnetJob.Id);
            if (appTuan == null)
            {
                var resume = await context.Resumes.FirstOrDefaultAsync(r => r.CandidateId == candTuan.Id)
                    ?? await context.Resumes.FirstAsync();

                appTuan = new Application
                {
                    Id = Guid.NewGuid(),
                    JobPostingId = dotnetJob.Id,
                    CandidateId = candTuan.Id,
                    ResumeId = resume.Id,
                    CurrentStageId = stage4.Id,
                    Status = ApplicationStatus.IN_PROCESS,
                    AppliedAt = DateTimeOffset.UtcNow.AddDays(-7),
                    TrackingCode = "NOV-APP-202610-0001",
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-7),
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                await context.Applications.AddAsync(appTuan);
                await context.SaveChangesAsync();

                await context.ApplicationStageHistories.AddRangeAsync(
                    new ApplicationStageHistory { Id = Guid.NewGuid(), ApplicationId = appTuan.Id, FromStageId = null, ToStageId = stage1.Id, ChangedByUserId = hrRecruiter.Id, Comment = "Nộp hồ sơ trực tuyến.", CreatedAt = DateTimeOffset.UtcNow.AddDays(-7) },
                    new ApplicationStageHistory { Id = Guid.NewGuid(), ApplicationId = appTuan.Id, FromStageId = stage1.Id, ToStageId = stage2.Id, ChangedByUserId = hrRecruiter.Id, Comment = "Sơ loại CV đạt yêu cầu 4/5.", CreatedAt = DateTimeOffset.UtcNow.AddDays(-5) },
                    new ApplicationStageHistory { Id = Guid.NewGuid(), ApplicationId = appTuan.Id, FromStageId = stage2.Id, ToStageId = stage3.Id, ChangedByUserId = hrRecruiter.Id, Comment = "Hoàn thành phỏng vấn sơ loại văn hóa.", CreatedAt = DateTimeOffset.UtcNow.AddDays(-3) },
                    new ApplicationStageHistory { Id = Guid.NewGuid(), ApplicationId = appTuan.Id, FromStageId = stage3.Id, ToStageId = stage4.Id, ChangedByUserId = hrRecruiter.Id, Comment = "Chuyển tiếp vòng phỏng vấn chuyên môn cùng Ban Giám Đốc.", CreatedAt = DateTimeOffset.UtcNow.AddDays(-1) }
                );
                await context.SaveChangesAsync();
            }
        }

        // Ứng dụng mẫu cho Trần Minh Đức (AI Engineer - Phỏng vấn chuyên môn)
        Application? appDuc = null;
        if (candidateEntities.TryGetValue("duc.tran@gmail.com", out var candDuc))
        {
            appDuc = await context.Applications.FirstOrDefaultAsync(a => a.CandidateId == candDuc.Id && a.JobPostingId == aiJob.Id);
            if (appDuc == null)
            {
                var resume = await context.Resumes.FirstOrDefaultAsync(r => r.CandidateId == candDuc.Id)
                    ?? await context.Resumes.FirstAsync();

                appDuc = new Application
                {
                    Id = Guid.NewGuid(),
                    JobPostingId = aiJob.Id,
                    CandidateId = candDuc.Id,
                    ResumeId = resume.Id,
                    CurrentStageId = stage4.Id,
                    Status = ApplicationStatus.IN_PROCESS,
                    AppliedAt = DateTimeOffset.UtcNow.AddDays(-6),
                    TrackingCode = "NOV-APP-202610-0002",
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-6),
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                await context.Applications.AddAsync(appDuc);
                await context.SaveChangesAsync();
            }
        }

        // Ứng dụng mẫu cho Lê Thu Hà (Recruiter - Đang ở Stage 5 Offer)
        Application? appHa = null;
        if (candidateEntities.TryGetValue("ha.le@gmail.com", out var candHa))
        {
            appHa = await context.Applications.FirstOrDefaultAsync(a => a.CandidateId == candHa.Id && a.JobPostingId == recruiterJob.Id);
            if (appHa == null)
            {
                var resume = await context.Resumes.FirstOrDefaultAsync(r => r.CandidateId == candHa.Id)
                    ?? await context.Resumes.FirstAsync();

                appHa = new Application
                {
                    Id = Guid.NewGuid(),
                    JobPostingId = recruiterJob.Id,
                    CandidateId = candHa.Id,
                    ResumeId = resume.Id,
                    CurrentStageId = stage5.Id,
                    Status = ApplicationStatus.IN_PROCESS,
                    AppliedAt = DateTimeOffset.UtcNow.AddDays(-8),
                    TrackingCode = "NOV-APP-202610-0003",
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-8),
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                await context.Applications.AddAsync(appHa);
                await context.SaveChangesAsync();
            }
        }

        // Ứng dụng mẫu cho Stage 1, 2, 6, 7
        if (candidateEntities.TryGetValue("huy.dogia@gmail.com", out var candHuy))
        {
            if (!await context.Applications.AnyAsync(a => a.CandidateId == candHuy.Id))
            {
                var r = await context.Resumes.FirstAsync(re => re.CandidateId == candHuy.Id);
                await context.Applications.AddAsync(new Application
                {
                    Id = Guid.NewGuid(),
                    JobPostingId = poJob.Id,
                    CandidateId = candHuy.Id,
                    ResumeId = r.Id,
                    CurrentStageId = stage1.Id,
                    Status = ApplicationStatus.IN_PROCESS,
                    AppliedAt = DateTimeOffset.UtcNow.AddHours(-18),
                    CreatedAt = DateTimeOffset.UtcNow.AddHours(-18),
                    UpdatedAt = DateTimeOffset.UtcNow
                });
            }
        }

        if (candidateEntities.TryGetValue("dang.hoang@gmail.com", out var candDang))
        {
            if (!await context.Applications.AnyAsync(a => a.CandidateId == candDang.Id))
            {
                var r = await context.Resumes.FirstAsync(re => re.CandidateId == candDang.Id);
                await context.Applications.AddAsync(new Application
                {
                    Id = Guid.NewGuid(),
                    JobPostingId = devopsJob.Id,
                    CandidateId = candDang.Id,
                    ResumeId = r.Id,
                    CurrentStageId = stage2.Id,
                    Status = ApplicationStatus.IN_PROCESS,
                    AppliedAt = DateTimeOffset.UtcNow.AddDays(-2),
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-2),
                    UpdatedAt = DateTimeOffset.UtcNow
                });
            }
        }

        if (candidateEntities.TryGetValue("linh.nguyen@gmail.com", out var candLinh))
        {
            if (!await context.Applications.AnyAsync(a => a.CandidateId == candLinh.Id))
            {
                var r = await context.Resumes.FirstAsync(re => re.CandidateId == candLinh.Id);
                await context.Applications.AddAsync(new Application
                {
                    Id = Guid.NewGuid(),
                    JobPostingId = designJob.Id,
                    CandidateId = candLinh.Id,
                    ResumeId = r.Id,
                    CurrentStageId = stage2.Id,
                    Status = ApplicationStatus.IN_PROCESS,
                    AppliedAt = DateTimeOffset.UtcNow.AddDays(-3),
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-3),
                    UpdatedAt = DateTimeOffset.UtcNow
                });
            }
        }

        // Hired Candidates (Stage 6)
        if (candidateEntities.TryGetValue("hai.phan@gmail.com", out var candHai))
        {
            if (!await context.Applications.AnyAsync(a => a.CandidateId == candHai.Id))
            {
                var r = await context.Resumes.FirstAsync(re => re.CandidateId == candHai.Id);
                await context.Applications.AddAsync(new Application
                {
                    Id = Guid.NewGuid(),
                    JobPostingId = dotnetJob.Id,
                    CandidateId = candHai.Id,
                    ResumeId = r.Id,
                    CurrentStageId = stage6.Id,
                    Status = ApplicationStatus.HIRED,
                    AppliedAt = DateTimeOffset.UtcNow.AddDays(-20),
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-20),
                    UpdatedAt = DateTimeOffset.UtcNow.AddDays(-5)
                });
            }
        }

        if (candidateEntities.TryGetValue("nga.dinh@gmail.com", out var candNga))
        {
            if (!await context.Applications.AnyAsync(a => a.CandidateId == candNga.Id))
            {
                var r = await context.Resumes.FirstAsync(re => re.CandidateId == candNga.Id);
                await context.Applications.AddAsync(new Application
                {
                    Id = Guid.NewGuid(),
                    JobPostingId = dotnetJob.Id,
                    CandidateId = candNga.Id,
                    ResumeId = r.Id,
                    CurrentStageId = stage6.Id,
                    Status = ApplicationStatus.HIRED,
                    AppliedAt = DateTimeOffset.UtcNow.AddDays(-25),
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-25),
                    UpdatedAt = DateTimeOffset.UtcNow.AddDays(-3)
                });
            }
        }

        // Rejected Candidate (Stage 7)
        if (candidateEntities.TryGetValue("thang.vu@gmail.com", out var candThang))
        {
            if (!await context.Applications.AnyAsync(a => a.CandidateId == candThang.Id))
            {
                var r = await context.Resumes.FirstAsync(re => re.CandidateId == candThang.Id);
                await context.Applications.AddAsync(new Application
                {
                    Id = Guid.NewGuid(),
                    JobPostingId = dotnetJob.Id,
                    CandidateId = candThang.Id,
                    ResumeId = r.Id,
                    CurrentStageId = stage7.Id,
                    Status = ApplicationStatus.REJECTED,
                    AppliedAt = DateTimeOffset.UtcNow.AddDays(-12),
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-12),
                    UpdatedAt = DateTimeOffset.UtcNow.AddDays(-6)
                });
            }
        }
        await context.SaveChangesAsync();

        // Tự động bổ sung TrackingCode cho các hồ sơ chưa có mã tra cứu
        var appsWithoutCode = await context.Applications.Where(a => string.IsNullOrEmpty(a.TrackingCode)).ToListAsync();
        var codeCounter = 4;
        foreach (var app in appsWithoutCode)
        {
            app.TrackingCode = $"NOV-APP-202610-{codeCounter:D4}";
            codeCounter++;
        }
        if (appsWithoutCode.Any())
        {
            await context.SaveChangesAsync();
        }

        // 10. Seed Interviews (Phiên phỏng vấn thật có link Google Meet và phân công hội đồng)
        var adminUser = await context.Users.FirstOrDefaultAsync(u => u.Email == "admin@noveratech.vn" || u.Email == "hieu.luong@noveratech.digital")
            ?? await context.Users.FirstAsync();
        var interviewerUser = await context.Users.FirstOrDefaultAsync(u => u.Email == "interviewer.quanghuy@gmail.com" || u.Email == "huy.ngo@noveratech.digital")
            ?? adminUser;
        var hrManagerUser = await context.Users.FirstOrDefaultAsync(u => u.Email == "hr@noveratech.vn" || u.Email == "phuong.nguyen@noveratech.digital")
            ?? adminUser;

        var todayUtc = new DateTimeOffset(DateTime.UtcNow.Year, DateTime.UtcNow.Month, DateTime.UtcNow.Day, 0, 0, 0, TimeSpan.Zero);

        if (appTuan != null && !await context.Interviews.AnyAsync(i => i.ApplicationId == appTuan.Id))
        {
            var interviewTuan = new Interview
            {
                Id = Guid.NewGuid(),
                ApplicationId = appTuan.Id,
                RoundNumber = 1,
                Title = "Phỏng vấn Kỹ thuật vòng 1 (.NET 9 & System Architecture)",
                InterviewType = InterviewType.ONLINE_MEET,
                LocationOrLink = "https://meet.google.com/nov-interview-089",
                StartTime = todayUtc.AddDays(1).AddHours(14),
                EndTime = todayUtc.AddDays(1).AddHours(15).AddMinutes(30),
                CandidateConfirmed = CandidateConfirmStatus.PENDING,
                Status = InterviewStatus.SCHEDULED,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await context.Interviews.AddAsync(interviewTuan);
            await context.SaveChangesAsync();

            await context.InterviewPanelists.AddRangeAsync(
                new InterviewPanelist { Id = Guid.NewGuid(), InterviewId = interviewTuan.Id, InterviewerId = adminUser.Id, IsLead = true },
                new InterviewPanelist { Id = Guid.NewGuid(), InterviewId = interviewTuan.Id, InterviewerId = interviewerUser.Id, IsLead = false }
            );
            await context.SaveChangesAsync();

            // Seed phiếu đánh giá của Giám khảo 1 (Admin) nộp cách đây 2 giờ (< 24h) để kiểm thử Blind Review & Reopen (Scrum - Số 57)
            await context.InterviewEvaluations.AddAsync(new InterviewEvaluation
            {
                Id = Guid.NewGuid(),
                InterviewId = interviewTuan.Id,
                InterviewerId = adminUser.Id,
                OverallScore = 4.5m,
                Recommendation = RecommendationType.HIRE,
                Strengths = "Kiến trúc hệ thống tốt, nắm vững C# .NET 9 và CQRS pattern.",
                Weaknesses = "Cần củng cố kinh nghiệm Kubernetes production.",
                DetailedFeedback = "Ứng viên trả lời rành mạch, tư duy hệ thống vững. Đề xuất: Nên tuyển.",
                IsSubmitted = true,
                SubmittedAt = DateTimeOffset.UtcNow.AddHours(-2),
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-3),
                UpdatedAt = DateTimeOffset.UtcNow.AddHours(-2)
            });
            await context.SaveChangesAsync();
        }

        if (appDuc != null && !await context.Interviews.AnyAsync(i => i.ApplicationId == appDuc.Id))
        {
            var interviewDuc = new Interview
            {
                Id = Guid.NewGuid(),
                ApplicationId = appDuc.Id,
                RoundNumber = 1,
                Title = "Phỏng vấn Chuyên môn AI & RAG Pipeline",
                InterviewType = InterviewType.ONLINE_MEET,
                LocationOrLink = "https://meet.google.com/nov-ai-interview",
                StartTime = todayUtc.AddDays(2).AddHours(10),
                EndTime = todayUtc.AddDays(2).AddHours(11).AddMinutes(30),
                CandidateConfirmed = CandidateConfirmStatus.ACCEPTED,
                Status = InterviewStatus.SCHEDULED,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-2),
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await context.Interviews.AddAsync(interviewDuc);
            await context.SaveChangesAsync();

            await context.InterviewPanelists.AddAsync(
                new InterviewPanelist { Id = Guid.NewGuid(), InterviewId = interviewDuc.Id, InterviewerId = itManager.Id, IsLead = true }
            );
            await context.SaveChangesAsync();
        }

        if (appHa != null && !await context.Interviews.AnyAsync(i => i.ApplicationId == appHa.Id))
        {
            var interviewHa = new Interview
            {
                Id = Guid.NewGuid(),
                ApplicationId = appHa.Id,
                RoundNumber = 2,
                Title = "Phỏng vấn Văn hóa & Định hướng Tuyển dụng",
                InterviewType = InterviewType.ONLINE_MEET,
                LocationOrLink = "https://meet.google.com/nov-hr-interview",
                StartTime = todayUtc.AddDays(-1).AddHours(16),
                EndTime = todayUtc.AddDays(-1).AddHours(17),
                CandidateConfirmed = CandidateConfirmStatus.ACCEPTED,
                Status = InterviewStatus.COMPLETED,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-4),
                UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1)
            };
            await context.Interviews.AddAsync(interviewHa);
            await context.SaveChangesAsync();

            await context.InterviewPanelists.AddAsync(
                new InterviewPanelist { Id = Guid.NewGuid(), InterviewId = interviewHa.Id, InterviewerId = hrManagerUser.Id, IsLead = true }
            );

            // Đánh giá đã hoàn tất
            await context.InterviewEvaluations.AddAsync(new InterviewEvaluation
            {
                Id = Guid.NewGuid(),
                InterviewId = interviewHa.Id,
                InterviewerId = hrManagerUser.Id,
                OverallScore = 4.5m,
                Recommendation = RecommendationType.HIRE,
                Strengths = "Kỹ năng sourcing ứng viên tốt, nắm vững công cụ ATS và giao tiếp lôi cuốn.",
                Weaknesses = "Cần làm quen thêm với quy trình phê duyệt nội bộ của công ty.",
                DetailedFeedback = "Đạt yêu cầu xuất sắc cho vị trí Senior Talent Acquisition Specialist.",
                IsSubmitted = true,
                SubmittedAt = DateTimeOffset.UtcNow.AddDays(-1),
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
                UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1)
            });
            await context.SaveChangesAsync();
        }

        // SCRUM-336 / Scrum - Số 58: Seed dữ liệu so sánh ứng viên theo điểm đánh giá cho vị trí Senior .NET Developer
        await SeedCandidateEvaluationComparisonDataAsync(context, dotnetJob.Id, adminUser.Id, interviewerUser.Id);

        // 11. Seed JobOffer chờ duyệt (Dành cho chức năng Phê duyệt Offer của Ban Giám Đốc / Approver)
        var approverUser = await context.Users.FirstOrDefaultAsync(u => u.Email == "bod@noveratech.vn" || u.Email == "minh.tran@noveratech.digital")
            ?? adminUser;

        if (appTuan != null && !await context.JobOffers.AnyAsync(o => o.ApplicationId == appTuan.Id))
        {
            var offer = new JobOffer
            {
                Id = Guid.NewGuid(),
                ApplicationId = appTuan.Id,
                CreatedByRecruiterId = hrRecruiter.Id,
                BaseSalary = 45000000,
                BonusAllowance = 7000000,
                TotalPackage = 52000000,
                ProposedJoinDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(20)),
                ProbationPeriodMonths = 2,
                ContractType = "Hợp đồng lao động không xác định thời hạn (chính thức)",
                Status = OfferStatus.PENDING_APPROVAL,
                IsAboveBudget = false,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
                UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1)
            };
            await context.JobOffers.AddAsync(offer);
            await context.SaveChangesAsync();

            await context.OfferApprovals.AddAsync(new OfferApproval
            {
                Id = Guid.NewGuid(),
                OfferId = offer.Id,
                ApproverId = approverUser.Id,
                Level = 1,
                Status = ApprovalStatus.PENDING,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
                UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1)
            });
            await context.SaveChangesAsync();
        }

        // 17. Seed RejectionReasons (SCRUM-42)
        if (!await context.RejectionReasons.AnyAsync())
        {
            var rejectionReasons = new List<RejectionReason>
            {
                new() { Id = Guid.NewGuid(), Code = "REJ_SKILL", Name = "Chuyên môn chưa đạt yêu cầu", Description = "Kỹ năng kỹ thuật hoặc năng lực chuyên môn chưa đáp ứng yêu cầu của vị trí tuyển dụng", DisplayOrder = 1, IsActive = true },
                new() { Id = Guid.NewGuid(), Code = "REJ_EXP", Name = "Chưa đủ số năm kinh nghiệm", Description = "Số năm kinh nghiệm làm việc thực tế chưa đạt mức kỳ vọng của vị trí", DisplayOrder = 2, IsActive = true },
                new() { Id = Guid.NewGuid(), Code = "REJ_SALARY", Name = "Không thỏa thuận được mức lương", Description = "Kỳ vọng thu nhập của ứng viên vượt khung ngân sách hoặc chế độ đãi ngộ của công ty", DisplayOrder = 3, IsActive = true },
                new() { Id = Guid.NewGuid(), Code = "REJ_CULTURE", Name = "Chưa phù hợp văn hóa doanh nghiệp", Description = "Phong cách làm việc hoặc giá trị cốt lõi chưa tương thích với văn hóa đội ngũ", DisplayOrder = 4, IsActive = true },
                new() { Id = Guid.NewGuid(), Code = "REJ_WITHDRAW", Name = "Ứng viên chủ động rút lui", Description = "Ứng viên nhận được offer khác hoặc rút hồ sơ vì lý do cá nhân", DisplayOrder = 5, IsActive = true },
                new() { Id = Guid.NewGuid(), Code = "REJ_LOCATION", Name = "Địa điểm làm việc không thuận tiện", Description = "Khoảng cách địa lý không phù hợp hoặc không đáp ứng thời gian làm việc tại văn phòng", DisplayOrder = 6, IsActive = true },
                new() { Id = Guid.NewGuid(), Code = "REJ_OTHER", Name = "Lý do khác", Description = "Các lý do loại hồ sơ phát sinh khác", DisplayOrder = 7, IsActive = true }
            };
            await context.RejectionReasons.AddRangeAsync(rejectionReasons);
            await context.SaveChangesAsync();
        }

        // 12. Seed DepartmentHeadcountBudget (Scrum 24 - Quản lý ngân sách headcount theo phòng ban theo năm)
        var currentYear = DateTime.UtcNow.Year;
        if (!await context.DepartmentHeadcountBudgets.AnyAsync(b => b.Year == currentYear))
        {
            var depts = await context.Departments.ToListAsync();
            var admin = await context.Users.FirstOrDefaultAsync(u => u.Email == "admin@noveratech.vn");
            var adminId = admin?.Id;

            var deptBudgets = new List<DepartmentHeadcountBudget>();
            foreach (var d in depts)
            {
                var (targetHc, budget) = d.Code switch
                {
                    "IT" => (35, 15_000_000_000m),      // 35 nhân sự, 15 tỷ VND
                    "HR" => (10, 3_500_000_000m),       // 10 nhân sự, 3.5 tỷ VND
                    "SALES" => (20, 6_000_000_000m),    // 20 nhân sự, 6 tỷ VND
                    "BOD" => (5, 4_000_000_000m),       // 5 nhân sự, 4 tỷ VND
                    _ => (15, 5_000_000_000m)
                };

                deptBudgets.Add(new DepartmentHeadcountBudget
                {
                    Id = Guid.NewGuid(),
                    DepartmentId = d.Id,
                    Year = currentYear,
                    TargetHeadcount = targetHc,
                    SalaryBudget = budget,
                    Currency = "VND",
                    Note = $"Kế hoạch định biên và ngân sách lương năm {currentYear} đã được Ban Giám Đốc phê duyệt đầu năm.",
                    IsActive = true,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow,
                    CreatedById = adminId
                });
            }

            await context.DepartmentHeadcountBudgets.AddRangeAsync(deptBudgets);
            await context.SaveChangesAsync();
        }

        // 13. Seed Requisition Recruiters & Handover Histories (Scrum 26 - Phân công Recruiter phụ trách yêu cầu tuyển dụng)
        if (!await context.RequisitionRecruiters.AnyAsync())
        {
            var hrManager = await context.Users.FirstOrDefaultAsync(u => u.Email == "phuong.nguyen@noveratech.digital" || u.Email == "hr@noveratech.vn")
                ?? await context.Users.FirstAsync();
            var recruiter1 = await context.Users.FirstOrDefaultAsync(u => u.Email == "recruiter.thuydung@gmail.com" || u.Email == "dung.le@noveratech.digital");
            var recruiter2 = await context.Users.FirstOrDefaultAsync(u => u.Email == "recruiter.quocanh@gmail.com" || u.Email == "anh.pham@noveratech.digital");

            if (recruiter1 != null && recruiter2 != null)
            {
                var requisitions = await context.JobRequisitions.OrderBy(r => r.CreatedAt).ToListAsync();
                if (requisitions.Any())
                {
                    int count = requisitions.Count;
                    for (int i = 0; i < count; i++)
                    {
                        var req = requisitions[i];

                        // Để lại 2 vị trí cuối chưa phân công (Unassigned) để hiển thị thực tế trạng thái cần xử lý gấp
                        if (i >= count - 2)
                        {
                            req.AssignedRecruiterId = null;
                            req.UpdatedAt = DateTimeOffset.UtcNow;
                            continue;
                        }

                        var isLead1 = i % 2 == 0;
                        var lead = isLead1 ? recruiter1 : recruiter2;
                        var support = isLead1 ? recruiter2 : recruiter1;

                        req.AssignedRecruiterId = lead.Id;
                        req.UpdatedAt = DateTimeOffset.UtcNow;

                        // Add Primary
                        await context.RequisitionRecruiters.AddAsync(new RequisitionRecruiter
                        {
                            Id = Guid.NewGuid(),
                            RequisitionId = req.Id,
                            RecruiterId = lead.Id,
                            IsPrimary = true,
                            AssignedAt = DateTimeOffset.UtcNow.AddDays(-15),
                            AssignedById = hrManager.Id,
                            Notes = "Chuyên viên tuyển dụng phụ trách chính."
                        });

                        // Add Support (for even requisitions)
                        if (i % 2 == 0)
                        {
                            await context.RequisitionRecruiters.AddAsync(new RequisitionRecruiter
                            {
                                Id = Guid.NewGuid(),
                                RequisitionId = req.Id,
                                RecruiterId = support.Id,
                                IsPrimary = false,
                                AssignedAt = DateTimeOffset.UtcNow.AddDays(-10),
                                AssignedById = hrManager.Id,
                                Notes = "Hỗ trợ sàng lọc hồ sơ và điều phối phỏng vấn."
                            });
                        }
                    }

                    // Seed ít nhất 2 RequisitionHandoverHistory (Lịch sử chuyển giao)
                    var firstReq = requisitions.First();
                    await context.RequisitionHandoverHistories.AddAsync(new RequisitionHandoverHistory
                    {
                        Id = Guid.NewGuid(),
                        RequisitionId = firstReq.Id,
                        FromRecruiterId = null,
                        ToRecruiterId = recruiter1.Id,
                        HandoverType = "INITIAL_ASSIGNMENT",
                        Reason = "Phân công phụ trách ban đầu khi mở yêu cầu tuyển dụng.",
                        Notes = "Bắt đầu đăng tuyển và tiếp nhận ứng viên.",
                        TransferredById = hrManager.Id,
                        TransferredAt = DateTimeOffset.UtcNow.AddDays(-20)
                    });

                    if (requisitions.Count > 1)
                    {
                        var secondReq = requisitions[1];
                        await context.RequisitionHandoverHistories.AddAsync(new RequisitionHandoverHistory
                        {
                            Id = Guid.NewGuid(),
                            RequisitionId = secondReq.Id,
                            FromRecruiterId = recruiter1.Id,
                            ToRecruiterId = recruiter2.Id,
                            HandoverType = "LEAD_HANDOVER",
                            Reason = "Điều chuyển khối lượng công việc, tối ưu thời gian phản hồi ứng viên.",
                            Notes = "Đã bàn giao 03 hồ sơ đang ở vòng phỏng vấn chuyên môn.",
                            TransferredById = hrManager.Id,
                            TransferredAt = DateTimeOffset.UtcNow.AddDays(-5)
                        });
                    }

                    await context.SaveChangesAsync();
                }
            }
        }

        // 14. Seed Hồ sơ Hợp nhất (Scrum 39: Lịch sử nhiều đợt ứng tuyển, Nhãn và Ghi chú nội bộ)
        if (candidateEntities.TryGetValue("candidate.minhtuan@gmail.com", out var candTuanUnified))
        {
            // Seed lần ứng tuyển trong quá khứ (Lịch sử ứng tuyển các vị trí khác nhau của cùng người này)
            if (!await context.Applications.AnyAsync(a => a.CandidateId == candTuanUnified.Id && a.JobPostingId == devopsJob.Id))
            {
                var resumeTuan = await context.Resumes.FirstOrDefaultAsync(r => r.CandidateId == candTuanUnified.Id)
                    ?? await context.Resumes.FirstAsync();

                var pastApp = new Application
                {
                    Id = Guid.NewGuid(),
                    JobPostingId = devopsJob.Id,
                    CandidateId = candTuanUnified.Id,
                    ResumeId = resumeTuan.Id,
                    CurrentStageId = stage7.Id,
                    Status = ApplicationStatus.REJECTED,
                    RejectionNote = "Hồ sơ năng lực tốt, phù hợp định hướng Backend .NET hơn DevOps. Đã lưu talent pool.",
                    AppliedAt = DateTimeOffset.UtcNow.AddMonths(-6),
                    CreatedAt = DateTimeOffset.UtcNow.AddMonths(-6),
                    UpdatedAt = DateTimeOffset.UtcNow.AddMonths(-5)
                };
                await context.Applications.AddAsync(pastApp);

                await context.ApplicationStageHistories.AddRangeAsync(
                    new ApplicationStageHistory { Id = Guid.NewGuid(), ApplicationId = pastApp.Id, FromStageId = null, ToStageId = stage1.Id, ChangedByUserId = hrRecruiter.Id, Comment = "Ứng tuyển đợt tuyển dụng Q2/2026.", CreatedAt = DateTimeOffset.UtcNow.AddMonths(-6) },
                    new ApplicationStageHistory { Id = Guid.NewGuid(), ApplicationId = pastApp.Id, FromStageId = stage1.Id, ToStageId = stage2.Id, ChangedByUserId = hrRecruiter.Id, Comment = "Đạt sơ loại hồ sơ CV.", CreatedAt = DateTimeOffset.UtcNow.AddMonths(-6).AddDays(3) },
                    new ApplicationStageHistory { Id = Guid.NewGuid(), ApplicationId = pastApp.Id, FromStageId = stage2.Id, ToStageId = stage7.Id, ChangedByUserId = hrRecruiter.Id, Comment = "Đề xuất chuyển hướng sang vị trí .NET khi có đợt tuyển mới.", CreatedAt = DateTimeOffset.UtcNow.AddMonths(-5) }
                );
            }

            // Seed Nhãn (Tags) cho ứng viên
            if (!await context.CandidateTags.AnyAsync(t => t.CandidateId == candTuanUnified.Id))
            {
                await context.CandidateTags.AddRangeAsync(
                    new CandidateTag { Id = Guid.NewGuid(), CandidateId = candTuanUnified.Id, TagName = "Senior .NET", ColorCode = "#2563EB", CreatedByUserId = hrRecruiter.Id, CreatedAt = DateTimeOffset.UtcNow.AddDays(-7) },
                    new CandidateTag { Id = Guid.NewGuid(), CandidateId = candTuanUnified.Id, TagName = "Tiềm năng cao", ColorCode = "#059669", CreatedByUserId = hrRecruiter.Id, CreatedAt = DateTimeOffset.UtcNow.AddDays(-6) },
                    new CandidateTag { Id = Guid.NewGuid(), CandidateId = candTuanUnified.Id, TagName = "Tiếng Anh tốt", ColorCode = "#D97706", CreatedByUserId = hrRecruiter.Id, CreatedAt = DateTimeOffset.UtcNow.AddDays(-4) }
                );
            }

            // Seed Ghi chú nội bộ (Recruitment Notes) cho ứng viên
            if (!await context.CandidateNotes.AnyAsync(n => n.CandidateId == candTuanUnified.Id))
            {
                await context.CandidateNotes.AddRangeAsync(
                    new CandidateNote
                    {
                        Id = Guid.NewGuid(),
                        CandidateId = candTuanUnified.Id,
                        AuthorUserId = hrRecruiter.Id,
                        NoteType = "RECRUITER_CALL",
                        Content = "Đã trao đổi qua điện thoại vòng sơ loại: Ứng viên tự tin, giao tiếp lưu loát, có định hướng gắn bó lâu dài và mong muốn phát triển lên Tech Lead.",
                        CreatedAt = DateTimeOffset.UtcNow.AddDays(-6)
                    },
                    new CandidateNote
                    {
                        Id = Guid.NewGuid(),
                        CandidateId = candTuanUnified.Id,
                        AuthorUserId = hrRecruiter.Id,
                        NoteType = "EVALUATION",
                        Content = "Đánh giá phỏng vấn kỹ thuật: Nắm chắc ASP.NET Core 9, Clean Architecture, PostgreSQL và Docker. Trả lời bài toán concurrency xuất sắc.",
                        CreatedAt = DateTimeOffset.UtcNow.AddDays(-3)
                    },
                    new CandidateNote
                    {
                        Id = Guid.NewGuid(),
                        CandidateId = candTuanUnified.Id,
                        AuthorUserId = hrRecruiter.Id,
                        NoteType = "SALARY",
                        Content = "Mức lương kỳ vọng: 45.000.000 VNĐ net. Thời gian có thể bắt đầu làm việc: 30 ngày sau khi nhận offer.",
                        CreatedAt = DateTimeOffset.UtcNow.AddDays(-2)
                    }
                );
            }
        }

        if (candidateEntities.TryGetValue("duc.tran@gmail.com", out var candDucUnified))
        {
            if (!await context.CandidateTags.AnyAsync(t => t.CandidateId == candDucUnified.Id))
            {
                await context.CandidateTags.AddRangeAsync(
                    new CandidateTag { Id = Guid.NewGuid(), CandidateId = candDucUnified.Id, TagName = "AI Engineer", ColorCode = "#7C3AED", CreatedByUserId = hrRecruiter.Id, CreatedAt = DateTimeOffset.UtcNow.AddDays(-5) },
                    new CandidateTag { Id = Guid.NewGuid(), CandidateId = candDucUnified.Id, TagName = "Python/PyTorch", ColorCode = "#0284C7", CreatedByUserId = hrRecruiter.Id, CreatedAt = DateTimeOffset.UtcNow.AddDays(-5) }
                );
            }
            if (!await context.CandidateNotes.AnyAsync(n => n.CandidateId == candDucUnified.Id))
            {
                await context.CandidateNotes.AddAsync(new CandidateNote
                {
                    Id = Guid.NewGuid(),
                    CandidateId = candDucUnified.Id,
                    AuthorUserId = hrRecruiter.Id,
                    NoteType = "EVALUATION",
                    Content = "Ứng viên có các bài báo nghiên cứu về NLP và RAG. Đang chờ kết quả phỏng vấn chuyên môn với ban công nghệ.",
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-4)
                });
            }
        }

        // 9. Seed Kho Ứng viên Tiềm năng (Scrum 45 - Talent Pool)
        if (!await context.TalentPoolCandidates.AnyAsync())
        {
            var hrLeadUser = await context.Users.FirstOrDefaultAsync(u => u.Email == "hr@noveratech.vn" || u.Email == "phuong.nguyen@noveratech.digital")
                ?? await context.Users.FirstAsync();

            var talentPoolSeeds = new List<(string Email, string TargetTitle, string LastStage, string Reason, string Skills, int Exp, int Rating, string Status, string Notes)>
            {
                (
                    "hai.phan@gmail.com",
                    "Senior Software Engineer (.NET & React)",
                    "Đề nghị tuyển dụng (Offer)",
                    "Ứng viên chuyên môn xuất sắc, giải quyết bài toán microservices và concurrency vượt trội; trượt vòng cuối do chỉ tiêu tuyển 1 người ở đợt này.",
                    ".NET Core, C#, PostgreSQL, Docker, Kubernetes, Microservices, Redis, React",
                    5,
                    5,
                    "AVAILABLE",
                    "Ưu tiên liên hệ đầu tiên khi mở đợt tuyển Senior .NET hoặc Tech Lead mới."
                ),
                (
                    "dang.hoang@gmail.com",
                    "Senior DevOps & Cloud Platform Engineer",
                    "Phỏng vấn chuyên môn",
                    "Năng lực CI/CD và Kubernetes rất tốt, kiến thức AWS vững vàng; lọt vào top 2 ứng viên xuất sắc nhất của đợt tuyển dụng.",
                    "DevOps, Docker, Kubernetes, CI/CD, Terraform, AWS, Linux, Python",
                    4,
                    4,
                    "AVAILABLE",
                    "Sẵn sàng nhận việc sau 2 tuần. Kỳ vọng môi trường làm việc hybrid."
                ),
                (
                    "thang.vu@gmail.com",
                    "Senior Frontend / Fullstack Engineer",
                    "Phỏng vấn chuyên môn",
                    "Kỹ năng React & TypeScript bài bản, tư duy UI/UX hiện đại; giữ lại kho tiềm năng để liên hệ ngay khi mở dự án mới.",
                    "React, TypeScript, Next.js, TailwindCSS, REST API, Redux",
                    3,
                    4,
                    "AVAILABLE",
                    "Thái độ học hỏi rất tốt, tiếng Anh đọc hiểu tài liệu kỹ thuật thành thạo."
                ),
                (
                    "linh.nguyen@gmail.com",
                    "Senior UI/UX Product Designer",
                    "Đề nghị tuyển dụng (Offer)",
                    "Portfolio xuất sắc, tư duy sản phẩm thực tế và thấu cảm người dùng; tạm gác lại do ngân sách Q2 ưu tiên khối kỹ thuật.",
                    "Figma, UI/UX, Design System, User Research, Wireframing, Prototyping",
                    4,
                    5,
                    "CONTACTED",
                    "Đã liên hệ sơ bộ qua LinkedIn, ứng viên vẫn quan tâm đến NoveraTech."
                ),
                (
                    "huy.dogia@gmail.com",
                    "Product Owner (Fintech & AI Platforms)",
                    "Phỏng vấn chuyên môn",
                    "Hiểu sâu về domain Fintech và thanh toán trực tuyến, có kinh nghiệm quản lý backlog tốt; đề xuất ưu tiên khi mở thêm squad mới.",
                    "Product Management, Scrum, Agile, Backlog Grooming, Jira, Fintech",
                    3,
                    4,
                    "AVAILABLE",
                    "Có chứng chỉ PSPO I, phong thái làm việc chuyên nghiệp."
                )
            };

            foreach (var item in talentPoolSeeds)
            {
                if (candidateEntities.TryGetValue(item.Email, out var candObj))
                {
                    await context.TalentPoolCandidates.AddAsync(new TalentPoolCandidate
                    {
                        Id = Guid.NewGuid(),
                        CandidateId = candObj.Id,
                        TargetJobTitle = item.TargetTitle,
                        LastStageReached = item.LastStage,
                        Reason = item.Reason,
                        Skills = item.Skills,
                        ExperienceYears = item.Exp,
                        Rating = item.Rating,
                        Status = item.Status,
                        Notes = item.Notes,
                        AddedByUserId = hrLeadUser.Id,
                        AddedAt = DateTimeOffset.UtcNow.AddDays(-14),
                        CreatedAt = DateTimeOffset.UtcNow.AddDays(-14),
                        UpdatedAt = DateTimeOffset.UtcNow.AddDays(-14)
                    });
                }
            }
        }

        await context.SaveChangesAsync();
    }

    private static async Task SeedCandidateEvaluationComparisonDataAsync(
        ApplicationDbContext context,
        Guid dotnetJobId,
        Guid adminUserId,
        Guid interviewerUserId)
    {
        var crit1Id = new Guid("a0000000-0000-0000-0000-000000000001");
        var crit2Id = new Guid("a0000000-0000-0000-0000-000000000002");
        var crit3Id = new Guid("a0000000-0000-0000-0000-000000000003");
        var crit4Id = new Guid("a0000000-0000-0000-0000-000000000004");
        var crit5Id = new Guid("a0000000-0000-0000-0000-000000000005");
        var crit6Id = new Guid("a0000000-0000-0000-0000-000000000006");

        // 1. Đảm bảo 6 tiêu chí đánh giá chuẩn hóa đã được seed
        if (!await context.EvaluationCriterias.AnyAsync())
        {
            var now = DateTimeOffset.UtcNow;
            context.EvaluationCriterias.AddRange(
                new EvaluationCriteria { Id = crit1Id, Name = "Kiến trúc hệ thống & Clean Architecture", CriteriaType = "HARD_SKILL", Weight = 1.5m, CreatedAt = now, UpdatedAt = now },
                new EvaluationCriteria { Id = crit2Id, Name = "Lập trình C# .NET & Xử lý bất đồng bộ (Async Concurrency)", CriteriaType = "HARD_SKILL", Weight = 1.5m, CreatedAt = now, UpdatedAt = now },
                new EvaluationCriteria { Id = crit3Id, Name = "Thiết kế Cơ sở dữ liệu & Tối ưu hóa SQL", CriteriaType = "HARD_SKILL", Weight = 1.2m, CreatedAt = now, UpdatedAt = now },
                new EvaluationCriteria { Id = crit4Id, Name = "Tư duy giải quyết vấn đề & Phân tích thuật toán", CriteriaType = "SOFT_SKILL", Weight = 1.0m, CreatedAt = now, UpdatedAt = now },
                new EvaluationCriteria { Id = crit5Id, Name = "Giao tiếp chuyên môn & Phối hợp đội ngũ", CriteriaType = "SOFT_SKILL", Weight = 1.0m, CreatedAt = now, UpdatedAt = now },
                new EvaluationCriteria { Id = crit6Id, Name = "Văn hóa Engineering-First & Tinh thần trách nhiệm (Ownership)", CriteriaType = "CULTURE", Weight = 1.0m, CreatedAt = now, UpdatedAt = now }
            );
            await context.SaveChangesAsync();
        }

        // 2. Tìm các ứng viên cùng ứng tuyển vị trí dotnetJobId
        var dotnetApps = await context.Applications
            .Include(a => a.Candidate)
            .Include(a => a.Interviews)
                .ThenInclude(i => i.Evaluations)
                    .ThenInclude(e => e.Scores)
            .Where(a => a.JobPostingId == dotnetJobId && !a.IsDeleted)
            .ToListAsync();

        var appTuan = dotnetApps.FirstOrDefault(a => a.Candidate.Email == "tuan.le@gmail.com");
        var appHai = dotnetApps.FirstOrDefault(a => a.Candidate.Email == "hai.phan@gmail.com");
        var appNga = dotnetApps.FirstOrDefault(a => a.Candidate.Email == "nga.dinh@gmail.com");

        // 3. Bổ sung EvaluationScores cho Tuấn (nếu đã có phiếu đánh giá nhưng chưa có điểm chi tiết từng tiêu chí)
        if (appTuan != null)
        {
            var tuanInterview = appTuan.Interviews.FirstOrDefault();
            if (tuanInterview != null)
            {
                var tuanEval = tuanInterview.Evaluations.FirstOrDefault(e => e.IsSubmitted);
                if (tuanEval != null && !await context.EvaluationScores.AnyAsync(s => s.EvaluationId == tuanEval.Id))
                {
                    context.EvaluationScores.AddRange(
                        new EvaluationScore { Id = Guid.NewGuid(), EvaluationId = tuanEval.Id, CriteriaId = crit1Id, Score = 5, Comment = "Kiến trúc hệ thống rất tốt" },
                        new EvaluationScore { Id = Guid.NewGuid(), EvaluationId = tuanEval.Id, CriteriaId = crit2Id, Score = 5, Comment = "Lập trình C# .NET 9 xuất sắc" },
                        new EvaluationScore { Id = Guid.NewGuid(), EvaluationId = tuanEval.Id, CriteriaId = crit3Id, Score = 4, Comment = "SQL và EF Core vững" },
                        new EvaluationScore { Id = Guid.NewGuid(), EvaluationId = tuanEval.Id, CriteriaId = crit4Id, Score = 4, Comment = "Tư duy giải quyết bài toán nhanh" },
                        new EvaluationScore { Id = Guid.NewGuid(), EvaluationId = tuanEval.Id, CriteriaId = crit5Id, Score = 4, Comment = "Giao tiếp rõ ràng" },
                        new EvaluationScore { Id = Guid.NewGuid(), EvaluationId = tuanEval.Id, CriteriaId = crit6Id, Score = 5, Comment = "Tinh thần trách nhiệm cao" }
                    );
                    await context.SaveChangesAsync();
                }
            }
        }

        // 4. Seed buổi phỏng vấn & đánh giá cho Hải (candHai)
        if (appHai != null && !appHai.Interviews.Any())
        {
            var interviewHai = new Interview
            {
                Id = Guid.NewGuid(),
                ApplicationId = appHai.Id,
                RoundNumber = 2,
                Title = "Phỏng vấn Chuyên môn Vòng cuối (.NET & SQL Optimization)",
                InterviewType = InterviewType.ONLINE_MEET,
                LocationOrLink = "https://meet.google.com/nov-dotnet-hai",
                StartTime = DateTimeOffset.UtcNow.AddDays(-3).AddHours(10),
                EndTime = DateTimeOffset.UtcNow.AddDays(-3).AddHours(11).AddMinutes(30),
                CandidateConfirmed = CandidateConfirmStatus.ACCEPTED,
                Status = InterviewStatus.COMPLETED,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-5),
                UpdatedAt = DateTimeOffset.UtcNow.AddDays(-3)
            };
            await context.Interviews.AddAsync(interviewHai);
            await context.SaveChangesAsync();

            await context.InterviewPanelists.AddRangeAsync(
                new InterviewPanelist { Id = Guid.NewGuid(), InterviewId = interviewHai.Id, InterviewerId = adminUserId, IsLead = true },
                new InterviewPanelist { Id = Guid.NewGuid(), InterviewId = interviewHai.Id, InterviewerId = interviewerUserId, IsLead = false }
            );

            var evalHai = new InterviewEvaluation
            {
                Id = Guid.NewGuid(),
                InterviewId = interviewHai.Id,
                InterviewerId = adminUserId,
                OverallScore = 4.2m,
                Recommendation = RecommendationType.HIRE,
                Strengths = "Thiết kế cơ sở dữ liệu và tối ưu truy vấn SQL xuất sắc, xử lý indexing rất chuyên sâu.",
                Weaknesses = "Kinh nghiệm microservices event-driven cần hoàn thiện thêm.",
                DetailedFeedback = "Ứng viên có nền tảng vững, phù hợp với tiêu chí tuyển dụng kỹ thuật của team.",
                IsSubmitted = true,
                SubmittedAt = DateTimeOffset.UtcNow.AddDays(-3),
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-3),
                UpdatedAt = DateTimeOffset.UtcNow.AddDays(-3)
            };
            await context.InterviewEvaluations.AddAsync(evalHai);
            await context.SaveChangesAsync();

            context.EvaluationScores.AddRange(
                new EvaluationScore { Id = Guid.NewGuid(), EvaluationId = evalHai.Id, CriteriaId = crit1Id, Score = 4, Comment = "Kiến trúc hệ thống tốt" },
                new EvaluationScore { Id = Guid.NewGuid(), EvaluationId = evalHai.Id, CriteriaId = crit2Id, Score = 4, Comment = "C# .NET đạt yêu cầu" },
                new EvaluationScore { Id = Guid.NewGuid(), EvaluationId = evalHai.Id, CriteriaId = crit3Id, Score = 5, Comment = "SQL & Indexing xuất sắc" },
                new EvaluationScore { Id = Guid.NewGuid(), EvaluationId = evalHai.Id, CriteriaId = crit4Id, Score = 4, Comment = "Giải quyết vấn đề tốt" },
                new EvaluationScore { Id = Guid.NewGuid(), EvaluationId = evalHai.Id, CriteriaId = crit5Id, Score = 4, Comment = "Hòa nhập nhóm nhanh" },
                new EvaluationScore { Id = Guid.NewGuid(), EvaluationId = evalHai.Id, CriteriaId = crit6Id, Score = 4, Comment = "Văn hóa phù hợp" }
            );
            await context.SaveChangesAsync();
        }

        // 5. Seed buổi phỏng vấn & đánh giá cho Nga (candNga)
        if (appNga != null && !appNga.Interviews.Any())
        {
            var interviewNga = new Interview
            {
                Id = Guid.NewGuid(),
                ApplicationId = appNga.Id,
                RoundNumber = 2,
                Title = "Phỏng vấn Đánh giá Năng lực Vòng cuối",
                InterviewType = InterviewType.ONLINE_MEET,
                LocationOrLink = "https://meet.google.com/nov-dotnet-nga",
                StartTime = DateTimeOffset.UtcNow.AddDays(-4).AddHours(14),
                EndTime = DateTimeOffset.UtcNow.AddDays(-4).AddHours(15).AddMinutes(30),
                CandidateConfirmed = CandidateConfirmStatus.ACCEPTED,
                Status = InterviewStatus.COMPLETED,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-6),
                UpdatedAt = DateTimeOffset.UtcNow.AddDays(-4)
            };
            await context.Interviews.AddAsync(interviewNga);
            await context.SaveChangesAsync();

            await context.InterviewPanelists.AddRangeAsync(
                new InterviewPanelist { Id = Guid.NewGuid(), InterviewId = interviewNga.Id, InterviewerId = adminUserId, IsLead = true },
                new InterviewPanelist { Id = Guid.NewGuid(), InterviewId = interviewNga.Id, InterviewerId = interviewerUserId, IsLead = false }
            );

            var evalNga = new InterviewEvaluation
            {
                Id = Guid.NewGuid(),
                InterviewId = interviewNga.Id,
                InterviewerId = adminUserId,
                OverallScore = 3.8m,
                Recommendation = RecommendationType.CONSIDER,
                Strengths = "Tư duy thuật toán sáng sủa, giao tiếp cởi mở và nhiệt tình.",
                Weaknesses = "Kinh nghiệm thực chiến xử lý bài toán chịu tải cao còn hạn chế.",
                DetailedFeedback = "Đạt mức khá, đề xuất cân nhắc tuyển dụng kèm lộ trình đào tạo mentor chuyên sâu.",
                IsSubmitted = true,
                SubmittedAt = DateTimeOffset.UtcNow.AddDays(-4),
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-4),
                UpdatedAt = DateTimeOffset.UtcNow.AddDays(-4)
            };
            await context.InterviewEvaluations.AddAsync(evalNga);
            await context.SaveChangesAsync();

            context.EvaluationScores.AddRange(
                new EvaluationScore { Id = Guid.NewGuid(), EvaluationId = evalNga.Id, CriteriaId = crit1Id, Score = 4, Comment = "Hiểu biết kiến trúc cơ bản" },
                new EvaluationScore { Id = Guid.NewGuid(), EvaluationId = evalNga.Id, CriteriaId = crit2Id, Score = 4, Comment = "Lập trình C# khá" },
                new EvaluationScore { Id = Guid.NewGuid(), EvaluationId = evalNga.Id, CriteriaId = crit3Id, Score = 3, Comment = "SQL ở mức khá" },
                new EvaluationScore { Id = Guid.NewGuid(), EvaluationId = evalNga.Id, CriteriaId = crit4Id, Score = 4, Comment = "Tư duy thuật toán tốt" },
                new EvaluationScore { Id = Guid.NewGuid(), EvaluationId = evalNga.Id, CriteriaId = crit5Id, Score = 4, Comment = "Giao tiếp rất tốt" },
                new EvaluationScore { Id = Guid.NewGuid(), EvaluationId = evalNga.Id, CriteriaId = crit6Id, Score = 4, Comment = "Thái độ tích cực" }
            );
            await context.SaveChangesAsync();
        }

        await SeedOnboardingTemplatesAsync(context);
    }

    private static async Task SeedOnboardingTemplatesAsync(ApplicationDbContext context)
    {
        if (await context.OnboardingTemplates.AnyAsync())
        {
            return;
        }

        var itDept = await context.Departments.FirstOrDefaultAsync(d => d.Code == "IT");
        var hrDept = await context.Departments.FirstOrDefaultAsync(d => d.Code == "HR");

        var hrUser = await context.Users.FirstOrDefaultAsync(u => u.Email == "hr@noveratech.vn" || u.Email == "phuong.nguyen@noveratech.digital");
        var itUser = await context.Users.FirstOrDefaultAsync(u => u.Email == "it@noveratech.vn" || u.Email == "long.vu@noveratech.digital");
        var adminUser = await context.Users.FirstOrDefaultAsync(u => u.Email == "admin@noveratech.vn");

        var itAssignee = itUser?.Id ?? adminUser?.Id;
        var hrAssignee = hrUser?.Id ?? adminUser?.Id;

        // 1. Mẫu IT / Kỹ thuật
        if (itDept != null)
        {
            var itTemplate = new OnboardingTemplate
            {
                Id = Guid.NewGuid(),
                Name = "Checklist Onboarding Khối Công nghệ & IT",
                Description = "Mẫu chuẩn bị thiết bị, tài khoản hệ thống và hồ sơ cho nhân sự khối Kỹ thuật & Công nghệ thông tin.",
                DepartmentId = itDept.Id,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            itTemplate.Items = new List<OnboardingTemplateItem>
            {
                // ACCOUNT
                new() { Id = Guid.NewGuid(), TemplateId = itTemplate.Id, Category = OnboardingCategory.ACCOUNT, Title = "Cấp tài khoản Email công ty (@noveratech.digital)", Description = "Tạo email và phân quyền truy cập Google Workspace doanh nghiệp.", DefaultAssignedUserId = itAssignee, DaysBeforeJoinDateOffset = 2, DisplayOrder = 1 },
                new() { Id = Guid.NewGuid(), TemplateId = itTemplate.Id, Category = OnboardingCategory.ACCOUNT, Title = "Cấp tài khoản GitHub Enterprise, Jira & Confluence", Description = "Add vào Organization trên GitHub và cấp quyền truy cập các dự án liên quan.", DefaultAssignedUserId = itAssignee, DaysBeforeJoinDateOffset = 1, DisplayOrder = 2 },
                new() { Id = Guid.NewGuid(), TemplateId = itTemplate.Id, Category = OnboardingCategory.ACCOUNT, Title = "Cấu hình VPN nội bộ & quyền truy cập Cloud AWS Staging", Description = "Cung cấp profile OpenVPN và key truy cập môi trường thử nghiệm.", DefaultAssignedUserId = itAssignee, DaysBeforeJoinDateOffset = 0, DisplayOrder = 3 },

                // EQUIPMENT
                new() { Id = Guid.NewGuid(), TemplateId = itTemplate.Id, Category = OnboardingCategory.EQUIPMENT, Title = "Cấp phát Laptop cấu hình cao & màn hình phụ 27 inch 4K", Description = "Cài sẵn hệ điều hành, docker và các công cụ phát triển phần mềm chuẩn.", DefaultAssignedUserId = itAssignee, DaysBeforeJoinDateOffset = 1, DisplayOrder = 4 },
                new() { Id = Guid.NewGuid(), TemplateId = itTemplate.Id, Category = OnboardingCategory.EQUIPMENT, Title = "Cấp phát bàn phím cơ & chuột công thái học", Description = "Bàn phím cơ Bluetooth và chuột công thái học chống mỏi cổ tay.", DefaultAssignedUserId = itAssignee, DaysBeforeJoinDateOffset = 1, DisplayOrder = 5 },
                new() { Id = Guid.NewGuid(), TemplateId = itTemplate.Id, Category = OnboardingCategory.EQUIPMENT, Title = "Cấp tai nghe chống ồn khử tạp âm", Description = "Tai nghe phục vụ họp trực tuyến và tập trung làm việc.", DefaultAssignedUserId = itAssignee, DaysBeforeJoinDateOffset = 0, DisplayOrder = 6 },

                // SEATING
                new() { Id = Guid.NewGuid(), TemplateId = itTemplate.Id, Category = OnboardingCategory.SEATING, Title = "Sắp xếp chỗ ngồi tại Khu vực Kỹ thuật (Tầng 4)", Description = "Dọn dẹp bàn làm việc, kiểm tra ổ cắm điện, cổng mạng LAN và ghế công thái học.", DefaultAssignedUserId = hrAssignee, DaysBeforeJoinDateOffset = 1, DisplayOrder = 7 },
                new() { Id = Guid.NewGuid(), TemplateId = itTemplate.Id, Category = OnboardingCategory.SEATING, Title = "Cấp thẻ từ nhân viên & đăng ký vân tay ra vào", Description = "In thẻ nhân viên có ảnh và kích hoạt quyền mở cửa văn phòng.", DefaultAssignedUserId = hrAssignee, DaysBeforeJoinDateOffset = 0, DisplayOrder = 8 },
                new() { Id = Guid.NewGuid(), TemplateId = itTemplate.Id, Category = OnboardingCategory.SEATING, Title = "Chuẩn bị bộ quà tặng Welcome Kit & Sổ tay văn hóa", Description = "Bình giữ nhiệt, sổ tay Novera, bút ký và áo phông đồng phục.", DefaultAssignedUserId = hrAssignee, DaysBeforeJoinDateOffset = 0, DisplayOrder = 9 },

                // DOCUMENT
                new() { Id = Guid.NewGuid(), TemplateId = itTemplate.Id, Category = OnboardingCategory.DOCUMENT, Title = "Ký Hợp đồng thử việc & Thỏa thuận bảo mật thông tin (NDA)", Description = "Ký số hoặc ký giấy hợp đồng và tài liệu cam kết bảo mật mã nguồn công ty.", DefaultAssignedUserId = hrAssignee, DaysBeforeJoinDateOffset = 0, DisplayOrder = 10 },
                new() { Id = Guid.NewGuid(), TemplateId = itTemplate.Id, Category = OnboardingCategory.DOCUMENT, Title = "Thu nhận hồ sơ lý lịch, CCCD & bằng cấp công chứng", Description = "Kiểm tra và lưu trữ hồ sơ nhân sự theo đúng quy định pháp luật.", DefaultAssignedUserId = hrAssignee, DaysBeforeJoinDateOffset = 0, DisplayOrder = 11 },
                new() { Id = Guid.NewGuid(), TemplateId = itTemplate.Id, Category = OnboardingCategory.DOCUMENT, Title = "Đăng ký mã số thuế cá nhân & tài khoản nhận lương", Description = "Thu thập số tài khoản Vietcombank/Techcombank để phòng kế toán chi trả lương.", DefaultAssignedUserId = hrAssignee, DaysBeforeJoinDateOffset = 0, DisplayOrder = 12 }
            };

            await context.OnboardingTemplates.AddAsync(itTemplate);
        }

        // 2. Mẫu Nhân sự / Hành chính
        if (hrDept != null)
        {
            var hrTemplate = new OnboardingTemplate
            {
                Id = Guid.NewGuid(),
                Name = "Checklist Onboarding Khối Nhân sự & Vận hành",
                Description = "Mẫu chuẩn bị cho nhân sự phụ trách tuyển dụng, C&B, hành chính và vận hành doanh nghiệp.",
                DepartmentId = hrDept.Id,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            hrTemplate.Items = new List<OnboardingTemplateItem>
            {
                // ACCOUNT
                new() { Id = Guid.NewGuid(), TemplateId = hrTemplate.Id, Category = OnboardingCategory.ACCOUNT, Title = "Cấp Email công ty & phân quyền hệ thống HRM", Description = "Cấp tài khoản email và quyền truy cập ATS/HRM.", DefaultAssignedUserId = itAssignee, DaysBeforeJoinDateOffset = 2, DisplayOrder = 1 },
                new() { Id = Guid.NewGuid(), TemplateId = hrTemplate.Id, Category = OnboardingCategory.ACCOUNT, Title = "Cấp tài khoản phần mềm MISA Amis / Bảo hiểm xã hội", Description = "Phân quyền quản lý chấm công, tính lương và bảo hiểm.", DefaultAssignedUserId = itAssignee, DaysBeforeJoinDateOffset = 1, DisplayOrder = 2 },

                // EQUIPMENT
                new() { Id = Guid.NewGuid(), TemplateId = hrTemplate.Id, Category = OnboardingCategory.EQUIPMENT, Title = "Cấp phát Laptop văn phòng & chuột không dây", Description = "Laptop Lenovo ThinkPad / Dell Latitude phục vụ công việc hành chính.", DefaultAssignedUserId = itAssignee, DaysBeforeJoinDateOffset = 1, DisplayOrder = 3 },
                new() { Id = Guid.NewGuid(), TemplateId = hrTemplate.Id, Category = OnboardingCategory.EQUIPMENT, Title = "Cấp điện thoại hotline hoặc SIM liên hệ ứng viên", Description = "Thiết bị liên lạc với ứng viên và đối tác tuyển dụng.", DefaultAssignedUserId = hrAssignee, DaysBeforeJoinDateOffset = 0, DisplayOrder = 4 },

                // SEATING
                new() { Id = Guid.NewGuid(), TemplateId = hrTemplate.Id, Category = OnboardingCategory.SEATING, Title = "Bố trí chỗ ngồi tại Khu vực Nhân sự (Tầng 3)", Description = "Chuẩn bị bàn ghế, tủ tài liệu có khóa cá nhân.", DefaultAssignedUserId = hrAssignee, DaysBeforeJoinDateOffset = 1, DisplayOrder = 5 },
                new() { Id = Guid.NewGuid(), TemplateId = hrTemplate.Id, Category = OnboardingCategory.SEATING, Title = "Cấp thẻ nhân viên & thẻ gửi xe", Description = "Bàn giao thẻ chấm công ra vào tòa nhà.", DefaultAssignedUserId = hrAssignee, DaysBeforeJoinDateOffset = 0, DisplayOrder = 6 },

                // DOCUMENT
                new() { Id = Guid.NewGuid(), TemplateId = hrTemplate.Id, Category = OnboardingCategory.DOCUMENT, Title = "Ký Hợp đồng lao động & Phụ lục nhân sự", Description = "Hoàn tất thủ tục pháp lý nhân sự ngày đầu nhận việc.", DefaultAssignedUserId = hrAssignee, DaysBeforeJoinDateOffset = 0, DisplayOrder = 7 },
                new() { Id = Guid.NewGuid(), TemplateId = hrTemplate.Id, Category = OnboardingCategory.DOCUMENT, Title = "Thu thập hồ sơ nhân sự & sổ BHXH", Description = "Lưu trữ hồ sơ cá nhân và kiểm tra thông tin quá trình đóng BHXH.", DefaultAssignedUserId = hrAssignee, DaysBeforeJoinDateOffset = 0, DisplayOrder = 8 }
            };

            await context.OnboardingTemplates.AddAsync(hrTemplate);
        }

        // 3. Mẫu Chuẩn Toàn Công Ty (Mặc định khi phòng ban khác chưa có mẫu riêng)
        var defaultTemplate = new OnboardingTemplate
        {
            Id = Guid.NewGuid(),
            Name = "Checklist Onboarding Chuẩn Toàn Công Ty (General)",
            Description = "Mẫu chuẩn bị tiêu chuẩn áp dụng cho toàn bộ các phòng ban và nhân sự mới tiếp nhận việc.",
            DepartmentId = null,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        defaultTemplate.Items = new List<OnboardingTemplateItem>
        {
            // ACCOUNT
            new() { Id = Guid.NewGuid(), TemplateId = defaultTemplate.Id, Category = OnboardingCategory.ACCOUNT, Title = "Cấp tài khoản Email công ty và Slack", Description = "Cung cấp địa chỉ email doanh nghiệp và tài khoản trao đổi nội bộ.", DefaultAssignedUserId = itAssignee, DaysBeforeJoinDateOffset = 2, DisplayOrder = 1 },
            new() { Id = Guid.NewGuid(), TemplateId = defaultTemplate.Id, Category = OnboardingCategory.ACCOUNT, Title = "Cấp tài khoản cổng thông tin nội bộ (Portal)", Description = "Truy cập thông tin quy chế công ty, xin nghỉ phép, chấm công.", DefaultAssignedUserId = itAssignee, DaysBeforeJoinDateOffset = 1, DisplayOrder = 2 },

            // EQUIPMENT
            new() { Id = Guid.NewGuid(), TemplateId = defaultTemplate.Id, Category = OnboardingCategory.EQUIPMENT, Title = "Cấp phát máy tính làm việc và phụ kiện", Description = "Máy tính xách tay hoặc để bàn kèm phụ kiện văn phòng.", DefaultAssignedUserId = itAssignee, DaysBeforeJoinDateOffset = 1, DisplayOrder = 3 },

            // SEATING
            new() { Id = Guid.NewGuid(), TemplateId = defaultTemplate.Id, Category = OnboardingCategory.SEATING, Title = "Bố trí bàn làm việc và ghế ngồi", Description = "Đảm bảo vị trí ngồi sạch sẽ, đủ ánh sáng và tiện nghi.", DefaultAssignedUserId = hrAssignee, DaysBeforeJoinDateOffset = 1, DisplayOrder = 4 },
            new() { Id = Guid.NewGuid(), TemplateId = defaultTemplate.Id, Category = OnboardingCategory.SEATING, Title = "Cấp thẻ từ ra vào & Welcome Kit", Description = "Thẻ mở cửa và bộ quà tặng chào đón thành viên mới.", DefaultAssignedUserId = hrAssignee, DaysBeforeJoinDateOffset = 0, DisplayOrder = 5 },

            // DOCUMENT
            new() { Id = Guid.NewGuid(), TemplateId = defaultTemplate.Id, Category = OnboardingCategory.DOCUMENT, Title = "Ký Hợp đồng lao động và cam kết bảo mật", Description = "Hoàn tất thủ tục pháp lý ngày đầu tiên tiếp nhận vị trí.", DefaultAssignedUserId = hrAssignee, DaysBeforeJoinDateOffset = 0, DisplayOrder = 6 },
            new() { Id = Guid.NewGuid(), TemplateId = defaultTemplate.Id, Category = OnboardingCategory.DOCUMENT, Title = "Tiếp nhận hồ sơ nhân sự và mở tài khoản ngân hàng", Description = "Hoàn thiện hồ sơ lưu trữ và đăng ký tài khoản trả lương.", DefaultAssignedUserId = hrAssignee, DaysBeforeJoinDateOffset = 0, DisplayOrder = 7 }
        };

        await context.OnboardingTemplates.AddAsync(defaultTemplate);

        await context.SaveChangesAsync();
    }
}

