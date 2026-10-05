using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Services;
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

        var itDept = await context.Departments.FirstOrDefaultAsync(d => d.Code == "IT") ?? await context.Departments.FirstAsync();
        var hrDept = await context.Departments.FirstOrDefaultAsync(d => d.Code == "HR") ?? await context.Departments.FirstAsync();
        var salesDept = await context.Departments.FirstOrDefaultAsync(d => d.Code == "SALES") ?? await context.Departments.FirstAsync();

        // 3. Seed JobPositions (Standard base positions)
        var initialPositions = new List<(string Code, string Title, Guid DeptId, string Level)>
        {
            ("SWE-SENIOR", "Senior Software Engineer (.NET & React)", itDept.Id, "SENIOR"),
            ("QA-MID", "Middle QA Engineer", itDept.Id, "MIDDLE"),
            ("REC-SENIOR", "Senior Recruiter", hrDept.Id, "SENIOR"),
            ("HR-MANAGER", "HR Manager", hrDept.Id, "LEAD"),
            ("AI-ENG", "AI / LLM Application Engineer", itDept.Id, "SENIOR"),
            ("PO-FINTECH", "Product Owner (Fintech & AI Platforms)", itDept.Id, "SENIOR"),
            ("DEVOPS-SR", "Senior DevOps & Cloud Platform Engineer", itDept.Id, "SENIOR"),
            ("LEAD-ARCH", "Lead Solution Architect", itDept.Id, "LEAD"),
            ("QA-LEAD", "QA / Test Automation Lead", itDept.Id, "LEAD"),
            ("MOB-SR", "Senior Mobile Engineer (Flutter & iOS)", itDept.Id, "SENIOR"),
            ("DATA-ENG", "Senior Data Engineer & Lakehouse Architect", itDept.Id, "SENIOR"),
            ("CYBER-SEC", "Cybersecurity & DevSecOps Specialist", itDept.Id, "SENIOR"),
            ("UIUX-SR", "Senior UI/UX Product Designer", itDept.Id, "SENIOR"),
            ("SALES-LEAD", "Technical Customer Success Lead", salesDept.Id, "LEAD")
        };

        foreach (var p in initialPositions)
        {
            if (!await context.JobPositions.AnyAsync(jp => jp.Code == p.Code))
            {
                await context.JobPositions.AddAsync(new JobPosition
                {
                    Id = Guid.NewGuid(),
                    Code = p.Code,
                    Title = p.Title,
                    DepartmentId = p.DeptId,
                    JobLevel = p.Level,
                    IsActive = true,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                });
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
                new() { Id = Guid.NewGuid(), Name = "Từ chối", StageOrder = 7, ColorCode = "#dc3545" }
            };
            await context.PipelineStages.AddRangeAsync(stages);
            await context.SaveChangesAsync();
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
            if (user == null)
            {
                user = new User
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

        await context.SaveChangesAsync();
    }
}
