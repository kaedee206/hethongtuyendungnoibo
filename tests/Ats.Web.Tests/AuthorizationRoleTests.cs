using Ats.Web.Constants;
using FluentAssertions;

namespace Ats.Web.Tests;

public class AuthorizationRoleTests
{
    [Fact]
    public void AdminRole_HasFullManagementPermissions()
    {
        // Assert
        Permissions.HasPermission(UserRoles.Admin, Permissions.UsersView).Should().BeTrue();
        Permissions.HasPermission(UserRoles.Admin, Permissions.UsersLock).Should().BeTrue();
        Permissions.HasPermission(UserRoles.Admin, Permissions.SalaryView).Should().BeTrue();
        Permissions.HasPermission(UserRoles.Admin, Permissions.CandidatesViewAll).Should().BeTrue();
    }

    [Fact]
    public void HRManagerRole_HasRecruitmentPermissions_ButDeniedUserManagement()
    {
        // Assert
        Permissions.HasPermission(UserRoles.HRManager, Permissions.RequisitionsCreate).Should().BeTrue();
        Permissions.HasPermission(UserRoles.HRManager, Permissions.CandidatesViewAll).Should().BeTrue();
        Permissions.HasPermission(UserRoles.HRManager, Permissions.SalaryView).Should().BeTrue();

        // Must NOT have user management or lock permissions (S1-05 AC)
        Permissions.HasPermission(UserRoles.HRManager, Permissions.UsersView).Should().BeFalse();
        Permissions.HasPermission(UserRoles.HRManager, Permissions.UsersCreate).Should().BeFalse();
        Permissions.HasPermission(UserRoles.HRManager, Permissions.UsersLock).Should().BeFalse();
    }

    [Fact]
    public void InterviewerRole_HasEvaluationPermissions_ButDeniedHRAndAdminPermissions()
    {
        // Assert
        Permissions.HasPermission(UserRoles.Interviewer, Permissions.InterviewsEvaluate).Should().BeTrue();

        // Must NOT have job creation, candidate editing or user management (S1-05 AC)
        Permissions.HasPermission(UserRoles.Interviewer, Permissions.RequisitionsCreate).Should().BeFalse();
        Permissions.HasPermission(UserRoles.Interviewer, Permissions.UsersCreate).Should().BeFalse();
        Permissions.HasPermission(UserRoles.Interviewer, Permissions.UsersLock).Should().BeFalse();
        Permissions.HasPermission(UserRoles.Interviewer, Permissions.SalaryView).Should().BeFalse();
    }

    [Theory]
    [InlineData("Admin", "Quản trị viên")]
    [InlineData("HRManager", "Quản lý nhân sự")]
    [InlineData("Recruiter", "Chuyên viên tuyển dụng")]
    [InlineData("HiringManager", "Quản lý tuyển dụng")]
    [InlineData("Interviewer", "Người phỏng vấn")]
    [InlineData("Approver", "Người phê duyệt")]
    public void GetDisplayName_ReturnsAccurateVietnameseName(string roleCode, string expectedDisplayName)
    {
        var displayName = UserRoles.GetDisplayName(roleCode);
        displayName.Should().Be(expectedDisplayName);
    }
}
