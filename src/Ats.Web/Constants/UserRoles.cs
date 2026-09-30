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
        Admin or "Quản trị viên hệ thống" => "Quản trị hệ thống",
        HRManager or "Trưởng phòng nhân sự" or "HR" => "Trưởng phòng Nhân sự",
        HiringManager or "Quản lý chuyên môn" => "Trưởng bộ phận",
        Interviewer or "Người phỏng vấn" => "Người phỏng vấn",
        Recruiter or "Chuyên viên tuyển dụng" => "Nhân viên tuyển dụng",
        Approver or "Người phê duyệt" => "Người duyệt",
        Candidate or "Ứng viên nội bộ" => "Ứng viên",
        _ => role ?? string.Empty
    };

    public static string NormalizeRole(string? role) => role switch
    {
        "Quản trị viên hệ thống" or "Admin" => Admin,
        "Trưởng phòng nhân sự" or "HRManager" or "HR" => HRManager,
        "Quản lý chuyên môn" or "HiringManager" => HiringManager,
        "Người phỏng vấn" or "Interviewer" => Interviewer,
        "Chuyên viên tuyển dụng" or "Recruiter" => Recruiter,
        "Người phê duyệt" or "Approver" => Approver,
        "Ứng viên nội bộ" or "Candidate" => Candidate,
        _ => role ?? Candidate
    };
}