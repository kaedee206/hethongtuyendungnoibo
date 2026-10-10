using Ats.Web.Data;
using Ats.Web.Models.ViewModels.Jobs;
using Ats.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Ats.Web.Services;

public class JobService : IJobService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<JobService> _logger;

    public JobService(ApplicationDbContext dbContext, ILogger<JobService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<JobListViewModel> GetJobListAsync(string? search = null, string? department = null, string? location = null, string? employmentType = null, int page = 1, int pageSize = 9)
    {
        if (page < 1) page = 1;
        if (pageSize <= 0) pageSize = 9;

        var allJobs = await GetAllJobsInternalAsync();

        var query = allJobs.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(search) && search.Trim().Length >= 3)
        {
            var s = search.Trim().ToLower();
            query = query.Where(j => 
                j.Title.ToLower().Contains(s) ||
                j.ShortSummary.ToLower().Contains(s) ||
                j.Overview.ToLower().Contains(s) ||
                j.PositionDescription.ToLower().Contains(s) ||
                j.TechStack.Any(t => t.ToLower().Contains(s)) ||
                j.Requirements.Any(r => r.ToLower().Contains(s)) ||
                j.Department.ToLower().Contains(s));
        }

        if (!string.IsNullOrWhiteSpace(department) && department != "all")
        {
            var d = department.Trim().ToLower();
            query = query.Where(j => j.DepartmentCategory.ToLower() == d || j.Department.ToLower().Contains(d));
        }

        if (!string.IsNullOrWhiteSpace(location) && location != "all")
        {
            var loc = location.Trim().ToLower();
            query = query.Where(j => j.WorkLocation.ToLower().Contains(loc));
        }

        if (!string.IsNullOrWhiteSpace(employmentType) && employmentType != "all")
        {
            var et = employmentType.Trim().ToLower();
            query = query.Where(j => j.EmploymentType.ToLower().Contains(et));
        }

        var filteredList = query.ToList();
        var totalRecords = filteredList.Count;
        var pagedList = filteredList.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return new JobListViewModel
        {
            Jobs = pagedList,
            AllJobs = allJobs,
            SearchKeyword = search,
            SelectedDepartment = department,
            SelectedLocation = location,
            SelectedEmploymentType = employmentType,
            CurrentPage = page,
            PageSize = pageSize,
            TotalRecords = totalRecords
        };
    }

    public async Task<List<JobItemViewModel>> GetHotJobsAsync(int count = 6)
    {
        var allJobs = await GetAllJobsInternalAsync();
        return allJobs.Where(j => j.IsHot).Take(count).ToList();
    }

    public async Task<JobDetailViewModel?> GetJobDetailAsync(string idOrSlug)
    {
        if (string.IsNullOrWhiteSpace(idOrSlug)) return null;

        var allJobs = await GetAllJobsInternalAsync();
        var normalized = idOrSlug.Trim().ToLower();

        var job = allJobs.FirstOrDefault(j => 
            j.Id.Equals(normalized, StringComparison.OrdinalIgnoreCase) || 
            j.Slug.Equals(normalized, StringComparison.OrdinalIgnoreCase));

        if (job == null) return null;

        var related = allJobs
            .Where(j => j.Id != job.Id && (j.DepartmentCategory == job.DepartmentCategory || j.IsHot))
            .Take(3)
            .ToList();

        return new JobDetailViewModel
        {
            Job = job,
            RelatedJobs = related
        };
    }

    public async Task<int> GetTotalActiveJobsCountAsync()
    {
        var allJobs = await GetAllJobsInternalAsync();
        return allJobs.Count;
    }

    private async Task<List<JobItemViewModel>> GetAllJobsInternalAsync()
    {
        // 1. Kiểm tra xem trong DB đã có JobPostings publish chưa
        try
        {
            var dbJobs = await _dbContext.JobPostings
                .Include(j => j.Requisition)
                    .ThenInclude(r => r.Department)
                .Include(j => j.Requisition)
                    .ThenInclude(r => r.JobPosition)
                .Where(j => j.Status == Models.Enums.JobPostingStatus.PUBLISHED 
                         && (j.ExpiredAt == null || j.ExpiredAt > DateTimeOffset.UtcNow))
                .ToListAsync();

            if (dbJobs.Any())
            {
                var mapped = dbJobs.Select(j => MapEntityToViewModel(j)).ToList();
                return mapped;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không thể đọc JobPostings từ database, sử dụng danh mục tiêu chuẩn của NoveraTech.");
        }

        // 2. Fallback: Danh sách 12 vị trí việc làm chuyên sâu của NoveraTech
        var standardJobs = GetStandardNoveraTechJobs();
        foreach (var sj in standardJobs)
        {
            if (string.IsNullOrWhiteSpace(sj.PositionDescription))
            {
                sj.PositionDescription = $"Vị trí {sj.Title} thuộc {sj.Department}, đảm nhiệm vai trò chủ chốt trong việc hoạch định giải pháp kỹ thuật, triển khai công nghệ lõi và tối ưu hóa quy trình nghiệp vụ theo tiêu chuẩn quốc tế.";
            }
        }
        return standardJobs;
    }

    private JobItemViewModel MapEntityToViewModel(Models.Entities.JobPosting entity)
    {
        var deptName = entity.Requisition?.Department?.Name ?? "Công nghệ Thông tin (IT)";
        var level = entity.Requisition?.JobPosition?.JobLevel ?? "SENIOR";
        var isHot = entity.SalaryDisplay?.Contains("40") == true || entity.SalaryDisplay?.Contains("60") == true;

        var category = "it";
        if (deptName.Contains("AI", StringComparison.OrdinalIgnoreCase)) category = "ai";
        else if (deptName.Contains("Kinh doanh", StringComparison.OrdinalIgnoreCase) || deptName.Contains("Sales", StringComparison.OrdinalIgnoreCase)) category = "other";
        else if (deptName.Contains("Nhân sự", StringComparison.OrdinalIgnoreCase) || deptName.Contains("HR", StringComparison.OrdinalIgnoreCase)) category = "hr";

        var rawReqs = entity.Requirements ?? "";
        var techList = new List<string>();
        if (rawReqs.Contains("[TECHSTACK]") && rawReqs.Contains("[/TECHSTACK]"))
        {
            var start = rawReqs.IndexOf("[TECHSTACK]") + "[TECHSTACK]".Length;
            var end = rawReqs.IndexOf("[/TECHSTACK]");
            var techStr = rawReqs.Substring(start, end - start);
            techList = techStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            rawReqs = rawReqs.Substring(end + "[/TECHSTACK]".Length).Trim();
        }
        if (!techList.Any())
        {
            techList = new List<string> { ".NET 9", "Clean Architecture", "PostgreSQL", "React" };
        }

        var respList = new List<string>();
        if (rawReqs.Contains("[RESPONSIBILITIES]") && rawReqs.Contains("[/RESPONSIBILITIES]"))
        {
            var start = rawReqs.IndexOf("[RESPONSIBILITIES]") + "[RESPONSIBILITIES]".Length;
            var end = rawReqs.IndexOf("[/RESPONSIBILITIES]");
            var respStr = rawReqs.Substring(start, end - start);
            respList = respStr.Split(new[] { '\n', '\r', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            rawReqs = rawReqs.Substring(end + "[/RESPONSIBILITIES]".Length).Trim();
        }
        else
        {
            respList = rawReqs.Split(new[] { '\n', '\r', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }

        var reqList = rawReqs.Split(new[] { '\n', '\r', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        var positionDesc = entity.Requisition?.JobPosition?.Description;
        var reqJobDesc = entity.Requisition?.JobDescription;
        var postJobDesc = entity.JobDescription;

        var resolvedPosDesc = !string.IsNullOrWhiteSpace(positionDesc) 
            ? positionDesc 
            : (!string.IsNullOrWhiteSpace(reqJobDesc) ? reqJobDesc : (postJobDesc ?? ""));

        var resolvedOverview = !string.IsNullOrWhiteSpace(postJobDesc) 
            ? postJobDesc 
            : (!string.IsNullOrWhiteSpace(resolvedPosDesc) ? resolvedPosDesc : "NoveraTech tuyển dụng nhân tài cùng phát triển các giải pháp công nghệ cao.");

        return new JobItemViewModel
        {
            Id = entity.Id.ToString(),
            Title = entity.Title,
            Slug = string.IsNullOrWhiteSpace(entity.Slug) ? entity.Id.ToString() : entity.Slug,
            Department = deptName,
            DepartmentCategory = category,
            WorkLocation = string.IsNullOrWhiteSpace(entity.WorkLocation) ? "Hà Nội (Hybrid 2 ngày WFH)" : entity.WorkLocation,
            EmploymentType = "Toàn thời gian (Full-time)",
            ExperienceLevel = level,
            SalaryDisplay = entity.SalaryDisplay ?? "Thương lượng theo năng lực",
            IsHot = isHot,
            ShortSummary = resolvedOverview.Length > 160 ? resolvedOverview[..160] + "..." : resolvedOverview,
            Overview = resolvedOverview,
            PositionDescription = resolvedPosDesc,
            Responsibilities = respList.Any() ? respList : new List<string> { "Tham gia phát triển các tính năng phần mềm theo yêu cầu kiến trúc.", "Phối hợp cùng đội ngũ kỹ sư và kiểm thử chất lượng sản phẩm." },
            Requirements = reqList.Any() ? reqList : new List<string> { "Có kinh nghiệm thực chiến trong lĩnh vực tương đương.", "Tư duy giải quyết vấn đề tốt, cầu tiến." },
            Benefits = entity.Benefits?.Split(new[] { '\n', '\r', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList() ?? GetDefaultNoveraTechBenefits(),
            TechStack = techList,
            PostedDate = entity.PublishedAt?.DateTime ?? DateTime.UtcNow.AddDays(-5),
            Deadline = entity.ExpiredAt?.DateTime ?? DateTime.UtcNow.AddDays(25)
        };
    }

    private static List<string> GetDefaultNoveraTechBenefits()
    {
        return new List<string>
        {
            "Mức thu nhập cạnh tranh Top 10% thị trường công nghệ, thưởng dự án định kỳ, 14 – 16 tháng lương/năm.",
            "Review đánh giá hiệu suất và điều chỉnh lương 2 lần/năm (Tháng 3 & Tháng 9) minh bạch.",
            "Trang bị 100% máy tính Apple MacBook Pro (chip Apple Silicon M3/M4 Max) kèm 2 màn hình 4K công thái học.",
            "Gói bảo hiểm sức khỏe cao cấp NoveraCare VIP (hạn mức nội trú & ngoại trú bảo lãnh viện quốc tế Vinmec, Việt Pháp) cho bạn và người thân.",
            "Chính sách làm việc linh hoạt (Hybrid working 2 ngày/tuần), giờ giấc linh hoạt, tôn trọng đời sống cá nhân, tuyệt đối không văn hóa OT.",
            "Ngân sách tài trợ phát triển kỹ năng & thi chứng chỉ quốc tế (AWS, Azure, TOGAF, CMMI, PMP) lên tới 25.000.000 VNĐ/năm.",
            "Du lịch nghỉ dưỡng hàng năm chuẩn resort 5 sao, teambuilding sôi động, tiệc Year End Party và các hoạt động thể thao (Gym, Bơi, Yoga, Bóng đá tài trợ 100%)."
        };
    }

    public static List<JobItemViewModel> GetStandardNoveraTechJobs()
    {
        return new List<JobItemViewModel>
        {
            // 1. Senior Fullstack .NET / React Engineer
            new()
            {
                Id = "job-001",
                Slug = "senior-fullstack-dotnet-react-engineer",
                Title = "Senior Fullstack .NET 9 & React Engineer",
                Department = "Khối Kỹ thuật Phần mềm (Software Engineering)",
                DepartmentCategory = "it",
                WorkLocation = "Hà Nội (Hybrid 2 ngày WFH)",
                EmploymentType = "Toàn thời gian (Full-time)",
                ExperienceLevel = "Senior (3 – 5+ năm kinh nghiệm)",
                SalaryDisplay = "35 – 50 Triệu VNĐ",
                IsHot = true,
                ShortSummary = "Chịu trách nhiệm thiết kế kiến trúc, phát triển các dịch vụ microservices chịu tải cao với .NET 9, tối ưu truy vấn PostgreSQL và xây dựng giao diện React 19 / TypeScript hiện đại.",
                Overview = "NoveraTech đang mở rộng nền tảng công nghệ lõi phục vụ hàng triệu người dùng doanh nghiệp. Bạn sẽ làm việc cùng các Principal Engineers để xây dựng các giải pháp phần mềm cấp doanh nghiệp, xử lý lượng giao dịch lớn với độ tin cậy 99.99%.",
                Responsibilities = new()
                {
                    "Tham gia thiết kế kiến trúc hệ thống, phát triển các dịch vụ cốt lõi bằng .NET 9 (C#) theo mô hình Clean Architecture & Domain-Driven Design (DDD).",
                    "Xây dựng giao diện Single Page Application (SPA) trực quan, chuẩn responsive và hiệu năng cao bằng React 19, TypeScript, TanStack Query và Tailwind/Vanilla CSS.",
                    "Thiết kế RESTful APIs và gRPC endpoints chuẩn OpenAPI/Swagger; tối ưu hóa truy vấn CSDL PostgreSQL, caching Redis và message broker RabbitMQ/Kafka.",
                    "Viết unit tests, integration tests đạt độ bao phủ tối thiểu 80% (xUnit, Moq, FluentAssertions, Playwright).",
                    "Tham gia review code chuyên sâu, cố vấn kỹ thuật (mentoring) cho các kỹ sư Middle/Junior trong nhóm.",
                    "Phối hợp chặt chẽ cùng Product Owner và DevOps team để triển khai hệ thống an toàn qua CI/CD pipeline lên cụm Kubernetes."
                },
                Requirements = new()
                {
                    "Tối thiểu 3 năm kinh nghiệm lập trình thực chiến với hệ sinh thái .NET (.NET Core 6/7/8/9, ASP.NET Core Web API, Entity Framework Core).",
                    "Thành thạo ReactJS (từ phiên bản 18+), TypeScript, hiểu sâu về component lifecycle, React Hooks, custom hooks và state management.",
                    "Nắm vững nguyên lý Clean Architecture, SOLID, Design Patterns, RESTful API và mô hình bảo mật OWASP Top 10.",
                    "Kinh nghiệm làm việc sâu với cơ sở dữ liệu quan hệ (PostgreSQL, SQL Server): phân tích Execution Plan, thiết kế Index và tối ưu hóa câu lệnh SQL phức tạp.",
                    "Quen thuộc với Git, Docker containerization và quy trình Agile/Scrum.",
                    "Tư duy giải quyết vấn đề tốt, chủ động đề xuất giải pháp kỹ thuật và có tinh thần trách nhiệm cao đối với sản phẩm."
                },
                Benefits = GetDefaultNoveraTechBenefits(),
                TechStack = new() { ".NET 9", "C#", "React 19", "TypeScript", "PostgreSQL", "Clean Architecture", "Redis", "Docker" },
                PostedDate = DateTime.UtcNow.AddDays(-2),
                Deadline = DateTime.UtcNow.AddDays(28)
            },

            // 2. AI / LLM Application Engineer
            new()
            {
                Id = "job-002",
                Slug = "ai-llm-application-engineer",
                Title = "AI / LLM Application Engineer (RAG & Agentic Workflows)",
                Department = "Trung tâm Nghiên cứu AI & Dữ liệu (AI & Data Lab)",
                DepartmentCategory = "ai",
                WorkLocation = "Hà Nội / TP. Hồ Chí Minh",
                EmploymentType = "Toàn thời gian (Full-time)",
                ExperienceLevel = "Middle – Senior (2 – 5 năm)",
                SalaryDisplay = "40 – 65 Triệu VNĐ",
                IsHot = true,
                ShortSummary = "Nghiên cứu, thiết kế và triển khai các giải pháp AI thế hệ mới (GenAI), kiến trúc RAG nâng cao, Vector Search và Agentic Workflows tự động hóa các bài toán nghiệp vụ phức tạp.",
                Overview = "AI & Data Lab tại NoveraTech là mũi nhọn đổi mới sáng tạo, phát triển các trợ lý ảo AI thông minh, tự động hóa quy trình phân tích và khai phá tri thức từ kho dữ liệu phi cấu trúc khổng lồ của khách hàng doanh nghiệp.",
                Responsibilities = new()
                {
                    "Thiết kế và xây dựng các pipeline Retrieval-Augmented Generation (RAG) tiên tiến kết hợp Hybrid Search (Dense & Sparse retrieval, re-ranking).",
                    "Phát triển hệ thống Multi-Agent và Agentic Workflows (sử dụng LangChain, LangGraph, LlamaIndex hoặc Semantic Kernel) xử lý các tác vụ đa bước tự động.",
                    "Tối ưu hóa chi phí token, độ trễ và độ chính xác của các mô hình LLM lớn (Claude 3.5, GPT-4o, Gemini 1.5, DeepSeek, open-source models Ollama/vLLM).",
                    "Xây dựng hệ thống Vector Database (Qdrant, pgvector, Milvus) có khả năng scale hàng triệu vector embeddings với độ trễ phản hồi dưới 50ms.",
                    "Thiết lập bộ khung đánh giá (LLM Evaluation Framework: RAGAS, TruLens) để đo lường độ ảo giác (hallucination) và độ hữu ích câu trả lời.",
                    "Tích hợp các dịch vụ AI vào backend API hiện có qua REST/gRPC an toàn."
                },
                Requirements = new()
                {
                    "Từ 2 năm kinh nghiệm thực chiến phát triển ứng dụng GenAI / LLMs hoặc Machine Learning / NLP.",
                    "Thành thạo Python (FastAPI, Pydantic, asyncio) hoặc C# (.NET 8/9 kết hợp Semantic Kernel).",
                    "Hiểu sâu về Prompt Engineering kỹ thuật cao (CoT, ReAct, Self-Consistency, Few-shot) và fine-tuning mô hình (LoRA, QLoRA) là điểm cộng lớn.",
                    "Kinh nghiệm thực tiễn với Vector Databases: pgvector (PostgreSQL), Qdrant, Chroma hoặc Pinecone.",
                    "Có kinh nghiệm xử lý tài liệu đa định dạng (PDF, DOCX, OCR, chunking strategies thông minh).",
                    "Đam mê cập nhật liên tục các nghiên cứu và mô hình AI mới nhất trên thế giới."
                },
                Benefits = GetDefaultNoveraTechBenefits(),
                TechStack = new() { "Python", "LangChain", "LangGraph", "pgvector", "FastAPI", "Docker", "LLM Evaluation" },
                PostedDate = DateTime.UtcNow.AddDays(-3),
                Deadline = DateTime.UtcNow.AddDays(27)
            },

            // 3. Senior DevOps & Cloud Platform Engineer
            new()
            {
                Id = "job-003",
                Slug = "senior-devops-cloud-platform-engineer",
                Title = "Senior DevOps & Cloud Platform Engineer",
                Department = "Hạ tầng & Nền tảng Đám mây (Cloud & Infrastructure)",
                DepartmentCategory = "cloud",
                WorkLocation = "Hà Nội (Hybrid 2 ngày WFH)",
                EmploymentType = "Toàn thời gian (Full-time)",
                ExperienceLevel = "Senior (3 – 6 năm kinh nghiệm)",
                SalaryDisplay = "38 – 55 Triệu VNĐ",
                IsHot = true,
                ShortSummary = "Quản trị cụm Kubernetes sản xuất, thiết kế CI/CD pipeline tự động hóa chuẩn GitOps, tự động hóa hạ tầng bằng Terraform và đảm bảo tính sẵn sàng 99.99%.",
                Overview = "Khối Hạ tầng NoveraTech vận hành hệ sinh thái đám mây lai (Hybrid Cloud) phục vụ các hệ thống tài chính và quản trị doanh nghiệp đòi hỏi độ bảo mật, tính liên tục kinh doanh và khả năng phục hồi thảm họa cao.",
                Responsibilities = new()
                {
                    "Thiết kế, xây dựng và tối ưu hóa hạ tầng Kubernetes (EKS / GKE / On-prem K8s) chịu tải lớn, áp dụng autoscaling HPA và VPA.",
                    "Xây dựng và duy trì hệ thống CI/CD đa giai đoạn chuẩn GitOps (GitHub Actions, GitLab CI, ArgoCD) với kiểm thử tự động và security scanning.",
                    "Quản lý toàn bộ tài nguyên đám mây dưới dạng mã nguồn (Infrastructure as Code - IaC) bằng Terraform và Ansible.",
                    "Thiết lập hệ thống quan sát toàn diện (Observability: Prometheus, Grafana, OpenTelemetry, Loki, Jaeger) với cảnh báo tự động qua Slack/Telegram.",
                    "Tối ưu hóa chi phí hạ tầng Cloud (FinOps), phân bổ tài nguyên hợp lý và giảm thiểu lãng phí compute/storage.",
                    "Xây dựng kế hoạch sao lưu định kỳ, phòng ngừa rủi ro và diễn tập khôi phục thảm họa (Disaster Recovery)."
                },
                Requirements = new()
                {
                    "Tối thiểu 3 năm kinh nghiệm chuyên sâu ở vị trí DevOps / SRE / Cloud Platform Engineer.",
                    "Thành thạo Kubernetes (networking, storage class, ingress controller, security context) và Docker container.",
                    "Kinh nghiệm thực tiễn với Terraform (modules, state management, remote backend) trên AWS hoặc GCP.",
                    "Làm chủ GitOps workflows (ArgoCD hoặc FluxCD) và hệ thống CI/CD hiện đại.",
                    "Kỹ năng lập trình kịch bản tự động hóa vững chắc (Bash shell, Python hoặc Go).",
                    "Ưu tiên ứng viên sở hữu chứng chỉ chuyên nghiệp CKA, CKAD hoặc AWS Solutions Architect / DevOps Professional."
                },
                Benefits = GetDefaultNoveraTechBenefits(),
                TechStack = new() { "Kubernetes", "Docker", "Terraform", "ArgoCD", "AWS", "Prometheus", "Grafana", "Bash/Python" },
                PostedDate = DateTime.UtcNow.AddDays(-1),
                Deadline = DateTime.UtcNow.AddDays(30)
            },

            // 4. Lead Solution Architect
            new()
            {
                Id = "job-004",
                Slug = "lead-solution-architect",
                Title = "Lead Solution Architect (Enterprise & High Scalability)",
                Department = "Ban Kiến trúc Công nghệ (Architecture Board)",
                DepartmentCategory = "it",
                WorkLocation = "Hà Nội / TP. Hồ Chí Minh",
                EmploymentType = "Toàn thời gian (Full-time)",
                ExperienceLevel = "Lead / Principal (7+ năm kinh nghiệm)",
                SalaryDisplay = "60 – 85 Triệu VNĐ",
                IsHot = true,
                ShortSummary = "Định hình kiến trúc kỹ thuật toàn diện cho các dòng sản phẩm chiến lược của NoveraTech, làm việc trực tiếp với CEO & CTO để đưa ra quyết định công nghệ dài hạn.",
                Overview = "Bạn sẽ đóng vai trò nhạc trưởng công nghệ tại NoveraTech, dẫn dắt các quyết định kiến trúc cốt lõi, bảo đảm tính mở rộng, bảo mật, và khả năng tích hợp linh hoạt cho hệ sinh thái phần mềm doanh nghiệp thế hệ mới.",
                Responsibilities = new()
                {
                    "Chủ trì thiết kế kiến trúc giải pháp tổng thể (High-level & Low-level Design) cho các sản phẩm lớn của NoveraTech.",
                    "Đánh giá và lựa chọn công nghệ mới (Tech Stack selection), xây dựng các chuẩn kiến trúc (ADR - Architecture Decision Records) cho toàn bộ kỹ sư.",
                    "Giải quyết các nút thắt cổ chai về hiệu năng (Bottlenecks), kiến trúc chịu tải hàng trăm nghìn RPS và dữ liệu hàng chục Terabytes.",
                    "Đảm bảo an toàn thông tin và tuân thủ các tiêu chuẩn bảo mật quốc tế (ISO 27001, OWASP, PCI-DSS).",
                    "Đồng hành cùng Ban Giám đốc và các Tech Lead trong việc tư vấn giải pháp kỹ thuật cho các đối tác chiến lược lớn.",
                    "Dẫn dắt các buổi chia sẻ công nghệ (Tech Talks), đào tạo nâng tầm năng lực kiến trúc cho đội ngũ kỹ sư NoveraTech."
                },
                Requirements = new()
                {
                    "Từ 7 năm kinh nghiệm phát triển phần mềm, trong đó có ít nhất 3 năm giữ vị trí Solution Architect hoặc Principal Software Engineer.",
                    "Hiểu sâu sắc về Distributed Systems, Microservices, Event-Driven Architecture (Kafka/RabbitMQ), CQRS và Data Consistency trong môi trường phân tán.",
                    "Nền tảng vững vàng về cả backend (.NET, Java hoặc Go), frontend hiện đại, và cơ sở dữ liệu (PostgreSQL, NoSQL, In-memory DB).",
                    "Khả năng giao tiếp xuất sắc, truyền đạt mạch lạc các khái niệm kỹ thuật phức tạp cho cả đối tượng kinh doanh và đội ngũ kỹ thuật.",
                    "Kinh nghiệm làm việc trong các hệ thống quy mô lớn (Large-Scale Enterprise, Fintech, Ecommerce).",
                    "Ưu tiên ứng viên có chứng chỉ TOGAF, AWS/GCP Certified Professional Architect."
                },
                Benefits = GetDefaultNoveraTechBenefits(),
                TechStack = new() { "Microservices", "Event-Driven", "Kafka", ".NET 9", "PostgreSQL", "Cloud Architecture", "TOGAF" },
                PostedDate = DateTime.UtcNow.AddDays(-4),
                Deadline = DateTime.UtcNow.AddDays(25)
            },

            // 5. Product Owner (Fintech & AI Platforms)
            new()
            {
                Id = "job-005",
                Slug = "product-owner-fintech-ai-platforms",
                Title = "Product Owner (Fintech & Enterprise AI Platforms)",
                Department = "Khối Quản trị Sản phẩm (Product Management)",
                DepartmentCategory = "product",
                WorkLocation = "Hà Nội",
                EmploymentType = "Toàn thời gian (Full-time)",
                ExperienceLevel = "Middle – Senior (3 – 5 năm)",
                SalaryDisplay = "40 – 55 Triệu VNĐ",
                IsHot = true,
                ShortSummary = "Định hình lộ trình sản phẩm (Product Roadmap), quản lý backlog, làm việc cùng Tech Lead và Ban Giám Đốc để biến nhu cầu khách hàng thành các tính năng công nghệ đột phá.",
                Overview = "Sản phẩm của NoveraTech tập trung vào tự động hóa vận hành thông minh và số hóa quy trình doanh nghiệp. Product Owner là cầu nối quyết định giữa thị trường và đội ngũ kỹ thuật xuất sắc của công ty.",
                Responsibilities = new()
                {
                    "Xây dựng tầm nhìn sản phẩm (Product Vision), định nghĩa Product Roadmap theo từng quý và đo lường các chỉ số thành công (OKRs/KPIs).",
                    "Phân tích nhu cầu nghiệp vụ, viết tài liệu đặc tả yêu cầu chi tiết (PRD), User Stories kèm tiêu chí nghiệm thu rõ ràng (Acceptance Criteria).",
                    "Quản lý, ưu tiên hóa Product Backlog theo giá trị kinh doanh và tính khả thi công nghệ.",
                    "Phối hợp chặt chẽ với Scrum Master, Tech Lead, UI/UX Designer và QA trong các phiên Sprint Planning, Daily Standup và Sprint Review.",
                    "Tiến hành nghiên cứu người dùng (User Research), phân tích dữ liệu hành vi (Product Analytics) để liên tục cải tiến trải nghiệm người dùng.",
                    "Tổ chức đào tạo nội bộ và bàn giao tính năng mới cho bộ phận Customer Success và Sales."
                },
                Requirements = new()
                {
                    "Tối thiểu 3 năm kinh nghiệm làm Product Owner hoặc Business Analyst tại các công ty sản phẩm công nghệ (Ưu tiên B2B SaaS, FinTech, HRTech).",
                    "Nắm vững phương pháp luận Agile/Scrum, có chứng chỉ CSPO hoặc PSPO là lợi thế lớn.",
                    "Tư duy dữ liệu sắc bén (Data-driven mindset), thành thạo các công cụ phân tích (Mixpanel, Google Analytics, Metabase).",
                    "Khả năng thấu hiểu kỹ thuật (Technical fluency) để làm việc ăn ý với các kỹ sư phần mềm.",
                    "Kỹ năng đàm phán, giao tiếp và quản lý kỳ vọng của nhiều bên liên quan (Stakeholder Management) xuất sắc."
                },
                Benefits = GetDefaultNoveraTechBenefits(),
                TechStack = new() { "Agile/Scrum", "Jira/Confluence", "PRD/User Stories", "Figma", "Data Analytics", "B2B SaaS" },
                PostedDate = DateTime.UtcNow.AddDays(-5),
                Deadline = DateTime.UtcNow.AddDays(26)
            },

            // 6. QA / Test Automation Lead
            new()
            {
                Id = "job-006",
                Slug = "qa-test-automation-lead",
                Title = "QA / Test Automation Lead (Playwright & API Framework)",
                Department = "Khối Đảm bảo Chất lượng (Quality Assurance)",
                DepartmentCategory = "qa",
                WorkLocation = "Hà Nội",
                EmploymentType = "Toàn thời gian (Full-time)",
                ExperienceLevel = "Lead (4 – 6+ năm kinh nghiệm)",
                SalaryDisplay = "30 – 45 Triệu VNĐ",
                IsHot = true,
                ShortSummary = "Xây dựng automation framework từ đầu cho Web và API microservices, thực hiện kiểm thử tải k6, tích hợp CI/CD và dẫn dắt đội ngũ QA NoveraTech.",
                Overview = "Chất lượng là danh dự của kỹ sư NoveraTech. QA Lead sẽ chịu trách nhiệm thiết lập văn hóa 'Zero Defect' trong môi trường triển khai liên tục (CI/CD) của công ty.",
                Responsibilities = new()
                {
                    "Xây dựng và chuẩn hóa Automation Testing Framework cho cả tầng UI (Playwright, Cypress) và tầng API (RestAssured, Postman, C# xUnit).",
                    "Tích hợp các bài test tự động vào quy trình CI/CD (GitHub Actions / GitLab CI) để đảm bảo chất lượng trước khi merge code vào production.",
                    "Lên kịch bản và thực hiện kiểm thử tải, hiệu năng hệ thống (Performance & Load Testing) bằng k6 hoặc JMeter.",
                    "Thiết lập chiến lược kiểm thử tổng thể (Test Strategy), quản lý rủi ro và theo dõi chỉ số lỗi (Bug metrics).",
                    "Cố vấn, đào tạo các thành viên QA trong nhóm chuyển dịch từ manual testing sang automation testing chuyên nghiệp.",
                    "Tham gia phân tích yêu cầu từ sớm (Shift-Left Testing) để phát hiện sai sót ngay từ giai đoạn thiết kế tính năng."
                },
                Requirements = new()
                {
                    "Tối thiểu 4 năm kinh nghiệm trong lĩnh vực kiểm thử phần mềm, có ít nhất 2 năm dẫn dắt (Lead) đội nhóm QA.",
                    "Thành thạo một ngôn ngữ lập trình cho test automation (TypeScript/JavaScript, C# hoặc Python).",
                    "Kinh nghiệm thực tiễn vững chắc với Playwright hoặc Selenium/Cypress cho Web E2E testing.",
                    "Hiểu sâu về API Testing (REST, GraphQL, gRPC), mô phỏng dữ liệu (Mock services) và kiểm thử CSDL.",
                    "Hiểu biết về Performance Testing, Security Testing cơ bản.",
                    "Có chứng chỉ ISTQB (Advanced Level) là một lợi thế."
                },
                Benefits = GetDefaultNoveraTechBenefits(),
                TechStack = new() { "Playwright", "TypeScript", "k6 Load Test", "API Testing", "CI/CD", "ISTQB", "Docker" },
                PostedDate = DateTime.UtcNow.AddDays(-2),
                Deadline = DateTime.UtcNow.AddDays(28)
            },

            // 7. Senior Mobile Engineer (Flutter & iOS)
            new()
            {
                Id = "job-007",
                Slug = "senior-mobile-engineer-flutter-ios",
                Title = "Senior Mobile Engineer (Flutter & iOS Native)",
                Department = "Khối Kỹ thuật Phần mềm (Software Engineering)",
                DepartmentCategory = "it",
                WorkLocation = "Hà Nội (Hybrid)",
                EmploymentType = "Toàn thời gian (Full-time)",
                ExperienceLevel = "Senior (3 – 5 năm)",
                SalaryDisplay = "28 – 42 Triệu VNĐ",
                IsHot = false,
                ShortSummary = "Phát triển ứng dụng di động đa nền tảng bằng Flutter kết hợp tối ưu module native trên iOS/Android, mang lại trải nghiệm mượt mà 60fps cho người dùng.",
                Overview = "NoveraTech phát triển các ứng dụng di động phục vụ cán bộ nhân viên và khách hàng doanh nghiệp, yêu cầu giao diện đẹp, bảo mật sinh trắc học và hoạt động offline ổn định.",
                Responsibilities = new()
                {
                    "Phát triển và bảo trì các ứng dụng mobile đa nền tảng hiệu năng cao bằng Flutter (Dart).",
                    "Xây dựng các custom native bridge/plugin trên iOS (Swift) và Android (Kotlin) khi cần tích hợp sâu phần cứng thiết bị.",
                    "Quản lý state chặt chẽ bằng BLoC / Riverpod, áp dụng kiến trúc Clean Architecture cho ứng dụng mobile.",
                    "Tối ưu hiệu năng render (đạt chuẩn 60fps mượt mà), tối ưu kích thước ứng dụng và tiết kiệm pin.",
                    "Tích hợp Push Notification (FCM, APNs), sinh trắc học (Biometrics FaceID/Fingerprint) và bảo mật lưu trữ cục bộ (Keychain/Keystore).",
                    "Phối hợp phát hành và cập nhật app lên Apple App Store và Google Play Store."
                },
                Requirements = new()
                {
                    "Từ 3 năm kinh nghiệm lập trình Mobile, trong đó có ít nhất 2 năm làm việc chuyên sâu với Flutter.",
                    "Hiểu sâu về Dart, Flutter rendering pipeline, state management (BLoC, Riverpod).",
                    "Có kinh nghiệm làm việc với Swift/iOS Native hoặc Kotlin/Android Native.",
                    "Kinh nghiệm tích hợp RESTful APIs, WebSockets, background services và SQLite/Hive offline sync.",
                    "Có ít nhất 2 ứng dụng đã xuất bản và đang hoạt động tốt trên App Store hoặc Google Play."
                },
                Benefits = GetDefaultNoveraTechBenefits(),
                TechStack = new() { "Flutter", "Dart", "Swift", "iOS", "BLoC", "Clean Architecture", "REST API" },
                PostedDate = DateTime.UtcNow.AddDays(-6),
                Deadline = DateTime.UtcNow.AddDays(24)
            },

            // 8. Senior Data Engineer & Lakehouse
            new()
            {
                Id = "job-008",
                Slug = "senior-data-engineer-lakehouse",
                Title = "Senior Data Engineer & Lakehouse Architect",
                Department = "Trung tâm Nghiên cứu AI & Dữ liệu (AI & Data Lab)",
                DepartmentCategory = "ai",
                WorkLocation = "Hà Nội / TP. Hồ Chí Minh",
                EmploymentType = "Toàn thời gian (Full-time)",
                ExperienceLevel = "Senior (3 – 6 năm)",
                SalaryDisplay = "32 – 48 Triệu VNĐ",
                IsHot = false,
                ShortSummary = "Thiết kế và xây dựng các luồng dữ liệu (Data Pipelines) xử lý batch và streaming, quản trị Data Lakehouse phục vụ mô hình AI và Business Intelligence.",
                Overview = "Dữ liệu là tài sản quý giá nhất tại NoveraTech. Đội ngũ Data Engineering chịu trách nhiệm thu thập, làm sạch và xây dựng kho dữ liệu tập trung phục vụ quyết định kinh doanh và huấn luyện mô hình AI.",
                Responsibilities = new()
                {
                    "Thiết kế, xây dựng và vận hành các pipeline dữ liệu quy mô lớn (ETL/ELT) với Apache Spark, dbt và Airflow.",
                    "Xây dựng kiến trúc Data Lakehouse hiện đại (Delta Lake / Iceberg) tối ưu hóa truy vấn phân tích.",
                    "Xử lý dữ liệu thời gian thực (Real-time Streaming) bằng Kafka và Spark Streaming / Flink.",
                    "Phối hợp cùng Data Scientists và AI Engineers để chuẩn bị các Feature Stores phục vụ trực tiếp cho mô hình máy học.",
                    "Đảm bảo chất lượng dữ liệu (Data Quality Frameworks), giám sát tính toàn vẹn và bảo mật dữ liệu theo chuẩn GDPR/NDPA.",
                    "Tối ưu hóa chi phí lưu trữ và tính toán trên nền tảng đám mây (AWS S3, Redshift, Snowflake hoặc GCP BigQuery)."
                },
                Requirements = new()
                {
                    "Tối thiểu 3 năm kinh nghiệm làm việc ở vị trí Data Engineer.",
                    "Thành thạo Python, SQL nâng cao (Window functions, CTE, performance tuning).",
                    "Kinh nghiệm thực tiễn với Apache Spark (PySpark), Apache Airflow và dbt.",
                    "Hiểu biết sâu sắc về mô hình dữ liệu (Dimensional Modeling, Star/Snowflake Schema, Data Vault).",
                    "Kinh nghiệm với các giải pháp Big Data đám mây (AWS, GCP hoặc Azure)."
                },
                Benefits = GetDefaultNoveraTechBenefits(),
                TechStack = new() { "Apache Spark", "Python", "SQL", "Airflow", "Kafka", "dbt", "PostgreSQL", "AWS" },
                PostedDate = DateTime.UtcNow.AddDays(-7),
                Deadline = DateTime.UtcNow.AddDays(23)
            },

            // 9. Cybersecurity & DevSecOps Specialist
            new()
            {
                Id = "job-009",
                Slug = "cybersecurity-devsecops-specialist",
                Title = "Cybersecurity & DevSecOps Specialist",
                Department = "Phòng An toàn & Bảo mật Thông tin (Cybersecurity)",
                DepartmentCategory = "security",
                WorkLocation = "Hà Nội",
                EmploymentType = "Toàn thời gian (Full-time)",
                ExperienceLevel = "Senior (3 – 5 năm)",
                SalaryDisplay = "35 – 52 Triệu VNĐ",
                IsHot = false,
                ShortSummary = "Thiết lập hệ thống phòng thủ đa lớp, tích hợp bảo mật tự động vào quy trình CI/CD (SAST/DAST), rà quét lỗ hổng và kiểm thử xâm nhập định kỳ.",
                Overview = "Đội ngũ Cybersecurity tại NoveraTech bảo vệ hạ tầng và dữ liệu khách hàng trước mọi mối đe dọa tấn công mạng, tuân thủ các quy định bảo mật khắt khe nhất của các tổ chức tài chính.",
                Responsibilities = new()
                {
                    "Triển khai và tự động hóa các công cụ kiểm thử bảo mật mã nguồn (SAST, DAST, SCA) vào pipeline CI/CD.",
                    "Thực hiện đánh giá lỗ hổng bảo mật (Vulnerability Assessment) và kiểm thử xâm nhập (Penetration Testing) định kỳ cho Web, API và hạ tầng Cloud.",
                    "Thiết lập và giám sát hệ thống phát hiện xâm nhập (SIEM/SOC, WAF, Cloud Security Posture Management).",
                    "Phối hợp với Engineering team xử lý các cảnh báo an ninh, khắc phục lỗ hổng theo chuẩn OWASP Top 10 và CWE.",
                    "Xây dựng và cập nhật chính sách an toàn thông tin, tổ chức đào tạo nhận thức bảo mật cho toàn bộ nhân viên công ty.",
                    "Chủ trì diễn tập ứng phó sự cố an ninh mạng (Incident Response) và điều tra số (Digital Forensics) khi có yêu cầu."
                },
                Requirements = new()
                {
                    "Tối thiểu 3 năm kinh nghiệm trong lĩnh vực bảo mật thông tin, DevSecOps hoặc Penetration Testing.",
                    "Nắm vững các phương thức tấn công và phòng thủ trên nền tảng Web, Mobile và API.",
                    "Thành thạo các công cụ bảo mật (Burp Suite, OWASP ZAP, SonarQube, Snyk, Trivy, Nessus).",
                    "Hiểu sâu về cơ chế xác thực/ủy quyền an toàn (OAuth2, OpenID Connect, JWT, mTLS, RBAC/ABAC).",
                    "Ưu tiên ứng viên có các chứng chỉ uy tín: CEH, OSCP, CISSP, CompTIA Security+ hoặc AWS Certified Security."
                },
                Benefits = GetDefaultNoveraTechBenefits(),
                TechStack = new() { "DevSecOps", "Burp Suite", "SonarQube", "OWASP", "SIEM", "OAuth2", "Linux Security" },
                PostedDate = DateTime.UtcNow.AddDays(-8),
                Deadline = DateTime.UtcNow.AddDays(22)
            },

            // 10. Senior UI/UX Product Designer
            new()
            {
                Id = "job-010",
                Slug = "senior-ui-ux-product-designer",
                Title = "Senior UI/UX Product Designer (Design System & Enterprise)",
                Department = "Khối Quản trị Sản phẩm (Product Management)",
                DepartmentCategory = "product",
                WorkLocation = "Hà Nội (Hybrid)",
                EmploymentType = "Toàn thời gian (Full-time)",
                ExperienceLevel = "Senior (3 – 5 năm)",
                SalaryDisplay = "28 – 40 Triệu VNĐ",
                IsHot = false,
                ShortSummary = "Xây dựng và phát triển Design System chuẩn mực, thiết kế giao diện ứng dụng doanh nghiệp tinh tế, trực quan và lấy người dùng làm trung tâm.",
                Overview = "Tại NoveraTech, chúng tôi tin rằng phần mềm doanh nghiệp cũng phải đẹp, thanh thoát và dễ dùng như các ứng dụng tiêu dùng hàng đầu. Bạn sẽ là người tạo nên linh hồn thị giác cho các sản phẩm.",
                Responsibilities = new()
                {
                    "Thiết kế trọn vẹn luồng trải nghiệm người dùng (User Flows, Wireframes, Interactive Prototypes) cho các sản phẩm Web và Mobile phức tạp.",
                    "Xây dựng, mở rộng và bảo trì Design System của NoveraTech (Figma Tokens, Components, Typography, Color Palette).",
                    "Tiến hành phỏng vấn người dùng, usability testing và phân tích dữ liệu trải nghiệm để đưa ra các cải tiến thiết kế có cơ sở.",
                    "Phối hợp nhịp nhàng với đội ngũ Frontend Engineers để đảm bảo sản phẩm hoàn thiện chính xác từng pixel (Pixel-perfect execution).",
                    "Đảm bảo thiết kế tuân thủ tiêu chuẩn tiếp cận Web Accessibility (WCAG 2.1 AA)."
                },
                Requirements = new()
                {
                    "Tối thiểu 3 năm kinh nghiệm thiết kế UI/UX cho sản phẩm phần mềm (B2B SaaS, Dashboard, Mobile Apps).",
                    "Sử dụng Figma ở mức độ chuyên gia (Auto Layout, Variants, Variables, Component Properties, Prototyping).",
                    "Có Portfolio chất lượng thể hiện rõ tư duy giải quyết vấn đề, quy trình thiết kế và sản phẩm đã ra mắt thực tế.",
                    "Hiểu biết cơ bản về HTML/CSS và khả năng hiện thực hóa giao diện của lập trình viên.",
                    "Gu thẩm mỹ hiện đại, tinh tế, ưu tiên sự tối giản, tiện dụng và sạch sẽ."
                },
                Benefits = GetDefaultNoveraTechBenefits(),
                TechStack = new() { "Figma", "Design System", "Prototyping", "User Research", "WCAG", "Micro-interactions" },
                PostedDate = DateTime.UtcNow.AddDays(-5),
                Deadline = DateTime.UtcNow.AddDays(25)
            },

            // 11. Talent Acquisition Specialist (Tech Recruiter)
            new()
            {
                Id = "job-011",
                Slug = "talent-acquisition-specialist-tech-recruiter",
                Title = "Talent Acquisition Specialist (Tech Recruiter)",
                Department = "Khối Nhân sự & Văn hóa Doanh nghiệp (HR & Culture)",
                DepartmentCategory = "hr",
                WorkLocation = "Hà Nội (Văn phòng chính)",
                EmploymentType = "Toàn thời gian (Full-time)",
                ExperienceLevel = "Middle (2 – 4 năm)",
                SalaryDisplay = "18 – 28 Triệu VNĐ (+ Thưởng tuyển dụng)",
                IsHot = false,
                ShortSummary = "Tìm kiếm và kết nối các ứng viên tài năng trong lĩnh vực công nghệ cao (.NET, AI, Cloud), đồng hành cùng ứng viên và xây dựng thương hiệu tuyển dụng NoveraTech.",
                Overview = "Bộ phận Nhân sự NoveraTech là cầu nối mang lại trải nghiệm ứng tuyển ấm áp, chuyên nghiệp và minh bạch nhất cho các ứng viên công nghệ.",
                Responsibilities = new()
                {
                    "Chủ động tìm kiếm, săn tìm (Headhunting & Sourcing) các nhân tài công nghệ cấp cao qua LinkedIn, GitHub, Tech Communities và mạng lưới quan hệ.",
                    "Quản lý toàn bộ vòng đời tuyển dụng (Full-cycle recruiting) từ tiếp nhận yêu cầu, đăng tin, sàng lọc hồ sơ, phỏng vấn sơ loại đến thương lượng offer.",
                    "Vận hành và khai thác tối đa hệ thống ATS nội bộ của công ty để quản lý dữ liệu ứng viên khoa học và chuẩn hóa.",
                    "Đồng hành, chăm sóc và tạo trải nghiệm ứng viên (Candidate Experience) chu đáo, cam kết phản hồi minh bạch trong 48 giờ.",
                    "Tổ chức các sự kiện tuyển dụng, Tech Talks, Job Fairs và kết nối với các trường đại học công nghệ hàng đầu."
                },
                Requirements = new()
                {
                    "Từ 2 năm kinh nghiệm Tech Recruitment (Headhunt agency hoặc In-house tech company).",
                    "Hiểu biết sâu sắc về thuật ngữ công nghệ thông tin (.NET, React, Python, DevOps, Cloud, AI).",
                    "Kỹ năng giao tiếp, thương lượng và thuyết phục ứng viên xuất sắc.",
                    "Năng động, có tính tổ chức cao và tinh thần trách nhiệm trong công việc.",
                    "Kỹ năng tiếng Anh giao tiếp tốt là một lợi thế."
                },
                Benefits = GetDefaultNoveraTechBenefits(),
                TechStack = new() { "Tech Sourcing", "Headhunting", "ATS Management", "Employer Branding", "Interviewing" },
                PostedDate = DateTime.UtcNow.AddDays(-3),
                Deadline = DateTime.UtcNow.AddDays(27)
            },

            // 12. Technical Customer Success Lead
            new()
            {
                Id = "job-012",
                Slug = "technical-customer-success-lead",
                Title = "Technical Customer Success & Operations Lead",
                Department = "Khối Vận hành & Hỗ trợ Khách hàng (Operations & CS)",
                DepartmentCategory = "other",
                WorkLocation = "Hà Nội",
                EmploymentType = "Toàn thời gian (Full-time)",
                ExperienceLevel = "Middle – Senior (2 – 5 năm)",
                SalaryDisplay = "22 – 32 Triệu VNĐ",
                IsHot = false,
                ShortSummary = "Đồng hành cùng khách hàng doanh nghiệp trong quá trình triển khai, đào tạo sử dụng và giải quyết các vấn đề kỹ thuật chuyên sâu của hệ sinh thái NoveraTech.",
                Overview = "Đội ngũ Customer Success đảm bảo khách hàng khai thác tối đa giá trị từ các giải pháp của NoveraTech, duy trì mối quan hệ hợp tác tin cậy lâu dài.",
                Responsibilities = new()
                {
                    "Chủ trì hướng dẫn triển khai (Onboarding) và đào tạo sử dụng sản phẩm cho các khách hàng doanh nghiệp lớn.",
                    "Tiếp nhận, phân loại và phối hợp với đội ngũ Kỹ thuật để xử lý dứt điểm các vướng mắc, lỗi kỹ thuật từ phía khách hàng.",
                    "Theo dõi các chỉ số sức khỏe khách hàng (Customer Health Score, Churn Rate, NPS, CSAT).",
                    "Xây dựng tài liệu hướng dẫn sử dụng (User Guide), video hướng dẫn và hệ thống câu hỏi thường gặp (Knowledge Base).",
                    "Tổng hợp phản hồi của khách hàng để đóng góp ý kiến cải tiến tính năng cho đội ngũ Product và Engineering."
                },
                Requirements = new()
                {
                    "Từ 2 năm kinh nghiệm ở vị trí Customer Success, Technical Support hoặc IT Business Analyst trong các công ty B2B phần mềm.",
                    "Có kiến thức nền tảng về công nghệ thông tin, đọc hiểu API và luồng dữ liệu hệ thống.",
                    "Kỹ năng lắng nghe, thấu cảm và giải quyết khiếu nại của khách hàng điềm đạm, chuyên nghiệp.",
                    "Khả năng trình bày và thuyết trình lôi cuốn trước khách hàng doanh nghiệp."
                },
                Benefits = GetDefaultNoveraTechBenefits(),
                TechStack = new() { "Customer Success", "Enterprise Onboarding", "CRM", "Technical Support", "Knowledge Base" },
                PostedDate = DateTime.UtcNow.AddDays(-4),
                Deadline = DateTime.UtcNow.AddDays(26)
            }
        };
    }
}
