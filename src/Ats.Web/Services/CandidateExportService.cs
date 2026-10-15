using System.Text.RegularExpressions;
using Ats.Web.Data;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Services.Interfaces;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace Ats.Web.Services;

public class CandidateExportService : ICandidateExportService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<CandidateExportService> _logger;

    public CandidateExportService(
        ApplicationDbContext dbContext,
        ILogger<CandidateExportService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<(byte[] FileBytes, string FileName, string ContentType)> ExportCandidatesShortlistAsync(
        Guid? jobPostingId,
        string? stageOrder,
        string? searchKeyword,
        string? exporterFullName,
        CancellationToken cancellationToken = default)
    {
        // 1. Khởi tạo truy vấn danh sách ứng viên (Applications)
        var query = _dbContext.Applications
            .Include(a => a.Candidate)
                .ThenInclude(c => c.Resumes)
            .Include(a => a.JobPosting)
                .ThenInclude(j => j.Requisition)
                    .ThenInclude(r => r.JobPosition)
            .Include(a => a.JobPosting)
                .ThenInclude(j => j.Requisition)
                    .ThenInclude(r => r.Department)
            .Include(a => a.CurrentStage)
            .Include(a => a.Resume)
            .Where(a => !a.IsDeleted)
            .AsNoTracking()
            .AsQueryable();

        // 2. Lọc theo vị trí tuyển dụng (JobPosting)
        JobPosting? targetJob = null;
        if (jobPostingId.HasValue && jobPostingId.Value != Guid.Empty)
        {
            query = query.Where(a => a.JobPostingId == jobPostingId.Value);
            targetJob = await _dbContext.JobPostings
                .Include(j => j.Requisition)
                    .ThenInclude(r => r.JobPosition)
                .Include(j => j.Requisition)
                    .ThenInclude(r => r.Department)
                .AsNoTracking()
                .FirstOrDefaultAsync(j => j.Id == jobPostingId.Value, cancellationToken);
        }

        // 3. Lọc theo giai đoạn tuyển chọn (Stage)
        var stageFilterDisplay = "Tất cả giai đoạn";
        if (!string.IsNullOrWhiteSpace(stageOrder) && !stageOrder.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            if (stageOrder.Equals("REJECTED", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(a => a.Status == ApplicationStatus.REJECTED);
                stageFilterDisplay = "Đã từ chối";
            }
            else if (int.TryParse(stageOrder, out var order))
            {
                query = query.Where(a => a.CurrentStage.StageOrder == order && a.Status != ApplicationStatus.REJECTED);
                var matchedStage = await _dbContext.PipelineStages
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.StageOrder == order, cancellationToken);
                stageFilterDisplay = matchedStage != null ? $"{matchedStage.StageOrder:D2}. {matchedStage.Name}" : $"Giai đoạn {order}";
            }
        }

        // 4. Lọc theo từ khóa tìm kiếm (tối thiểu 3 ký tự theo quy ước hệ thống)
        var searchFilterDisplay = "Không";
        if (!string.IsNullOrWhiteSpace(searchKeyword) && searchKeyword.Trim().Length >= 3)
        {
            var keyword = searchKeyword.Trim().ToLower();
            searchFilterDisplay = $"\"{searchKeyword.Trim()}\"";
            query = query.Where(a =>
                (a.Candidate.FirstName + " " + a.Candidate.LastName).ToLower().Contains(keyword) ||
                a.Candidate.Email.ToLower().Contains(keyword) ||
                (a.Candidate.Phone != null && a.Candidate.Phone.Contains(keyword)) ||
                a.JobPosting.Title.ToLower().Contains(keyword));
        }

        // 5. Sắp xếp: Giai đoạn tăng dần, ngày nộp mới nhất lên đầu
        var applications = await query
            .OrderBy(a => a.CurrentStage.StageOrder)
            .ThenByDescending(a => a.AppliedAt ?? a.CreatedAt)
            .ToListAsync(cancellationToken);

        // 6. Xác định mã vị trí (JobCode) cho tên tệp và tiêu đề
        // SCRUM-328: "Tên tệp chứa mã vị trí và ngày xuất"
        string rawJobCode;
        string jobTitleDisplay;
        string departmentDisplay = "Toàn công ty";

        if (targetJob != null)
        {
            rawJobCode = !string.IsNullOrWhiteSpace(targetJob.Requisition?.Code)
                ? targetJob.Requisition.Code
                : (!string.IsNullOrWhiteSpace(targetJob.Requisition?.JobPosition?.Code)
                    ? targetJob.Requisition.JobPosition.Code
                    : $"REQ-{targetJob.Id.ToString()[..8].ToUpper()}");

            jobTitleDisplay = targetJob.Title;
            if (targetJob.Requisition?.Department != null)
            {
                departmentDisplay = targetJob.Requisition.Department.Name;
            }
        }
        else
        {
            rawJobCode = "TAT_CA";
            jobTitleDisplay = "Tất cả các vị trí đang tuyển dụng";
        }

        // Chuẩn hóa tên mã vị trí để dùng an toàn trong tên tệp (loại bỏ ký tự đặc biệt)
        var safeJobCode = Regex.Replace(rawJobCode.Trim(), @"[^a-zA-Z0-9_\-]", "_");
        if (string.IsNullOrWhiteSpace(safeJobCode)) safeJobCode = "JOB";

        var exportDateStr = DateTime.Now.ToString("yyyyMMdd");
        var fileName = $"DanhSach_UngVien_{safeJobCode}_{exportDateStr}.xlsx";

        // 7. Tạo tài liệu Excel với ClosedXML
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Danh sách ứng viên");
        worksheet.ShowGridLines = true;

        // Thiết lập bảng màu chuẩn ATS Novera
        var primaryGreen = XLColor.FromHtml("#059669");   // Emerald 600
        var darkNavy = XLColor.FromHtml("#0F172A");       // Slate 900
        var slateMuted = XLColor.FromHtml("#64748B");     // Slate 500
        var lightGray = XLColor.FromHtml("#F8FAFC");       // Slate 50
        var borderGray = XLColor.FromHtml("#CBD5E1");      // Slate 300

        // ==========================================
        // Banner thông tin & Tiêu đề báo cáo
        // ==========================================
        worksheet.Cell(1, 1).Value = "HỆ THỐNG TUYỂN DỤNG NỘI BỘ — NOVERA ATS";
        worksheet.Range(1, 1, 1, 12).Merge();
        worksheet.Cell(1, 1).Style.Font.FontSize = 9.5;
        worksheet.Cell(1, 1).Style.Font.Bold = true;
        worksheet.Cell(1, 1).Style.Font.FontColor = primaryGreen;
        worksheet.Row(1).Height = 20;

        worksheet.Cell(2, 1).Value = "DANH SÁCH RÚT GỌN ỨNG VIÊN";
        worksheet.Range(2, 1, 2, 12).Merge();
        worksheet.Cell(2, 1).Style.Font.FontSize = 16;
        worksheet.Cell(2, 1).Style.Font.Bold = true;
        worksheet.Cell(2, 1).Style.Font.FontColor = darkNavy;
        worksheet.Cell(2, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        worksheet.Cell(2, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        worksheet.Row(2).Height = 28;

        // Metadata block (Dành cho Trưởng bộ phận thẩm định nhanh)
        worksheet.Cell(3, 1).Value = "Vị trí tuyển dụng:";
        worksheet.Cell(3, 1).Style.Font.Bold = true;
        worksheet.Cell(3, 2).Value = $"{jobTitleDisplay} (Mã: {rawJobCode})";
        worksheet.Range(3, 2, 3, 5).Merge();

        worksheet.Cell(3, 7).Value = "Phòng ban phụ trách:";
        worksheet.Cell(3, 7).Style.Font.Bold = true;
        worksheet.Cell(3, 8).Value = departmentDisplay;
        worksheet.Range(3, 8, 3, 12).Merge();

        worksheet.Cell(4, 1).Value = "Bộ lọc giai đoạn:";
        worksheet.Cell(4, 1).Style.Font.Bold = true;
        worksheet.Cell(4, 2).Value = stageFilterDisplay;
        worksheet.Range(4, 2, 4, 5).Merge();

        worksheet.Cell(4, 7).Value = "Từ khóa tìm kiếm:";
        worksheet.Cell(4, 7).Style.Font.Bold = true;
        worksheet.Cell(4, 8).Value = searchFilterDisplay;
        worksheet.Range(4, 8, 4, 12).Merge();

        worksheet.Cell(5, 1).Value = "Ngày xuất báo cáo:";
        worksheet.Cell(5, 1).Style.Font.Bold = true;
        worksheet.Cell(5, 2).Value = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
        worksheet.Range(5, 2, 5, 5).Merge();

        worksheet.Cell(5, 7).Value = "Người xuất / Phụ trách:";
        worksheet.Cell(5, 7).Style.Font.Bold = true;
        worksheet.Cell(5, 8).Value = !string.IsNullOrWhiteSpace(exporterFullName) ? exporterFullName : "Nhân viên tuyển dụng";

        worksheet.Cell(5, 11).Value = "Tổng số hồ sơ:";
        worksheet.Cell(5, 11).Style.Font.Bold = true;
        worksheet.Cell(5, 12).Value = applications.Count;
        worksheet.Cell(5, 12).Style.Font.Bold = true;
        worksheet.Cell(5, 12).Style.Font.FontColor = primaryGreen;

        for (int r = 3; r <= 5; r++)
        {
            worksheet.Row(r).Height = 20;
            worksheet.Cell(r, 1).Style.Font.FontColor = slateMuted;
            worksheet.Cell(r, 7).Style.Font.FontColor = slateMuted;
            worksheet.Cell(r, 11).Style.Font.FontColor = slateMuted;
        }

        // ==========================================
        // Dòng tiêu đề bảng (Table Header) - Row 7
        // ==========================================
        int headerRow = 7;
        worksheet.Row(headerRow).Height = 28;

        var headers = new string[]
        {
            "STT",
            "Mã hồ sơ",
            "Họ và tên ứng viên",
            "Email liên hệ",
            "Số điện thoại",
            "Vị trí ứng tuyển",
            "Mã vị trí / Yêu cầu",
            "Giai đoạn hiện tại",
            "Trạng thái hồ sơ",
            "Ngày nộp",
            "Chức danh hiện tại",
            "Tên tệp CV đính kèm"
        };

        for (int c = 0; c < headers.Length; c++)
        {
            var cell = worksheet.Cell(headerRow, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = primaryGreen;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.OutsideBorderColor = primaryGreen;
        }

        // ==========================================
        // Điền dữ liệu ứng viên - Bắt đầu từ Row 8
        // LƯU Ý BẢO MẬT: TUYỆT ĐỐI KHÔNG XUẤT CÁC GHI CHÚ NỘI BỘ NHẠY CẢM
        // (Không xuất RejectionNote, BlacklistReason, đánh giá bảo mật nội bộ)
        // ==========================================
        int currentRow = headerRow + 1;
        int stt = 1;

        if (applications.Count == 0)
        {
            worksheet.Cell(currentRow, 1).Value = "Không có ứng viên nào phù hợp với bộ lọc hiện tại.";
            worksheet.Range(currentRow, 1, currentRow, headers.Length).Merge();
            worksheet.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            worksheet.Cell(currentRow, 1).Style.Font.Italic = true;
            worksheet.Cell(currentRow, 1).Style.Font.FontColor = slateMuted;
            worksheet.Row(currentRow).Height = 26;
            currentRow++;
        }
        else
        {
            foreach (var app in applications)
            {
                var cand = app.Candidate;
                var candFullName = $"{cand.FirstName} {cand.LastName}".Trim();
                if (string.IsNullOrWhiteSpace(candFullName)) candFullName = "Chưa cập nhật";

                var jobCode = app.JobPosting?.Requisition?.Code 
                    ?? app.JobPosting?.Requisition?.JobPosition?.Code 
                    ?? "—";

                var statusName = MapStatusToVietnamese(app.Status);
                var appliedDateDisplay = app.AppliedAt.HasValue
                    ? app.AppliedAt.Value.ToString("dd/MM/yyyy")
                    : app.CreatedAt.ToString("dd/MM/yyyy");

                var primaryResume = cand.Resumes.FirstOrDefault(r => r.IsPrimary) 
                    ?? cand.Resumes.FirstOrDefault() 
                    ?? app.Resume;
                var resumeFileName = primaryResume?.FileName ?? "Chưa đính kèm CV";

                worksheet.Row(currentRow).Height = 22;

                // Cột 1: STT
                worksheet.Cell(currentRow, 1).Value = stt++;
                worksheet.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                // Cột 2: Mã hồ sơ (Application Code ngắn gọn)
                worksheet.Cell(currentRow, 2).Value = $"APP-{app.Id.ToString()[..8].ToUpper()}";
                worksheet.Cell(currentRow, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                worksheet.Cell(currentRow, 2).Style.Font.FontName = "Consolas";

                // Cột 3: Họ và tên
                worksheet.Cell(currentRow, 3).Value = candFullName;
                worksheet.Cell(currentRow, 3).Style.Font.Bold = true;

                // Cột 4: Email
                worksheet.Cell(currentRow, 4).Value = cand.Email;

                // Cột 5: Số điện thoại
                worksheet.Cell(currentRow, 5).Value = !string.IsNullOrWhiteSpace(cand.Phone) ? cand.Phone : "Chưa có";
                worksheet.Cell(currentRow, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                // Cột 6: Vị trí ứng tuyển
                worksheet.Cell(currentRow, 6).Value = app.JobPosting?.Title ?? "—";

                // Cột 7: Mã vị trí
                worksheet.Cell(currentRow, 7).Value = jobCode;
                worksheet.Cell(currentRow, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                // Cột 8: Giai đoạn tuyển chọn hiện tại
                worksheet.Cell(currentRow, 8).Value = app.CurrentStage?.Name ?? "—";
                worksheet.Cell(currentRow, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                // Cột 9: Trạng thái hồ sơ
                worksheet.Cell(currentRow, 9).Value = statusName;
                worksheet.Cell(currentRow, 9).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                // Cột 10: Ngày nộp hồ sơ
                worksheet.Cell(currentRow, 10).Value = appliedDateDisplay;
                worksheet.Cell(currentRow, 10).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                // Cột 11: Chức danh hiện tại của ứng viên
                worksheet.Cell(currentRow, 11).Value = !string.IsNullOrWhiteSpace(cand.CurrentTitle) ? cand.CurrentTitle : "—";

                // Cột 12: Tên tệp CV đính kèm
                worksheet.Cell(currentRow, 12).Value = resumeFileName;
                worksheet.Cell(currentRow, 12).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

                // Đổi màu nền so le (Zebra rows) và viền ô
                var rowBg = (currentRow % 2 == 0) ? lightGray : XLColor.White;
                for (int col = 1; col <= headers.Length; col++)
                {
                    var cell = worksheet.Cell(currentRow, col);
                    cell.Style.Fill.BackgroundColor = rowBg;
                    cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    cell.Style.Border.OutsideBorderColor = borderGray;
                    cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                }

                currentRow++;
            }
        }

        // Kích hoạt tính năng AutoFilter để Trưởng bộ phận lọc trực tiếp trong Excel
        var lastDataRow = Math.Max(headerRow + 1, currentRow - 1);
        worksheet.Range(headerRow, 1, lastDataRow, headers.Length).SetAutoFilter();

        // Tự động căn chỉnh độ rộng cột với biên độ an toàn
        worksheet.Columns().AdjustToContents();
        worksheet.Column(1).Width = 8;   // STT
        worksheet.Column(2).Width = 16;  // Mã hồ sơ
        worksheet.Column(3).Width = 26;  // Họ tên
        worksheet.Column(4).Width = 28;  // Email
        worksheet.Column(5).Width = 16;  // SĐT
        worksheet.Column(6).Width = 32;  // Vị trí
        worksheet.Column(7).Width = 18;  // Mã vị trí
        worksheet.Column(8).Width = 20;  // Giai đoạn
        worksheet.Column(9).Width = 18;  // Trạng thái
        worksheet.Column(10).Width = 14; // Ngày nộp
        worksheet.Column(11).Width = 24; // Chức danh
        worksheet.Column(12).Width = 28; // Tệp CV

        // Chuyển đổi Workbook sang mảng byte
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        var fileBytes = stream.ToArray();

        _logger.LogInformation("SCRUM-328: Đã xuất thành công tệp Excel danh sách rút gọn {FileName} ({Count} ứng viên) bởi {Exporter}",
            fileName, applications.Count, exporterFullName ?? "Hệ thống");

        return (fileBytes, fileName, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    }

    private static string MapStatusToVietnamese(ApplicationStatus status)
    {
        return status switch
        {
            ApplicationStatus.IN_PROCESS => "Đang xử lý",
            ApplicationStatus.HIRED => "Đã tuyển dụng",
            ApplicationStatus.REJECTED => "Đã từ chối",
            ApplicationStatus.WITHDRAWN => "Ứng viên rút hồ sơ",
            _ => status.ToString()
        };
    }
}
