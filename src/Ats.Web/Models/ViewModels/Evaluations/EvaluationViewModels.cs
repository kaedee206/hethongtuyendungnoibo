using System.ComponentModel.DataAnnotations;

namespace Ats.Web.Models.ViewModels.Evaluations;

/// <summary>
/// Dữ liệu từng tiêu chí khởi tạo trong phiếu đánh giá tự động nạp từ khung năng lực (SCRUM-56).
/// </summary>
public class EvaluationItemGenerateViewModel
{
    public Guid? CriteriaId { get; set; }
    public string CriteriaName { get; set; } = string.Empty;
    public string? CriteriaDescription { get; set; }
    public decimal Weight { get; set; }
    public int Score { get; set; }
    public string? Comment { get; set; }
    public string? Rubric1 { get; set; }
    public string? Rubric2 { get; set; }
    public string? Rubric3 { get; set; }
    public string? Rubric4 { get; set; }
    public string? Rubric5 { get; set; }
}

/// <summary>
/// Dữ liệu khởi tạo phiếu tự động nạp từ khung năng lực của chức danh (SCRUM-56).
/// </summary>
public class EvaluationFormGenerateViewModel
{
    public Guid? EvaluationFormId { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid InterviewId { get; set; }
    public Guid EvaluatorId { get; set; }
    public Guid CandidateId { get; set; }
    public Guid? JobPositionId { get; set; }

    public string CandidateName { get; set; } = string.Empty;
    public string CandidateEmail { get; set; } = string.Empty;
    public string JobTitle { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public int InterviewRound { get; set; } = 1;

    public Guid? FrameworkId { get; set; }
    public string FrameworkName { get; set; } = string.Empty;
    public string Status { get; set; } = "NEW"; // NEW, DRAFT, SUBMITTED
    public decimal TotalScore { get; set; }
    public string? OverallRecommendation { get; set; }
    public string? GeneralNotes { get; set; }

    public bool IsReadOnly { get; set; }
    public bool CanEdit { get; set; } = true;

    public List<EvaluationItemGenerateViewModel> Items { get; set; } = [];
}

/// <summary>
/// Model nhận dữ liệu từng tiêu chí khi nộp hoặc lưu nháp phiếu đánh giá.
/// </summary>
public class EvaluationItemSubmitInputModel
{
    public Guid? CriteriaId { get; set; }

    [Required(ErrorMessage = "Tên tiêu chí không được để trống.")]
    public string CriteriaName { get; set; } = string.Empty;

    public string? CriteriaDescription { get; set; }

    [Range(0.0001, 100000, ErrorMessage = "Trọng số tiêu chí phải lớn hơn 0.")]
    public decimal Weight { get; set; }

    [Range(0, 5, ErrorMessage = "Điểm đánh giá phải từ 0 đến 5.")]
    public int Score { get; set; }

    public string? Comment { get; set; }
}

/// <summary>
/// Model submit form đánh giá (lưu nháp hoặc nộp chính thức).
/// </summary>
public class EvaluationFormSubmitInputModel
{
    public Guid? EvaluationFormId { get; set; }

    [Required]
    public Guid ApplicationId { get; set; }

    [Required]
    public Guid InterviewId { get; set; }

    public bool IsDraft { get; set; }

    [Required]
    public List<EvaluationItemSubmitInputModel> Items { get; set; } = [];

    public string? OverallRecommendation { get; set; }
    public string? GeneralNotes { get; set; }
}

/// <summary>
/// Chi tiết tiêu chí hiển thị trong phiếu đánh giá đã nộp.
/// </summary>
public class EvaluationItemDetailViewModel
{
    public Guid Id { get; set; }
    public Guid? CriteriaId { get; set; }
    public string CriteriaName { get; set; } = string.Empty;
    public string? CriteriaDescription { get; set; }
    public decimal Weight { get; set; }
    public int Score { get; set; }
    public decimal WeightedScore => Weight * Score;
    public string? Comment { get; set; }
}

/// <summary>
/// Dữ liệu chi tiết một phiếu đánh giá hoàn chỉnh (dùng cho Interviewer & HR Manager xem).
/// </summary>
public class EvaluationDetailViewModel
{
    public Guid EvaluationFormId { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid InterviewId { get; set; }
    public Guid EvaluatorId { get; set; }
    public string EvaluatorName { get; set; } = string.Empty;
    public string EvaluatorEmail { get; set; } = string.Empty;

    public string CandidateName { get; set; } = string.Empty;
    public string CandidateEmail { get; set; } = string.Empty;
    public string JobTitle { get; set; } = string.Empty;
    public int InterviewRound { get; set; }

    public string Status { get; set; } = string.Empty;
    public decimal TotalScore { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public string SnapshotVersion { get; set; } = "1.0";
    public string? OverallRecommendation { get; set; }
    public string? GeneralNotes { get; set; }

    public List<EvaluationItemDetailViewModel> Items { get; set; } = [];
}

/// <summary>
/// Thông tin tiêu chí cho bảng so sánh chuẩn hóa.
/// </summary>
public class CriteriaComparisonHeaderDto
{
    public string CriteriaName { get; set; } = string.Empty;
    public decimal Weight { get; set; }
}

/// <summary>
/// Dữ liệu điểm chi tiết của ứng viên theo tiêu chí.
/// </summary>
public class CandidateCriteriaScoreDto
{
    public string CriteriaName { get; set; } = string.Empty;
    public int Score { get; set; }
    public decimal Weight { get; set; }
    public string? Comment { get; set; }
}

/// <summary>
/// Hàng dữ liệu đánh giá của một ứng viên trong bảng so sánh tổng hợp (SCRUM-56).
/// </summary>
public class CandidateEvaluationSummaryRow
{
    public Guid ApplicationId { get; set; }
    public Guid EvaluationFormId { get; set; }
    public string CandidateName { get; set; } = string.Empty;
    public string CandidateEmail { get; set; } = string.Empty;
    public int InterviewRound { get; set; }
    public string EvaluatorName { get; set; } = string.Empty;
    public decimal TotalScore { get; set; }
    public string? Recommendation { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public List<CandidateCriteriaScoreDto> CriteriaScores { get; set; } = [];
}

/// <summary>
/// ViewModel dùng cho HR Manager / Admin so sánh các ứng viên trên cùng một thước đo khung năng lực (SCRUM-56).
/// </summary>
public class EvaluationSummaryComparisonViewModel
{
    public Guid JobPostingId { get; set; }
    public string JobTitle { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public string FrameworkName { get; set; } = string.Empty;

    public List<CriteriaComparisonHeaderDto> CriteriaHeaders { get; set; } = [];
    public List<CandidateEvaluationSummaryRow> Candidates { get; set; } = [];

    public decimal HighestScore { get; set; }
    public decimal LowestScore { get; set; }
    public decimal AverageScore { get; set; }
}
