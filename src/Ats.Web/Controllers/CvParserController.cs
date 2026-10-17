using Ats.Web.Models.DTOs;
using Ats.Web.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

/// <summary>
/// Controller API xử lý bóc tách thông tin ứng viên từ CV PDF (Scrum - Số 34)
/// </summary>
[ApiController]
[Route("api/cv")]
[Route("cv")]
public class CvParserController : ControllerBase
{
    private readonly ICvParserService _cvParserService;
    private readonly ILogger<CvParserController> _logger;

    public CvParserController(ICvParserService cvParserService, ILogger<CvParserController> logger)
    {
        _cvParserService = cvParserService;
        _logger = logger;
    }

    /// <summary>
    /// POST: /api/cv/parse hoặc /candidates/parse-cv
    /// Nhận tệp CV dạng PDF và trích xuất Họ tên, Email, Số điện thoại.
    /// Thiết kế fail-safe: Bóc tách lỗi không gây crash 500, trả về thông báo để người dùng tự nhập.
    /// </summary>
    [HttpPost("parse")]
    [HttpPost("/candidates/parse-cv")]
    [HttpPost("/ung-vien/parse-cv")]
    [IgnoreAntiforgeryToken]
    [RequestSizeLimit(10 * 1024 * 1024)] // Tối đa 10MB
    public async Task<IActionResult> ParseCv(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new
            {
                success = false,
                message = "Vui lòng chọn một tệp CV dạng PDF hợp lệ.",
                data = (CvParseResultDto?)null
            });
        }

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext != ".pdf")
        {
            return BadRequest(new
            {
                success = false,
                message = "Hệ thống chỉ hỗ trợ tự động bóc tách thông tin từ tệp định dạng PDF (.pdf). Bạn có thể tiếp tục nhập thông tin thủ công.",
                data = (CvParseResultDto?)null
            });
        }

        const long maxFileSize = 10 * 1024 * 1024;
        if (file.Length > maxFileSize)
        {
            return BadRequest(new
            {
                success = false,
                message = "Dung lượng tệp CV vượt quá giới hạn 10MB.",
                data = (CvParseResultDto?)null
            });
        }

        try
        {
            using var stream = file.OpenReadStream();
            var parseResult = await _cvParserService.ParsePdfCvAsync(stream, file.FileName, cancellationToken);

            return Ok(new
            {
                success = parseResult.Success,
                message = parseResult.Message,
                data = parseResult
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi không mong muốn khi tiếp nhận file CV {FileName}", file.FileName);
            return Ok(new
            {
                success = false,
                message = "Không thể bóc tách thông tin từ CV này. Bạn vui lòng nhập thông tin thủ công.",
                data = new CvParseResultDto
                {
                    Success = false,
                    Message = "Không thể bóc tách thông tin từ CV này. Bạn vui lòng nhập thông tin thủ công."
                }
            });
        }
    }
}
