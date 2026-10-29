using System.Diagnostics;
using Ats.Web.Data;
using Ats.Web.Models.Enums;
using Ats.Web.Models.ViewModels.Workspace;
using Microsoft.EntityFrameworkCore;

namespace Ats.Web.Services;

public sealed class HrmRecruitmentDashboardService(
    ApplicationDbContext dbContext,
    TimeProvider timeProvider) : Interfaces.IHrmRecruitmentDashboardService
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public async Task<HrmRecruitmentDashboardViewModel> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var now = timeProvider.GetUtcNow();
        var nowInVietnam = now.ToOffset(VietnamOffset);
        var todayInVietnam = DateOnly.FromDateTime(nowInVietnam.DateTime);
        var currentWeekStart = StartOfWeek(todayInVietnam);
        var firstWeekStart = currentWeekStart.AddDays(-7 * 11);
        var nextWeekStart = currentWeekStart.AddDays(7);
        var firstWeekUtc = AtVietnamMidnight(firstWeekStart);
        var nextWeekUtc = AtVietnamMidnight(nextWeekStart);
        var firstMonthStart = new DateOnly(todayInVietnam.Year, todayInVietnam.Month, 1).AddMonths(-11);
        var firstMonthUtc = AtVietnamMidnight(firstMonthStart);

        var openPositions = await dbContext.JobPostings
            .AsNoTracking()
            .CountAsync(
                posting => !posting.IsDeleted &&
                           posting.Status == JobPostingStatus.PUBLISHED &&
                           (!posting.ExpiredAt.HasValue || posting.ExpiredAt > now) &&
                           !posting.Requisition.IsDeleted &&
                           (!posting.Requisition.TargetHireDate.HasValue ||
                            posting.Requisition.TargetHireDate >= todayInVietnam) &&
                           (posting.Requisition.Status == RequisitionStatus.APPROVED ||
                            posting.Requisition.Status == RequisitionStatus.IN_PROGRESS),
                cancellationToken);

        var dailyApplicationCounts = await dbContext.Applications
            .AsNoTracking()
            .Where(application => !application.IsDeleted &&
                                  application.AppliedAt.HasValue &&
                                  application.AppliedAt >= firstWeekUtc &&
                                  application.AppliedAt < nextWeekUtc)
            .GroupBy(application => new
            {
                Year = application.AppliedAt!.Value.AddHours(7).Year,
                Month = application.AppliedAt.Value.AddHours(7).Month,
                Day = application.AppliedAt.Value.AddHours(7).Day
            })
            .Select(group => new
            {
                group.Key.Year,
                group.Key.Month,
                group.Key.Day,
                Count = group.Count()
            })
            .ToListAsync(cancellationToken);

        var applicationsByDay = dailyApplicationCounts.ToDictionary(
            item => new DateOnly(item.Year, item.Month, item.Day),
            item => item.Count);
        var applicationsByWeek = Enumerable.Range(0, 12)
            .Select(index =>
            {
                var weekStart = firstWeekStart.AddDays(index * 7);
                var count = applicationsByDay
                    .Where(item => item.Key >= weekStart && item.Key < weekStart.AddDays(7))
                    .Sum(item => item.Value);
                return new RecruitmentDashboardPeriodPoint { PeriodStart = weekStart, Count = count };
            })
            .ToList();

        var closedPositionCounts = await dbContext.JobPostings
            .AsNoTracking()
            .Where(posting => !posting.IsDeleted &&
                              posting.Status == JobPostingStatus.CLOSED &&
                              posting.ClosedAt.HasValue &&
                              posting.ClosedAt >= firstMonthUtc)
            .GroupBy(posting => new
            {
                Year = posting.ClosedAt!.Value.AddHours(7).Year,
                Month = posting.ClosedAt.Value.AddHours(7).Month
            })
            .Select(group => new
            {
                group.Key.Year,
                group.Key.Month,
                Count = group.Count()
            })
            .ToListAsync(cancellationToken);

        var closedPositionsByMonthLookup = closedPositionCounts.ToDictionary(
            item => new DateOnly(item.Year, item.Month, 1),
            item => item.Count);
        var closedPositionsByMonth = Enumerable.Range(0, 12)
            .Select(index =>
            {
                var monthStart = firstMonthStart.AddMonths(index);
                closedPositionsByMonthLookup.TryGetValue(monthStart, out var count);
                return new RecruitmentDashboardPeriodPoint { PeriodStart = monthStart, Count = count };
            })
            .ToList();

        var upcomingInterviews = await dbContext.Interviews
            .AsNoTracking()
            .CountAsync(
                interview => !interview.IsDeleted &&
                             interview.Status == InterviewStatus.SCHEDULED &&
                             interview.StartTime > now,
                cancellationToken);

        var offersAwaitingResponse = await dbContext.JobOffers
            .AsNoTracking()
            .CountAsync(
                offer => !offer.IsDeleted && offer.Status == OfferStatus.SENT_TO_CANDIDATE,
                cancellationToken);

        var overduePositionData = await dbContext.JobPostings
            .AsNoTracking()
            .Where(posting => !posting.IsDeleted &&
                              (posting.Status == JobPostingStatus.PUBLISHED ||
                               posting.Status == JobPostingStatus.EXPIRED) &&
                              !posting.Requisition.IsDeleted &&
                              (posting.Requisition.Status == RequisitionStatus.APPROVED ||
                               posting.Requisition.Status == RequisitionStatus.IN_PROGRESS ||
                               posting.Requisition.Status == RequisitionStatus.PAUSED) &&
                              posting.Requisition.TargetHireDate.HasValue &&
                              posting.Requisition.TargetHireDate < todayInVietnam)
            .OrderBy(posting => posting.Requisition.TargetHireDate)
            .ThenBy(posting => posting.Title)
            .Select(posting => new
            {
                posting.Id,
                posting.Title,
                DepartmentName = posting.Requisition.Department != null &&
                                 !posting.Requisition.Department.IsDeleted
                    ? posting.Requisition.Department.Name
                    : null,
                TargetHireDate = posting.Requisition.TargetHireDate!.Value,
                posting.Status
            })
            .ToListAsync(cancellationToken);

        var overduePositions = overduePositionData
            .Select(position => new OverdueRecruitmentPosition
            {
                JobPostingId = position.Id,
                Title = position.Title,
                DepartmentName = position.DepartmentName,
                TargetHireDate = position.TargetHireDate,
                DaysOverdue = todayInVietnam.DayNumber - position.TargetHireDate.DayNumber,
                Status = position.Status.ToString(),
                StatusDisplay = position.Status == JobPostingStatus.EXPIRED ? "Đã hết hạn đăng" : "Đang đăng tuyển"
            })
            .OrderByDescending(position => position.DaysOverdue)
            .ThenBy(position => position.Title)
            .ToList();

        stopwatch.Stop();
        return new HrmRecruitmentDashboardViewModel
        {
            OpenPositions = openPositions,
            NewApplicationsThisWeek = applicationsByWeek[^1].Count,
            UpcomingInterviews = upcomingInterviews,
            OffersAwaitingResponse = offersAwaitingResponse,
            ApplicationsByWeek = applicationsByWeek,
            ClosedPositionsByMonth = closedPositionsByMonth,
            OverduePositions = overduePositions,
            QueryDurationMilliseconds = stopwatch.Elapsed.TotalMilliseconds
        };
    }

    private static DateOnly StartOfWeek(DateOnly date)
    {
        var daysSinceMonday = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-daysSinceMonday);
    }

    private static DateTimeOffset AtVietnamMidnight(DateOnly date) =>
        new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), VietnamOffset).ToUniversalTime();
}
