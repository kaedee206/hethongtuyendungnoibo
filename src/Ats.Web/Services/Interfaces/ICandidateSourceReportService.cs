using Ats.Web.Models.DTOs;

namespace Ats.Web.Services.Interfaces;

/// <summary>
/// Giao diện dịch vụ phân tích & báo cáo hiệu quả nguồn ứng viên (SP-8).
/// </summary>
public interface ICandidateSourceReportService
{
    /// <summary>
    /// Tính toán báo cáo hiệu quả nguồn ứng viên theo các tiêu chí lọc.
    /// </summary>
    Task<CandidateSourceReportSummaryDto> GetSourceEffectivenessReportAsync(
        CandidateSourceFilterDto filter,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Xuất báo cáo hiệu quả nguồn ứng viên ra file định dạng Excel (.xlsx).
    /// </summary>
    Task<byte[]> ExportSourceEffectivenessToExcelAsync(
        CandidateSourceFilterDto filter,
        CancellationToken cancellationToken = default);
}
