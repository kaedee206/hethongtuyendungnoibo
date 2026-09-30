namespace Ats.Web.Constants;

public static class Permissions
{
    // Quản trị tài khoản (S1-05, S1-08, S1-09, S1-10)
    public const string UsersView = "Permissions.Users.View";
    public const string UsersCreate = "Permissions.Users.Create";
    public const string UsersEdit = "Permissions.Users.Edit";
    public const string UsersLock = "Permissions.Users.Lock";

    // Phân quyền bảo mật dải lương (S1-05: Người phỏng vấn KHÔNG được xem dải lương)
    public const string SalaryView = "Permissions.Salary.View";

    // Phân quyền ứng viên (S1-05: Recruiter chỉ xem ứng viên vị trí của mình)
    public const string CandidatesViewAll = "Permissions.Candidates.ViewAll";
    public const string CandidatesViewAssigned = "Permissions.Candidates.ViewAssigned";

    // Phân quyền yêu cầu tuyển dụng & duyệt
    public const string RequisitionsCreate = "Permissions.Requisitions.Create";
    public const string RequisitionsApprove = "Permissions.Requisitions.Approve";

    // Phân quyền phỏng vấn
    public const string InterviewsEvaluate = "Permissions.Interviews.Evaluate";

    /// <summary>
    /// Kiểm tra quyền của một vai trò
    /// </summary>
    public static bool HasPermission(string role, string permission) =>
        HasPermission(new[] { role }, permission);

    /// <summary>
    /// Kiểm tra quyền của danh sách các vai trò
    /// </summary>
    public static bool HasPermission(IEnumerable<string> roles, string permission)
    {
        var normalizedRoles = roles.Select(UserRoles.NormalizeRole).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Admin có toàn quyền
        if (normalizedRoles.Contains(UserRoles.Admin)) return true;

        return permission switch
        {
            UsersView or UsersCreate or UsersEdit or UsersLock =>
                normalizedRoles.Contains(UserRoles.Admin),

            SalaryView =>
                normalizedRoles.Contains(UserRoles.Admin) || normalizedRoles.Contains(UserRoles.HRManager),

            CandidatesViewAll =>
                normalizedRoles.Contains(UserRoles.Admin) || normalizedRoles.Contains(UserRoles.HRManager),

            CandidatesViewAssigned =>
                normalizedRoles.Contains(UserRoles.Recruiter) || normalizedRoles.Contains(UserRoles.HiringManager),

            RequisitionsCreate =>
                normalizedRoles.Contains(UserRoles.Admin) || normalizedRoles.Contains(UserRoles.HRManager) || normalizedRoles.Contains(UserRoles.HiringManager),

            RequisitionsApprove =>
                normalizedRoles.Contains(UserRoles.Admin) || normalizedRoles.Contains(UserRoles.Approver) || normalizedRoles.Contains(UserRoles.HRManager),

            InterviewsEvaluate =>
                normalizedRoles.Contains(UserRoles.Admin) || normalizedRoles.Contains(UserRoles.Interviewer) || normalizedRoles.Contains(UserRoles.HiringManager),

            _ => false
        };
    }
}
