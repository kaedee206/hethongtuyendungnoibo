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
    public List<CandidateComparisonRowDto> Candidates { get; set; } = new();

    // Thống kê nhanh (KPIs)
    public int TotalCandidatesCount => Candidates.Count;
    public decimal HighestScore => Candidates.Count > 0 ? Candidates.Max(c => c.OverallScore) : 0;
    public decimal AverageScore => Candidates.Count > 0 ? Math.Round(Candidates.Average(c => c.OverallScore), 2) : 0;
    public string TopCandidateName => Candidates.FirstOrDefault()?.CandidateName ?? "Chưa có";
    public int RecommendHireCount => Candidates.Count(c => c.RecommendationKey == "STRONG_HIRE" || c.RecommendationKey == "HIRE");

    // Bộ lọc & sắp xếp hiện tại
    public string SortBy { get; set; } = "total_desc";
    public string StageFilter { get; set; } = "ALL";
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
    public string? Description { get; set; }
    public decimal Weight { get; set; } = 1.0m;
    public string CriteriaType { get; set; } = "HARD_SKILL"; // HARD_SKILL, SOFT_SKILL, CULTURE

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

    // Đánh giá đề xuất
    public string RecommendationKey { get; set; } = string.Empty;
    public string RecommendationDisplay { get; set; } = string.Empty;
    public string RecommendationBadgeClass { get; set; } = string.Empty;

    // Nhận xét định tính của hội đồng
    public string? Strengths { get; set; }
    public string? Weaknesses { get; set; }
    public string? DetailedFeedback { get; set; }
    public DateTimeOffset? LastEvaluatedAt { get; set; }
    public string LastEvaluatedAtDisplay => LastEvaluatedAt.HasValue
        ? LastEvaluatedAt.Value.ToString("dd/MM/yyyy HH:mm")
        : "—";
}
