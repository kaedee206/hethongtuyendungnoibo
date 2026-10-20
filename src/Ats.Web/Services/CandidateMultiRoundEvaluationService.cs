using System.Text.Json;
using Ats.Web.Constants;
using Ats.Web.Data;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Models.ViewModels.Candidates;
using Ats.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ats.Web.Services;

/// <summary>
/// Triển khai dịch vụ Tổng hợp kết quả nhiều vòng và Ra quyết định tuyển (SP - 7).
/// Tuân thủ: Primary Constructor, clean service layer, explicit transactions, UTC timestamps và audit logging.
/// </summary>
public class CandidateMultiRoundEvaluationService(
    ApplicationDbContext dbContext,
    ICandidateStageService stageService,
    ILogger<CandidateMultiRoundEvaluationService> logger) : ICandidateMultiRoundEvaluationService
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly ICandidateStageService _stageService = stageService;
    private readonly ILogger<CandidateMultiRoundEvaluationService> _logger = logger;

    public async Task<CandidateMultiRoundEvaluationSummaryViewModel> GetMultiRoundSummaryAsync(
        Guid applicationId,
        Guid currentUserId,
        string currentUserRole,
        CancellationToken cancellationToken = default)
    {
        if (applicationId == Guid.Empty)
        {
            throw new ArgumentException("Mã hồ sơ ứng tuyển không hợp lệ.", nameof(applicationId));
        }

        // 1. Tìm nạp hồ sơ ứng viên và thông tin liên kết
        var application = await _dbContext.Applications
            .Include(a => a.Candidate)
            .Include(a => a.CurrentStage)
            .Include(a => a.JobPosting)
                .ThenInclude(jp => jp.Requisition)
                    .ThenInclude(r => r.Department)
            .Include(a => a.Interviews)
                .ThenInclude(i => i.Panelists)
                    .ThenInclude(p => p.Interviewer)
            .Include(a => a.Interviews)
                .ThenInclude(i => i.Evaluations)
                    .ThenInclude(e => e.Interviewer)
            .Include(a => a.Interviews)
                .ThenInclude(i => i.Evaluations)
                    .ThenInclude(e => e.Scores)
                        .ThenInclude(s => s.Criteria)
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == applicationId && !a.IsDeleted, cancellationToken)
            ?? throw new KeyNotFoundException($"Không tìm thấy hồ sơ ứng tuyển với Id: {applicationId}");

        var candidate = application.Candidate;
        var req = application.JobPosting?.Requisition;

        // 2. Kiểm tra quyền hạn ra quyết định
        // Hiring Manager của vị trí, HR Manager, Recruiter phụ trách hoặc Admin
        var isHiringManager = req != null && req.HiringManagerId == currentUserId;
        var isAssignedRecruiter = req != null && req.AssignedRecruiterId == currentUserId;
        var normalizedRole = UserRoles.NormalizeRole(currentUserRole);
        var canMakeDecision = isHiringManager || isAssignedRecruiter ||
                              normalizedRole == UserRoles.HiringManager ||
                              normalizedRole == UserRoles.HRManager ||
                              normalizedRole == UserRoles.Admin;

        var currentUser = await _dbContext.Users.FindAsync(new object[] { currentUserId }, cancellationToken);

        // 3. Xử lý các vòng phỏng vấn và gộp toàn bộ phiếu đánh giá của từng vòng
        var roundsSummary = new List<CandidateInterviewRoundSummaryDto>();
        var allCriteriaScores = new List<CandidateCriteriaEvaluationDto>();

        var sortedInterviews = application.Interviews
            .Where(i => !i.IsDeleted)
            .OrderBy(i => i.RoundNumber)
            .ToList();

        // Nạp thêm EvaluationForms (phiếu theo khung năng lực nếu có)
        var evaluationForms = await _dbContext.EvaluationForms
            .Include(f => f.Evaluator)
            .Include(f => f.Items)
            .Where(f => f.ApplicationId == applicationId && !f.IsDeleted)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        foreach (var interview in sortedInterviews)
        {
            var roundDto = new CandidateInterviewRoundSummaryDto
            {
                InterviewId = interview.Id,
                RoundNumber = interview.RoundNumber,
                Title = string.IsNullOrWhiteSpace(interview.Title) ? $"Vòng {interview.RoundNumber}" : interview.Title,
                InterviewType = interview.InterviewType,
                LocationOrLink = interview.LocationOrLink,
                StartTime = interview.StartTime,
                EndTime = interview.EndTime,
                Status = interview.Status,
                Panelists = interview.Panelists
                    .Where(p => !p.IsDeleted)
                    .Select(p => p.Interviewer?.FullName ?? "Giám khảo")
                    .Distinct()
                    .ToList()
            };

            // Phiếu từ bảng InterviewEvaluation (Scrum-57)
            foreach (var eval in interview.Evaluations.Where(e => !e.IsDeleted))
            {
                var (recDisplay, recBadge) = FormatRecommendation(eval.Recommendation);
                var sheet = new RoundEvaluationSheetDto
                {
                    EvaluationId = eval.Id,
                    InterviewerId = eval.InterviewerId,
                    InterviewerName = eval.Interviewer?.FullName ?? "Giám khảo",
                    InterviewerEmail = eval.Interviewer?.Email ?? string.Empty,
                    OverallScore = eval.OverallScore,
                    Recommendation = eval.Recommendation,
                    RecommendationDisplay = recDisplay,
                    RecommendationBadgeClass = recBadge,
                    Strengths = eval.Strengths,
                    Weaknesses = eval.Weaknesses,
                    DetailedFeedback = eval.DetailedFeedback,
                    IsSubmitted = eval.IsSubmitted,
                    SubmittedAt = eval.SubmittedAt
                };

                foreach (var score in eval.Scores.Where(s => !s.IsDeleted && s.Criteria != null))
                {
                    var cDto = new CandidateCriteriaEvaluationDto
                    {
                        CriteriaId = score.CriteriaId,
                        CriteriaName = score.Criteria.Name,
                        CriteriaType = score.Criteria.CriteriaType,
                        Weight = score.Criteria.Weight,
                        Score = score.Score,
                        Comment = score.Comment
                    };
                    sheet.CriteriaScores.Add(cDto);
                    if (eval.IsSubmitted)
                    {
                        allCriteriaScores.Add(cDto);
                    }
                }

                roundDto.EvaluationSheets.Add(sheet);
            }

            // Phiếu từ bảng EvaluationForms (Scrum-56 - Khung năng lực) nếu chưa có trong InterviewEvaluation
            var formsForRound = evaluationForms.Where(f => f.InterviewId == interview.Id).ToList();
            foreach (var form in formsForRound)
            {
                // Nếu chưa được nạp từ InterviewEvaluation
                if (!roundDto.EvaluationSheets.Any(s => s.InterviewerId == form.EvaluatorId))
                {
                    var recType = ParseRecommendationType(form.OverallRecommendation);
                    var (recDisplay, recBadge) = FormatRecommendation(recType);

                    var sheet = new RoundEvaluationSheetDto
                    {
                        EvaluationId = form.Id,
                        InterviewerId = form.EvaluatorId,
                        InterviewerName = form.Evaluator?.FullName ?? "Đánh giá viên",
                        InterviewerEmail = form.Evaluator?.Email ?? string.Empty,
                        OverallScore = form.TotalScore,
                        Recommendation = recType,
                        RecommendationDisplay = recDisplay,
                        RecommendationBadgeClass = recBadge,
                        Strengths = form.GeneralNotes,
                        DetailedFeedback = form.GeneralNotes,
                        IsSubmitted = form.Status == EvaluationFormStatus.SUBMITTED,
                        SubmittedAt = form.SubmittedAt
                    };

                    foreach (var item in form.Items.Where(it => !it.IsDeleted))
                    {
                        var cDto = new CandidateCriteriaEvaluationDto
                        {
                            CriteriaId = item.CriteriaId,
                            CriteriaName = item.CriteriaName,
                            CriteriaType = "Khung năng lực",
                            Weight = item.Weight > 0 ? item.Weight : 1.0m,
                            Score = item.Score,
                            Comment = item.Comment
                        };
                        sheet.CriteriaScores.Add(cDto);
                        if (form.Status == EvaluationFormStatus.SUBMITTED)
                        {
                            allCriteriaScores.Add(cDto);
                        }
                    }

                    roundDto.EvaluationSheets.Add(sheet);
                }
            }

            roundsSummary.Add(roundDto);
        }

        // 4. Tính toán điểm trung bình theo từng tiêu chí qua mọi vòng
        var criteriaAverages = new List<CriteriaAverageScoreDto>();
        if (allCriteriaScores.Any())
        {
            var groupedCriteria = allCriteriaScores
                .GroupBy(c => c.CriteriaName.Trim())
                .OrderBy(g => g.Key);

            foreach (var group in groupedCriteria)
            {
                var avg = group.Average(x => x.Score);
                var first = group.First();
                criteriaAverages.Add(new CriteriaAverageScoreDto
                {
                    CriteriaName = group.Key,
                    CriteriaType = first.CriteriaType ?? "Chung",
                    Weight = first.Weight,
                    AverageScore = Math.Round(avg, 2),
                    EvaluationCount = group.Count()
                });
            }
        }

        // 5. Nạp danh sách nhật ký quyết định tuyển dụng từ AuditLog và CandidateStageHistories
        var decisionLogs = await GetHiringDecisionLogsAsync(applicationId, cancellationToken);

        var viewModel = new CandidateMultiRoundEvaluationSummaryViewModel
        {
            ApplicationId = application.Id,
            CandidateId = candidate.Id,
            CandidateName = $"{candidate.FirstName} {candidate.LastName}".Trim(),
            CandidateEmail = candidate.Email,
            CandidatePhone = candidate.Phone,
            AvatarUrl = null,
            JobPostingId = application.JobPostingId,
            JobTitle = application.JobPosting?.Title ?? "Vị trí tuyển dụng",
            JobCode = req?.Code ?? application.JobPosting?.Slug ?? "N/A",
            DepartmentName = req?.Department?.Name ?? "Phòng ban chuyên môn",
            CurrentStage = application.Stage,
            CurrentStageName = application.CurrentStage?.Name ?? application.Stage.GetDisplayName(),
            ApplicationStatus = application.Status,
            CanMakeDecision = canMakeDecision,
            CurrentUserName = currentUser?.FullName ?? "Trưởng bộ phận",
            CurrentUserRole = UserRoles.GetDisplayName(currentUserRole),
            Rounds = roundsSummary,
            CriteriaAverages = criteriaAverages,
            DecisionLogs = decisionLogs
        };

        return viewModel;
    }

    public async Task<HiringDecisionLogDto> MakeHiringDecisionAsync(
        CandidateHiringDecisionInputModel input,
        Guid currentUserId,
        string currentUserRole,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.ApplicationId == Guid.Empty)
        {
            throw new ArgumentException("Mã hồ sơ ứng tuyển không hợp lệ.");
        }

        // 1. Kiểm tra tồn tại hồ sơ và quyền quyết định
        var application = await _dbContext.Applications
            .Include(a => a.Candidate)
            .Include(a => a.JobPosting)
                .ThenInclude(jp => jp.Requisition)
            .FirstOrDefaultAsync(a => a.Id == input.ApplicationId && !a.IsDeleted, cancellationToken)
            ?? throw new KeyNotFoundException($"Không tìm thấy hồ sơ ứng tuyển với Id: {input.ApplicationId}");

        var user = await _dbContext.Users.FindAsync(new object[] { currentUserId }, cancellationToken)
            ?? throw new KeyNotFoundException($"Không tìm thấy người dùng với Id: {currentUserId}");

        var req = application.JobPosting?.Requisition;
        var isHiringManager = req != null && req.HiringManagerId == currentUserId;
        var isAssignedRecruiter = req != null && req.AssignedRecruiterId == currentUserId;
        var normalizedRole = UserRoles.NormalizeRole(currentUserRole);

        var canMakeDecision = isHiringManager || isAssignedRecruiter ||
                              normalizedRole == UserRoles.HiringManager ||
                              normalizedRole == UserRoles.HRManager ||
                              normalizedRole == UserRoles.Admin;

        if (!canMakeDecision)
        {
            throw new UnauthorizedAccessException("Bạn không có quyền ra quyết định tuyển cho hồ sơ này. Quyền thuộc về Trưởng bộ phận phụ trách vị trí hoặc Quản lý tuyển dụng.");
        }

        // 2. Chuẩn bị thao tác chuyển giai đoạn thông qua CandidateStageService
        var transitionModel = new TransitionCandidateStageInputModel
        {
            ApplicationId = input.ApplicationId,
            ToStage = input.Decision,
            RejectionReasonId = input.Decision == CandidateStage.REJECTED ? input.RejectionReasonId : null,
            Note = input.Note,
            SkipInterviewConfirmedByHrManager = normalizedRole == UserRoles.HRManager
        };

        // Thực hiện chuyển giai đoạn an toàn
        var historyResult = await _stageService.TransitionStageAsync(transitionModel, currentUserId, cancellationToken);

        // 3. Ghi bổ sung Nhật ký Quyết định chi tiết vào bảng AuditLog (SP-7: Quyết định được ghi nhật ký kèm người quyết định)
        var actionName = input.Decision == CandidateStage.OFFER ? "QUYET_DINH_DE_XUAT_OFFER" : "QUYET_DINH_LOAI_UNG_VIEN";
        var actionDisplay = input.Decision == CandidateStage.OFFER ? "Đề xuất tuyển (Offer)" : "Loại ứng viên";

        var auditLog = new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = currentUserId,
            Action = actionName,
            EntityName = nameof(Application),
            EntityId = application.Id.ToString(),
            OldValues = JsonSerializer.Serialize(new
            {
                Stage = historyResult.FromStage?.ToString(),
                Status = ApplicationStatus.IN_PROCESS.ToString()
            }),
            NewValues = JsonSerializer.Serialize(new
            {
                Stage = input.Decision.ToString(),
                Status = input.Decision == CandidateStage.REJECTED ? ApplicationStatus.REJECTED.ToString() : ApplicationStatus.IN_PROCESS.ToString(),
                RejectionReason = historyResult.RejectionReasonName,
                Note = input.Note,
                DecidedBy = user.FullName,
                DecidedByRole = UserRoles.GetDisplayName(currentUserRole),
                DecidedAt = DateTimeOffset.UtcNow
            }),
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _dbContext.AuditLogs.AddAsync(auditLog, cancellationToken);

        // Ghi thêm một bản ghi tương tác CandidateInteraction để hiển thị đồng bộ trong timeline hồ sơ
        var interaction = new CandidateInteraction
        {
            Id = Guid.NewGuid(),
            CandidateId = application.CandidateId,
            ApplicationId = application.Id,
            CreatedByUserId = currentUserId,
            InteractionType = CandidateInteractionType.NOTE,
            Title = $"Quyết định tuyển dụng: {actionDisplay}",
            Content = string.IsNullOrWhiteSpace(input.Note)
                ? (input.Decision == CandidateStage.OFFER ? "Trưởng bộ phận phê duyệt chuyển ứng viên sang giai đoạn đề xuất Offer sau khi tổng hợp toàn bộ các vòng phỏng vấn." : $"Trưởng bộ phận quyết định loại hồ sơ. Lý do: {historyResult.RejectionReasonName ?? "Không đạt"}.")
                : input.Note.Trim(),
            Outcome = actionName,
            InteractionDate = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            CreatedById = currentUserId,
            UpdatedById = currentUserId
        };

        await _dbContext.CandidateInteractions.AddAsync(interaction, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Người dùng {UserId} ({UserName}) đã ra quyết định {Decision} cho hồ sơ {AppId}",
            currentUserId, user.FullName, input.Decision, application.Id);

        return new HiringDecisionLogDto
        {
            Id = auditLog.Id,
            Action = input.Decision.ToString(),
            ActionDisplay = actionDisplay,
            BadgeClass = input.Decision == CandidateStage.OFFER ? "bg-success text-white" : "bg-danger text-white",
            Reason = historyResult.RejectionReasonName,
            Note = input.Note,
            DecidedByUserId = currentUserId,
            DecidedByUserName = user.FullName,
            DecidedByUserRole = UserRoles.GetDisplayName(currentUserRole),
            DecidedAt = auditLog.CreatedAt
        };
    }

    private async Task<List<HiringDecisionLogDto>> GetHiringDecisionLogsAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        var logs = new List<HiringDecisionLogDto>();

        // 1. Lấy từ AuditLog
        var auditLogs = await _dbContext.AuditLogs
            .Include(a => a.User)
            .Where(a => a.EntityName == nameof(Application) && a.EntityId == applicationId.ToString()
                        && (a.Action == "QUYET_DINH_DE_XUAT_OFFER" || a.Action == "QUYET_DINH_LOAI_UNG_VIEN" || a.Action.Contains("OFFER") || a.Action.Contains("STAGE_TRANSITION")))
            .OrderByDescending(a => a.CreatedAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        foreach (var a in auditLogs)
        {
            string? note = null;
            string? reason = null;
            if (!string.IsNullOrWhiteSpace(a.NewValues))
            {
                try
                {
                    using var doc = JsonDocument.Parse(a.NewValues);
                    if (doc.RootElement.TryGetProperty("Note", out var noteProp))
                    {
                        note = noteProp.GetString();
                    }
                    if (doc.RootElement.TryGetProperty("RejectionReason", out var reasonProp))
                    {
                        reason = reasonProp.GetString();
                    }
                }
                catch { }
            }

            var isOffer = a.Action == "QUYET_DINH_DE_XUAT_OFFER" || a.Action.Contains("OFFER");
            logs.Add(new HiringDecisionLogDto
            {
                Id = a.Id,
                Action = isOffer ? "OFFER" : "REJECTED",
                ActionDisplay = isOffer ? "Chuyển sang đề xuất Offer" : "Loại hồ sơ",
                BadgeClass = isOffer ? "bg-success text-white" : "bg-danger text-white",
                Reason = reason,
                Note = note,
                DecidedByUserId = a.UserId ?? Guid.Empty,
                DecidedByUserName = a.User?.FullName ?? "Quản lý tuyển dụng",
                DecidedByUserRole = UserRoles.GetDisplayName(a.User?.Role),
                DecidedAt = a.CreatedAt
            });
        }

        // 2. Nếu chưa có trong AuditLog, lấy từ CandidateStageHistories
        if (!logs.Any())
        {
            var stageHistories = await _dbContext.CandidateStageHistories
                .Include(h => h.ChangedByUser)
                .Include(h => h.RejectionReason)
                .Where(h => h.ApplicationId == applicationId && (h.ToStage == CandidateStage.OFFER || h.ToStage == CandidateStage.REJECTED))
                .OrderByDescending(h => h.CreatedAt)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            foreach (var h in stageHistories)
            {
                var isOffer = h.ToStage == CandidateStage.OFFER;
                logs.Add(new HiringDecisionLogDto
                {
                    Id = h.Id,
                    Action = h.ToStage.ToString(),
                    ActionDisplay = isOffer ? "Chuyển sang đề xuất Offer" : "Loại hồ sơ",
                    BadgeClass = isOffer ? "bg-success text-white" : "bg-danger text-white",
                    Reason = h.RejectionReason?.Name,
                    Note = h.Note,
                    DecidedByUserId = h.ChangedByUserId,
                    DecidedByUserName = h.ChangedByUser?.FullName ?? "Quản lý tuyển dụng",
                    DecidedByUserRole = UserRoles.GetDisplayName(h.ChangedByUser?.Role),
                    DecidedAt = h.CreatedAt
                });
            }
        }

        return logs;
    }

    private static (string Display, string Badge) FormatRecommendation(RecommendationType? rec) => rec switch
    {
        RecommendationType.STRONG_HIRE => ("Rất nên tuyển", "bg-success text-white"),
        RecommendationType.HIRE => ("Nên tuyển", "bg-primary text-white"),
        RecommendationType.CONSIDER => ("Cân nhắc", "bg-warning text-dark"),
        RecommendationType.STRONG_NO_HIRE => ("Không nên tuyển", "bg-danger text-white"),
        _ => ("Chưa có kết luận", "bg-secondary text-white")
    };

    private static RecommendationType? ParseRecommendationType(string? rec) => rec?.ToUpperInvariant() switch
    {
        "STRONG_HIRE" or "RẤT NÊN TUYỂN" => RecommendationType.STRONG_HIRE,
        "HIRE" or "NÊN TUYỂN" => RecommendationType.HIRE,
        "CONSIDER" or "CÂN NHẮC" => RecommendationType.CONSIDER,
        "STRONG_NO_HIRE" or "KHÔNG NÊN TUYỂN" => RecommendationType.STRONG_NO_HIRE,
        _ => null
    };
}
