namespace Ats.Web.Models.ViewModels.CandidateComparison;

/// <summary>
/// ViewModel chính cho màn hình So sánh ứng viên theo điểm đánh giá (SCRUM-336 / Scrum - Số 58).
/// Cho phép Trưởng bộ phận so sánh các ứng viên của cùng một vị trí dựa trên điểm từng tiêu chí.
/// </summary>
public class CandidateEvaluationComparisonViewModel
{
    public Guid? SelectedJobPostingId { get; set; }
    public string SelectedJobTitle { get; set; } = string.Empty;
    public string SelectedJobCode { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public string TargetLevel { get; set; } = string.Empty;

    /// <summary>
    /// Danh sách các vị trí tuyển dụng để hiển thị trong bộ lọc dropdown.
    /// </summary>
    public List<JobComparisonOptionDto> AvailableJobs { get; set; } = new();

    /// <summary>
    /// Danh sách các tiêu chí đánh giá chuẩn hóa được áp dụng.
    /// </summary>
    public List<EvaluationCriteriaHeaderDto> CriteriaHeaders { get; set; } = new();

    /// <summary>
    /// Danh sách ứng viên đủ điều kiện so sánh (đã có tối thiểu 1 phiếu đánh giá đã nộp).
    /// Mặc định sắp xếp theo điểm tổng giảm dần.
    /// </summary>
    // Header Info theo wireframe
    public string RoundName { get; set; } = "Final Interview";
    public int TotalCandidatesInJob { get; set; }
    public int EvaluatedCandidatesCount => Candidates.Count;
    public string EvaluationRatioDisplay => $"{EvaluatedCandidatesCount}/{(TotalCandidatesInJob > 0 ? TotalCandidatesInJob : EvaluatedCandidatesCount)}";

    // Danh sách ứng viên theo thứ tự cột so sánh
    public List<CandidateComparisonRowDto> Candidates { get; set; } = new();

    // Thống kê nhanh (KPIs)
    public int TotalCandidatesCount => Candidates.Count;
    public decimal HighestScore => Candidates.Count > 0 ? Candidates.Max(c => c.OverallScore) : 0;
    public decimal LowestScore => Candidates.Count > 0 ? Candidates.Min(c => c.OverallScore) : 0;
    public decimal AverageScore => Candidates.Count > 0 ? Math.Round(Candidates.Average(c => c.OverallScore), 2) : 0;
    public string TopCandidateName => Candidates.FirstOrDefault()?.CandidateName ?? "Chưa có";
    public int RecommendHireCount => Candidates.Count(c => c.RecommendationKey == "STRONG_HIRE" || c.RecommendationKey == "HIRE");

    // Khu vực 1: Bảng so sánh ma trận chuyển vị (Hàng = Tiêu chí, Cột = Ứng viên)
    public List<TransposedCriteriaRowDto> TransposedMatrix { get; set; } = new();

    // Khu vực 2: Xếp hạng
    public List<CandidateRankItemDto> Rankings { get; set; } = new();

    // Khu vực 3: Chênh lệch ứng viên (Tại sao đứng đầu?)
    public CandidateDeltaAnalysisDto? DeltaAnalysis { get; set; }

    // Khu vực 4: Chi tiết nguồn điểm đánh giá từng giám khảo (Chống thiên vị)
    public List<CandidateEvaluatorBreakdownDto> EvaluatorBreakdowns { get; set; } = new();

    // Khuyến nghị hành động hàng đầu (giữ tương thích)
    public TopRecommendationDto? TopRecommendation { get; set; }

    // Khu vực 5: Bộ lọc & sắp xếp hiện tại
    public string SortBy { get; set; } = "total_desc";
    public string StageFilter { get; set; } = "ALL";
    public bool ThresholdOnly { get; set; } = false;
    public decimal MinThreshold { get; set; } = 4.0m;
}

/// <summary>
/// Khối quyết định hành động đặt ở đầu trang (Quy tắc 7 & Quy tắc 1, 3).
/// Trả lời trực tiếp câu hỏi "Tôi nên làm gì?".
/// </summary>
public class TopRecommendationDto
{
    public Guid ApplicationId { get; set; }
    public Guid CandidateId { get; set; }
    public string CandidateName { get; set; } = string.Empty;
    public string CurrentTitle { get; set; } = string.Empty;
    public decimal OverallScore { get; set; }
    public decimal ScoreDifference { get; set; } // Ví dụ: +0.4
    public string ScoreDifferenceText { get; set; } = "+0.4";
    public string ScoreDifferencePercent { get; set; } = "10%";
    public string SecondCandidateName { get; set; } = string.Empty;
    public string KeyAdvantages { get; set; } = "SQL và kiến trúc hệ thống";
    public string DecisionBadge { get; set; } = "🏆 Ứng viên phù hợp nhất cho vị trí hiện tại"; // Quy tắc 1
    public string ActionHeadline { get; set; } = "Khuyến nghị: Tuyển Phan Văn Hải"; // Quy tắc 7
    public List<string> ThreeKeyInsights { get; set; } = new(); // Quy tắc 3 (3 insight quan trọng nhất)
    public List<string> DecisionReasons { get; set; } = new(); // Quy tắc 5 (Vì: ...)
    public Guid? LastInterviewId { get; set; }
    public string? ResumeFilePath { get; set; }
}

public class JobComparisonOptionDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public int EvaluatedCandidateCount { get; set; }
    public string DisplayText => string.IsNullOrWhiteSpace(Code)
        ? $"{Title} ({EvaluatedCandidateCount} ứng viên có điểm)"
        : $"[{Code}] {Title} ({EvaluatedCandidateCount} ứng viên có điểm)";
}

public class EvaluationCriteriaHeaderDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>
    /// Tiêu đề rút gọn tối đa 4 từ theo Quy tắc 6 (Architecture, .NET, SQL, Communication...).
    /// </summary>
    public string ShortName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Weight { get; set; } = 1.0m;
    public string CriteriaType { get; set; } = "HARD_SKILL"; // HARD_SKILL, SOFT_SKILL, CULTURE

    /// <summary>
    /// Hiển thị tiêu đề ngắn gọn theo Quy tắc 6 (tối đa 4 từ).
    /// </summary>
    public string DisplayTitle => !string.IsNullOrWhiteSpace(ShortName)
        ? ShortName
        : TruncateWords(Name, 4);

    public string TypeDisplayName => CriteriaType switch
    {
        "HARD_SKILL" => "Chuyên môn",
        "SOFT_SKILL" => "Kỹ năng mềm",
        "CULTURE" => "Văn hóa",
        _ => "Tiêu chí"
    };

    public string TypeBadgeClass => CriteriaType switch
    {
        "HARD_SKILL" => "bg-primary-subtle text-primary border border-primary-subtle",
        "SOFT_SKILL" => "bg-info-subtle text-info-emphasis border border-info-subtle",
        "CULTURE" => "bg-success-subtle text-success border border-success-subtle",
        _ => "bg-secondary-subtle text-secondary"
    };

    private static string TruncateWords(string text, int maxWords)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length <= maxWords ? text : string.Join(" ", words.Take(maxWords));
    }
}

public class CandidateComparisonRowDto
{
    public Guid ApplicationId { get; set; }
    public Guid CandidateId { get; set; }
    public string CandidateName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string CurrentTitle { get; set; } = string.Empty;
    public string? ResumeFilePath { get; set; }

    // Thông tin tiến trình tuyển dụng
    public Guid CurrentStageId { get; set; }
    public string CurrentStageName { get; set; } = string.Empty;
    public string StageColor { get; set; } = "#059669";
    public int CurrentStageOrder { get; set; }

    // Thông tin buổi phỏng vấn & hội đồng
    public Guid? LastInterviewId { get; set; }
    public string InterviewTitle { get; set; } = string.Empty;
    public int RoundNumber { get; set; }
    public bool IsFinalRound { get; set; }
    public int SubmittedEvaluationsCount { get; set; }
    public string PanelistNames { get; set; } = string.Empty;

    // Điểm số chi tiết từng tiêu chí (CriteriaId -> Điểm trung bình thang 1-5)
    public Dictionary<Guid, decimal> CriteriaScores { get; set; } = new();

    // Điểm tổng kết
    public decimal OverallScore { get; set; } // Thang 5.0
    public decimal WeightedScore { get; set; } // Điểm có nhân trọng số chuẩn hóa
    public int Rank { get; set; } // Thứ hạng (1, 2, 3...)
    public bool IsTopCandidate => Rank == 1 && OverallScore > 0;

    // Quy tắc 1: Ưu tiên quyết định thay vì dữ liệu
    public string DecisionTitle { get; set; } = string.Empty; // "🏆 Ứng viên phù hợp nhất cho vị trí hiện tại"

    // Đánh giá đề xuất
    public string RecommendationKey { get; set; } = string.Empty;
    public string RecommendationDisplay { get; set; } = string.Empty;
    public string RecommendationBadgeClass { get; set; } = string.Empty;

    // Quy tắc 5: Tất cả kết luận phải giải thích được ("Vì: • SQL 5/5 • Top 1 điểm tổng • Đạt ngưỡng tuyển dụng")
    public List<string> DecisionReasons { get; set; } = new();

    // Quy tắc 4: Nhận xét tự nhiên & Highlight khác nhau theo ứng viên (CẤM template Điểm mạnh: ... Cần cải thiện: ...)
    public List<string> HighlightBadges { get; set; } = new();
    public string NaturalCommentary { get; set; } = string.Empty;
    public List<string> QualitativeBullets { get; set; } = new();

    // Nhận xét gốc định tính của hội đồng
    public string? Strengths { get; set; }
    public string? Weaknesses { get; set; }
    public string? DetailedFeedback { get; set; }
    public DateTimeOffset? LastEvaluatedAt { get; set; }
    public string LastEvaluatedAtDisplay => LastEvaluatedAt.HasValue
        ? LastEvaluatedAt.Value.ToString("dd/MM/yyyy HH:mm")
        : "—";
}

/// <summary>
/// Hàng tiêu chí trong ma trận so sánh chuyển vị (Khu vực 1: Hàng = Tiêu chí, Cột = Ứng viên).
/// </summary>
public class TransposedCriteriaRowDto
{
    public Guid CriteriaId { get; set; }
    public string CriteriaName { get; set; } = string.Empty;
    public string DisplayTitle { get; set; } = string.Empty; // Tiêu đề rút gọn: .NET, SQL, Architecture...
    public decimal Weight { get; set; } = 1.0m;
    public string CriteriaType { get; set; } = "HARD_SKILL";
    public string TypeDisplayName { get; set; } = "Chuyên môn";
    public string TypeBadgeClass { get; set; } = string.Empty;

    /// <summary>
    /// Điểm chi tiết của từng ứng viên đối với tiêu chí này (Key: CandidateId).
    /// </summary>
    public Dictionary<Guid, CellEvaluationDetailDto> ScoresByCandidateId { get; set; } = new();

    /// <summary>
    /// Điểm cao nhất trong hàng này (được tô xanh).
    /// </summary>
    public decimal MaxScore { get; set; }

    /// <summary>
    /// Điểm thấp nhất trong hàng này (được tô đỏ nhạt).
    /// </summary>
    public decimal MinScore { get; set; }
}

/// <summary>
/// Chi tiết điểm của 1 ô trong ma trận so sánh (khi hover hiện: số người đánh giá, điểm trung bình, độ lệch).
/// </summary>
public class CellEvaluationDetailDto
{
    public Guid CandidateId { get; set; }
    public decimal Score { get; set; } // Điểm hiển thị (vd: 4.8)
    public int EvaluatorCount { get; set; } // Số người đánh giá (vd: 3)
    public decimal AverageScore { get; set; } // Điểm trung bình
    public decimal MinScore { get; set; }
    public decimal MaxScore { get; set; }
    public decimal ScoreSpread => Math.Round(MaxScore - MinScore, 2); // Độ lệch (Max - Min)
    public bool IsHighestInRow { get; set; } // Điểm cao nhất mỗi hàng tô xanh
    public bool IsLowestInRow { get; set; } // Điểm thấp nhất mỗi hàng tô đỏ nhạt
    public List<decimal> IndividualScores { get; set; } = new();
}

/// <summary>
/// Mục xếp hạng ứng viên theo điểm tổng (Khu vực 2: #1 Hải ━━━━ 4.54).
/// </summary>
public class CandidateRankItemDto
{
    public int Rank { get; set; }
    public Guid CandidateId { get; set; }
    public Guid ApplicationId { get; set; }
    public string CandidateName { get; set; } = string.Empty;
    public decimal OverallScore { get; set; }
    public string OverallScoreDisplay => OverallScore.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
    public int ScorePercent { get; set; } // Tỷ lệ % vẽ thanh bar (vd: 4.54 / 5.0 * 100 = 91%)
    public bool IsLeader { get; set; }
}

/// <summary>
/// Phân tích chênh lệch ứng viên (Khu vực 3: "Tại sao Hải đang đứng đầu?").
/// So sánh ứng viên dẫn đầu (#1) với ứng viên bám đuổi (#2).
/// </summary>
public class CandidateDeltaAnalysisDto
{
    public string LeaderName { get; set; } = string.Empty;
    public decimal LeaderScore { get; set; }
    public string RunnerUpName { get; set; } = string.Empty;
    public decimal RunnerUpScore { get; set; }
    public decimal TotalDifference { get; set; }
    public string TotalDifferenceText { get; set; } = "+0.0";
    public List<CriteriaDeltaItemDto> CriteriaDeltas { get; set; } = new();
}

public class CriteriaDeltaItemDto
{
    public string CriteriaName { get; set; } = string.Empty;
    public decimal LeaderScore { get; set; }
    public decimal RunnerUpScore { get; set; }
    public decimal Delta { get; set; } // LeaderScore - RunnerUpScore
    public string DeltaText { get; set; } = string.Empty; // "+0.3" hoặc "-0.2"
    public bool IsAdvantage => Delta > 0;
    public bool IsDisadvantage => Delta < 0;
    public bool IsEqual => Delta == 0;
    public bool IsHighestInGroup { get; set; } // Cao nhất trong tất cả ứng viên
}

/// <summary>
/// Chi tiết nguồn điểm đánh giá của từng ứng viên (Khu vực 4: [+] Xem nguồn điểm - Chống thiên vị).
/// </summary>
public class CandidateEvaluatorBreakdownDto
{
    public Guid CandidateId { get; set; }
    public string CandidateName { get; set; } = string.Empty;
    public decimal AverageScore { get; set; }
    public int EvaluatorCount { get; set; }
    public decimal Spread { get; set; } // Độ lệch min-max giữa các giám khảo
    public bool HasDiscrepancy => Spread >= 1.0m; // Có chênh lệch lớn giữa các giám khảo
    public List<EvaluatorScoreItemDto> Evaluators { get; set; } = new();
}

public class EvaluatorScoreItemDto
{
    public string EvaluatorName { get; set; } = string.Empty;
    public decimal OverallScore { get; set; }
    public string Recommendation { get; set; } = string.Empty;
    public DateTimeOffset? SubmittedAt { get; set; }
    public Dictionary<string, decimal> CriteriaScores { get; set; } = new();
}
