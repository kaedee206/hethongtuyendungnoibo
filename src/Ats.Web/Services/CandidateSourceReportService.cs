using Ats.Web.Data;
using Ats.Web.Models.DTOs;
using Ats.Web.Models.Enums;
using Ats.Web.Services.Interfaces;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ats.Web.Services;

/// <summary>
/// Dịch vụ tính toán báo cáo hiệu quả nguồn ứng viên & đánh giá kênh chất lượng cao (SP-8).
/// </summary>
public class CandidateSourceReportService(
    ApplicationDbContext dbContext,
    ILogger<CandidateSourceReportService> logger) : ICandidateSourceReportService
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly ILogger<CandidateSourceReportService> _logger = logger;

    // Ngưỡng số lượng hồ sơ tối thiểu để xét kênh chất lượng cao nhất (tránh mẫu quá nhỏ)
    private const int MinSampleThreshold = 3;

    /// <inheritdoc />
    public async Task<CandidateSourceReportSummaryDto> GetSourceEffectivenessReportAsync(
        CandidateSourceFilterDto filter,
        CancellationToken cancellationToken = default)
    {
        filter ??= new CandidateSourceFilterDto();

        // 1. Xây dựng truy vấn Application kết hợp bộ lọc thời gian & vị trí
        var query = _dbContext.Applications
            .AsNoTracking()
            .Where(a => !a.IsDeleted);

        // Lọc theo thời gian ứng tuyển (Từ ngày)
        if (filter.FromDate.HasValue)
        {
            var fromUtc = DateTime.SpecifyKind(filter.FromDate.Value.Date, DateTimeKind.Utc);
            query = query.Where(a => (a.AppliedAt ?? a.CreatedAt) >= fromUtc);
        }

        // Lọc theo thời gian ứng tuyển (Đến ngày - hết ngày 23:59:59)
        if (filter.ToDate.HasValue)
        {
            var toUtc = DateTime.SpecifyKind(filter.ToDate.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);
            query = query.Where(a => (a.AppliedAt ?? a.CreatedAt) <= toUtc);
        }

        // Lọc theo Vị trí chuyên môn (JobPosition)
        if (filter.JobPositionId.HasValue && filter.JobPositionId.Value != Guid.Empty)
        {
            query = query.Where(a => a.JobPosting.Requisition.JobPositionId == filter.JobPositionId.Value);
        }

        // Lọc theo Yêu cầu tuyển dụng (JobRequisition)
        if (filter.JobRequisitionId.HasValue && filter.JobRequisitionId.Value != Guid.Empty)
        {
            query = query.Where(a => a.JobPosting.RequisitionId == filter.JobRequisitionId.Value);
        }

        // Lọc theo Tin đăng tuyển dụng (JobPosting)
        if (filter.JobPostingId.HasValue && filter.JobPostingId.Value != Guid.Empty)
        {
            query = query.Where(a => a.JobPostingId == filter.JobPostingId.Value);
        }

        // 2. Tối ưu hóa truy vấn: Chỉ select các trường cần thiết, giảm thiểu payload
        var rawApplications = await query
            .Select(a => new
            {
                CandidateSource = (CandidateSource?)a.Candidate.Source,
                Stage = a.Stage,
                Status = a.Status,
                HasInterview = a.Interviews.Any() || a.Stage == CandidateStage.INTERVIEW || a.CandidateStageHistories.Any(h => h.ToStage == CandidateStage.INTERVIEW),
                HasOffer = a.JobOffers.Any() || a.Stage == CandidateStage.OFFER || a.CandidateStageHistories.Any(h => h.ToStage == CandidateStage.OFFER),
                IsHired = a.Status == ApplicationStatus.HIRED || a.Stage == CandidateStage.HIRED || a.CandidateStageHistories.Any(h => h.ToStage == CandidateStage.HIRED),
                PassedScreening = (a.Stage == CandidateStage.SCREENING && a.Status != ApplicationStatus.REJECTED)
                    || a.Stage == CandidateStage.INTERVIEW
                    || a.Stage == CandidateStage.OFFER
                    || a.Stage == CandidateStage.HIRED
                    || a.Status == ApplicationStatus.HIRED
                    || a.Interviews.Any()
                    || a.JobOffers.Any()
                    || a.CandidateStageHistories.Any(h => h.ToStage == CandidateStage.INTERVIEW || h.ToStage == CandidateStage.OFFER || h.ToStage == CandidateStage.HIRED)
                    || a.StageHistories.Any(sh => sh.ToStage != null && sh.ToStage.StageOrder >= 3)
            })
            .ToListAsync(cancellationToken);

        // 3. Gom nhóm theo Source
        var grouped = rawApplications
            .GroupBy(a => a.CandidateSource ?? CandidateSource.OTHER)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Danh sách tất cả các nguồn chuẩn hóa trong hệ thống
        var definedSources = Enum.GetValues<CandidateSource>()
            .Distinct()
            .ToList();

        var metricItems = new List<CandidateSourceMetricItemDto>();

        foreach (var source in definedSources)
        {
            grouped.TryGetValue(source, out var sourceApps);
            var totalApps = sourceApps?.Count ?? 0;
            var passedScreening = sourceApps?.Count(x => x.PassedScreening) ?? 0;
            var interviews = sourceApps?.Count(x => x.HasInterview) ?? 0;
            var offers = sourceApps?.Count(x => x.HasOffer) ?? 0;
            var hired = sourceApps?.Count(x => x.IsHired) ?? 0;

            // Phòng vệ chia cho 0
            var screeningRate = totalApps > 0
                ? Math.Round((double)passedScreening / totalApps * 100.0, 1)
                : 0.0;

            var hireRate = totalApps > 0
                ? Math.Round((double)hired / totalApps * 100.0, 1)
                : 0.0;

            // Quality Score = 70% Tỷ lệ tuyển dụng + 30% Tỷ lệ qua sàng lọc
            var qualityScore = Math.Round((hireRate * 0.7) + (screeningRate * 0.3), 1);

            metricItems.Add(new CandidateSourceMetricItemDto
            {
                Source = source,
                SourceName = source.GetDisplayName(),
                TotalApplications = totalApps,
                PassedScreeningCount = passedScreening,
                ScreeningPassRate = screeningRate,
                InterviewCount = interviews,
                OfferCount = offers,
                HiredCount = hired,
                HireRate = hireRate,
                QualityScore = qualityScore,
                IsTopQuality = false,
                IsTopVolume = false
            });
        }

        // Bổ sung các nguồn lạ ngoài enum nếu có dữ liệu
        foreach (var kvp in grouped)
        {
            if (!definedSources.Contains(kvp.Key))
            {
                var totalApps = kvp.Value.Count;
                var passedScreening = kvp.Value.Count(x => x.PassedScreening);
                var interviews = kvp.Value.Count(x => x.HasInterview);
                var offers = kvp.Value.Count(x => x.HasOffer);
                var hired = kvp.Value.Count(x => x.IsHired);

                var screeningRate = totalApps > 0 ? Math.Round((double)passedScreening / totalApps * 100.0, 1) : 0.0;
                var hireRate = totalApps > 0 ? Math.Round((double)hired / totalApps * 100.0, 1) : 0.0;
                var qualityScore = Math.Round((hireRate * 0.7) + (screeningRate * 0.3), 1);

                metricItems.Add(new CandidateSourceMetricItemDto
                {
                    Source = kvp.Key,
                    SourceName = kvp.Key.GetDisplayName(),
                    TotalApplications = totalApps,
                    PassedScreeningCount = passedScreening,
                    ScreeningPassRate = screeningRate,
                    InterviewCount = interviews,
                    OfferCount = offers,
                    HiredCount = hired,
                    HireRate = hireRate,
                    QualityScore = qualityScore
                });
            }
        }

        // 4. Xác định Nguồn nhiều hồ sơ nhất (Top Volume)
        var topVolumeCandidate = metricItems
            .Where(m => m.TotalApplications > 0)
            .OrderByDescending(m => m.TotalApplications)
            .ThenByDescending(m => m.HireRate)
            .FirstOrDefault();

        if (topVolumeCandidate != null)
        {
            topVolumeCandidate.IsTopVolume = true;
        }

        // 5. Xác định Nguồn chất lượng cao nhất (Top Quality) với ngưỡng số lượng tối thiểu
        var eligibleForQuality = metricItems
            .Where(m => m.TotalApplications >= MinSampleThreshold && (m.HiredCount > 0 || m.PassedScreeningCount > 0))
            .ToList();

        // Nếu không có nguồn nào đạt ngưỡng MinSampleThreshold, fallback về các nguồn có >= 1 hồ sơ
        if (eligibleForQuality.Count == 0)
        {
            eligibleForQuality = metricItems
                .Where(m => m.TotalApplications > 0 && (m.HiredCount > 0 || m.PassedScreeningCount > 0))
                .ToList();
        }

        var topQualityCandidate = eligibleForQuality
            .OrderByDescending(m => m.HireRate)
            .ThenByDescending(m => m.QualityScore)
            .ThenByDescending(m => m.ScreeningPassRate)
            .ThenByDescending(m => m.TotalApplications)
            .FirstOrDefault();

        if (topQualityCandidate != null)
        {
            topQualityCandidate.IsTopQuality = true;
        }

        // 6. Gán nhãn đánh giá chất lượng (Badge & Style) cho từng nguồn
        foreach (var item in metricItems)
        {
            if (item.IsTopQuality)
            {
                item.QualityBadge = "Chất lượng cao nhất 🏆";
                item.QualityBadgeCssClass = "badge-emerald";
            }
            else if (item.TotalApplications >= MinSampleThreshold && item.HireRate >= 20.0)
            {
                item.QualityBadge = "Chất lượng cao";
                item.QualityBadgeCssClass = "badge-success";
            }
            else if (item.TotalApplications >= 5 && item.HireRate <= 5.0)
            {
                item.QualityBadge = "Hồ sơ nhiều - Chuyển đổi thấp";
                item.QualityBadgeCssClass = "badge-warning";
            }
            else if (item.TotalApplications > 0)
            {
                item.QualityBadge = "Hiệu quả trung bình";
                item.QualityBadgeCssClass = "badge-secondary";
            }
            else
            {
                item.QualityBadge = "Chưa có dữ liệu";
                item.QualityBadgeCssClass = "badge-light text-muted border";
            }
        }

        // 7. Sắp xếp danh sách hiển thị: Nguồn có hồ sơ xếp trước, ưu tiên TotalApplications & HireRate
        metricItems = metricItems
            .OrderByDescending(m => m.TotalApplications)
            .ThenByDescending(m => m.HireRate)
            .ThenBy(m => m.SourceName)
            .ToList();

        // 8. Tính toán các chỉ số tổng hợp toàn hệ thống
        var totalAllApps = metricItems.Sum(m => m.TotalApplications);
        var totalAllPassed = metricItems.Sum(m => m.PassedScreeningCount);
        var totalAllInterviews = metricItems.Sum(m => m.InterviewCount);
        var totalAllOffers = metricItems.Sum(m => m.OfferCount);
        var totalAllHired = metricItems.Sum(m => m.HiredCount);

        var avgScreeningRate = totalAllApps > 0
            ? Math.Round((double)totalAllPassed / totalAllApps * 100.0, 1)
            : 0.0;

        var avgHireRate = totalAllApps > 0
            ? Math.Round((double)totalAllHired / totalAllApps * 100.0, 1)
            : 0.0;

        // 9. Xây dựng gợi ý phân bổ ngân sách thông minh (AI / Rule-based Insights)
        var recommendation = GenerateBudgetAllocationRecommendation(
            topQualityCandidate,
            topVolumeCandidate,
            totalAllApps,
            avgHireRate);

        return new CandidateSourceReportSummaryDto
        {
            Items = metricItems,
            TopQualitySource = topQualityCandidate?.SourceName,
            TopQualityHireRate = topQualityCandidate?.HireRate ?? 0.0,
            TopVolumeSource = topVolumeCandidate?.SourceName,
            TopVolumeApplications = topVolumeCandidate?.TotalApplications ?? 0,
            TotalApplicationsAllSources = totalAllApps,
            TotalPassedScreeningAllSources = totalAllPassed,
            TotalInterviewedAllSources = totalAllInterviews,
            TotalOfferedAllSources = totalAllOffers,
            TotalHiredAllSources = totalAllHired,
            AverageScreeningPassRate = avgScreeningRate,
            AverageHireRate = avgHireRate,
            BudgetAllocationRecommendation = recommendation,
            Filter = filter
        };
    }

    /// <inheritdoc />
    public async Task<byte[]> ExportSourceEffectivenessToExcelAsync(
        CandidateSourceFilterDto filter,
        CancellationToken cancellationToken = default)
    {
        var summary = await GetSourceEffectivenessReportAsync(filter, cancellationToken);

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Hieu_Qua_Nguon_Ung_Vien");

        // Thiết lập cấu hình hiển thị lưới
        worksheet.ShowGridLines = true;

        // Bảng màu thiết kế thương hiệu ATS NoveraTech
        var primaryGreen = XLColor.FromArgb(13, 92, 77);   // #0D5C4D Emerald
        var accentGreen = XLColor.FromArgb(5, 150, 105);   // #059669
        var softBgGreen = XLColor.FromArgb(236, 253, 245); // #ECFDF5
        var textDark = XLColor.FromArgb(15, 23, 42);       // #0F172A
        var textMuted = XLColor.FromArgb(100, 116, 139);   // #64748B
        var borderLight = XLColor.FromArgb(226, 232, 240); // #E2E8F0

        // ==========================================
        // 1. Header Báo cáo & Metadata (Rows 1 - 6)
        // ==========================================
        worksheet.Cell(1, 1).Value = "NOVERATECH CAREERS — HỆ THỐNG QUẢN TRỊ TUYỂN DỤNG NỘI BỘ (ATS)";
        worksheet.Cell(1, 1).Style.Font.Bold = true;
        worksheet.Cell(1, 1).Style.Font.FontSize = 10;
        worksheet.Cell(1, 1).Style.Font.FontColor = accentGreen;

        worksheet.Cell(2, 1).Value = "BÁO CÁO HIỆU QUẢ NGUỒN ỨNG VIÊN & ĐÁNH GIÁ CHẤT LƯỢNG NGUỒN";
        worksheet.Range(2, 1, 2, 11).Merge();
        worksheet.Cell(2, 1).Style.Font.FontSize = 15;
        worksheet.Cell(2, 1).Style.Font.Bold = true;
        worksheet.Cell(2, 1).Style.Font.FontColor = primaryGreen;
        worksheet.Cell(2, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        worksheet.Row(2).Height = 28;

        // Thông tin bộ lọc
        var fromStr = filter.FromDate.HasValue ? filter.FromDate.Value.ToString("dd/MM/yyyy") : "Toàn thời gian";
        var toStr = filter.ToDate.HasValue ? filter.ToDate.Value.ToString("dd/MM/yyyy") : "Hiện tại";

        worksheet.Cell(3, 1).Value = "Khoảng thời gian:";
        worksheet.Cell(3, 1).Style.Font.Bold = true;
        worksheet.Cell(3, 1).Style.Font.FontColor = textMuted;
        worksheet.Cell(3, 2).Value = $"{fromStr} - {toStr}";
        worksheet.Range(3, 2, 3, 4).Merge();

        worksheet.Cell(3, 6).Value = "Ngày xuất báo cáo:";
        worksheet.Cell(3, 6).Style.Font.Bold = true;
        worksheet.Cell(3, 6).Style.Font.FontColor = textMuted;
        worksheet.Cell(3, 7).Value = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
        worksheet.Range(3, 7, 3, 8).Merge();

        // Highlight Top KPI
        worksheet.Cell(4, 1).Value = "🏆 Nguồn chất lượng cao nhất:";
        worksheet.Cell(4, 1).Style.Font.Bold = true;
        worksheet.Cell(4, 1).Style.Font.FontColor = textMuted;
        worksheet.Cell(4, 2).Value = !string.IsNullOrEmpty(summary.TopQualitySource)
            ? $"{summary.TopQualitySource} (Tỷ lệ tuyển: {summary.TopQualityHireRate:F1}%)"
            : "Chưa đủ dữ liệu đánh giá";
        worksheet.Cell(4, 2).Style.Font.Bold = true;
        worksheet.Cell(4, 2).Style.Font.FontColor = accentGreen;
        worksheet.Range(4, 2, 4, 4).Merge();

        worksheet.Cell(4, 6).Value = "📦 Nguồn nhiều hồ sơ nhất:";
        worksheet.Cell(4, 6).Style.Font.Bold = true;
        worksheet.Cell(4, 6).Style.Font.FontColor = textMuted;
        worksheet.Cell(4, 7).Value = !string.IsNullOrEmpty(summary.TopVolumeSource)
            ? $"{summary.TopVolumeSource} ({summary.TopVolumeApplications} hồ sơ)"
            : "Chưa có dữ liệu";
        worksheet.Cell(4, 7).Style.Font.Bold = true;
        worksheet.Range(4, 7, 4, 8).Merge();

        // Gợi ý ngân sách
        worksheet.Cell(5, 1).Value = "💡 Gợi ý phân bổ ngân sách:";
        worksheet.Cell(5, 1).Style.Font.Bold = true;
        worksheet.Cell(5, 1).Style.Font.FontColor = textMuted;
        worksheet.Cell(5, 2).Value = summary.BudgetAllocationRecommendation;
        worksheet.Cell(5, 2).Style.Font.Italic = true;
        worksheet.Cell(5, 2).Style.Alignment.WrapText = true;
        worksheet.Range(5, 2, 5, 11).Merge();
        worksheet.Row(5).Height = 28;

        // ==========================================
        // 2. Table Header (Row 7)
        // ==========================================
        int headerRow = 7;
        worksheet.Row(headerRow).Height = 26;

        var headers = new (string Title, int Width, XLAlignmentHorizontalValues Align)[]
        {
            ("STT", 6, XLAlignmentHorizontalValues.Center),
            ("Nguồn tuyển dụng", 26, XLAlignmentHorizontalValues.Left),
            ("Tổng số hồ sơ", 16, XLAlignmentHorizontalValues.Right),
            ("Qua sàng lọc", 15, XLAlignmentHorizontalValues.Right),
            ("Tỷ lệ sàng lọc (%)", 18, XLAlignmentHorizontalValues.Right),
            ("Phỏng vấn", 14, XLAlignmentHorizontalValues.Right),
            ("Đề xuất Offer", 14, XLAlignmentHorizontalValues.Right),
            ("Nhận việc (Hired)", 18, XLAlignmentHorizontalValues.Right),
            ("Tỷ lệ tuyển dụng (%)", 20, XLAlignmentHorizontalValues.Right),
            ("Điểm chất lượng", 16, XLAlignmentHorizontalValues.Right),
            ("Đánh giá chất lượng", 25, XLAlignmentHorizontalValues.Center)
        };

        for (int c = 0; c < headers.Length; c++)
        {
            var cell = worksheet.Cell(headerRow, c + 1);
            cell.Value = headers[c].Title;
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = primaryGreen;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.OutsideBorderColor = primaryGreen;
            worksheet.Column(c + 1).Width = headers[c].Width;
        }

        // ==========================================
        // 3. Data Rows
        // ==========================================
        int currentRow = headerRow + 1;
        int stt = 1;

        foreach (var item in summary.Items)
        {
            worksheet.Row(currentRow).Height = 22;

            worksheet.Cell(currentRow, 1).Value = stt++;
            worksheet.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            // Nguồn tuyển dụng kèm biểu tượng Top Quality / Top Volume nếu có
            var sourceDisplay = item.SourceName;
            if (item.IsTopQuality) sourceDisplay = $"🏆 {sourceDisplay} (Top Quality)";
            else if (item.IsTopVolume) sourceDisplay = $"📦 {sourceDisplay} (Top Volume)";
            worksheet.Cell(currentRow, 2).Value = sourceDisplay;
            worksheet.Cell(currentRow, 2).Style.Font.Bold = item.IsTopQuality || item.IsTopVolume;

            worksheet.Cell(currentRow, 3).Value = item.TotalApplications;
            worksheet.Cell(currentRow, 3).Style.NumberFormat.Format = "#,##0";
            worksheet.Cell(currentRow, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

            worksheet.Cell(currentRow, 4).Value = item.PassedScreeningCount;
            worksheet.Cell(currentRow, 4).Style.NumberFormat.Format = "#,##0";
            worksheet.Cell(currentRow, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

            worksheet.Cell(currentRow, 5).Value = item.ScreeningPassRate / 100.0;
            worksheet.Cell(currentRow, 5).Style.NumberFormat.Format = "0.0%";
            worksheet.Cell(currentRow, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

            worksheet.Cell(currentRow, 6).Value = item.InterviewCount;
            worksheet.Cell(currentRow, 6).Style.NumberFormat.Format = "#,##0";
            worksheet.Cell(currentRow, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

            worksheet.Cell(currentRow, 7).Value = item.OfferCount;
            worksheet.Cell(currentRow, 7).Style.NumberFormat.Format = "#,##0";
            worksheet.Cell(currentRow, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

            worksheet.Cell(currentRow, 8).Value = item.HiredCount;
            worksheet.Cell(currentRow, 8).Style.NumberFormat.Format = "#,##0";
            worksheet.Cell(currentRow, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            if (item.HiredCount > 0)
            {
                worksheet.Cell(currentRow, 8).Style.Font.Bold = true;
                worksheet.Cell(currentRow, 8).Style.Font.FontColor = accentGreen;
            }

            worksheet.Cell(currentRow, 9).Value = item.HireRate / 100.0;
            worksheet.Cell(currentRow, 9).Style.NumberFormat.Format = "0.0%";
            worksheet.Cell(currentRow, 9).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            if (item.HireRate >= 20.0)
            {
                worksheet.Cell(currentRow, 9).Style.Font.Bold = true;
                worksheet.Cell(currentRow, 9).Style.Font.FontColor = accentGreen;
            }

            worksheet.Cell(currentRow, 10).Value = item.QualityScore;
            worksheet.Cell(currentRow, 10).Style.NumberFormat.Format = "0.0";
            worksheet.Cell(currentRow, 10).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

            worksheet.Cell(currentRow, 11).Value = item.QualityBadge;
            worksheet.Cell(currentRow, 11).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            // Highlight dòng Top Quality với màu nền nhẹ
            if (item.IsTopQuality)
            {
                worksheet.Range(currentRow, 1, currentRow, 11).Style.Fill.BackgroundColor = softBgGreen;
            }

            for (int col = 1; col <= 11; col++)
            {
                worksheet.Cell(currentRow, col).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                worksheet.Cell(currentRow, col).Style.Border.OutsideBorderColor = borderLight;
            }

            currentRow++;
        }

        // ==========================================
        // 4. Summary Row (Dòng Tổng Cộng)
        // ==========================================
        worksheet.Row(currentRow).Height = 24;
        worksheet.Cell(currentRow, 1).Value = "TỔNG CỘNG";
        worksheet.Range(currentRow, 1, currentRow, 2).Merge();
        worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
        worksheet.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        worksheet.Cell(currentRow, 3).Value = summary.TotalApplicationsAllSources;
        worksheet.Cell(currentRow, 3).Style.NumberFormat.Format = "#,##0";
        worksheet.Cell(currentRow, 3).Style.Font.Bold = true;

        worksheet.Cell(currentRow, 4).Value = summary.TotalPassedScreeningAllSources;
        worksheet.Cell(currentRow, 4).Style.NumberFormat.Format = "#,##0";
        worksheet.Cell(currentRow, 4).Style.Font.Bold = true;

        worksheet.Cell(currentRow, 5).Value = summary.AverageScreeningPassRate / 100.0;
        worksheet.Cell(currentRow, 5).Style.NumberFormat.Format = "0.0%";
        worksheet.Cell(currentRow, 5).Style.Font.Bold = true;

        worksheet.Cell(currentRow, 6).Value = summary.TotalInterviewedAllSources;
        worksheet.Cell(currentRow, 6).Style.NumberFormat.Format = "#,##0";
        worksheet.Cell(currentRow, 6).Style.Font.Bold = true;

        worksheet.Cell(currentRow, 7).Value = summary.TotalOfferedAllSources;
        worksheet.Cell(currentRow, 7).Style.NumberFormat.Format = "#,##0";
        worksheet.Cell(currentRow, 7).Style.Font.Bold = true;

        worksheet.Cell(currentRow, 8).Value = summary.TotalHiredAllSources;
        worksheet.Cell(currentRow, 8).Style.NumberFormat.Format = "#,##0";
        worksheet.Cell(currentRow, 8).Style.Font.Bold = true;
        worksheet.Cell(currentRow, 8).Style.Font.FontColor = accentGreen;

        worksheet.Cell(currentRow, 9).Value = summary.AverageHireRate / 100.0;
        worksheet.Cell(currentRow, 9).Style.NumberFormat.Format = "0.0%";
        worksheet.Cell(currentRow, 9).Style.Font.Bold = true;
        worksheet.Cell(currentRow, 9).Style.Font.FontColor = accentGreen;

        worksheet.Cell(currentRow, 10).Value = summary.Items.Count > 0 ? summary.Items.Average(m => m.QualityScore) : 0.0;
        worksheet.Cell(currentRow, 10).Style.NumberFormat.Format = "0.0";
        worksheet.Cell(currentRow, 10).Style.Font.Bold = true;

        worksheet.Cell(currentRow, 11).Value = "Tổng hợp toàn bộ kênh";
        worksheet.Cell(currentRow, 11).Style.Font.Bold = true;
        worksheet.Cell(currentRow, 11).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        var summaryRange = worksheet.Range(currentRow, 1, currentRow, 11);
        summaryRange.Style.Fill.BackgroundColor = XLColor.FromArgb(241, 245, 249); // #F1F5F9 Slate
        summaryRange.Style.Border.TopBorder = XLBorderStyleValues.Thin;
        summaryRange.Style.Border.BottomBorder = XLBorderStyleValues.Double;
        summaryRange.Style.Border.BottomBorderColor = primaryGreen;

        using var memoryStream = new MemoryStream();
        workbook.SaveAs(memoryStream);
        return memoryStream.ToArray();
    }

    /// <summary>
    /// Xây dựng văn bản phân tích và khuyến nghị phân bổ ngân sách
    /// </summary>
    private static string GenerateBudgetAllocationRecommendation(
        CandidateSourceMetricItemDto? topQuality,
        CandidateSourceMetricItemDto? topVolume,
        int totalApps,
        double avgHireRate)
    {
        if (totalApps == 0)
        {
            return "Chưa ghi nhận dữ liệu ứng viên trong khoảng thời gian hoặc điều kiện lọc đã chọn. Vui lòng mở rộng khoảng thời gian hoặc chọn chức danh tuyển dụng khác để phân tích.";
        }

        if (topQuality != null && topVolume != null && topQuality.SourceName != topVolume.SourceName)
        {
            return $"Nguồn '{topQuality.SourceName}' dẫn đầu về chất lượng với tỷ lệ trúng tuyển ấn tượng ({topQuality.HireRate:F1}%), trong khi nguồn '{topVolume.SourceName}' mang lại lượng hồ sơ lớn nhất ({topVolume.TotalApplications} CV) nhưng tỷ lệ nhận việc còn khiêm tốn ({topVolume.HireRate:F1}%). Khuyến nghị: Doanh nghiệp nên cân nhắc tối ưu ngân sách truyền thông tuyển dụng bằng cách tái phân bổ hoặc chuyển dịch bớt chi phí từ '{topVolume.SourceName}' sang đầu tư thúc đẩy nguồn '{topQuality.SourceName}' (ví dụ: tăng chính sách thưởng nhân viên giới thiệu nội bộ hoặc đẩy mạnh tài trợ tin tuyển dụng LinkedIn) để giảm chi phí trên mỗi nhân sự nhận việc (Cost per Hire) và nâng cao tỷ lệ tuyển dụng thành công.";
        }

        if (topQuality != null && topVolume != null && topQuality.SourceName == topVolume.SourceName)
        {
            return $"Nguồn '{topQuality.SourceName}' đang thể hiện xuất sắc toàn diện, dẫn đầu cả về số lượng hồ sơ tiếp nhận ({topVolume.TotalApplications} CV) lẫn tỷ lệ chuyển đổi trúng tuyển ({topQuality.HireRate:F1}%). Khuyến nghị: Tiếp tục duy trì và tăng cường ngân sách để củng cố vị thế kênh tuyển dụng chiến lược này.";
        }

        return $"Hệ thống ghi nhận tổng cộng {totalApps} hồ sơ với tỷ lệ tuyển dụng trung bình {avgHireRate:F1}%. Tiếp tục theo dõi quá trình phỏng vấn và tiếp nhận ứng viên ở các vòng kế tiếp để có phân tích chuyển đổi nguồn chuyên sâu.";
    }
}
