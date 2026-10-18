namespace Ats.Web.Constants;

/// <summary>
/// Quản lý tập trung 7 Vai trò chuẩn doanh nghiệp của hệ thống ATS (Rule 11).
/// Định nghĩa duy nhất làm Single Source of Truth cho toàn bộ ứng dụng.
/// </summary>
public static class UserRoles
{
    // ── 7 Mã vai trò chuẩn (Canonical Role Codes) ──
    public const string Candidate = "Candidate";
    public const string Recruiter = "Recruiter";
    public const string HiringManager = "HiringManager";
    public const string Interviewer = "Interviewer";
    public const string HRManager = "HRManager";
    public const string Approver = "Approver";
    public const string Admin = "Admin";

    // ── 7 Tên vai trò Tiếng Việt chuẩn hóa ──
    public const string NameCandidate = "Ứng viên";
    public const string NameRecruiter = "Nhân viên tuyển dụng";
    public const string NameHiringManager = "Trưởng bộ phận";
    public const string NameInterviewer = "Người phỏng vấn";
    public const string NameHRManager = "Trưởng phòng Nhân sự";
    public const string NameApprover = "Người duyệt";
    public const string NameAdmin = "Quản trị hệ thống";

    /// <summary>
    /// Danh sách 7 mã vai trò chuẩn theo đúng thứ tự nghiệp vụ
    /// </summary>
    public static readonly string[] AllRoles =
    [
        Candidate,
        Recruiter,
        HiringManager,
        Interviewer,
        HRManager,
        Approver,
        Admin
    ];

    /// <summary>
    /// Bảng định nghĩa chi tiết 7 vai trò: Code, Tên Tiếng Việt, Đối tượng, Mục tiêu chính
    /// </summary>
    public static readonly (string Code, string Name, string Who, string Goal)[] Definitions =
    [
        (Candidate, NameCandidate, "Người nộp hồ sơ từ bên ngoài, không có tài khoản nội bộ", "Nộp CV, theo dõi trạng thái hồ sơ, xác nhận lịch phỏng vấn và phản hồi offer"),
        (Recruiter, NameRecruiter, "Người vận hành tuyển dụng hằng ngày", "Sàng lọc CV, điều phối pipeline, đặt lịch phỏng vấn, soạn offer"),
        (HiringManager, NameHiringManager, "Người cần người, sở hữu vị trí tuyển dụng", "Tạo yêu cầu tuyển dụng, xem ứng viên của vị trí mình, quyết định tuyển"),
        (Interviewer, NameInterviewer, "Nhân sự được mời tham gia một vòng phỏng vấn", "Xem lịch, đọc CV, nộp phiếu đánh giá theo khung năng lực"),
        (HRManager, NameHRManager, "Chủ sở hữu toàn bộ hoạt động tuyển dụng", "Giám sát tất cả vị trí, phân công recruiter, theo dõi ngân sách headcount và báo cáo"),
        (Approver, NameApprover, "Ban giám đốc hoặc cấp duyệt theo hạn mức", "Phê duyệt yêu cầu tuyển dụng và offer vượt hạn mức lương"),
        (Admin, NameAdmin, "Người vận hành ứng dụng", "Quản lý tài khoản, vai trò, danh mục dùng chung, xem nhật ký hệ thống")
    ];

    /// <summary>
    /// Lấy Tên hiển thị Tiếng Việt chuẩn theo bảng 7 Vai trò
    /// </summary>
    public static string GetDisplayName(string? role) => NormalizeRole(role) switch
    {
        Candidate => NameCandidate,
        Recruiter => NameRecruiter,
        HiringManager => NameHiringManager,
        Interviewer => NameInterviewer,
        HRManager => NameHRManager,
        Approver => NameApprover,
        Admin => NameAdmin,
        _ => role ?? string.Empty
    };

    /// <summary>
    /// Lấy mô tả "Là ai trong doanh nghiệp"
    /// </summary>
    public static string GetWho(string? role) => NormalizeRole(role) switch
    {
        Candidate => "Người nộp hồ sơ từ bên ngoài, không có tài khoản nội bộ",
        Recruiter => "Người vận hành tuyển dụng hằng ngày",
        HiringManager => "Người cần người, sở hữu vị trí tuyển dụng",
        Interviewer => "Nhân sự được mời tham gia một vòng phỏng vấn",
        HRManager => "Chủ sở hữu toàn bộ hoạt động tuyển dụng",
        Approver => "Ban giám đốc hoặc cấp duyệt theo hạn mức",
        Admin => "Người vận hành ứng dụng",
        _ => string.Empty
    };

    /// <summary>
    /// Lấy mô tả "Mục tiêu chính khi dùng hệ thống"
    /// </summary>
    public static string GetGoal(string? role) => NormalizeRole(role) switch
    {
        Candidate => "Nộp CV, theo dõi trạng thái hồ sơ, xác nhận lịch phỏng vấn và phản hồi offer",
        Recruiter => "Sàng lọc CV, điều phối pipeline, đặt lịch phỏng vấn, soạn offer",
        HiringManager => "Tạo yêu cầu tuyển dụng, xem ứng viên của vị trí mình, quyết định tuyển",
        Interviewer => "Xem lịch, đọc CV, nộp phiếu đánh giá theo khung năng lực",
        HRManager => "Giám sát tất cả vị trí, phân công recruiter, theo dõi ngân sách headcount và báo cáo",
        Approver => "Phê duyệt yêu cầu tuyển dụng và offer vượt hạn mức lương",
        Admin => "Quản lý tài khoản, vai trò, danh mục dùng chung, xem nhật ký hệ thống",
        _ => string.Empty
    };

    /// <summary>
    /// Chuẩn hóa bất kỳ chuỗi vai trò nào (mã tiếng Anh, tên tiếng Việt, alias cũ) về 1 trong 7 mã Canonical
    /// </summary>
    public static string NormalizeRole(string? role) => role?.Trim().ToUpperInvariant() switch
    {
        "QUẢN TRỊ HỆ THỐNG" or "QUẢN TRỊ VIÊN HỆ THỐNG" or "QUẢN TRỊ VIÊN" or "ADMIN" => Admin,
        "TRƯỞNG PHÒNG NHÂN SỰ" or "TRƯỞNG PHÒNG NHÂN SỰ" or "HRMANAGER" or "HR_MANAGER" or "HR MANAGER" or "QUẢN LÝ NHÂN SỰ" or "HR" => HRManager,
        "TRƯỞNG BỘ PHẬN" or "QUẢN LÝ CHUYÊN MÔN" or "QUẢN LÝ TUYỂN DỤNG" or "HIRINGMANAGER" or "HIRING_MANAGER" or "HIRING MANAGER" => HiringManager,
        "NGƯỜI PHỎNG VẤN" or "INTERVIEWER" => Interviewer,
        "NHÂN VIÊN TUYỂN DỤNG" or "CHUYÊN VIÊN TUYỂN DỤNG" or "RECRUITER" => Recruiter,
        "NGƯỜI DUYỆT" or "NGƯỜI PHÊ DUYỆT" or "APPROVER" => Approver,
        "ỨNG VIÊN" or "ỨNG VIÊN NỘI BỘ" or "CANDIDATE" => Candidate,
        _ => role ?? Candidate
    };

    /// <summary>
    /// Độ ưu tiên của vai trò để xác định Primary Role khi người dùng sở hữu nhiều vai trò
    /// </summary>
    public static int GetRolePriority(string? role) => NormalizeRole(role) switch
    {
        Admin => 1,
        HRManager => 2,
        HiringManager => 3,
        Recruiter => 4,
        Approver => 5,
        Interviewer => 6,
        Candidate => 7,
        _ => 99
    };

    /// <summary>
    /// Lấy vai trò chính ưu tiên cao nhất
    /// </summary>
    public static string GetPrimaryRole(IEnumerable<string>? roles)
    {
        if (roles == null || !roles.Any()) return Candidate;
        return roles
            .Select(NormalizeRole)
            .OrderBy(GetRolePriority)
            .FirstOrDefault() ?? Candidate;
    }

    /// <summary>
    /// Lấy tất cả tên alias tương đương (Mã chuẩn, Tên chuẩn Tiếng Việt, Alias cũ) phục vụ Claim Mapping
    /// </summary>
    public static string[] GetAllRoleAliases(string? role)
    {
        var canonical = NormalizeRole(role);
        return canonical switch
        {
            Admin => [Admin, NameAdmin, "Quản trị viên hệ thống", "Quản trị viên"],
            HRManager => [HRManager, NameHRManager, "Trưởng phòng nhân sự", "Quản lý nhân sự", "HR", "HR_MANAGER", "HR Manager"],
            HiringManager => [HiringManager, NameHiringManager, "Quản lý chuyên môn", "Quản lý tuyển dụng", "HIRING_MANAGER", "Hiring Manager"],
            Interviewer => [Interviewer, NameInterviewer, "INTERVIEWER"],
            Recruiter => [Recruiter, NameRecruiter, "Chuyên viên tuyển dụng", "RECRUITER"],
            Approver => [Approver, NameApprover, "Người phê duyệt", "APPROVER"],
            Candidate => [Candidate, NameCandidate, "Ứng viên nội bộ", "CANDIDATE"],
            _ => [canonical]
        };
    }

    /// <summary>
    /// Kiểm tra vai trò có thuộc nhóm nhân sự được phép tham gia hội đồng phỏng vấn hay không.
    /// Tuyệt đối KHÔNG cho phép vai trò Ứng viên (Candidate) tham gia phỏng vấn (Rule 12).
    /// </summary>
    public static bool CanBeInterviewer(string? role)
    {
        var canonical = NormalizeRole(role);
        return canonical switch
        {
            Interviewer => true,
            HiringManager => true,
            Recruiter => true,
            HRManager => true,
            Admin => true,
            Approver => true,
            Candidate => false,
            _ => false
        };
    }
}