using System.Text;
using System.Text.RegularExpressions;
using Ats.Web.Models.DTOs;
using Ats.Web.Services.Interfaces;
using UglyToad.PdfPig;

namespace Ats.Web.Services;

/// <summary>
/// Dịch vụ bóc tách thông tin ứng viên từ CV PDF (Scrum - Số 34).
/// Sử dụng UglyToad.PdfPig thuần C# cho Linux/Docker, kết hợp Regular Expressions và Heuristics tiếng Việt.
/// </summary>
public partial class CvParserService : ICvParserService
{
    private readonly ILogger<CvParserService> _logger;

    // Các họ phổ biến của người Việt để nhận diện họ tên ứng viên
    private static readonly HashSet<string> VietnameseSurnames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Nguyễn", "Nguyen", "Trần", "Tran", "Lê", "Le", "Phạm", "Pham",
        "Hoàng", "Hoang", "Huỳnh", "Huynh", "Phan", "Vũ", "Vu", "Võ", "Vo",
        "Đặng", "Dang", "Bùi", "Bui", "Đỗ", "Do", "Hồ", "Ho", "Ngô", "Ngo",
        "Dương", "Duong", "Lý", "Ly", "Đinh", "Dinh", "Đoàn", "Doan", "Lâm", "Lam",
        "Trịnh", "Trinh", "Mai", "Đào", "Dao", "Cao", "Hà", "Ha", "Lưu", "Luu",
        "Lương", "Luong", "Thái", "Thai", "Châu", "Chau", "Tạ", "Ta", "Phùng", "Phung",
        "Tô", "To", "Vương", "Vuong", "Tăng", "Tang", "Hứa", "Hua", "Triệu", "Trieu"
    };

    // Các từ khóa nhiễu cần loại bỏ khi tìm họ tên
    private static readonly HashSet<string> NoiseTitles = new(StringComparer.OrdinalIgnoreCase)
    {
        "CURRICULUM VITAE", "CV", "RESUME", "PROFILE", "MY RESUME", "PORTFOLIO",
        "THÔNG TIN CÁ NHÂN", "THÔNG TIN LIÊN HỆ", "CONTACT INFORMATION",
        "PERSONAL INFORMATION", "CAREER OBJECTIVE", "MỤC TIÊU NGHỀ NGHIỆP",
        "KINH NGHIỆM LÀM VIỆC", "WORK EXPERIENCE", "EXPERIENCE",
        "KỸ NĂNG", "SKILLS", "TECHNICAL SKILLS", "HỌC VẤN", "EDUCATION",
        "CERTIFICATIONS", "CHỨNG CHỈ", "PROJECTS", "DỰ ÁN", "SUMMARY"
    };

    // Regex Email chuẩn RFC
    [GeneratedRegex(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b", RegexOptions.IgnoreCase)]
    private static partial Regex EmailRegex();

    // Regex Số điện thoại Việt Nam & quốc tế
    [GeneratedRegex(@"(?:(?:\+84|84|0)\s*(?:3|5|7|8|9)\d(?:\s*[\.\-]?\s*\d){7})\b")]
    private static partial Regex VietnamesePhoneRegex();

    // Regex số điện thoại mở rộng (10-11 chữ số)
    [GeneratedRegex(@"(?:\+?\d{1,3}[\s\.\-]?)?\(?\d{2,4}\)?[\s\.\-]?\d{3,4}[\s\.\-]?\d{3,4}\b")]
    private static partial Regex GenericPhoneRegex();

    // Regex LinkedIn profile
    [GeneratedRegex(@"https?:\/\/(?:[a-z]{2,3}\.)?linkedin\.com\/(?:in|pub)\/[a-zA-Z0-9\-_%]+", RegexOptions.IgnoreCase)]
    private static partial Regex LinkedInRegex();

    // Regex tiền tố họ tên
    [GeneratedRegex(@"(?:họ\s*(?:và|&)?\s*tên|full\s*name|họ\s*tên|candidate\s*name|họ\s*tên\s*ứng\s*viên)\s*[:：\-–]\s*([^\r\n,;|]+)", RegexOptions.IgnoreCase)]
    private static partial Regex NamePrefixRegex();

    public CvParserService(ILogger<CvParserService> logger)
    {
        _logger = logger;
    }

    public async Task<CvParseResultDto> ParsePdfCvAsync(Stream pdfStream, string fileName, CancellationToken cancellationToken = default)
    {
        var result = new CvParseResultDto();

        if (pdfStream == null || pdfStream.Length == 0)
        {
            result.Success = false;
            result.Message = "Tệp tin rỗng hoặc không hợp lệ.";
            return result;
        }

        try
        {
            // Đọc stream vào MemoryStream để PdfPig xử lý an toàn
            using var memoryStream = new MemoryStream();
            await pdfStream.CopyToAsync(memoryStream, cancellationToken);
            memoryStream.Position = 0;

            var fullTextBuilder = new StringBuilder();
            var pagesRead = 0;

            using (var document = PdfDocument.Open(memoryStream))
            {
                // Chỉ quét tối đa 2 trang đầu tiên (thông tin liên hệ luôn nằm ở phần đầu)
                var maxPages = Math.Min(document.NumberOfPages, 2);

                for (int pageIndex = 1; pageIndex <= maxPages; pageIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var page = document.GetPage(pageIndex);
                    var words = page.GetWords();
                    var pageText = words != null && words.Any()
                        ? string.Join(" ", words.Select(w => w.Text))
                        : page.Text;

                    if (!string.IsNullOrWhiteSpace(pageText))
                    {
                        fullTextBuilder.AppendLine(pageText);
                        pagesRead++;
                    }
                }
            }

            var fullText = fullTextBuilder.ToString();

            // Trường hợp file scan hoặc không có lớp text
            if (string.IsNullOrWhiteSpace(fullText) || pagesRead == 0)
            {
                result.Success = false;
                result.Message = "Không phát hiện văn bản trong file PDF (có thể là file ảnh scan hoặc file được mã hóa bảo mật). Bạn vui lòng nhập thông tin thủ công.";
                return result;
            }

            // Trích xuất Email
            result.Email = ExtractEmail(fullText);

            // Trích xuất Số điện thoại
            result.PhoneNumber = ExtractPhoneNumber(fullText);

            // Trích xuất Họ và tên
            result.FullName = ExtractFullName(fullText);

            // Trích xuất LinkedIn
            result.LinkedInUrl = ExtractLinkedIn(fullText);

            // Đoạn trích dẫn xem trước (tối đa 300 ký tự đầu)
            result.RawTextPreview = fullText.Length > 300 ? fullText[..300] + "..." : fullText;

            if (result.HasDetectedData)
            {
                result.Success = true;
                result.Message = "Bóc tách thông tin từ CV thành công. Vui lòng xác nhận hoặc sửa lại trước khi lưu.";
            }
            else
            {
                result.Success = false;
                result.Message = "Không tìm thấy thông tin cơ bản rõ ràng từ CV. Bạn có thể nhập thông tin thủ công.";
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // BẢO VỆ LUỒNG CHÍNH: Không ném lỗi ra ngoài làm sập việc nộp hồ sơ
            _logger.LogWarning(ex, "Lỗi khi bóc tách thông tin từ file CV PDF: {FileName}", fileName);
            result.Success = false;
            result.Message = "Không thể bóc tách thông tin từ tệp PDF này. Bạn có thể tiếp tục nhập thông tin thủ công.";
            return result;
        }
    }

    private static string? ExtractEmail(string text)
    {
        var match = EmailRegex().Match(text);
        if (!match.Success) return null;

        var email = match.Value.Trim();
        // Loại bỏ email mẫu hệ thống hoặc đuôi rác nếu có
        if (email.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
            email.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var validEndings = new[] { ".com", ".vn", ".net", ".org", ".com.vn", ".edu.vn", ".gov.vn", ".io", ".co", ".ai", ".me", ".info", ".biz" };
        if (validEndings.Any(ext => email.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
        {
            return email.ToLowerInvariant();
        }

        // Tách trường hợp dính chữ sau tên miền phổ biến (ví dụ: .comphone -> .com)
        var cutOffExtensions = new[] { ".com.vn", ".edu.vn", ".gov.vn", ".com", ".net", ".org", ".vn" };
        foreach (var dom in cutOffExtensions)
        {
            var idx = email.IndexOf(dom, StringComparison.OrdinalIgnoreCase);
            if (idx > 0 && idx + dom.Length < email.Length)
            {
                var after = email.Substring(idx + dom.Length);
                if (after.All(char.IsLetter))
                {
                    email = email.Substring(0, idx + dom.Length);
                    break;
                }
            }
        }

        return email.ToLowerInvariant();
    }

    private static string? ExtractPhoneNumber(string text)
    {
        // 1. Thử match số điện thoại Việt Nam trước
        var vnMatch = VietnamesePhoneRegex().Match(text);
        if (vnMatch.Success)
        {
            return CleanPhoneNumber(vnMatch.Value);
        }

        // 2. Thử match số điện thoại thông thường
        var matches = GenericPhoneRegex().Matches(text);
        foreach (Match match in matches)
        {
            var cleaned = CleanPhoneNumber(match.Value);
            // Kiểm tra độ dài hợp lệ số di động (10 đến 11 số)
            if (cleaned.Length is >= 10 and <= 12 && (cleaned.StartsWith('0') || cleaned.StartsWith("84")))
            {
                return cleaned;
            }
        }

        return null;
    }

    private static string CleanPhoneNumber(string rawPhone)
    {
        var digitsOnly = new string(rawPhone.Where(char.IsDigit).ToArray());
        if (digitsOnly.StartsWith("84") && digitsOnly.Length == 11)
        {
            return "0" + digitsOnly[2..];
        }
        return digitsOnly;
    }

    private static string? ExtractFullName(string text)
    {
        var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(l => l.Trim())
                        .Where(l => !string.IsNullOrWhiteSpace(l))
                        .Take(25) // Chỉ tìm trong 25 dòng đầu
                        .ToList();

        // 1. Tìm theo tiền tố "Họ và tên:", "Full Name:"
        foreach (var line in lines)
        {
            var prefixMatch = NamePrefixRegex().Match(line);
            if (prefixMatch.Success && prefixMatch.Groups.Count > 1)
            {
                var candidateName = CleanCandidateName(prefixMatch.Groups[1].Value);
                if (IsValidCandidateName(candidateName))
                {
                    return candidateName;
                }
            }
        }

        // 2. Tìm theo cấu trúc dòng tiêu đề (Dòng ngắn, 2-5 từ, chứa Họ Việt Nam hoặc Viết hoa toàn bộ)
        foreach (var line in lines)
        {
            // Bỏ qua các dòng tiêu đề chung (CV, RESUME,...)
            if (NoiseTitles.Contains(line)) continue;

            // Bỏ qua dòng có email hoặc số điện thoại
            if (line.Contains('@') || line.Any(char.IsDigit)) continue;

            var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length is < 2 or > 5) continue;

            // Kiểm tra xem từ đầu tiên có phải họ tiếng Việt phổ biến không
            if (VietnameseSurnames.Contains(words[0]))
            {
                var name = CleanCandidateName(line);
                if (IsValidCandidateName(name))
                {
                    return name;
                }
            }

            // Kiểm tra dòng VIẾT HOA TOÀN BỘ (vd: NGUYEN VAN AN)
            if (line.Length <= 40 && words.All(w => w.All(c => !char.IsLetter(c) || char.IsUpper(c))))
            {
                var name = CleanCandidateName(line);
                if (IsValidCandidateName(name))
                {
                    return name;
                }
            }
        }

        return null;
    }

    private static string CleanCandidateName(string raw)
    {
        var clean = Regex.Replace(raw, @"[|•·\-_,;:\/\\<>{}\[\]\(\)]", " ").Trim();
        return Regex.Replace(clean, @"\s+", " ");
    }

    private static bool IsValidCandidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (name.Length is < 3 or > 50) return false;
        if (name.Contains('@') || name.Any(char.IsDigit)) return false;

        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length is >= 2 and <= 6;
    }

    private static string? ExtractLinkedIn(string text)
    {
        var match = LinkedInRegex().Match(text);
        return match.Success ? match.Value.Trim() : null;
    }
}
