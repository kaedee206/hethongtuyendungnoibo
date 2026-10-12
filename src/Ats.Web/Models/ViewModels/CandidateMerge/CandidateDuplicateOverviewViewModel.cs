namespace Ats.Web.Models.ViewModels.CandidateMerge;

/// <summary>
/// ViewModel cho màn hình Bàn làm việc Quản lý Hồ sơ Trùng Lặp (Scrum #40)
/// </summary>
public class CandidateDuplicateOverviewViewModel
{
    public int TotalDuplicateGroups { get; set; }
    public int PhoneDuplicateCount { get; set; }
    public int EmailDuplicateCount { get; set; }
    public int MergedCount { get; set; }
    public int CanUndoCount { get; set; }

    public List<DuplicateCandidateGroupDto> DuplicateGroups { get; set; } = new();
    public List<CandidateMergeHistoryDto> MergeHistories { get; set; } = new();

    public string ActiveTab { get; set; } = "duplicates"; // duplicates hoặc history
}
