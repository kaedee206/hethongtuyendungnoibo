using Ats.Web.Data;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Models.ViewModels.Interviews;
using Ats.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ats.Web.Services;

/// <summary>
/// Dịch vụ xử lý nghiệp vụ Phiếu đánh giá phỏng vấn (Scrum - Số 57)
/// Tuân thủ quy tắc kiến trúc và coding-convention của hệ thống ATS.
/// </summary>
public class InterviewEvaluationService(
    ApplicationDbContext dbContext,
    ILogger<InterviewEvaluationService> logger) : IInterviewEvaluationService
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly ILogger<InterviewEvaluationService> _logger = logger;

    /// <summary>
    /// Quy tắc 1: Chỉ người phỏng vấn được mời mới mở được phiếu của buổi đó
    /// Quy tắc 4: Không nhìn thấy đánh giá của người khác trước khi nộp phiếu của mình
    /// </summary>
    public async Task<(bool CanOpen, string? ErrorMessage, InterviewEvaluationSheetViewModel? Model)> GetEvaluationSheetAsync(
        Guid interviewId, Guid currentUserId, bool isStaffManager, CancellationToken cancellationToken = default)
    {
        var interview = await _dbContext.Interviews
            .Include(i => i.Application)
                .ThenInclude(a => a.Candidate)
            .Include(i => i.Application)
                .ThenInclude(a => a.JobPosting)
            .Include(i => i.Panelists)
                .ThenInclude(p => p.Interviewer)
            .Include(i => i.Evaluations)
                .ThenInclude(e => e.Interviewer)
            .Include(i => i.Evaluations)
                .ThenInclude(e => e.ReopenedByUser)
            .FirstOrDefaultAsync(i => i.Id == interviewId && !i.IsDeleted, cancellationToken);

        if (interview == null)
        {
            return (false, "Không tìm thấy buổi phỏng vấn yêu cầu.", null);
        }

        // Quy tắc 1: Kiểm tra người dùng có trong danh sách Panelists được mời hay không
        var isInvited = interview.Panelists.Any(p => p.InterviewerId == currentUserId && !p.IsDeleted);

        if (!isInvited && !isStaffManager)
        {
            return (false, "Chỉ người phỏng vấn được mời mới có quyền mở phiếu đánh giá của buổi phỏng vấn này.", null);
        }

        var candidate = interview.Application?.Candidate;
        var candName = candidate != null ? $"{candidate.FirstName} {candidate.LastName}".Trim() : "Ứng viên";
        var panelistNames = interview.Panelists
            .Select(p => p.Interviewer?.FullName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToList();
        var panelistStr = panelistNames.Any() ? string.Join(", ", panelistNames) : "Hội đồng phỏng vấn";

        // Lấy phiếu đánh giá của chính người dùng hiện tại (nếu có)
        var userEval = interview.Evaluations.FirstOrDefault(e => e.InterviewerId == currentUserId && !e.IsDeleted);
        var isSubmitted = userEval != null && userEval.IsSubmitted;
        var isReopened = userEval != null && userEval.IsReopened;

        // Tính toán quyền mở lại của Recruiter (trong 24 giờ)
        var canRecruiterReopen = false;
        var hoursRemaining = 0.0;
        if (userEval != null && userEval.IsSubmitted && userEval.SubmittedAt.HasValue)
        {
            var elapsed = DateTimeOffset.UtcNow - userEval.SubmittedAt.Value;
            canRecruiterReopen = isStaffManager && elapsed <= TimeSpan.FromHours(24);
            hoursRemaining = Math.Max(0, 24.0 - elapsed.TotalHours);
        }

        // Quy tắc 4: Không nhìn thấy đánh giá của người khác trước khi nộp phiếu của mình
        var canViewOthers = isStaffManager || isSubmitted;
        var blindNotice = !canViewOthers
            ? "🔒 Đánh giá độc lập: Đánh giá của các giám khảo khác trong hội đồng sẽ hiển thị sau khi bạn hoàn tất và nộp phiếu của mình."
            : null;

        var panelEvaluations = new List<PanelistEvaluationSummaryDto>();
        if (canViewOthers)
        {
            panelEvaluations = interview.Evaluations
                .Where(e => !e.IsDeleted && e.IsSubmitted)
                .Select(e =>
                {
                    var (disp, badge) = FormatRecommendation(e.Recommendation);
                    var elapsed = e.SubmittedAt.HasValue ? DateTimeOffset.UtcNow - e.SubmittedAt.Value : TimeSpan.MaxValue;
                    var canReopen = isStaffManager && e.IsSubmitted && elapsed <= TimeSpan.FromHours(24);

                    return new PanelistEvaluationSummaryDto
                    {
                        EvaluationId = e.Id,
                        InterviewerId = e.InterviewerId,
                        InterviewerName = e.Interviewer?.FullName ?? "Giám khảo",
                        InterviewerEmail = e.Interviewer?.Email ?? "",
                        OverallScore = e.OverallScore,
                        Recommendation = e.Recommendation.ToString(),
                        RecommendationDisplay = disp,
                        RecommendationBadgeClass = badge,
                        Strengths = e.Strengths,
                        Weaknesses = e.Weaknesses,
                        DetailedFeedback = e.DetailedFeedback,
                        IsSubmitted = e.IsSubmitted,
                        SubmittedAt = e.SubmittedAt,
                        SubmittedAtDisplay = e.SubmittedAt.HasValue ? e.SubmittedAt.Value.ToString("dd/MM/yyyy HH:mm") : "",
                        IsReopened = e.IsReopened,
                        ReopenedAt = e.ReopenedAt,
                        ReopenedByUserName = e.ReopenedByUser?.FullName,
                        ReopenReason = e.ReopenReason,
                        CanRecruiterReopen = canReopen,
                        HoursRemainingToReopen = Math.Max(0, 24.0 - elapsed.TotalHours)
                    };
                })
                .ToList();
        }

        var model = new InterviewEvaluationSheetViewModel
        {
            InterviewId = interview.Id,
            ApplicationId = interview.ApplicationId,
            CandidateName = candName,
            CandidateEmail = candidate?.Email ?? "",
            JobTitle = interview.Application?.JobPosting?.Title ?? "Vị trí tuyển dụng",
            RoundTitle = interview.Title,
            RoundNumber = interview.RoundNumber,
            InterviewDateDisplay = interview.StartTime.HasValue ? interview.StartTime.Value.ToString("dd/MM/yyyy") : "Chưa ấn định",
            TimeDisplay = interview.StartTime.HasValue && interview.EndTime.HasValue
                ? $"{interview.StartTime.Value:HH:mm} - {interview.EndTime.Value:HH:mm}"
                : "14:00 - 15:30",
            LocationOrLink = interview.LocationOrLink ?? "",
            PanelistNames = panelistStr,
            IsInvitedPanelist = isInvited,
            IsStaffManager = isStaffManager,
            CurrentUserEvaluationId = userEval?.Id,
            OverallScore = userEval?.OverallScore ?? 4.0m,
            Recommendation = userEval?.Recommendation.ToString(),
            RecommendationDisplay = userEval != null ? FormatRecommendation(userEval.Recommendation).Display : null,
            Strengths = userEval?.Strengths,
            Weaknesses = userEval?.Weaknesses,
            DetailedFeedback = userEval?.DetailedFeedback,
            IsSubmitted = isSubmitted,
            SubmittedAt = userEval?.SubmittedAt,
            SubmittedAtDisplay = userEval?.SubmittedAt?.ToString("dd/MM/yyyy HH:mm"),
            IsReopened = isReopened,
            ReopenedAt = userEval?.ReopenedAt,
            ReopenedByUserName = userEval?.ReopenedByUser?.FullName,
            ReopenReason = userEval?.ReopenReason,
            CanRecruiterReopen = canRecruiterReopen,
            HoursRemainingToReopen = hoursRemaining,
            CanViewOtherEvaluations = canViewOthers,
            BlindReviewNotice = blindNotice,
            PanelEvaluations = panelEvaluations
        };

        return (true, null, model);
    }

    /// <summary>
    /// Nộp phiếu đánh giá sau buổi phỏng vấn.
    /// Quy tắc 1: Chỉ người phỏng vấn được mời mới nộp được phiếu của buổi đó.
    /// Quy tắc 2: Bắt buộc chọn kết luận: Nên tuyển, Cân nhắc, Không nên tuyển.
    /// Quy tắc 3: Đã nộp thì không sửa được, trừ khi recruiter mở lại trong 24 giờ.
    /// </summary>
    public async Task<(bool Success, string Message, Guid? EvaluationId)> SubmitEvaluationAsync(
        InterviewEvaluationSubmitInputModel input, Guid currentUserId, CancellationToken cancellationToken = default)
    {
        // 1. Kiểm tra tồn tại buổi phỏng vấn
        var interview = await _dbContext.Interviews
            .Include(i => i.Panelists)
            .Include(i => i.Evaluations)
            .Include(i => i.Application)
                .ThenInclude(a => a.Candidate)
            .Include(i => i.Application)
                .ThenInclude(a => a.JobPosting)
            .FirstOrDefaultAsync(i => i.Id == input.InterviewId && !i.IsDeleted, cancellationToken);

        if (interview == null)
        {
            return (false, "Không tìm thấy thông tin buổi phỏng vấn.", null);
        }

        // 2. Quy tắc 1: Chỉ người phỏng vấn được mời mới được nộp phiếu
        var isInvited = interview.Panelists.Any(p => p.InterviewerId == currentUserId && !p.IsDeleted);
        if (!isInvited)
        {
            return (false, "Chỉ người phỏng vấn được mời mới có quyền mở hoặc nộp phiếu đánh giá của buổi này.", null);
        }

        // 3. Quy tắc 2: Bắt buộc chọn kết luận: Nên tuyển, Cân nhắc, Không nên tuyển
        var (isValidRec, parsedRec, recDisplayName) = ParseRecommendation(input.Recommendation);
        if (!isValidRec)
        {
            return (false, "Bắt buộc chọn kết luận đánh giá: 'Nên tuyển', 'Cân nhắc' hoặc 'Không nên tuyển'.", null);
        }

        // 4. Quy tắc 3: Đã nộp thì không sửa được, trừ khi recruiter mở lại trong 24 giờ
        var userEval = interview.Evaluations.FirstOrDefault(e => e.InterviewerId == currentUserId && !e.IsDeleted);
        if (userEval != null && userEval.IsSubmitted && !userEval.IsReopened)
        {
            return (false, "Phiếu đánh giá đã được nộp và không thể chỉnh sửa, trừ khi Recruiter mở lại trong vòng 24 giờ.", userEval.Id);
        }

        if (userEval == null)
        {
            userEval = new InterviewEvaluation
            {
                Id = Guid.NewGuid(),
                InterviewId = interview.Id,
                InterviewerId = currentUserId,
                CreatedAt = DateTimeOffset.UtcNow
            };
            await _dbContext.InterviewEvaluations.AddAsync(userEval, cancellationToken);
        }

        // Ghi nhận dữ liệu đánh giá
        userEval.OverallScore = input.OverallScore ?? 4.0m;
        userEval.Recommendation = parsedRec;
        userEval.Strengths = input.Strengths?.Trim();
        userEval.Weaknesses = input.Weaknesses?.Trim();
        userEval.DetailedFeedback = input.DetailedFeedback?.Trim() ?? "Đã hoàn thành đánh giá phỏng vấn.";
        userEval.IsSubmitted = true;
        userEval.SubmittedAt = DateTimeOffset.UtcNow;
        userEval.IsReopened = false; // Reset trạng thái mở lại khi đã nộp lại
        userEval.UpdatedAt = DateTimeOffset.UtcNow;

        // Cập nhật trạng thái buổi phỏng vấn nếu tất cả giám khảo được mời đã nộp phiếu
        var allPanelistsSubmitted = interview.Panelists
            .Where(p => !p.IsDeleted)
            .All(p => p.InterviewerId == currentUserId || 
                      interview.Evaluations.Any(e => e.InterviewerId == p.InterviewerId && e.IsSubmitted && !e.IsDeleted));

        if (allPanelistsSubmitted)
        {
            interview.Status = InterviewStatus.COMPLETED;
            interview.UpdatedAt = DateTimeOffset.UtcNow;
        }

        // Pipeline stage transition tự động nếu kết luận Đạt/Không đạt
        if (parsedRec == RecommendationType.HIRE || parsedRec == RecommendationType.STRONG_HIRE)
        {
            var stageOffer = await _dbContext.PipelineStages.FirstOrDefaultAsync(s => s.StageOrder == 5, cancellationToken)
                ?? await _dbContext.PipelineStages.FirstOrDefaultAsync(s => s.Name.Contains("Offer"), cancellationToken);

            if (stageOffer != null && interview.Application != null)
            {
                var prevStageId = interview.Application.CurrentStageId;
                interview.Application.CurrentStageId = stageOffer.Id;
                interview.Application.StageEnteredAt = DateTimeOffset.UtcNow;
                interview.Application.IsSlaOverdue = false;
                interview.Application.UpdatedAt = DateTimeOffset.UtcNow;

                await _dbContext.ApplicationStageHistories.AddAsync(new ApplicationStageHistory
                {
                    Id = Guid.NewGuid(),
                    ApplicationId = interview.Application.Id,
                    FromStageId = prevStageId,
                    ToStageId = stageOffer.Id,
                    ChangedByUserId = currentUserId,
                    Comment = $"Đã phỏng vấn ĐẠT (Kết luận: {recDisplayName}). Chuyển sang giai đoạn Offer.",
                    CreatedAt = DateTimeOffset.UtcNow
                }, cancellationToken);
            }
        }
        else if (parsedRec == RecommendationType.STRONG_NO_HIRE)
        {
            var stageReject = await _dbContext.PipelineStages.FirstOrDefaultAsync(s => s.StageOrder == 7, cancellationToken)
                ?? await _dbContext.PipelineStages.FirstOrDefaultAsync(s => s.Name.Contains("Từ chối"), cancellationToken);

            if (stageReject != null && interview.Application != null)
            {
                interview.Application.CurrentStageId = stageReject.Id;
                interview.Application.Status = ApplicationStatus.REJECTED;
                interview.Application.StageEnteredAt = DateTimeOffset.UtcNow;
                interview.Application.IsSlaOverdue = false;
                interview.Application.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var candName = interview.Application?.Candidate != null
            ? $"{interview.Application.Candidate.FirstName} {interview.Application.Candidate.LastName}".Trim()
            : "ứng viên";

        _logger.LogInformation("Người phỏng vấn {UserId} đã nộp phiếu đánh giá {EvalId} cho buổi phỏng vấn {InterviewId} thành công",
            currentUserId, userEval.Id, interview.Id);

        return (true, $"📋 Phiếu đánh giá cho ứng viên {candName} đã được nộp thành công! Kết luận: {recDisplayName}.", userEval.Id);
    }

    /// <summary>
    /// Recruiter mở lại phiếu đánh giá cho người phỏng vấn trong vòng 24 giờ kể từ khi nộp.
    /// Quy tắc 3: Đã nộp thì không sửa được, trừ khi recruiter mở lại trong 24 giờ.
    /// </summary>
    public async Task<(bool Success, string Message)> ReopenEvaluationAsync(
        Guid evaluationId, Guid recruiterUserId, string? reason, CancellationToken cancellationToken = default)
    {
        var eval = await _dbContext.InterviewEvaluations
            .Include(e => e.Interviewer)
            .Include(e => e.Interview)
                .ThenInclude(i => i.Application)
                    .ThenInclude(a => a.Candidate)
            .FirstOrDefaultAsync(e => e.Id == evaluationId && !e.IsDeleted, cancellationToken);

        if (eval == null)
        {
            return (false, "Không tìm thấy phiếu đánh giá cần mở lại.");
        }

        if (!eval.IsSubmitted)
        {
            return (false, "Phiếu đánh giá chưa được nộp nên không cần mở lại.");
        }

        // Kiểm tra thời hạn 24 giờ
        if (!eval.SubmittedAt.HasValue)
        {
            return (false, "Không xác định được thời điểm nộp phiếu.");
        }

        var elapsed = DateTimeOffset.UtcNow - eval.SubmittedAt.Value;
        if (elapsed > TimeSpan.FromHours(24))
        {
            return (false, "Đã quá thời hạn 24 giờ kể từ khi nộp phiếu, không thể mở lại.");
        }

        // Mở lại phiếu cho Interviewer chỉnh sửa
        eval.IsSubmitted = false;
        eval.IsReopened = true;
        eval.ReopenedAt = DateTimeOffset.UtcNow;
        eval.ReopenedByUserId = recruiterUserId;
        eval.ReopenReason = string.IsNullOrWhiteSpace(reason) ? "Recruiter mở lại để cập nhật nhận định chi tiết." : reason.Trim();
        eval.UpdatedAt = DateTimeOffset.UtcNow;

        // Nếu buổi phỏng vấn đã đánh dấu COMPLETED, chuyển lại SCHEDULED để chờ hoàn tất phiếu
        if (eval.Interview != null && eval.Interview.Status == InterviewStatus.COMPLETED)
        {
            eval.Interview.Status = InterviewStatus.SCHEDULED;
            eval.Interview.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Recruiter {RecruiterId} đã mở lại phiếu đánh giá {EvalId} cho Interviewer {InterviewerId}",
            recruiterUserId, eval.Id, eval.InterviewerId);

        var interviewerName = eval.Interviewer?.FullName ?? "Người phỏng vấn";
        return (true, $"Đã mở lại phiếu đánh giá thành công! Giám khảo {interviewerName} hiện có thể chỉnh sửa và nộp lại.");
    }

    /// <summary>
    /// Quy tắc 4: Không nhìn thấy đánh giá của người khác trước khi nộp phiếu của mình
    /// </summary>
    public async Task<(bool Success, string? Message, List<PanelistEvaluationSummaryDto>? Evaluations)> GetPanelEvaluationsAsync(
        Guid interviewId, Guid currentUserId, bool isStaffManager, CancellationToken cancellationToken = default)
    {
        var interview = await _dbContext.Interviews
            .Include(i => i.Panelists)
            .Include(i => i.Evaluations)
                .ThenInclude(e => e.Interviewer)
            .Include(i => i.Evaluations)
                .ThenInclude(e => e.ReopenedByUser)
            .FirstOrDefaultAsync(i => i.Id == interviewId && !i.IsDeleted, cancellationToken);

        if (interview == null)
        {
            return (false, "Không tìm thấy buổi phỏng vấn.", null);
        }

        var isInvited = interview.Panelists.Any(p => p.InterviewerId == currentUserId && !p.IsDeleted);
        if (!isInvited && !isStaffManager)
        {
            return (false, "Chỉ người phỏng vấn được mời mới có quyền xem đánh giá của buổi này.", null);
        }

        // Kiểm tra xem người dùng hiện tại đã nộp phiếu chưa
        var userEval = interview.Evaluations.FirstOrDefault(e => e.InterviewerId == currentUserId && !e.IsDeleted);
        var hasUserSubmitted = userEval != null && userEval.IsSubmitted;

        if (!hasUserSubmitted && !isStaffManager)
        {
            return (false, "Bạn chưa nộp phiếu đánh giá của mình. Để đảm bảo tính khách quan và độc lập, bạn chỉ có thể xem đánh giá của các giám khảo khác sau khi đã nộp phiếu.", null);
        }

        var results = interview.Evaluations
            .Where(e => !e.IsDeleted && e.IsSubmitted)
            .Select(e =>
            {
                var (disp, badge) = FormatRecommendation(e.Recommendation);
                var elapsed = e.SubmittedAt.HasValue ? DateTimeOffset.UtcNow - e.SubmittedAt.Value : TimeSpan.MaxValue;
                var canReopen = isStaffManager && e.IsSubmitted && elapsed <= TimeSpan.FromHours(24);

                return new PanelistEvaluationSummaryDto
                {
                    EvaluationId = e.Id,
                    InterviewerId = e.InterviewerId,
                    InterviewerName = e.Interviewer?.FullName ?? "Giám khảo",
                    InterviewerEmail = e.Interviewer?.Email ?? "",
                    OverallScore = e.OverallScore,
                    Recommendation = e.Recommendation.ToString(),
                    RecommendationDisplay = disp,
                    RecommendationBadgeClass = badge,
                    Strengths = e.Strengths,
                    Weaknesses = e.Weaknesses,
                    DetailedFeedback = e.DetailedFeedback,
                    IsSubmitted = e.IsSubmitted,
                    SubmittedAt = e.SubmittedAt,
                    SubmittedAtDisplay = e.SubmittedAt.HasValue ? e.SubmittedAt.Value.ToString("dd/MM/yyyy HH:mm") : "",
                    IsReopened = e.IsReopened,
                    ReopenedAt = e.ReopenedAt,
                    ReopenedByUserName = e.ReopenedByUser?.FullName,
                    ReopenReason = e.ReopenReason,
                    CanRecruiterReopen = canReopen,
                    HoursRemainingToReopen = Math.Max(0, 24.0 - elapsed.TotalHours)
                };
            })
            .ToList();

        return (true, null, results);
    }

    /// <summary>
    /// Helper: Chuẩn hóa và ánh xạ kết luận đánh giá (Nên tuyển, Cân nhắc, Không nên tuyển)
    /// </summary>
    public static (bool IsValid, RecommendationType Parsed, string DisplayName) ParseRecommendation(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return (false, RecommendationType.CONSIDER, "");
        }

        var val = raw.Trim().ToUpperInvariant();

        if (val == "HIRE" || val == "STRONG_HIRE" || val == "PASS" || val == "NÊN TUYỂN" || val == "NEN TUYEN" || val == "NÊN TUYEN")
        {
            return (true, RecommendationType.HIRE, "Nên tuyển");
        }

        if (val == "CONSIDER" || val == "CÂN NHẮC" || val == "CAN NHAC" || val == "XEM XÉT" || val == "XEM XET")
        {
            return (true, RecommendationType.CONSIDER, "Cân nhắc");
        }

        if (val == "STRONG_NO_HIRE" || val == "NO_HIRE" || val == "REJECT" || val == "KHÔNG NÊN TUYỂN" || val == "KHONG NEN TUYEN" || val == "KHÔNG ĐẠT")
        {
            return (true, RecommendationType.STRONG_NO_HIRE, "Không nên tuyển");
        }

        return (false, RecommendationType.CONSIDER, "");
    }

    /// <summary>
    /// Helper: Trả về tên hiển thị và badge CSS cho kết luận đánh giá
    /// </summary>
    public static (string Display, string BadgeClass) FormatRecommendation(RecommendationType rec) => rec switch
    {
        RecommendationType.HIRE or RecommendationType.STRONG_HIRE => ("Nên tuyển", "bg-success text-white"),
        RecommendationType.CONSIDER => ("Cân nhắc", "bg-warning text-dark"),
        RecommendationType.STRONG_NO_HIRE => ("Không nên tuyển", "bg-danger text-white"),
        _ => ("Chưa kết luận", "bg-secondary text-white")
    };
}
