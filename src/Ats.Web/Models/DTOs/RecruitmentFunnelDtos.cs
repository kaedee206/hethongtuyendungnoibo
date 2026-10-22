using Microsoft.AspNetCore.Mvc.Rendering;

namespace Ats.Web.Models.DTOs;

/// <summary>
/// Tiêu chí lọc báo cáo phễu tuyển dụng (SCRUM-352).
/// </summary>
public class FunnelFilterCriteriaDto
{
    public Guid? JobPostingId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string DatePreset { get; set; } = "all"; // all, month, quarter, year, custom
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}

/// <summary>
/// Chỉ số chi tiết từng bước trong phễu tuyển dụng.
/// </summary>
public class FunnelStageMetricDto
{
    public int StageOrder { get; set; }
    public string StageKey { get; set; } = string.Empty;
    public string StageName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = "bi-circle";
    public string ColorCode { get; set; } = "#2563EB";

    /// <summary>Số lượng ứng viên đã qua giai đoạn này</summary>
    public int CandidateCount { get; set; }

    /// <summary>Tỷ lệ chuyển đổi so với giai đoạn liền trước (%)</summary>
    public double StageConversionRate { get; set; }

    /// <summary>Tỷ lệ chuyển đổi tổng thể so với số lượng ứng tuyển ban đầu (%)</summary>
    public double OverallConversionRate { get; set; }

    /// <summary>Số lượng ứng viên dừng lại / rơi rụng sau vòng này</summary>
    public int DropOffCount { get; set; }

    /// <summary>Tỷ lệ rơi rụng (%)</summary>
    public double DropOffRate { get; set; }

    /// <summary>Đánh dấu giai đoạn này là điểm nghẽn lớn nhất trong quy trình</summary>
    public bool IsBottleneck { get; set; }
}

/// <summary>
/// Báo cáo hoàn chỉnh Phễu tuyển dụng & Tỷ lệ chuyển đổi cho Trưởng phòng Nhân sự (SCRUM-352).
/// </summary>
public class RecruitmentFunnelReportDto
{
    public List<FunnelStageMetricDto> Stages { get; set; } = new();
    public int TotalApplications { get; set; }
    public int TotalHired { get; set; }
    public double OverallConversionRate { get; set; }

    // Điểm nghẽn lớn nhất (Bottleneck)
    public string HighestDropOffStageName { get; set; } = string.Empty;
    public double HighestDropOffRate { get; set; }
    public int HighestDropOffCount { get; set; }
    public string BottleneckInsight { get; set; } = string.Empty;
    public string BottleneckRecommendation { get; set; } = string.Empty;

    // Bộ lọc & danh sách lựa chọn
    public FunnelFilterCriteriaDto Criteria { get; set; } = new();
    public List<SelectListItem> AvailableDepartments { get; set; } = new();
    public List<SelectListItem> AvailableJobPostings { get; set; } = new();
}
