namespace Ats.Web.Constants;

public static class UserRoles
{
    public const string Admin = "Admin";
    public const string HRManager = "HRManager";
    public const string HiringManager = "HiringManager";
    public const string Interviewer = "Interviewer";
    public const string Recruiter = "Recruiter";
    public const string Approver = "Approver";
    public const string Candidate = "Candidate";

    public static readonly string[] AllRoles =
    [
        Admin,
        HRManager,
        HiringManager,
        Interviewer,
        Recruiter,
        Approver,
        Candidate
    ];

    public static string GetDisplayName(string? role) => role switch
    {
        Admin or "Quản trị viên hệ thống" or "Quản trị viên" => "Quản trị viên",
        HRManager or "Trưởng phòng nhân sự" or "Trưởng phòng Nhân sự" or "Quản lý nhân sự" or "HR" => "Quản lý nhân sự",
        HiringManager or "Quản lý chuyên môn" or "Quản lý tuyển dụng" => "Quản lý tuyển dụng",
        Interviewer or "Người phỏng vấn" => "Người phỏng vấn",
        Recruiter or "Chuyên viên tuyển dụng" or "Nhân viên tuyển dụng" => "Chuyên viên tuyển dụng",
        Approver or "Người phê duyệt" or "Người duyệt" => "Người phê duyệt",
        Candidate or "Ứng viên nội bộ" => "Ứng viên",
        _ => role ?? string.Empty
    };

    public static string NormalizeRole(string? role) => role?.Trim().ToUpperInvariant() switch
    {
        "QUẢN TRỊ VIÊN HỆ THỐNG" or "ADMIN" => Admin,
        "TRƯỞNG PHÒNG NHÂN SỰ" or "HRMANAGER" or "HR_MANAGER" or "HR" => HRManager,
        "QUẢN LÝ CHUYÊN MÔN" or "HIRINGMANAGER" or "HIRING_MANAGER" => HiringManager,
        "NGƯỜI PHỎNG VẤN" or "INTERVIEWER" => Interviewer,
        "CHUYÊN VIÊN TUYỂN DỤNG" or "RECRUITER" => Recruiter,
        "NGƯỜI PHÊ DUYỆT" or "APPROVER" => Approver,
        "ỨNG VIÊN NỘI BỘ" or "CANDIDATE" => Candidate,
        _ => role ?? Candidate
    };
}