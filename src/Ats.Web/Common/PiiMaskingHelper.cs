namespace Ats.Web.Common;

/// <summary>
/// Helper che giấu thông tin định danh cá nhân (PII Masking) tuân thủ Nghị định 13/2023/NĐ-CP về Bảo vệ dữ liệu cá nhân.
/// Sử dụng khi ghi nhật ký (logging) và hiển thị thông báo ra giao diện bên ngoài.
/// </summary>
public static class PiiMaskingHelper
{
    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "******";

        var parts = email.Trim().Split('@');
        if (parts.Length != 2) return "******";

        var name = parts[0];
        var domain = parts[1];

        if (name.Length <= 2)
        {
            return $"{name[0]}*@{domain}";
        }

        return $"{name[0]}***{name[^1]}@{domain}";
    }

    public static string MaskPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return "******";

        var clean = new string(phone.Where(char.IsDigit).ToArray());
        if (clean.Length < 7) return "******";

        // Giữ 3 số đầu và 3 số cuối: 091****678
        return $"{clean[..3]}****{clean[^3..]}";
    }

    public static string MaskFullName(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return "***";

        var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= 1) return $"{fullName[0]}***";

        // Giữ họ, che tên lót và tên chính: Nguyễn V***
        return $"{parts[0]} {parts[^1][0]}***";
    }
}
