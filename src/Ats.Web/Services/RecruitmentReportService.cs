using Ats.Web.Data;
using Ats.Web.Models.DTOs;
using Ats.Web.Models.Enums;
using Ats.Web.Services.Interfaces;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ats.Web.Services;

/// <summary>
/// Dịch vụ tính toán báo cáo phễu tuyển dụng & tỷ lệ chuyển đổi cho Trưởng phòng Nhân sự (SCRUM-352).
/// </summary>
public class RecruitmentReportService(
    ApplicationDbContext dbContext,
    ILogger<RecruitmentReportService> logger) : IRecruitmentReportService
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly ILogger<RecruitmentReportService> _logger = logger;

    public async Task<RecruitmentFunnelReportDto> GetRecruitmentFunnelReportAsync(
        FunnelFilterCriteriaDto criteria,
        CancellationToken cancellationToken = default)
    {
        criteria ??= new FunnelFilterCriteriaDto();

        // 1. Phân giải khoảng thời gian lọc (DatePreset)
        DateTime? fromDateUtc = null;
        DateTime? toDateUtc = null;
        var now = DateTime.UtcNow;

        switch (criteria.DatePreset?.ToLowerInvariant())
        {
            case "month":
                fromDateUtc = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                toDateUtc = fromDateUtc.Value.AddMonths(1).AddTicks(-1);
                break;
            case "quarter":
                int quarter = (now.Month - 1) / 3 + 1;
                int startMonth = (quarter - 1) * 3 + 1;
                fromDateUtc = new DateTime(now.Year, startMonth, 1, 0, 0, 0, DateTimeKind.Utc);
                toDateUtc = fromDateUtc.Value.AddMonths(3).AddTicks(-1);
                break;
            case "year":
                fromDateUtc = new DateTime(now.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                toDateUtc = new DateTime(now.Year, 12, 31, 23, 59, 59, DateTimeKind.Utc);
                break;
            case "custom":
                if (criteria.FromDate.HasValue)
                    fromDateUtc = DateTime.SpecifyKind(criteria.FromDate.Value.Date, DateTimeKind.Utc);
                if (criteria.ToDate.HasValue)
                    toDateUtc = DateTime.SpecifyKind(criteria.ToDate.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);
                break;
            default:
                criteria.DatePreset = "all";
                break;
        }

        // 2. Truy vấn hồ sơ ứng tuyển theo điều kiện lọc
        var query = _dbContext.Applications
            .AsNoTracking()
            .Include(a => a.CurrentStage)
            .Include(a => a.JobPosting)
                .ThenInclude(j => j.Requisition)
                    .ThenInclude(r => r.Department)
            .Include(a => a.StageHistories)
                .ThenInclude(h => h.ToStage)
            .Include(a => a.Interviews)
            .Include(a => a.JobOffers)
            .Where(a => !a.IsDeleted);

        if (criteria.JobPostingId.HasValue && criteria.JobPostingId.Value != Guid.Empty)
        {
            query = query.Where(a => a.JobPostingId == criteria.JobPostingId.Value);
        }

        if (criteria.DepartmentId.HasValue && criteria.DepartmentId.Value != Guid.Empty)
        {
            query = query.Where(a => a.JobPosting != null &&
                                     a.JobPosting.Requisition != null &&
                                     a.JobPosting.Requisition.DepartmentId == criteria.DepartmentId.Value);
        }

        if (fromDateUtc.HasValue)
        {
            var offsetFrom = new DateTimeOffset(fromDateUtc.Value);
            query = query.Where(a => a.AppliedAt >= offsetFrom);
        }

        if (toDateUtc.HasValue)
        {
            var offsetTo = new DateTimeOffset(toDateUtc.Value);
            query = query.Where(a => a.AppliedAt <= offsetTo);
        }

        var applications = await query.ToListAsync(cancellationToken);

        // 3. Phân loại tiến trình đạt được của từng hồ sơ qua 5 giai đoạn cốt lõi
        int countApplied = applications.Count;
        int countScreening = 0;
        int countInterview = 0;
        int countOffer = 0;
        int countHired = 0;

        foreach (var app in applications)
        {
            int maxStage = 1; // Mọi hồ sơ nộp đơn đều bắt đầu từ Ứng tuyển mới

            // Đánh giá theo CurrentStage
            if (app.CurrentStage != null)
            {
                if (app.CurrentStage.StageOrder >= 6) maxStage = Math.Max(maxStage, 5);
                else if (app.CurrentStage.StageOrder >= 5) maxStage = Math.Max(maxStage, 4);
                else if (app.CurrentStage.StageOrder >= 3) maxStage = Math.Max(maxStage, 3);
                else if (app.CurrentStage.StageOrder >= 2) maxStage = Math.Max(maxStage, 2);
            }

            // Đánh giá theo ApplicationStatus
            if (app.Status == ApplicationStatus.HIRED)
            {
                maxStage = Math.Max(maxStage, 5);
            }

            // Đánh giá theo quan hệ thực thể (Offers & Interviews)
            if (app.JobOffers != null && app.JobOffers.Any())
            {
                maxStage = Math.Max(maxStage, 4);
            }

            if (app.Interviews != null && app.Interviews.Any())
            {
                maxStage = Math.Max(maxStage, 3);
            }

            // Đánh giá theo Lịch sử chuyển giai đoạn
            if (app.StageHistories != null)
            {
                foreach (var h in app.StageHistories)
                {
                    var order = h.ToStage?.StageOrder ?? 0;
                    if (order >= 6) maxStage = Math.Max(maxStage, 5);
                    else if (order >= 5) maxStage = Math.Max(maxStage, 4);
                    else if (order >= 3) maxStage = Math.Max(maxStage, 3);
                    else if (order >= 2) maxStage = Math.Max(maxStage, 2);
                }
            }

            if (maxStage >= 2) countScreening++;
            if (maxStage >= 3) countInterview++;
            if (maxStage >= 4) countOffer++;
            if (maxStage >= 5) countHired++;
        }

        // 4. Định nghĩa 5 tầng phễu tuyển dụng
        var stageDefs = new List<(int Order, string Key, string Name, string Desc, string Icon, string Color, int Count)>
        {
            (1, "applied", "Ứng tuyển mới", "Hồ sơ ứng viên nộp qua cổng Portal hoặc nộp thủ công", "bi-person-plus-fill", "#2563EB", countApplied),
            (2, "screening", "Sàng lọc hồ sơ", "Hồ sơ vượt qua vòng sơ tuyển & đáp ứng tiêu chuẩn CV", "bi-file-earmark-check-fill", "#0284C7", countScreening),
            (3, "interview", "Phỏng vấn", "Ứng viên tham gia ít nhất một vòng phỏng vấn chuyên môn", "bi-people-fill", "#7C3AED", countInterview),
            (4, "offer", "Đề xuất nhận việc (Offer)", "Ứng viên được gửi thư mời nhận việc chính thức", "bi-envelope-paper-heart-fill", "#D97706", countOffer),
            (5, "hired", "Tuyển dụng thành công", "Ứng viên đồng ý offer và được onboard chính thức", "bi-check-circle-fill", "#059669", countHired)
        };

        var stageMetrics = new List<FunnelStageMetricDto>();
        int prevCount = 0;

        foreach (var def in stageDefs)
        {
            double stageConvRate = 100.0;
            double overallConvRate = countApplied > 0 ? Math.Round((double)def.Count / countApplied * 100.0, 1) : 0.0;
            int dropOffCount = 0;
            double dropOffRate = 0.0;

            if (def.Order > 1)
            {
                stageConvRate = prevCount > 0 ? Math.Round((double)def.Count / prevCount * 100.0, 1) : 0.0;
                dropOffCount = Math.Max(0, prevCount - def.Count);
                dropOffRate = prevCount > 0 ? Math.Round((double)dropOffCount / prevCount * 100.0, 1) : 0.0;
            }

            stageMetrics.Add(new FunnelStageMetricDto
            {
                StageOrder = def.Order,
                StageKey = def.Key,
                StageName = def.Name,
                Description = def.Desc,
                Icon = def.Icon,
                ColorCode = def.Color,
                CandidateCount = def.Count,
                StageConversionRate = stageConvRate,
                OverallConversionRate = overallConvRate,
                DropOffCount = dropOffCount,
                DropOffRate = dropOffRate,
                IsBottleneck = false
            });

            prevCount = def.Count;
        }

        // 5. Xác định điểm nghẽn (Bottleneck) có tỷ lệ rơi rụng cao nhất
        var validDropOffStages = stageMetrics.Where(s => s.StageOrder > 1 && s.DropOffCount > 0).ToList();
        FunnelStageMetricDto? bottleneck = null;

        if (validDropOffStages.Count > 0)
        {
            // Điểm nghẽn là giai đoạn có DropOffRate cao nhất
            bottleneck = validDropOffStages.OrderByDescending(s => s.DropOffRate).ThenByDescending(s => s.DropOffCount).First();
            bottleneck.IsBottleneck = true;
        }

        string insight = string.Empty;
        string recommendation = string.Empty;

        if (bottleneck != null)
        {
            var prevStage = stageMetrics.First(s => s.StageOrder == bottleneck.StageOrder - 1);
            insight = $"Điểm nghẽn lớn nhất trong quy trình là giai đoạn chuyển giao từ [{prevStage.StageName}] sang [{bottleneck.StageName}] với tỷ lệ rơi rụng lên tới {bottleneck.DropOffRate}% (dừng lại {bottleneck.DropOffCount} ứng viên).";

            recommendation = bottleneck.StageKey switch
            {
                "screening" => "Rà soát lại nội dung bản mô tả công việc (JD) trên các kênh truyền thông để thu hút đúng đối tượng mục tiêu; đồng thời nới lỏng hoặc tối ưu lại các tiêu chí lọc CV ban đầu.",
                "interview" => "Chuẩn hóa ngân hàng câu hỏi phỏng vấn theo khung năng lực chuẩn, tổ chức đào tạo kỹ năng phỏng vấn cho người đánh giá chuyên môn để tránh đánh giá chủ quan.",
                "offer" => "Khảo sát và rà soát lại mức độ cạnh tranh của dải lương (Salary Band) so với thị trường; đồng thời chủ động lắng nghe nguyện vọng thu nhập của ứng viên ngay từ vòng đầu.",
                "hired" => "Rút ngắn thời gian từ lúc gửi Offer đến ngày nhận việc; duy trì tương tác tích cực để củng cố cam kết gắn bó của ứng viên trước ngày đi làm.",
                _ => "Tập trung rà soát và tối ưu hóa thời gian xử lý hồ sơ tại bước này để nâng cao trải nghiệm ứng viên."
            };
        }
        else
        {
            insight = countApplied > 0
                ? "Dữ liệu tuyển dụng hiện tại cho thấy tỷ lệ chuyển đổi ổn định và chưa xuất hiện điểm rơi rụng bất thường."
                : "Chưa có đủ dữ liệu hồ sơ ứng tuyển trong khoảng thời gian hoặc điều kiện lọc đã chọn để phát hiện điểm nghẽn.";
            recommendation = "Tiếp tục theo dõi các đợt tuyển dụng tiếp theo để có bức tranh toàn diện hơn.";
        }

        // 6. Nạp danh sách bộ lọc Dropdown
        var depts = await _dbContext.Departments
            .AsNoTracking()
            .Where(d => !d.IsDeleted)
            .OrderBy(d => d.Name)
            .Select(d => new SelectListItem
            {
                Value = d.Id.ToString(),
                Text = d.Name,
                Selected = criteria.DepartmentId.HasValue && criteria.DepartmentId.Value == d.Id
            })
            .ToListAsync(cancellationToken);

        var jobQuery = _dbContext.JobPostings
            .AsNoTracking()
            .Where(j => !j.IsDeleted);

        if (criteria.DepartmentId.HasValue && criteria.DepartmentId.Value != Guid.Empty)
        {
            jobQuery = jobQuery.Where(j => j.Requisition != null && j.Requisition.DepartmentId == criteria.DepartmentId.Value);
        }

        var jobs = await jobQuery
            .OrderByDescending(j => j.CreatedAt)
            .Select(j => new SelectListItem
            {
                Value = j.Id.ToString(),
                Text = j.Title,
                Selected = criteria.JobPostingId.HasValue && criteria.JobPostingId.Value == j.Id
            })
            .ToListAsync(cancellationToken);

        double totalOverallRate = countApplied > 0 ? Math.Round((double)countHired / countApplied * 100.0, 1) : 0.0;

        return new RecruitmentFunnelReportDto
        {
            Stages = stageMetrics,
            TotalApplications = countApplied,
            TotalHired = countHired,
            OverallConversionRate = totalOverallRate,
            HighestDropOffStageName = bottleneck?.StageName ?? "Chưa ghi nhận",
            HighestDropOffRate = bottleneck?.DropOffRate ?? 0.0,
            HighestDropOffCount = bottleneck?.DropOffCount ?? 0,
            BottleneckInsight = insight,
            BottleneckRecommendation = recommendation,
            Criteria = criteria,
            AvailableDepartments = depts,
            AvailableJobPostings = jobs
        };
    }
}
