using Ats.Web.Models.DTOs;

namespace Ats.Web.Services.Interfaces;

/// <summary>
/// Dịch vụ kết xuất báo cáo phân tích tuyển dụng và phễu chuyển đổi (SCRUM-352).
/// </summary>
public interface IRecruitmentReportService
{
    /// <summary>
    /// Tính toán các chỉ số phễu tuyển dụng, tỷ lệ chuyển đổi, tỷ lệ rơi rụng và điểm nghẽn quy trình.
    /// </summary>
    Task<RecruitmentFunnelReportDto> GetRecruitmentFunnelReportAsync(
        FunnelFilterCriteriaDto criteria,
        CancellationToken cancellationToken = default);
}
