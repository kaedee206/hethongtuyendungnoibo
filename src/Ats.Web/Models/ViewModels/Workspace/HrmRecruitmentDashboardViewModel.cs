namespace Ats.Web.Models.ViewModels.Workspace;

public sealed class HrmRecruitmentDashboardViewModel
{
    public int OpenPositions { get; init; }
    public int NewApplicationsThisWeek { get; init; }
    public int UpcomingInterviews { get; init; }
    public int OffersAwaitingResponse { get; init; }
    public IReadOnlyList<RecruitmentDashboardPeriodPoint> ApplicationsByWeek { get; init; } = [];
    public IReadOnlyList<RecruitmentDashboardPeriodPoint> ClosedPositionsByMonth { get; init; } = [];
    public IReadOnlyList<OverdueRecruitmentPosition> OverduePositions { get; init; } = [];
    public double QueryDurationMilliseconds { get; init; }
}

public sealed class RecruitmentDashboardPeriodPoint
{
    public DateOnly PeriodStart { get; init; }
    public int Count { get; init; }
}

public sealed class OverdueRecruitmentPosition
{
    public Guid JobPostingId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? DepartmentName { get; init; }
    public DateOnly TargetHireDate { get; init; }
    public int DaysOverdue { get; init; }
    public string Status { get; init; } = string.Empty;
    public string StatusDisplay { get; init; } = string.Empty;
}
