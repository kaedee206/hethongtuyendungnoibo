namespace Ats.Web.Models.DTOs;

/// <summary>
/// DTO dòng ứng viên hợp lệ từ file Excel
/// </summary>
public class ImportCandidateRowDto
{
    public int RowIndex { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string? TargetJobTitle { get; set; }
    public int? ExperienceYears { get; set; }
    public string? Skills { get; set; }
    public string? Notes { get; set; }
}

public class ImportCandidateErrorDetailDto
{
    public string ColumnName { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
}

public class ImportCandidateErrorRowDto
{
    public int RowIndex { get; set; }
    public List<ImportCandidateErrorDetailDto> Errors { get; set; } = new();
}

public class ImportCandidateResultDto
{
    public int TotalRows => ValidRows.Count + ErrorRows.Count;
    public List<ImportCandidateRowDto> ValidRows { get; set; } = new();
    public List<ImportCandidateErrorRowDto> ErrorRows { get; set; } = new();
}

public class ExecuteCandidateImportRequestDto
{
    public List<ImportCandidateRowDto> ValidRows { get; set; } = new();
    public Guid? JobPostingId { get; set; }
}
