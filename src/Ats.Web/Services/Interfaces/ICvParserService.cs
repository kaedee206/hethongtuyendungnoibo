using Ats.Web.Models.DTOs;

namespace Ats.Web.Services.Interfaces;

/// <summary>
/// Service bóc tách họ tên, email, số điện thoại từ CV dạng PDF (Scrum - Số 34)
/// </summary>
public interface ICvParserService
{
    /// <summary>
    /// Bóc tách văn bản và nhận diện họ tên, email, số điện thoại từ stream file PDF.
    /// Bọc lỗi an toàn toàn diện, không bao giờ ném Exception làm hỏng luồng nộp hồ sơ.
    /// </summary>
    Task<CvParseResultDto> ParsePdfCvAsync(Stream pdfStream, string fileName, CancellationToken cancellationToken = default);
}
