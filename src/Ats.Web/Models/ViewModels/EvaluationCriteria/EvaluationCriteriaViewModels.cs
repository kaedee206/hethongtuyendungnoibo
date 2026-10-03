using System.ComponentModel.DataAnnotations;

namespace Ats.Web.Models.ViewModels.EvaluationCriteria;

public class CriteriaRubricLevelViewModel
{
    public int Score { get; set; }
    public string LevelName { get; set; } = string.Empty;
    public string BehavioralDescription { get; set; } = string.Empty;
    public bool IsRequired { get; set; } = true;
}

public class CriteriaRubricViewModel
{
    public Guid CriteriaId { get; set; }
    public string CriteriaName { get; set; } = string.Empty;
    public string? CriteriaDescription { get; set; }
    public string CriteriaType { get; set; } = "HARD_SKILL";
    public decimal Weight { get; set; } = 1.0m;
    public int ScaleMin { get; set; } = 1;
    public int ScaleMax { get; set; } = 5;
    public List<CriteriaRubricLevelViewModel> Levels { get; set; } = [];
    public DateTimeOffset? UpdatedAt { get; set; }
    public bool IsFullyDefined => Levels.Count >= (ScaleMax - ScaleMin + 1) && 
                                  Levels.Where(l => l.IsRequired).All(l => !string.IsNullOrWhiteSpace(l.BehavioralDescription) && l.BehavioralDescription.Length >= 10);
}

public class CriteriaRubricSaveInputModel
{
    [Required]
    public Guid CriteriaId { get; set; }

    [Range(1, 1)]
    public int ScaleMin { get; set; } = 1;

    [Range(3, 5)]
    public int ScaleMax { get; set; } = 5;

    public string? Summary { get; set; }

    [Required]
    public List<CriteriaRubricLevelViewModel> Levels { get; set; } = [];
}

public class EvaluationCriteriaItemViewModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string CriteriaType { get; set; } = string.Empty;
    public string CriteriaTypeDisplayName { get; set; } = string.Empty;
    public decimal Weight { get; set; } = 1.0m;
    public int ScaleMin { get; set; } = 1;
    public int ScaleMax { get; set; } = 5;
    public int DefinedLevelsCount { get; set; }
    public bool IsFullyDefined { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public class EvaluationCriteriaListViewModel
{
    public List<EvaluationCriteriaItemViewModel> CriteriaList { get; set; } = [];
    public int TotalCriteria => CriteriaList.Count;
    public int HardSkillsCount => CriteriaList.Count(c => c.CriteriaType == "HARD_SKILL");
    public int SoftSkillsCount => CriteriaList.Count(c => c.CriteriaType == "SOFT_SKILL");
    public int CultureFitCount => CriteriaList.Count(c => c.CriteriaType == "CULTURE");
    public int FullyDefinedCount => CriteriaList.Count(c => c.IsFullyDefined);
}

public class InterviewEvaluationSheetViewModel
{
    public List<CriteriaRubricViewModel> CriteriaRubrics { get; set; } = [];
    public int TotalCriteria => CriteriaRubrics.Count;
    public decimal TotalWeight => CriteriaRubrics.Sum(c => c.Weight);
}
