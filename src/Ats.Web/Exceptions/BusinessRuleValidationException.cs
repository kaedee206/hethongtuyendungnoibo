namespace Ats.Web.Exceptions;

/// <summary>
/// Ngoại lệ ném ra khi vi phạm quy tắc nghiệp vụ trong hệ thống ATS (Scrum - Số 53).
/// Tuân thủ quy chuẩn đặt tên C# Naming Conventions (hậu tố Exception).
/// </summary>
public class BusinessRuleValidationException : Exception
{
    /// <summary>
    /// Danh sách các lỗi ràng buộc nghiệp vụ cụ thể.
    /// </summary>
    public List<string> ValidationErrors { get; } = new();

    /// <summary>
    /// Dữ liệu chi tiết đính kèm theo lỗi (ví dụ: thông tin các ca phỏng vấn bị xung đột).
    /// </summary>
    public object? ErrorData { get; }

    public BusinessRuleValidationException(string message) : base(message)
    {
    }

    public BusinessRuleValidationException(string message, List<string> errors) : base(message)
    {
        ValidationErrors = errors ?? new List<string>();
    }

    public BusinessRuleValidationException(string message, object? errorData) : base(message)
    {
        ErrorData = errorData;
    }

    public BusinessRuleValidationException(string message, List<string> errors, object? errorData) : base(message)
    {
        ValidationErrors = errors ?? new List<string>();
        ErrorData = errorData;
    }
}
