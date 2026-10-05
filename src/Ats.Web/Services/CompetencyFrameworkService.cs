using Ats.Web.Data;
using Ats.Web.Models.ViewModels.CompetencyFrameworks;
using Ats.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Ats.Web.Services;

public class CompetencyFrameworkService(ApplicationDbContext dbContext) : ICompetencyFrameworkService
{
    private readonly ApplicationDbContext _dbContext = dbContext;

    public async Task<CompetencyFrameworkListViewModel> GetFrameworksAsync(
        string? keyword,
        string? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (page < 1)
        {
            page = 1;
        }

        if (pageSize <= 0 || pageSize > 100)
        {
            pageSize = 15;
        }

        var allPositions = await _dbContext.JobPositions
            .Include(p => p.Department)
            .AsNoTracking()
            .Where(p => !p.IsDeleted)
            .Select(p => new JobPositionAssignedViewModel
            {
                Id = p.Id,
                Code = p.Code,
                Title = p.Title,
                DepartmentName = p.Department.Name,
                JobLevel = p.JobLevel,
                IsActive = p.IsActive
            })
            .ToListAsync(cancellationToken);

        var allFrameworks = GenerateSampleFrameworks(allPositions);

        var query = allFrameworks.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim().ToLowerInvariant();
            query = query.Where(f => f.Code.ToLowerInvariant().Contains(kw) || f.Name.ToLowerInvariant().Contains(kw));
        }

        if (!string.IsNullOrWhiteSpace(status) && !status.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            if (status.Equals("ACTIVE", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(f => f.IsActive);
            }
            else if (status.Equals("INACTIVE", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(f => !f.IsActive);
            }
        }

        var filteredList = query.ToList();
        var totalRecords = filteredList.Count;

        var pagedItems = filteredList
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var totalActive = allFrameworks.Count(f => f.IsActive);
        var totalInactive = allFrameworks.Count(f => !f.IsActive);
        var totalMappedPositions = allFrameworks.Sum(f => f.AssociatedJobPositionsCount);

        return new CompetencyFrameworkListViewModel
        {
            Frameworks = pagedItems,
            Keyword = keyword,
            StatusFilter = status,
            TotalRecords = totalRecords,
            CurrentPage = page,
            PageSize = pageSize,
            TotalActiveFrameworks = totalActive,
            TotalInactiveFrameworks = totalInactive,
            TotalPositionsMapped = totalMappedPositions
        };
    }

    public async Task<CompetencyFrameworkItemViewModel?> GetFrameworkByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var allPositions = await _dbContext.JobPositions
            .Include(p => p.Department)
            .AsNoTracking()
            .Where(p => !p.IsDeleted)
            .Select(p => new JobPositionAssignedViewModel
            {
                Id = p.Id,
                Code = p.Code,
                Title = p.Title,
                DepartmentName = p.Department.Name,
                JobLevel = p.JobLevel,
                IsActive = p.IsActive
            })
            .ToListAsync(cancellationToken);

        var allFrameworks = GenerateSampleFrameworks(allPositions);
        return allFrameworks.FirstOrDefault(f => f.Id == id);
    }

    public async Task<List<JobPositionAssignedViewModel>> GetAssociatedPositionsAsync(
        Guid frameworkId,
        CancellationToken cancellationToken = default)
    {
        var framework = await GetFrameworkByIdAsync(frameworkId, cancellationToken);
        return framework?.AssociatedJobPositions ?? [];
    }

    private static List<CompetencyFrameworkItemViewModel> GenerateSampleFrameworks(List<JobPositionAssignedViewModel> existingPositions)
    {
        var frameworks = new List<CompetencyFrameworkItemViewModel>();

        var baseCategories = new[]
        {
            ("Kỹ thuật phần mềm (Software Engineering)", "TECH", new[]
            {
                "Kỹ sư Backend C# / .NET Core",
                "Kỹ sư Frontend React & TypeScript",
                "Kỹ sư Fullstack Web & Cloud",
                "Kỹ sư Lập trình Di động iOS (Swift)",
                "Kỹ sư Lập trình Di động Android (Kotlin)",
                "Kỹ sư Phần mềm Nhúng (Embedded C/C++)",
                "Kiến trúc sư Giải pháp Phần mềm (Solution Architect)",
                "Kỹ sư Lập trình Golang Microservices",
                "Kỹ sư Lập trình Java Spring Boot",
                "Kỹ sư Lập trình Python FastAPI & Async"
            }),
            ("Trí tuệ nhân tạo & Dữ liệu (AI & Data)", "AI", new[]
            {
                "Kỹ sư Học máy & MLOps (Machine Learning Engineer)",
                "Kỹ sư Dữ liệu Lớn (Big Data Engineer)",
                "Nhà Khoa học Dữ liệu (Data Scientist)",
                "Chuyên viên Phân tích Dữ liệu Kinh doanh (BI Analyst)",
                "Kỹ sư Trí tuệ Nhân tạo & LLM Application",
                "Kỹ sư Xử lý Ngôn ngữ Tự nhiên (NLP Specialist)",
                "Kỹ sư Thị giác Máy tính (Computer Vision Engineer)",
                "Kỹ sư Tối ưu hóa Vector Database & RAG"
            }),
            ("Hạ tầng, Đám mây & Vận hành (Cloud & DevOps)", "OPS", new[]
            {
                "Kỹ sư DevOps & CI/CD Pipeline Automation",
                "Kỹ sư Độ tin cậy Hệ thống (Site Reliability Engineer - SRE)",
                "Kiến trúc sư Đám mây AWS / Azure",
                "Kỹ sư Quản trị Cụm Kubernetes & Service Mesh",
                "Chuyên viên Quản trị Cơ sở dữ liệu PostgreSQL",
                "Kỹ sư Hạ tầng Mạng Doanh nghiệp & Hybrid Cloud"
            }),
            ("Bảo mật & An toàn thông tin (Security)", "SEC", new[]
            {
                "Kỹ sư An toàn Thông tin & Ứng phó Sự cố (SOC)",
                "Chuyên gia Đánh giá Lỗ hổng & Kiểm thử Xâm nhập (Pentest)",
                "Kỹ sư DevSecOps & Bảo mật Ứng dụng (AppSec)",
                "Chuyên viên Quản trị Tuân thủ Bảo mật (GRC & ISO 27001)"
            }),
            ("Quản trị Sản phẩm & Thiết kế (Product & Design)", "PROD", new[]
            {
                "Giám đốc Sản phẩm (Product Manager - Enterprise)",
                "Chủ sở hữu Sản phẩm (Product Owner - Agile)",
                "Chuyên viên Thiết kế Trải nghiệm Người dùng (Senior UI/UX)",
                "Chuyên viên Nghiên cứu Người dùng (User Researcher)",
                "Kỹ sư Thiết kế Hệ thống UI (Design System Specialist)"
            }),
            ("Đảm bảo Chất lượng Phần mềm (QA & Testing)", "QA", new[]
            {
                "Kỹ sư Kiểm thử Tự động (Automation QA Engineer)",
                "Chuyên viên Kiểm thử Chức năng (Manual QA Specialist)",
                "Kỹ sư Kiểm thử Hiệu năng & Tải (Performance QA)",
                "Trưởng nhóm Đảm bảo Chất lượng Phần mềm (QA Lead)"
            }),
            ("Quản lý, Lãnh đạo & Điều hành (Management)", "MGT", new[]
            {
                "Trưởng nhóm Kỹ thuật (Engineering Lead)",
                "Quản lý Kỹ thuật Phần mềm (Engineering Manager)",
                "Chuyên gia Điều phối Dự án Agile (Scrum Master)",
                "Giám đốc Khối Công nghệ (Head of Technology)",
                "Quản lý Chương trình Kỹ thuật (Technical Program Manager)"
            }),
            ("Nhân sự, Tuyển dụng & Vận hành (HR & Operations)", "CORP", new[]
            {
                "Trưởng phòng Tuyển dụng Tài năng Công nghệ (Lead IT Recruiter)",
                "Chuyên viên Quản trị Nhân sự & Đãi ngộ (HR & C&B Specialist)",
                "Chuyên viên Đào tạo & Phát triển Năng lực (L&D Specialist)",
                "Chuyên viên Vận hành Hệ thống ATS & Tuyển dụng Nội bộ"
            })
        };

        var seedIndex = 1;
        var fixedDate = new DateTimeOffset(2026, 9, 1, 8, 30, 0, TimeSpan.Zero);

        foreach (var (catName, catPrefix, titles) in baseCategories)
        {
            foreach (var title in titles)
            {
                var frameworkCode = $"CF-{catPrefix}-{seedIndex:D3}";
                var frameworkId = new Guid($"00000000-0000-0000-0002-{seedIndex:D12}");

                var matchedPositions = new List<JobPositionAssignedViewModel>();

                if (existingPositions.Count > 0)
                {
                    if (catPrefix == "TECH")
                    {
                        matchedPositions.AddRange(existingPositions.Where(p => 
                            p.Code.Contains("DEV", StringComparison.OrdinalIgnoreCase) || 
                            p.Code.Contains("SWE", StringComparison.OrdinalIgnoreCase) ||
                            p.Title.Contains("Lập trình", StringComparison.OrdinalIgnoreCase) ||
                            p.Title.Contains("Frontend", StringComparison.OrdinalIgnoreCase) ||
                            p.Title.Contains("Backend", StringComparison.OrdinalIgnoreCase)));
                    }
                    else if (catPrefix == "AI")
                    {
                        matchedPositions.AddRange(existingPositions.Where(p => 
                            p.Code.Contains("AI", StringComparison.OrdinalIgnoreCase) || 
                            p.Code.Contains("DATA", StringComparison.OrdinalIgnoreCase) ||
                            p.Title.Contains("AI", StringComparison.OrdinalIgnoreCase) ||
                            p.Title.Contains("Dữ liệu", StringComparison.OrdinalIgnoreCase)));
                    }
                    else if (catPrefix == "OPS")
                    {
                        matchedPositions.AddRange(existingPositions.Where(p => 
                            p.Code.Contains("DEVOPS", StringComparison.OrdinalIgnoreCase) || 
                            p.Code.Contains("SRE", StringComparison.OrdinalIgnoreCase) ||
                            p.Title.Contains("DevOps", StringComparison.OrdinalIgnoreCase)));
                    }
                    else if (catPrefix == "CORP")
                    {
                        matchedPositions.AddRange(existingPositions.Where(p => 
                            p.Code.Contains("HR", StringComparison.OrdinalIgnoreCase) || 
                            p.Title.Contains("Nhân sự", StringComparison.OrdinalIgnoreCase)));
                    }
                    else if (catPrefix == "MGT")
                    {
                        matchedPositions.AddRange(existingPositions.Where(p => 
                            p.JobLevel == "LEAD" || p.JobLevel == "MANAGER"));
                    }
                }

                if (matchedPositions.Count == 0 && (seedIndex % 3 == 0 || seedIndex % 5 == 0))
                {
                    matchedPositions.Add(new JobPositionAssignedViewModel
                    {
                        Id = Guid.NewGuid(),
                        Code = $"{catPrefix}-POS-01",
                        Title = $"Vị trí {title} Tiêu chuẩn",
                        DepartmentName = "Khối Công nghệ & Sản phẩm",
                        JobLevel = "MIDDLE",
                        IsActive = true
                    });
                    if (seedIndex % 5 == 0)
                    {
                        matchedPositions.Add(new JobPositionAssignedViewModel
                        {
                            Id = Guid.NewGuid(),
                            Code = $"{catPrefix}-POS-02",
                            Title = $"Vị trí {title} Cấp cao",
                            DepartmentName = "Khối Công nghệ & Sản phẩm",
                            JobLevel = "SENIOR",
                            IsActive = true
                        });
                    }
                }

                frameworks.Add(new CompetencyFrameworkItemViewModel
                {
                    Id = frameworkId,
                    Code = frameworkCode,
                    Name = $"Khung năng lực {title}",
                    Category = catName,
                    Description = $"Bộ tiêu chuẩn năng lực chuyên môn, kỹ năng cốt lõi và tiêu chí đánh giá định ngạch cho {title} tại NoveraTech.",
                    IsActive = seedIndex % 9 != 0,
                    CompetenciesCount = 6 + (seedIndex % 10),
                    AssociatedJobPositions = matchedPositions.DistinctBy(p => p.Id).ToList(),
                    UpdatedAt = fixedDate.AddDays(seedIndex)
                });

                seedIndex++;
            }
        }

        while (frameworks.Count < 215)
        {
            var num = frameworks.Count + 1;
            var subCat = baseCategories[num % baseCategories.Length];
            var fCode = $"CF-SPEC-{num:D3}";
            var fId = new Guid($"00000000-0000-0000-0003-{num:D12}");

            var samplePos = new List<JobPositionAssignedViewModel>();
            if (num % 2 == 0)
            {
                samplePos.Add(new JobPositionAssignedViewModel
                {
                    Id = Guid.NewGuid(),
                    Code = $"POS-EXT-{num:D3}",
                    Title = $"Chuyên viên {subCat.Item3[num % subCat.Item3.Length]} Bậc {(num % 4) + 1}",
                    DepartmentName = "Phòng Phát triển Công nghệ",
                    JobLevel = num % 3 == 0 ? "SENIOR" : "MIDDLE",
                    IsActive = true
                });
            }

            frameworks.Add(new CompetencyFrameworkItemViewModel
            {
                Id = fId,
                Code = fCode,
                Name = $"Khung năng lực chuyên sâu {subCat.Item3[num % subCat.Item3.Length]} (Phân hệ {num})",
                Category = subCat.Item1,
                Description = $"Bộ khung tiêu chí thẩm định năng lực đặc thù cho kỹ sư phụ trách phân hệ {num}.",
                IsActive = num % 11 != 0,
                CompetenciesCount = 5 + (num % 8),
                AssociatedJobPositions = samplePos,
                UpdatedAt = fixedDate.AddHours(num * 6)
            });
        }

        return frameworks;
    }
}
