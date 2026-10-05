using Ats.Web.Models.Enums;

namespace Ats.Web.Models.Entities;

public class InterviewEvaluation : BaseEntity
{
    public Guid Id { get; set; }
    public Guid InterviewId { get; set; }
    public Guid InterviewerId { get; set; }
    
    public decimal? OverallScore { get; set; }
    public RecommendationType Recommendation { get; set; }
    public string? Strengths { get; set; }
    public string? Weaknesses { get; set; }
    public string? DetailedFeedback { get; set; }
    
    public bool IsSubmitted { get; set; } = false;
    public DateTimeOffset? SubmittedAt { get; set; }

    public Interview Interview { get; set; } = null!;
    public User Interviewer { get; set; } = null!;
    public ICollection<EvaluationScore> Scores { get; set; } = new List<EvaluationScore>();
}
