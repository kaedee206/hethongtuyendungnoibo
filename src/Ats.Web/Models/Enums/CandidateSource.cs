namespace Ats.Web.Models.Enums;

/// <summary>
/// Nguồn gốc tiếp nhận hồ sơ ứng viên.
/// Mặc định là LINKEDIN khi thêm thủ công, hỗ trợ HEADHUNT, REFERRAL, DIRECT và PORTAL.
/// </summary>
public enum CandidateSource
{
    PORTAL = 0,
    LINKEDIN = 1,
    REFERRAL = 2,
    HEADHUNTER = 3,
    OTHER = 4,
    DIRECT = 5,
    HEADHUNT = HEADHUNTER
}

/// <summary>
/// Tiện ích mở rộng hiển thị tên tiếng Việt cho nguồn ứng viên.
/// </summary>
public static class CandidateSourceExtensions
{
    public static string GetDisplayName(this CandidateSource source) => source switch
    {
        CandidateSource.LINKEDIN => "LinkedIn",
        CandidateSource.PORTAL => "Cổng tuyển dụng (Portal)",
        CandidateSource.REFERRAL => "Giới thiệu nội bộ (Referral)",
        CandidateSource.HEADHUNTER => "Headhunt / Tuyển dụng ngoài",
        CandidateSource.DIRECT => "Nộp trực tiếp (Direct)",
        CandidateSource.OTHER => "Nguồn khác",
        _ => source.ToString()
    };
}

