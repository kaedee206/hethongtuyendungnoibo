using Ats.Web.Models.Enums;

namespace Ats.Web.Models.DTOs;

/// <summary>
/// Bộ lọc báo cáo hiệu quả nguồn ứng viên (SP-8).
/// </summary>
public class CandidateSourceFilterDto
{
    /// <summary>
    /// Từ ngày ứng tuyển
    /// </summary>
    public DateTime? FromDate { get; set; }

    /// <summary>
    /// Đến ngày ứng tuyển
    /// </summary>
    public DateTime? ToDate { get; set; }

    /// <summary>
    /// Lọc theo vị trí / chức danh chuyên môn (JobPosition)
    /// </summary>
    public Guid? JobPositionId { get; set; }

    /// <summary>
    /// Lọc theo yêu cầu tuyển dụng (JobRequisition)
    /// </summary>
    public Guid? JobRequisitionId { get; set; }

    /// <summary>
    /// Lọc theo tin đăng tuyển dụng (JobPosting)
    /// </summary>
    public Guid? JobPostingId { get; set; }
}

/// <summary>
/// Thống kê chi tiết số liệu theo từng kênh / nguồn tuyển dụng (SP-8).
/// </summary>
public class CandidateSourceMetricItemDto
{
    /// <summary>
    /// Mã nguồn enum (nếu có)
    /// </summary>
    public CandidateSource? Source { get; set; }

    /// <summary>
    /// Tên nguồn tuyển dụng hiển thị (LinkedIn, TopCV, Giới thiệu nội bộ,...)
    /// </summary>
    public string SourceName { get; set; } = string.Empty;

    /// <summary>
    /// Tổng số hồ sơ tiếp nhận (Total Applications)
    /// </summary>
    public int TotalApplications { get; set; }

    /// <summary>
    /// Số lượng hồ sơ vượt qua vòng sàng lọc sơ bộ
    /// </summary>
    public int PassedScreeningCount { get; set; }

    /// <summary>
    /// Tỷ lệ hồ sơ qua sàng lọc (%)
    /// </summary>
    public double ScreeningPassRate { get; set; }

    /// <summary>
    /// Số lượng ứng viên tham gia ít nhất 1 vòng phỏng vấn
    /// </summary>
    public int InterviewCount { get; set; }

    /// <summary>
    /// Số lượng đề xuất tuyển dụng (Offer) được gửi
    /// </summary>
    public int OfferCount { get; set; }

    /// <summary>
    /// Số lượng ứng viên trúng tuyển nhận việc (Hired / Onboarded)
    /// </summary>
    public int HiredCount { get; set; }

    /// <summary>
    /// Tỷ lệ tuyển dụng thành công tổng thể (%) = Hired / Total Applications
    /// </summary>
    public double HireRate { get; set; }

    /// <summary>
    /// Điểm đánh giá chất lượng nguồn tổng hợp (Quality Score)
    /// Công thức: (HireRate * 0.7) + (ScreeningPassRate * 0.3)
    /// </summary>
    public double QualityScore { get; set; }

    /// <summary>
    /// Đánh dấu kênh đạt danh hiệu Nguồn chất lượng cao nhất (Top Quality)
    /// </summary>
    public bool IsTopQuality { get; set; }

    /// <summary>
    /// Đánh dấu kênh đạt danh hiệu Nguồn nhiều hồ sơ nhất (Top Volume)
    /// </summary>
    public bool IsTopVolume { get; set; }

    /// <summary>
    /// Nhãn hiển thị đánh giá chất lượng nguồn (Badge text)
    /// </summary>
    public string QualityBadge { get; set; } = "Hiệu quả trung bình";

    /// <summary>
    /// Lớp CSS của badge
    /// </summary>
    public string QualityBadgeCssClass { get; set; } = "badge-secondary";
}

/// <summary>
/// Dữ liệu tổng hợp toàn diện báo cáo hiệu quả nguồn ứng viên (SP-8).
/// </summary>
public class CandidateSourceReportSummaryDto
{
    /// <summary>
    /// Danh sách số liệu chi tiết từng nguồn tuyển dụng
    /// </summary>
    public List<CandidateSourceMetricItemDto> Items { get; set; } = new();

    /// <summary>
    /// Tên kênh tuyển dụng chất lượng cao nhất
    /// </summary>
    public string? TopQualitySource { get; set; }

    /// <summary>
    /// Tỷ lệ trúng tuyển của nguồn chất lượng cao nhất (%)
    /// </summary>
    public double TopQualityHireRate { get; set; }

    /// <summary>
    /// Tên kênh mang lại nhiều hồ sơ CV nhất
    /// </summary>
    public string? TopVolumeSource { get; set; }

    /// <summary>
    /// Số lượng hồ sơ của kênh nhiều hồ sơ nhất
    /// </summary>
    public int TopVolumeApplications { get; set; }

    /// <summary>
    /// Tổng số hồ sơ tiếp nhận trên toàn bộ các nguồn
    /// </summary>
    public int TotalApplicationsAllSources { get; set; }

    /// <summary>
    /// Tổng số hồ sơ qua sàng lọc trên toàn bộ các nguồn
    /// </summary>
    public int TotalPassedScreeningAllSources { get; set; }

    /// <summary>
    /// Tổng số hồ sơ vào vòng phỏng vấn
    /// </summary>
    public int TotalInterviewedAllSources { get; set; }

    /// <summary>
    /// Tổng số hồ sơ được gửi Offer
    /// </summary>
    public int TotalOfferedAllSources { get; set; }

    /// <summary>
    /// Tổng số nhân sự tuyển dụng thành công nhận việc (Hired)
    /// </summary>
    public int TotalHiredAllSources { get; set; }

    /// <summary>
    /// Tỷ lệ vượt qua sàng lọc trung bình toàn hệ thống (%)
    /// </summary>
    public double AverageScreeningPassRate { get; set; }

    /// <summary>
    /// Tỷ lệ tuyển dụng thành công trung bình toàn hệ thống (%)
    /// </summary>
    public double AverageHireRate { get; set; }

    /// <summary>
    /// Gợi ý thông minh phân bổ ngân sách tuyển dụng
    /// </summary>
    public string BudgetAllocationRecommendation { get; set; } = string.Empty;

    /// <summary>
    /// Tiêu chí lọc hiện tại đã áp dụng
    /// </summary>
    public CandidateSourceFilterDto Filter { get; set; } = new();
}
