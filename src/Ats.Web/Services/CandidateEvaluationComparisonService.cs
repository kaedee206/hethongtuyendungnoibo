using Ats.Web.Data;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Models.ViewModels.CandidateComparison;
using Ats.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ats.Web.Services;

/// <summary>
/// Cài đặt dịch vụ So sánh ứng viên theo điểm đánh giá (SCRUM-336 / Scrum - Số 58).
/// Xử lý logic tổng hợp điểm tiêu chí, sắp xếp theo điểm tổng, và lọc chỉ ứng viên có phiếu đã nộp.
/// </summary>
public class CandidateEvaluationComparisonService(
    ApplicationDbContext dbContext,
    IEvaluationCriteriaService criteriaService,
    ILogger<CandidateEvaluationComparisonService> logger) : ICandidateEvaluationComparisonService
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly IEvaluationCriteriaService _criteriaService = criteriaService;
    private readonly ILogger<CandidateEvaluationComparisonService> _logger = logger;

    public async Task<CandidateEvaluationComparisonViewModel> GetEvaluationComparisonAsync(
        Guid? jobPostingId,
        string? stageFilter = null,
        string? sortBy = null,
        CancellationToken cancellationToken = default)
    {
        // 1. Đảm bảo các tiêu chí chuẩn hóa đã được khởi tạo
        await _criteriaService.SeedStandardCriteriaIfEmptyAsync(cancellationToken);

        // 2. Lấy danh sách tiêu chí đánh giá chuẩn
        var criteriaEntities = await _dbContext.EvaluationCriterias
            .AsNoTracking()
            .Where(c => !c.IsDeleted)
            .OrderBy(c => c.CriteriaType == "HARD_SKILL" ? 1 : (c.CriteriaType == "SOFT_SKILL" ? 2 : 3))
            .ThenByDescending(c => c.Weight)
            .ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);

        var criteriaHeaders = criteriaEntities.Select(c => new EvaluationCriteriaHeaderDto
        {
            Id = c.Id,
            Name = c.Name,
            Description = c.Description,
            Weight = c.Weight,
            CriteriaType = c.CriteriaType
        }).ToList();

        // 3. Lấy danh sách các vị trí tuyển dụng có ứng viên đã được đánh giá
        var availableJobs = await GetAvailableJobsForComparisonAsync(cancellationToken);

        // 4. Xác định vị trí tuyển dụng cần so sánh
        JobPosting? targetJob = null;
        if (jobPostingId.HasValue && jobPostingId.Value != Guid.Empty)
        {
            targetJob = await _dbContext.JobPostings
                .Include(j => j.Requisition)
                    .ThenInclude(r => r.Department)
                .Include(j => j.Requisition)
                    .ThenInclude(r => r.JobPosition)
                .AsNoTracking()
                .FirstOrDefaultAsync(j => j.Id == jobPostingId.Value && !j.IsDeleted, cancellationToken);
        }

        if (targetJob == null)
        {
            // Mặc định chọn vị trí có nhiều ứng viên đã đánh giá nhất, hoặc vị trí đầu tiên
            var defaultJobOption = availableJobs.FirstOrDefault(j => j.EvaluatedCandidateCount > 0)
                ?? availableJobs.FirstOrDefault();

            if (defaultJobOption != null)
            {
                targetJob = await _dbContext.JobPostings
                    .Include(j => j.Requisition)
                        .ThenInclude(r => r.Department)
                    .Include(j => j.Requisition)
                        .ThenInclude(r => r.JobPosition)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(j => j.Id == defaultJobOption.Id && !j.IsDeleted, cancellationToken);
            }
        }

        var viewModel = new CandidateEvaluationComparisonViewModel
        {
            SelectedJobPostingId = targetJob?.Id,
            SelectedJobTitle = targetJob?.Title ?? "Chưa chọn vị trí",
            SelectedJobCode = targetJob?.Requisition?.Code
                ?? targetJob?.Requisition?.JobPosition?.Code
                ?? (targetJob != null ? $"REQ-{targetJob.Id.ToString()[..8].ToUpper()}" : string.Empty),
            DepartmentName = targetJob?.Requisition?.Department?.Name ?? "Toàn công ty",
            TargetLevel = targetJob?.Requisition?.JobPosition?.JobLevel ?? "Chuyên môn",
            AvailableJobs = availableJobs,
            CriteriaHeaders = criteriaHeaders,
            SortBy = string.IsNullOrWhiteSpace(sortBy) ? "total_desc" : sortBy,
            StageFilter = string.IsNullOrWhiteSpace(stageFilter) ? "ALL" : stageFilter
        };

        if (targetJob == null)
        {
            return viewModel;
        }

        // 5. Truy vấn danh sách ứng viên của vị trí này
        var evaluatedInterviewIds = await _dbContext.InterviewEvaluations
            .AsNoTracking()
            .Where(e => e.IsSubmitted && !e.IsDeleted)
            .Select(e => e.InterviewId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var evaluatedAppIds = await _dbContext.Interviews
            .AsNoTracking()
            .Where(i => evaluatedInterviewIds.Contains(i.Id) && !i.IsDeleted)
            .Select(i => i.ApplicationId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (evaluatedAppIds.Count == 0)
        {
            return viewModel;
        }

        var appsQuery = _dbContext.Applications
            .Include(a => a.Candidate)
                .ThenInclude(c => c.Resumes)
            .Include(a => a.CurrentStage)
            .Include(a => a.Interviews)
                .ThenInclude(i => i.Panelists)
                    .ThenInclude(p => p.Interviewer)
            .Include(a => a.Interviews)
                .ThenInclude(i => i.Evaluations)
                    .ThenInclude(e => e.Interviewer)
            .Include(a => a.Interviews)
                .ThenInclude(i => i.Evaluations)
                    .ThenInclude(e => e.Scores)
            .Where(a => a.JobPostingId == targetJob.Id
                     && evaluatedAppIds.Contains(a.Id)
                     && !a.IsDeleted
                     && a.Status != ApplicationStatus.WITHDRAWN
                     && !a.Candidate.IsMerged)
            .AsNoTracking()
            .AsQueryable();

        // Lọc theo giai đoạn nếu được chọn (ví dụ: chỉ vòng cuối / Offer / Hired)
        if (!string.IsNullOrWhiteSpace(stageFilter) && !stageFilter.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            if (stageFilter.Equals("FINAL_ROUND", StringComparison.OrdinalIgnoreCase))
            {
                // Vòng cuối: hồ sơ ở giai đoạn Phỏng vấn trở đi hoặc có vòng phỏng vấn >= 2
                appsQuery = appsQuery.Where(a =>
                    a.CurrentStage.StageOrder >= 3 ||
                    a.Interviews.Any(i => i.RoundNumber >= 2 && i.Evaluations.Any(e => e.IsSubmitted && !e.IsDeleted)));
            }
            else if (int.TryParse(stageFilter, out var stageOrder))
            {
                appsQuery = appsQuery.Where(a => a.CurrentStage.StageOrder == stageOrder);
            }
        }

        var qualifyingApplications = await appsQuery.ToListAsync(cancellationToken);

        // 6. Xử lý và tính điểm từng tiêu chí cho từng ứng viên
        var candidateRows = new List<CandidateComparisonRowDto>();

        foreach (var app in qualifyingApplications)
        {
            var cand = app.Candidate;
            var candName = $"{cand.FirstName} {cand.LastName}".Trim();
            if (string.IsNullOrWhiteSpace(candName)) candName = "Ứng viên " + app.Id.ToString()[..6];

            var primaryResume = cand.Resumes.FirstOrDefault(r => r.IsPrimary) ?? cand.Resumes.FirstOrDefault();

            // Thu thập toàn bộ phiếu đánh giá đã nộp cho ứng viên này
            var submittedEvals = app.Interviews
                .Where(i => !i.IsDeleted)
                .SelectMany(i => i.Evaluations)
                .Where(e => e.IsSubmitted && !e.IsDeleted)
                .ToList();

            if (submittedEvals.Count == 0)
            {
                continue; // Bỏ qua nếu không có phiếu nào đã nộp
            }

            // Buổi phỏng vấn mới nhất đã có đánh giá
            var latestInterview = app.Interviews
                .Where(i => !i.IsDeleted && i.Evaluations.Any(e => e.IsSubmitted && !e.IsDeleted))
                .OrderByDescending(i => i.RoundNumber)
                .ThenByDescending(i => i.StartTime ?? i.CreatedAt)
                .FirstOrDefault();

            var roundNumber = latestInterview?.RoundNumber ?? 1;
            var isFinalRound = roundNumber >= 2 || app.CurrentStage.StageOrder >= 4;
            var interviewTitle = latestInterview?.Title ?? $"Phỏng vấn vòng {roundNumber}";

            // Tên các giám khảo đã nộp phiếu
            var panelistNamesList = submittedEvals
                .Select(e => e.Interviewer?.FullName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct()
                .ToList();
            var panelistStr = panelistNamesList.Count > 0
                ? string.Join(", ", panelistNamesList)
                : "Hội đồng phỏng vấn";

            // Tính điểm trung bình từng tiêu chí
            var criteriaScoreDict = new Dictionary<Guid, decimal>();
            var allScoresInEvals = submittedEvals.SelectMany(e => e.Scores).ToList();

            // Điểm tổng trung bình các phiếu
            var validOverallScores = submittedEvals
                .Where(e => e.OverallScore.HasValue && e.OverallScore.Value > 0)
                .Select(e => e.OverallScore!.Value)
                .ToList();

            var avgOverall = validOverallScores.Count > 0
                ? Math.Round(validOverallScores.Average(), 1)
                : 3.5m;

            decimal totalWeightedScore = 0;
            decimal totalWeight = 0;

            foreach (var criteria in criteriaEntities)
            {
                var matchedScores = allScoresInEvals
                    .Where(s => s.CriteriaId == criteria.Id && s.Score > 0)
                    .Select(s => (decimal)s.Score)
                    .ToList();

                decimal scoreValue;
                if (matchedScores.Count > 0)
                {
                    scoreValue = Math.Round(matchedScores.Average(), 1);
                }
                else
                {
                    // Dự phòng an toàn: nếu bản ghi cũ chưa có EvaluationScore riêng lẻ,
                    // lấy điểm OverallScore làm mốc để không bị hiển thị 0 điểm
                    scoreValue = avgOverall;
                }

                criteriaScoreDict[criteria.Id] = scoreValue;
                totalWeightedScore += scoreValue * criteria.Weight;
                totalWeight += criteria.Weight;
            }

            var weightedAvg = totalWeight > 0
                ? Math.Round(totalWeightedScore / totalWeight, 2)
                : avgOverall;

            // Tổng hợp kết luận đề xuất (Recommendation)
            var (recKey, recDisplay, recBadge) = AggregateRecommendations(submittedEvals);

            // Điểm mạnh & Điểm yếu tổng hợp
            var strengthsList = submittedEvals
                .Where(e => !string.IsNullOrWhiteSpace(e.Strengths))
                .Select(e => e.Strengths!.Trim())
                .Distinct()
                .ToList();
            var weaknessesList = submittedEvals
                .Where(e => !string.IsNullOrWhiteSpace(e.Weaknesses))
                .Select(e => e.Weaknesses!.Trim())
                .Distinct()
                .ToList();
            var feedbackList = submittedEvals
                .Where(e => !string.IsNullOrWhiteSpace(e.DetailedFeedback))
                .Select(e => e.DetailedFeedback!.Trim())
                .Distinct()
                .ToList();

            var latestSubmittedAt = submittedEvals
                .Where(e => e.SubmittedAt.HasValue)
                .Max(e => e.SubmittedAt);

            candidateRows.Add(new CandidateComparisonRowDto
            {
                ApplicationId = app.Id,
                CandidateId = cand.Id,
                CandidateName = candName,
                Email = cand.Email,
                Phone = cand.Phone ?? "—",
                CurrentTitle = !string.IsNullOrWhiteSpace(cand.CurrentTitle) ? cand.CurrentTitle : "Ứng viên",
                ResumeFilePath = primaryResume?.FilePath,
                CurrentStageId = app.CurrentStageId,
                CurrentStageName = app.CurrentStage.Name,
                StageColor = app.CurrentStage.ColorCode ?? "#059669",
                CurrentStageOrder = app.CurrentStage.StageOrder,
                LastInterviewId = latestInterview?.Id,
                InterviewTitle = interviewTitle,
                RoundNumber = roundNumber,
                IsFinalRound = isFinalRound,
                SubmittedEvaluationsCount = submittedEvals.Count,
                PanelistNames = panelistStr,
                CriteriaScores = criteriaScoreDict,
                OverallScore = avgOverall,
                WeightedScore = weightedAvg,
                RecommendationKey = recKey,
                RecommendationDisplay = recDisplay,
                RecommendationBadgeClass = recBadge,
                Strengths = strengthsList.Count > 0 ? string.Join("; ", strengthsList) : null,
                Weaknesses = weaknessesList.Count > 0 ? string.Join("; ", weaknessesList) : null,
                DetailedFeedback = feedbackList.Count > 0 ? string.Join(" | ", feedbackList) : null,
                LastEvaluatedAt = latestSubmittedAt
            });
        }

        // 7. Sắp xếp danh sách ứng viên
        // YÊU CẦU SCRUM-336: Sắp xếp theo điểm tổng
        var sortedCandidates = viewModel.SortBy switch
        {
            "total_asc" => candidateRows.OrderBy(c => c.OverallScore).ThenBy(c => c.WeightedScore).ToList(),
            "weighted_desc" => candidateRows.OrderByDescending(c => c.WeightedScore).ThenByDescending(c => c.OverallScore).ToList(),
            "name_asc" => candidateRows.OrderBy(c => c.CandidateName).ToList(),
            _ => candidateRows.OrderByDescending(c => c.OverallScore).ThenByDescending(c => c.WeightedScore).ToList()
        };

        // Gán thứ hạng Rank
        for (int i = 0; i < sortedCandidates.Count; i++)
        {
            sortedCandidates[i].Rank = i + 1;
        }

        viewModel.Candidates = sortedCandidates;

        _logger.LogInformation("SCRUM-336: Đã tạo bảng so sánh {Count} ứng viên cho vị trí {JobTitle} (JobId: {JobId})",
            viewModel.Candidates.Count, viewModel.SelectedJobTitle, viewModel.SelectedJobPostingId);

        return viewModel;
    }

    public async Task<List<JobComparisonOptionDto>> GetAvailableJobsForComparisonAsync(
        CancellationToken cancellationToken = default)
    {
        var allJobs = await _dbContext.JobPostings
            .Include(j => j.Requisition)
                .ThenInclude(r => r.Department)
            .Include(j => j.Requisition)
                .ThenInclude(r => r.JobPosition)
            .Where(j => !j.IsDeleted)
            .OrderBy(j => j.Title)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // Đếm số lượng ứng viên có tối thiểu 1 phiếu đánh giá đã nộp theo từng JobPosting
        var evaluatedInterviewIds = await _dbContext.InterviewEvaluations
            .AsNoTracking()
            .Where(e => e.IsSubmitted && !e.IsDeleted)
            .Select(e => e.InterviewId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var evaluatedAppIds = await _dbContext.Interviews
            .AsNoTracking()
            .Where(i => evaluatedInterviewIds.Contains(i.Id) && !i.IsDeleted)
            .Select(i => i.ApplicationId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var jobEvalCounts = await _dbContext.Applications
            .Where(a => !a.IsDeleted
                     && a.Status != ApplicationStatus.WITHDRAWN
                     && !a.Candidate.IsMerged
                     && evaluatedAppIds.Contains(a.Id))
            .GroupBy(a => a.JobPostingId)
            .Select(g => new { JobPostingId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.JobPostingId, x => x.Count, cancellationToken);

        var result = allJobs.Select(j =>
        {
            jobEvalCounts.TryGetValue(j.Id, out var count);
            var code = j.Requisition?.Code
                ?? j.Requisition?.JobPosition?.Code
                ?? $"REQ-{j.Id.ToString()[..6].ToUpper()}";

            return new JobComparisonOptionDto
            {
                Id = j.Id,
                Code = code,
                Title = j.Title,
                DepartmentName = j.Requisition?.Department?.Name ?? "Toàn công ty",
                EvaluatedCandidateCount = count
            };
        })
        .OrderByDescending(j => j.EvaluatedCandidateCount)
        .ThenBy(j => j.Title)
        .ToList();

        return result;
    }

    private static (string Key, string Display, string BadgeClass) AggregateRecommendations(List<InterviewEvaluation> evaluations)
    {
        if (evaluations.Count == 0)
        {
            return ("PENDING", "Chưa có kết luận", "bg-secondary-subtle text-secondary");
        }

        var strongHire = evaluations.Count(e => e.Recommendation == RecommendationType.STRONG_HIRE);
        var hire = evaluations.Count(e => e.Recommendation == RecommendationType.HIRE);
        var consider = evaluations.Count(e => e.Recommendation == RecommendationType.CONSIDER);
        var noHire = evaluations.Count(e => e.Recommendation == RecommendationType.STRONG_NO_HIRE);

        int total = evaluations.Count;

        if (strongHire > 0 && strongHire + hire == total)
        {
            return ("STRONG_HIRE", $"Tuyển dụng tuyệt đối ({total}/{total})", "bg-success text-white shadow-sm");
        }

        if (strongHire + hire >= consider + noHire && (strongHire + hire) > 0)
        {
            var text = total > 1 ? $"Nên tuyển ({strongHire + hire}/{total} phiếu)" : "Nên tuyển";
            return ("HIRE", text, "bg-success-subtle text-success border border-success-subtle fw-semibold");
        }

        if (consider >= noHire && consider > 0)
        {
            var text = total > 1 ? $"Cân nhắc ({consider}/{total} phiếu)" : "Cân nhắc";
            return ("CONSIDER", text, "bg-warning-subtle text-warning-emphasis border border-warning-subtle fw-semibold");
        }

        var noHireText = total > 1 ? $"Không nên tuyển ({noHire}/{total} phiếu)" : "Không nên tuyển";
        return ("NO_HIRE", noHireText, "bg-danger-subtle text-danger border border-danger-subtle fw-semibold");
    }
}
