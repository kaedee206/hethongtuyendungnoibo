namespace Ats.Web.Models.DTOs;

/// <summary>
/// DTO chứa kết quả trích xuất thông tin tự động từ CV PDF (Scrum - Số 34)
/// </summary>
public class CvParseResultDto
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public string? LinkedInUrl { get; set; }
    public string? RawTextPreview { get; set; }
    public bool HasDetectedData => !string.IsNullOrWhiteSpace(FullName) 
                                || !string.IsNullOrWhiteSpace(Email) 
                                || !string.IsNullOrWhiteSpace(PhoneNumber);
}
