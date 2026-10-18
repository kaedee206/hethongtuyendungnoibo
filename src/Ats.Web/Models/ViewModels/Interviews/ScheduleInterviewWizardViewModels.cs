using System.ComponentModel.DataAnnotations;
using Ats.Web.Models.DTOs;

namespace Ats.Web.Models.ViewModels.Interviews;

/// <summary>
/// ViewModel chính cho Trang Wizard Lên lịch phỏng vấn chuyên biệt (Option 2 - /phong-van/dat-lich)
/// Tuân thủ quy chuẩn thiết kế RULE.md và Clean Architecture.
/// </summary>
public class ScheduleInterviewWizardViewModel
{
    /// <summary>
    /// ID hồ sơ ứng tuyển được chọn sẵn (nếu truy cập từ Kanban hoặc Candidate Details)
    /// </summary>
    public Guid? PreselectedApplicationId { get; set; }

    /// <summary>
    /// Thông tin hồ sơ ứng viên được chọn hiện tại
    /// </summary>
    public CandidateWizardDetailDto? SelectedCandidate { get; set; }

    /// <summary>
    /// Danh sách tất cả các ứng viên trong pipeline tuyển dụng để chọn ở Bước 1
    /// </summary>
    public List<CandidatePipelineOptionDto> PipelineCandidates { get; set; } = new();

    /// <summary>
    /// Danh sách nhân sự nội bộ có thể phân công phỏng vấn ở Bước 2
    /// </summary>
    public List<InterviewerLookupDto> AvailableInterviewers { get; set; } = new();

    /// <summary>
    /// Danh sách các phòng họp và mẫu đường link họp trực tuyến được cấu hình sẵn
    /// </summary>
    public List<RoomPresetDto> RoomPresets { get; set; } = new();

    /// <summary>
    /// Dữ liệu submit form đặt lịch phỏng vấn
    /// </summary>
    public ScheduleInterviewInputModel FormInput { get; set; } = new();

    /// <summary>
    /// Mẫu ghi chú chuẩn bị mặc định cho ứng viên
    /// </summary>
    public string DefaultPreparationGuide { get; set; } =
        "• Kiểm tra camera, micro và đường truyền internet ổn định trước giờ hẹn 5 - 10 phút.\n" +
        "• Chuẩn bị sẵn CV, Portfolio hoặc source code dự án tiêu biểu để sẵn sàng trao đổi cùng Hội đồng.\n" +
        "• Trang phục lịch sự, chuyên nghiệp; lựa chọn không gian yên tĩnh.";

    /// <summary>
    /// Thông tin liên hệ hỗ trợ nhân sự mặc định
    /// </summary>
    public string DefaultHrContactNote { get; set; } =
        "Phòng Tuyển dụng & Thu hút Nhân tài NoveraTech · Hotline: (+84) 24 3795 8888 · Email: tuyendung@noveratech.com";
}

/// <summary>
/// DTO ứng viên trong pipeline tuyển dụng phục vụ tìm kiếm và chọn nhanh ở Bước 1
/// </summary>
public class CandidatePipelineOptionDto
{
    public Guid ApplicationId { get; set; }
    public Guid CandidateId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public Guid JobPostingId { get; set; }
    public string JobTitle { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public string CurrentStageName { get; set; } = string.Empty;
    public int CurrentStageOrder { get; set; }
    public bool HasCv { get; set; }
    public Guid? ResumeId { get; set; }
    public string? ResumeFileName { get; set; }
    public int? MatchScore { get; set; }
    public int TotalInterviewsCount { get; set; }
    public string InitialLetter => !string.IsNullOrWhiteSpace(FullName) ? FullName.Trim().Substring(0, 1).ToUpper() : "C";
    public DateTimeOffset AppliedAt { get; set; }
    public string FormattedAppliedDate => AppliedAt.ToString("dd/MM/yyyy");
}

/// <summary>
/// DTO thông tin tóm tắt hồ sơ chuyên sâu của ứng viên được chọn (Dossier Summary)
/// </summary>
public class CandidateWizardDetailDto
{
    public Guid ApplicationId { get; set; }
    public Guid CandidateId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string CurrentTitle { get; set; } = string.Empty;
    public string CurrentCompany { get; set; } = string.Empty;
    public string InitialLetter => !string.IsNullOrWhiteSpace(FullName) ? FullName.Trim().Substring(0, 1).ToUpper() : "C";

    public Guid JobPostingId { get; set; }
    public string JobTitle { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public string JobLevel { get; set; } = string.Empty;

    public string CurrentStageName { get; set; } = string.Empty;
    public int CurrentStageOrder { get; set; }

    public Guid? ActiveResumeId { get; set; }
    public string? ResumeFileName { get; set; }
    public string? ResumeFileSizeDisplay { get; set; }

    public int? MatchScore { get; set; }

    /// <summary>
    /// Vòng phỏng vấn gợi ý kế tiếp (Ví dụ: đã có 1 vòng thì gợi ý Vòng 2)
    /// </summary>
    public int NextRoundNumber { get; set; } = 1;

    /// <summary>
    /// Tiêu đề vòng phỏng vấn gợi ý
    /// </summary>
    public string SuggestedRoundTitle { get; set; } = string.Empty;

    /// <summary>
    /// Lịch sử các vòng phỏng vấn trước đó của ứng viên
    /// </summary>
    public List<InterviewHistorySummaryDto> InterviewHistory { get; set; } = new();
}

/// <summary>
/// DTO tóm tắt các vòng phỏng vấn đã thực hiện trước đó của ứng viên
/// </summary>
public class InterviewHistorySummaryDto
{
    public Guid InterviewId { get; set; }
    public int RoundNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public string InterviewTypeDisplay { get; set; } = string.Empty;
    public string? LocationOrLink { get; set; }
    public DateTimeOffset? StartTime { get; set; }
    public DateTimeOffset? EndTime { get; set; }
    public string FormattedTimeRange => StartTime.HasValue
        ? $"{StartTime.Value.ToOffset(TimeSpan.FromHours(7)):dd/MM/yyyy HH:mm} - {EndTime?.ToOffset(TimeSpan.FromHours(7)):HH:mm}"
        : "Chưa xác định";
    public string StatusDisplay { get; set; } = string.Empty;
    public string CandidateConfirmedDisplay { get; set; } = string.Empty;
    public string PanelistsDisplay { get; set; } = string.Empty;
    public double? AverageScore { get; set; }
    public string? FinalConclusion { get; set; }
}

/// <summary>
/// Cấu hình phòng họp hoặc đường link họp mẫu ở Bước 2
/// </summary>
public class RoomPresetDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "ONLINE_MEET"; // ONLINE_MEET hoặc OFFLINE_OFFICE
    public string Platform { get; set; } = "MEET"; // MEET, TEAMS hoặc OFFLINE
    public string LocationOrLink { get; set; } = string.Empty;
    public string Capacity { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string BadgeText { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
}

/// <summary>
/// DTO kết quả kiểm tra trùng lịch / rảnh rỗi của giám khảo ở Bước 2
/// </summary>
public class InterviewerBusyCheckDto
{
    public Guid InterviewerId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsBusy { get; set; }
    public string StatusMessage { get; set; } = "Sẵn sàng (Trống lịch)";
    public string? ConflictTitle { get; set; }
    public string? ConflictTimeDisplay { get; set; }
}
