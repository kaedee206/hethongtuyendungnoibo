using Ats.Web.Models.ViewModels.Tracking;

namespace Ats.Web.Services.Interfaces;

/// <summary>
/// Service tra cứu trạng thái hồ sơ ứng viên (Scrum #35)
/// Tuân thủ quy chuẩn Clean Service, phương thức Async và CancellationToken.
/// </summary>
public interface IApplicationTrackingService
{
    /// <summary>
    /// Tra cứu hồ sơ dựa trên mã tra cứu và email ứng tuyển.
    /// </summary>
    Task<ApplicationTrackingResultViewModel> TrackApplicationAsync(
        ApplicationTrackingInputModel input, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tạo mã tra cứu duy nhất, thân thiện (ví dụ: NOV-APP-20261011-8F2A).
    /// </summary>
    Task<string> GenerateUniqueTrackingCodeAsync(CancellationToken cancellationToken = default);
}
