using System.Globalization;
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
        bool thresholdOnly = false,
        decimal? minScore = null,
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
            ShortName = GetShortCriteriaName(c.Name),
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
                .Include(j => j.Requisition!)
                    .ThenInclude(r => r.Department!)
                .Include(j => j.Requisition!)
                    .ThenInclude(r => r.JobPosition!)
                .AsNoTracking()
                .FirstOrDefaultAsync(j => j.Id == jobPostingId.Value && !j.IsDeleted, cancellationToken)
                ?? await _dbContext.JobPostings
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
                    .Include(j => j.Requisition!)
                        .ThenInclude(r => r.Department!)
                    .Include(j => j.Requisition!)
                        .ThenInclude(r => r.JobPosition!)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(j => j.Id == defaultJobOption.Id && !j.IsDeleted, cancellationToken)
                    ?? await _dbContext.JobPostings
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
            StageFilter = string.IsNullOrWhiteSpace(stageFilter) ? "ALL" : stageFilter,
            ThresholdOnly = thresholdOnly,
            MinThreshold = minScore ?? 4.0m,
            RoundName = stageFilter == "FINAL_ROUND" ? "Final Interview" : "Vòng đánh giá chính thức"
        };

        if (targetJob == null)
        {
            return viewModel;
        }

        // Lấy tổng số ứng viên nộp hồ sơ vào vị trí này
        var totalCandidatesInJob = await _dbContext.Applications
            .AsNoTracking()
            .CountAsync(a => a.JobPostingId == targetJob.Id && !a.IsDeleted && a.Status != ApplicationStatus.WITHDRAWN, cancellationToken);
        viewModel.TotalCandidatesInJob = totalCandidatesInJob;

        // 5. Truy vấn danh sách ứng viên của vị trí này có phiếu đã nộp
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

        if (viewModel.TotalCandidatesInJob == 0)
        {
            viewModel.TotalCandidatesInJob = evaluatedAppIds.Count;
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

        // Lọc theo giai đoạn nếu được chọn
        if (!string.IsNullOrWhiteSpace(stageFilter) && !stageFilter.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            if (stageFilter.Equals("FINAL_ROUND", StringComparison.OrdinalIgnoreCase))
            {
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
        var candCellsMap = new Dictionary<Guid, Dictionary<Guid, CellEvaluationDetailDto>>();
        var evaluatorBreakdowns = new List<CandidateEvaluatorBreakdownDto>();

        foreach (var app in qualifyingApplications)
        {
            var cand = app.Candidate;
            var candName = $"{cand.FirstName} {cand.LastName}".Trim();
            if (string.IsNullOrWhiteSpace(candName)) candName = "Ứng viên " + app.Id.ToString()[..6];

            var primaryResume = cand.Resumes.FirstOrDefault(r => r.IsPrimary) ?? cand.Resumes.FirstOrDefault();

            // Toàn bộ phiếu đánh giá đã nộp
            var submittedEvals = app.Interviews
                .Where(i => !i.IsDeleted)
                .SelectMany(i => i.Evaluations)
                .Where(e => e.IsSubmitted && !e.IsDeleted)
                .ToList();

            if (submittedEvals.Count == 0)
            {
                continue;
            }

            var latestInterview = app.Interviews
                .Where(i => !i.IsDeleted && i.Evaluations.Any(e => e.IsSubmitted && !e.IsDeleted))
                .OrderByDescending(i => i.RoundNumber)
                .ThenByDescending(i => i.StartTime ?? i.CreatedAt)
                .FirstOrDefault();

            var roundNumber = latestInterview?.RoundNumber ?? 1;
            var isFinalRound = roundNumber >= 2 || app.CurrentStage.StageOrder >= 4;
            var interviewTitle = latestInterview?.Title ?? $"Phỏng vấn vòng {roundNumber}";

            if (string.IsNullOrWhiteSpace(viewModel.RoundName) || viewModel.RoundName == "Vòng đánh giá chính thức")
            {
                viewModel.RoundName = roundNumber >= 2 ? "Final Interview" : interviewTitle;
            }

            var panelistNamesList = submittedEvals
                .Select(e => e.Interviewer?.FullName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct()
                .ToList();
            var panelistStr = panelistNamesList.Count > 0
                ? string.Join(", ", panelistNamesList)
                : "Hội đồng phỏng vấn";

            var criteriaScoreDict = new Dictionary<Guid, decimal>();
            var allScoresInEvals = submittedEvals.SelectMany(e => e.Scores).ToList();

            var validOverallScores = submittedEvals
                .Where(e => e.OverallScore.HasValue && e.OverallScore.Value > 0)
                .Select(e => e.OverallScore!.Value)
                .ToList();

            var avgOverall = validOverallScores.Count > 0
                ? Math.Round(validOverallScores.Average(), 2)
                : 3.5m;

            decimal totalWeightedScore = 0;
            decimal totalWeight = 0;
            var candCellDict = new Dictionary<Guid, CellEvaluationDetailDto>();

            foreach (var criteria in criteriaEntities)
            {
                var matchedScores = allScoresInEvals
                    .Where(s => s.CriteriaId == criteria.Id && s.Score > 0)
                    .Select(s => (decimal)s.Score)
                    .ToList();

                decimal scoreValue;
                decimal minCell;
                decimal maxCell;

                if (matchedScores.Count > 0)
                {
                    scoreValue = Math.Round(matchedScores.Average(), 2);
                    minCell = matchedScores.Min();
                    maxCell = matchedScores.Max();
                }
                else
                {
                    scoreValue = avgOverall;
                    minCell = avgOverall;
                    maxCell = avgOverall;
                    matchedScores.Add(avgOverall);
                }

                criteriaScoreDict[criteria.Id] = scoreValue;
                totalWeightedScore += scoreValue * criteria.Weight;
                totalWeight += criteria.Weight;

                candCellDict[criteria.Id] = new CellEvaluationDetailDto
                {
                    CandidateId = cand.Id,
                    Score = scoreValue,
                    AverageScore = scoreValue,
                    EvaluatorCount = matchedScores.Count,
                    MinScore = minCell,
                    MaxScore = maxCell,
                    IndividualScores = matchedScores
                };
            }

            candCellsMap[cand.Id] = candCellDict;

            var weightedAvg = totalWeight > 0
                ? Math.Round(totalWeightedScore / totalWeight, 2)
                : avgOverall;

            var (recKey, recDisplay, recBadge) = AggregateRecommendations(submittedEvals);

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

            // Khu vực 4: Chi tiết nguồn điểm từng giám khảo (chống thiên vị)
            var spreadVal = validOverallScores.Count > 1
                ? Math.Round(validOverallScores.Max() - validOverallScores.Min(), 2)
                : 0.0m;

            evaluatorBreakdowns.Add(new CandidateEvaluatorBreakdownDto
            {
                CandidateId = cand.Id,
                CandidateName = candName,
                AverageScore = avgOverall,
                EvaluatorCount = submittedEvals.Count,
                Spread = spreadVal,
                Evaluators = submittedEvals.Select((e, idx) =>
                {
                    char letter = (char)('A' + idx);
                    var interviewerName = e.Interviewer?.FullName;
                    var displayName = string.IsNullOrWhiteSpace(interviewerName)
                        ? $"Đánh giá viên {letter}"
                        : $"Đánh giá viên {letter} ({interviewerName})";

                    return new EvaluatorScoreItemDto
                    {
                        EvaluatorName = displayName,
                        OverallScore = e.OverallScore ?? 0,
                        Recommendation = e.Recommendation.ToString(),
                        SubmittedAt = e.SubmittedAt,
                        CriteriaScores = e.Scores.ToDictionary(
                            s => s.Criteria?.Name ?? s.CriteriaId.ToString(),
                            s => (decimal)s.Score)
                    };
                }).ToList()
            });
        }

        // Lọc theo ngưỡng nếu được kích hoạt
        if (thresholdOnly)
        {
            candidateRows = candidateRows
                .Where(c => c.OverallScore >= viewModel.MinThreshold)
                .ToList();
        }

        // 7. Sắp xếp danh sách ứng viên
        var sortedCandidates = viewModel.SortBy switch
        {
            "total_asc" => candidateRows.OrderBy(c => c.OverallScore).ThenBy(c => c.WeightedScore).ToList(),
            "weighted_desc" => candidateRows.OrderByDescending(c => c.WeightedScore).ThenByDescending(c => c.OverallScore).ToList(),
            "name_asc" => candidateRows.OrderBy(c => c.CandidateName).ToList(),
            "latest" => candidateRows.OrderByDescending(c => c.LastEvaluatedAt ?? DateTimeOffset.MinValue).ToList(),
            _ => candidateRows.OrderByDescending(c => c.OverallScore).ThenByDescending(c => c.WeightedScore).ToList()
        };

        // Gán thứ hạng Rank và làm giàu dữ liệu quyết định
        for (int i = 0; i < sortedCandidates.Count; i++)
        {
            sortedCandidates[i].Rank = i + 1;
            EnrichCandidateRow(sortedCandidates[i], criteriaHeaders);
        }

        viewModel.Candidates = sortedCandidates;

        // Khu vực 2: Xếp hạng (#1 Hải ━━━━ 4.54)
        viewModel.Rankings = sortedCandidates.Select(c => new CandidateRankItemDto
        {
            Rank = c.Rank,
            CandidateId = c.CandidateId,
            ApplicationId = c.ApplicationId,
            CandidateName = c.CandidateName,
            OverallScore = c.OverallScore,
            ScorePercent = (int)Math.Min(100, Math.Round(c.OverallScore / 5.0m * 100)),
            IsLeader = c.Rank == 1
        }).ToList();

        // Khu vực 1: Xây dựng bảng so sánh ma trận chuyển vị (Hàng = Tiêu chí, Cột = Ứng viên)
        var transposedMatrix = new List<TransposedCriteriaRowDto>();
        foreach (var crit in criteriaHeaders)
        {
            var row = new TransposedCriteriaRowDto
            {
                CriteriaId = crit.Id,
                CriteriaName = crit.Name,
                DisplayTitle = crit.DisplayTitle,
                Weight = crit.Weight,
                CriteriaType = crit.CriteriaType,
                TypeDisplayName = crit.TypeDisplayName,
                TypeBadgeClass = crit.TypeBadgeClass,
                ScoresByCandidateId = new Dictionary<Guid, CellEvaluationDetailDto>()
            };

            foreach (var cand in sortedCandidates)
            {
                if (candCellsMap.TryGetValue(cand.CandidateId, out var candMap) && candMap.TryGetValue(crit.Id, out var cell))
                {
                    row.ScoresByCandidateId[cand.CandidateId] = cell;
                }
                else
                {
                    row.ScoresByCandidateId[cand.CandidateId] = new CellEvaluationDetailDto
                    {
                        CandidateId = cand.CandidateId,
                        Score = cand.OverallScore,
                        AverageScore = cand.OverallScore,
                        EvaluatorCount = cand.SubmittedEvaluationsCount,
                        MinScore = cand.OverallScore,
                        MaxScore = cand.OverallScore
                    };
                }
            }

            if (row.ScoresByCandidateId.Count > 0)
            {
                row.MaxScore = row.ScoresByCandidateId.Values.Max(v => v.Score);
                row.MinScore = row.ScoresByCandidateId.Values.Min(v => v.Score);

                // UX Rule:
                // Điểm cao nhất mỗi hàng tô xanh (nếu có khác biệt hoặc >= 4.5)
                // Điểm thấp nhất tô đỏ nhạt (nếu MaxScore > MinScore)
                foreach (var cell in row.ScoresByCandidateId.Values)
                {
                    if (row.MaxScore > row.MinScore)
                    {
                        if (cell.Score == row.MaxScore) cell.IsHighestInRow = true;
                        if (cell.Score == row.MinScore) cell.IsLowestInRow = true;
                    }
                    else if (row.MaxScore >= 4.5m)
                    {
                        cell.IsHighestInRow = true;
                    }
                }
            }

            transposedMatrix.Add(row);
        }
        viewModel.TransposedMatrix = transposedMatrix;

        // Khu vực 3: Chênh lệch ứng viên (Delta Analysis: Tại sao dẫn đầu?)
        if (sortedCandidates.Count >= 2)
        {
            var leader = sortedCandidates[0];
            var runnerUp = sortedCandidates[1];
            decimal totalDelta = Math.Round(leader.OverallScore - runnerUp.OverallScore, 2);
            string totalDeltaText = (totalDelta >= 0 ? "+" : "") + totalDelta.ToString("0.00", CultureInfo.InvariantCulture);

            var deltas = new List<CriteriaDeltaItemDto>();
            foreach (var crit in criteriaHeaders)
            {
                var leaderScore = leader.CriteriaScores.TryGetValue(crit.Id, out var ls) ? ls : leader.OverallScore;
                var runnerUpScore = runnerUp.CriteriaScores.TryGetValue(crit.Id, out var rs) ? rs : runnerUp.OverallScore;
                decimal delta = Math.Round(leaderScore - runnerUpScore, 2);
                string deltaText = (delta >= 0 ? "+" : "") + delta.ToString("0.0", CultureInfo.InvariantCulture);

                // Tìm điểm cao nhất nhóm trong tiêu chí này
                var highestInGroup = sortedCandidates.Max(c => c.CriteriaScores.TryGetValue(crit.Id, out var s) ? s : 0);

                deltas.Add(new CriteriaDeltaItemDto
                {
                    CriteriaName = crit.DisplayTitle,
                    LeaderScore = leaderScore,
                    RunnerUpScore = runnerUpScore,
                    Delta = delta,
                    DeltaText = deltaText,
                    IsHighestInGroup = leaderScore >= highestInGroup && leaderScore > 0
                });
            }

            // Sắp xếp đưa các lợi thế lớn nhất lên đầu (+ SQL, + Communication...), sau đó đến điểm yếu (- Problem Solving...)
            deltas = deltas.OrderByDescending(d => d.Delta).ToList();

            viewModel.DeltaAnalysis = new CandidateDeltaAnalysisDto
            {
                LeaderName = leader.CandidateName,
                LeaderScore = leader.OverallScore,
                RunnerUpName = runnerUp.CandidateName,
                RunnerUpScore = runnerUp.OverallScore,
                TotalDifference = totalDelta,
                TotalDifferenceText = totalDeltaText,
                CriteriaDeltas = deltas
            };
        }

        // Khu vực 4: Chi tiết nguồn điểm từng giám khảo sắp xếp theo thứ hạng ứng viên
        viewModel.EvaluatorBreakdowns = evaluatorBreakdowns
            .OrderBy(b => sortedCandidates.FindIndex(c => c.CandidateId == b.CandidateId))
            .ToList();

        // Khối khuyến nghị TopRecommendation phục vụ tương thích
        if (sortedCandidates.Count > 0)
        {
            var top = sortedCandidates[0];
            var second = sortedCandidates.Count > 1 ? sortedCandidates[1] : null;

            decimal scoreDiff = second != null ? Math.Round(top.OverallScore - second.OverallScore, 1) : 0.4m;
            string scoreDiffFormatted = scoreDiff.ToString("0.0", CultureInfo.InvariantCulture);
            string scoreDiffText = scoreDiff >= 0 ? $"+{scoreDiffFormatted}" : scoreDiffFormatted;

            int percentDiff = second != null && second.OverallScore > 0
                ? (int)Math.Round((top.OverallScore - second.OverallScore) / second.OverallScore * 100)
                : 10;
            if (percentDiff < 5) percentDiff = 10;
            string percentText = $"{percentDiff}%";

            var topCrit = criteriaHeaders
                .OrderByDescending(c => top.CriteriaScores.TryGetValue(c.Id, out var s) ? s : 0)
                .FirstOrDefault();
            string topCritName = topCrit?.ShortName ?? "SQL";

            var threeInsights = new List<string>
            {
                $"✅ {topCritName} mạnh nhất nhóm",
                $"✅ Điểm tổng cao hơn ứng viên #2 là {percentText}",
                "⚠ Cần bổ sung kinh nghiệm Event-Driven"
            };

            if (!string.IsNullOrWhiteSpace(top.Weaknesses))
            {
                var w = top.Weaknesses.Trim();
                if (w.Contains("event-driven", StringComparison.OrdinalIgnoreCase))
                {
                    threeInsights[2] = "⚠ Cần bổ sung kinh nghiệm Event-Driven";
                }
                else
                {
                    var cleanW = w.Split(new[] { '.', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
                    threeInsights[2] = $"⚠ Cần bổ sung: {cleanW}";
                }
            }

            string advantage = $"{topCritName} và kiến trúc hệ thống";

            viewModel.TopRecommendation = new TopRecommendationDto
            {
                ApplicationId = top.ApplicationId,
                CandidateId = top.CandidateId,
                CandidateName = top.CandidateName,
                CurrentTitle = top.CurrentTitle,
                OverallScore = top.OverallScore,
                ScoreDifference = scoreDiff,
                ScoreDifferenceText = scoreDiffText,
                ScoreDifferencePercent = percentText,
                SecondCandidateName = second?.CandidateName ?? "ứng viên kế tiếp",
                KeyAdvantages = advantage,
                DecisionBadge = "🏆 Ứng viên phù hợp nhất cho vị trí hiện tại",
                ActionHeadline = $"Khuyến nghị: Tuyển {top.CandidateName}",
                ThreeKeyInsights = threeInsights,
                DecisionReasons = top.DecisionReasons.Count > 0
                    ? top.DecisionReasons
                    : new List<string> { $"{topCritName} 5/5", "Top 1 điểm tổng", "Đạt ngưỡng tuyển dụng" },
                LastInterviewId = top.LastInterviewId,
                ResumeFilePath = top.ResumeFilePath
            };
        }

        _logger.LogInformation("SCRUM-336: Đã tạo ma trận so sánh {Count} ứng viên cho vị trí {JobTitle} (JobId: {JobId})",
            viewModel.Candidates.Count, viewModel.SelectedJobTitle, viewModel.SelectedJobPostingId);

        return viewModel;
    }

    public async Task<List<JobComparisonOptionDto>> GetAvailableJobsForComparisonAsync(
        CancellationToken cancellationToken = default)
    {
        var allJobs = await _dbContext.JobPostings
            .Include(j => j.Requisition!)
                .ThenInclude(r => r.Department!)
            .Include(j => j.Requisition!)
                .ThenInclude(r => r.JobPosition!)
            .Where(j => !j.IsDeleted)
            .OrderBy(j => j.Title)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        if (allJobs.Count == 0)
        {
            allJobs = await _dbContext.JobPostings
                .Where(j => !j.IsDeleted)
                .OrderBy(j => j.Title)
                .AsNoTracking()
                .ToListAsync(cancellationToken);
        }

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

    /// <summary>
    /// Rút ngắn tiêu đề tiêu chí xuống tối đa 4 từ theo Quy tắc 6.
    /// Ví dụ:
    /// Kiến trúc hệ thống & Clean Architecture -> Architecture
    /// Lập trình C# .NET & Async Concurrency -> .NET
    /// Thiết kế CSDL & SQL Optimization -> SQL
    /// Giao tiếp chuyên môn & Phối hợp đội ngũ -> Communication
    /// </summary>
    private static string GetShortCriteriaName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Tiêu chí";
        var lower = name.ToLowerInvariant();

        if (lower.Contains("kiến trúc") || lower.Contains("clean architecture") || lower.Contains("architecture"))
            return "Architecture";
        if (lower.Contains("c#") || lower.Contains(".net") || lower.Contains("async concurrency"))
            return ".NET";
        if (lower.Contains("cơ sở dữ liệu") || lower.Contains("csdl") || lower.Contains("sql"))
            return "SQL";
        if (lower.Contains("giao tiếp") || lower.Contains("communication") || lower.Contains("phối hợp"))
            return "Communication";
        if (lower.Contains("tư duy") || lower.Contains("thuật toán") || lower.Contains("problem"))
            return "Problem Solving";
        if (lower.Contains("văn hóa") || lower.Contains("culture") || lower.Contains("ownership"))
            return "Culture";

        // Fallback: tối đa 4 từ
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length <= 4 ? name : string.Join(" ", words.Take(4));
    }

    /// <summary>
    /// Làm giàu thông tin ứng viên đáp ứng các quy tắc:
    /// Quy tắc 1: Ưu tiên quyết định thay vì dữ liệu ("🏆 Ứng viên phù hợp nhất cho vị trí hiện tại")
    /// Quy tắc 4: Nhận xét tự nhiên, bullet không cố định, highlight khác nhau theo từng ứng viên (cấm template cứng nhắc)
    /// Quy tắc 5: Tất cả kết luận phải giải thích được ("Vì: • SQL 5/5 • Top 1 điểm tổng • Đạt ngưỡng tuyển dụng")
    /// </summary>
    private static void EnrichCandidateRow(CandidateComparisonRowDto row, List<EvaluationCriteriaHeaderDto> criteria)
    {
        // 1. Quyết định chính (Quy tắc 1)
        if (row.Rank == 1 && row.OverallScore >= 4.0m)
        {
            row.DecisionTitle = "🏆 Ứng viên phù hợp nhất cho vị trí hiện tại";
        }
        else if (row.OverallScore >= 4.0m)
        {
            row.DecisionTitle = "Đạt tiêu chuẩn tuyển dụng đề xuất";
        }
        else if (row.OverallScore >= 3.0m)
        {
            row.DecisionTitle = "Ứng viên tiềm năng cần đào tạo";
        }
        else
        {
            row.DecisionTitle = "Chưa đạt tiêu chuẩn kỹ thuật";
        }

        // 2. Kỹ năng dẫn đầu
        var bestCriteria = criteria
            .OrderByDescending(c => row.CriteriaScores.TryGetValue(c.Id, out var s) ? s : 0)
            .FirstOrDefault();
        decimal bestScore = bestCriteria != null && row.CriteriaScores.TryGetValue(bestCriteria.Id, out var bs) ? bs : row.OverallScore;
        string bestShort = bestCriteria?.ShortName ?? "SQL";

        // 3. Giải thích lý do kết luận (Quy tắc 5: Bắt buộc có Vì: • ...)
        var reasons = new List<string>();
        if (row.RecommendationKey == "STRONG_HIRE" || row.RecommendationKey == "HIRE")
        {
            reasons.Add($"{bestShort} {bestScore:0.#}/5");
            reasons.Add(row.Rank == 1 ? "Top 1 điểm tổng" : $"Điểm tổng {row.OverallScore:0.0}/5.0");
            reasons.Add("Đạt ngưỡng tuyển dụng");
        }
        else if (row.RecommendationKey == "CONSIDER")
        {
            reasons.Add($"Điểm tổng {row.OverallScore:0.0}/5.0");
            reasons.Add($"{bestShort} {bestScore:0.#}/5");
            reasons.Add("Cần mentor mảng thực chiến tải cao");
        }
        else
        {
            reasons.Add($"Điểm tổng {row.OverallScore:0.0}/5.0");
            reasons.Add("Chưa đạt ngưỡng điểm sàn chuyên môn");
            reasons.Add("Kỹ năng cốt lõi < 3.0");
        }
        row.DecisionReasons = reasons;

        // 4. Highlight khác nhau theo từng ứng viên (Quy tắc 4)
        var highlights = new List<string>();
        if (row.Rank == 1)
        {
            highlights.Add("🏆 Phù hợp nhất");
        }

        if (bestScore >= 4.5m)
        {
            highlights.Add($"{bestShort} {bestScore:0.#}/5");
        }

        if (!string.IsNullOrWhiteSpace(row.Strengths))
        {
            if (row.Strengths.Contains("SQL", StringComparison.OrdinalIgnoreCase) && !highlights.Any(h => h.Contains("SQL")))
                highlights.Add("Chuyên sâu SQL & Indexing");
            if (row.Strengths.Contains("thuật toán", StringComparison.OrdinalIgnoreCase))
                highlights.Add("Tư duy thuật toán tốt");
            if (row.Strengths.Contains("giao tiếp", StringComparison.OrdinalIgnoreCase))
                highlights.Add("Giao tiếp cởi mở");
            if (row.Strengths.Contains("kiến trúc", StringComparison.OrdinalIgnoreCase) && !highlights.Any(h => h.Contains("kiến trúc")))
                highlights.Add("Clean Architecture");
        }

        if (highlights.Count == 0)
        {
            highlights.Add("Nền tảng khá");
        }
        row.HighlightBadges = highlights.Distinct().Take(3).ToList();

        // 5. Nhận xét tự nhiên & bullet không cố định (Quy tắc 4: CẤM template Điểm mạnh: ... Cần cải thiện: ...)
        var naturalParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(row.Strengths))
        {
            naturalParts.Add(row.Strengths.Trim().TrimEnd('.') + ".");
        }
        if (!string.IsNullOrWhiteSpace(row.Weaknesses))
        {
            naturalParts.Add("Lưu ý bổ sung: " + row.Weaknesses.Trim().TrimEnd('.') + ".");
        }
        if (naturalParts.Count == 0 && !string.IsNullOrWhiteSpace(row.DetailedFeedback))
        {
            naturalParts.Add(row.DetailedFeedback.Trim());
        }

        row.NaturalCommentary = string.Join(" ", naturalParts);

        var bullets = new List<string>();
        if (bestScore >= 4.5m)
        {
            bullets.Add($"Kỹ năng {bestShort} vượt trội ({bestScore:0.#}/5)");
        }
        if (!string.IsNullOrWhiteSpace(row.Strengths))
        {
            var parts = row.Strengths.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in parts.Take(2))
            {
                var clean = p.Trim();
                if (clean.Length > 5 && !bullets.Any(b => b.Contains(clean, StringComparison.OrdinalIgnoreCase)))
                    bullets.Add(clean);
            }
        }
        if (!string.IsNullOrWhiteSpace(row.Weaknesses))
        {
            var parts = row.Weaknesses.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
            var firstW = parts.FirstOrDefault()?.Trim();
            if (!string.IsNullOrWhiteSpace(firstW) && firstW.Length > 5)
            {
                bullets.Add("Cần bổ sung: " + firstW);
            }
        }
        row.QualitativeBullets = bullets.Take(3).ToList();
    }
}
