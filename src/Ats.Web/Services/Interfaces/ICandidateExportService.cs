namespace Ats.Web.Services.Interfaces;

public interface ICandidateExportService
{
    /// <summary>
    /// SCRUM-328: Xuất danh sách ứng viên rút gọn ra file Excel cho Trưởng bộ phận theo các bộ lọc đang áp dụng.
    /// Tệp bảo đảm không chứa bất kỳ thông tin/ghi chú nội bộ nhạy cảm nào.
    /// Tên file có định dạng chuẩn: DanhSach_UngVien_{JobCode}_{yyyyMMdd}.xlsx
    /// </summary>
    /// <param name="jobPostingId">ID vị trí tuyển dụng (nếu lọc theo vị trí)</param>
    /// <param name="stageOrder">Mã thứ tự giai đoạn hoặc 'ALL', 'REJECTED'</param>
    /// <param name="searchKeyword">Từ khóa tìm kiếm (tên, email, sđt, vị trí)</param>
    /// <param name="exporterFullName">Tên người xuất báo cáo (Nhân viên tuyển dụng / HR)</param>
    /// <param name="cancellationToken">Hủy thao tác nếu cần</param>
    /// <returns>Bộ dữ liệu bao gồm byte file Excel, tên file chuẩn hóa và content type</returns>
    Task<(byte[] FileBytes, string FileName, string ContentType)> ExportCandidatesShortlistAsync(
        Guid? jobPostingId,
        string? stageOrder,
        string? searchKeyword,
        string? exporterFullName,
        CancellationToken cancellationToken = default);
}
