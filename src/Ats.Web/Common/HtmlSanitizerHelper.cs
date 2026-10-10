using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Html;

namespace Ats.Web.Common;

/// <summary>
/// Helper làm sạch nội dung HTML rich-text để phòng chống triệt để Stored XSS và DOM XSS (OWASP Top 10 A03).
/// Cho phép các thẻ định dạng nội dung văn bản an toàn, loại bỏ toàn bộ thẻ mã độc, event handler và javascript URI.
/// </summary>
public static class HtmlSanitizerHelper
{
    // Danh sách thẻ nguy hiểm cần loại bỏ hoàn toàn (bao gồm cả nội dung bên trong cặp thẻ)
    private static readonly string[] DangerousBlockTags = new[]
    {
        "script", "style", "iframe", "object", "embed", "applet",
        "svg", "math", "form", "meta", "link", "base", "noscript"
    };

    // Danh sách thẻ đơn độc hại cần loại bỏ tag
    private static readonly string[] DangerousStandaloneTags = new[]
    {
        "input", "button", "select", "textarea", "keygen", "frameset", "frame", "html", "head", "body"
    };

    // Regex tìm event handler như onload=, onerror=, onclick=, onfocus=
    private static readonly Regex EventHandlerRegex = new(
        @"(?i)\s+on[a-z0-9_-]+\s*=\s*(?:""[^""]*""|'[^']*'|[^\s>]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(500));

    // Regex tìm javascript: hoặc vbscript: hoặc data: (không phải data:image) theo các dạng bọc nháy
    private static readonly Regex DangerousProtocolDoubleQuotesRegex = new(
        @"(?i)(href|src|action|formaction|poster|background)\s*=\s*""\s*(?:javascript|vbscript|data(?!\s*:\s*image)):[^""]*""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(500));

    private static readonly Regex DangerousProtocolSingleQuotesRegex = new(
        @"(?i)(href|src|action|formaction|poster|background)\s*=\s*'\s*(?:javascript|vbscript|data(?!\s*:\s*image)):[^']*'",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(500));

    private static readonly Regex DangerousProtocolNoQuotesRegex = new(
        @"(?i)(href|src|action|formaction|poster|background)\s*=\s*(?:javascript|vbscript|data(?!\s*:\s*image)):[^\s>]*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(500));

    // Regex xóa tàn dư thẻ script bị ngắt quãng hoặc lồng nhau
    private static readonly Regex ScriptRemnantsRegex = new(
        @"(?is)<\s*/?\s*scr(?:ipt)?\b[^>]*>?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(500));

    /// <summary>
    /// Làm sạch chuỗi HTML, trả về chuỗi văn bản HTML an toàn để render.
    /// </summary>
    public static string SanitizeHtml(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var sanitized = input;

        // Vòng lặp chống kỹ thuật lồng tag độc hại: <scr<script>ipt>
        for (int i = 0; i < 5; i++)
        {
            var previous = sanitized;

            // 1. Loại bỏ các cặp thẻ nguy hiểm cùng toàn bộ nội dung bên trong
            foreach (var tag in DangerousBlockTags)
            {
                var blockPattern = $@"(?is)<\s*{tag}\b[^>]*>.*?<\s*/\s*{tag}\s*>";
                sanitized = Regex.Replace(sanitized, blockPattern, string.Empty, RegexOptions.None, TimeSpan.FromMilliseconds(300));

                // Xóa cả thẻ mở hoặc đóng lẻ nếu không thành cặp
                var tagOnlyPattern = $@"(?is)<\s*/?\s*{tag}\b[^>]*>";
                sanitized = Regex.Replace(sanitized, tagOnlyPattern, string.Empty, RegexOptions.None, TimeSpan.FromMilliseconds(300));
            }

            // 2. Loại bỏ các thẻ standalone nguy hiểm
            foreach (var tag in DangerousStandaloneTags)
            {
                var standalonePattern = $@"(?is)<\s*/?\s*{tag}\b[^>]*>";
                sanitized = Regex.Replace(sanitized, standalonePattern, string.Empty, RegexOptions.None, TimeSpan.FromMilliseconds(300));
            }

            if (string.Equals(previous, sanitized, StringComparison.Ordinal))
            {
                break;
            }
        }

        // Loại bỏ tàn dư của script bị phân mảnh
        sanitized = ScriptRemnantsRegex.Replace(sanitized, string.Empty);

        // 3. Loại bỏ tất cả các thuộc tính sự kiện inline (on*)
        sanitized = EventHandlerRegex.Replace(sanitized, string.Empty);

        // 4. Loại bỏ các thuộc tính chứa giao thức javascript:, vbscript: hoặc data URI độc hại
        sanitized = DangerousProtocolDoubleQuotesRegex.Replace(sanitized, "$1=\"#\"");
        sanitized = DangerousProtocolSingleQuotesRegex.Replace(sanitized, "$1=\"#\"");
        sanitized = DangerousProtocolNoQuotesRegex.Replace(sanitized, "$1=\"#\"");

        // 5. Thêm rel="noopener noreferrer" cho các thẻ <a> có target="_blank" để chống Reverse Tabnabbing
        sanitized = Regex.Replace(
            sanitized,
            @"(?i)<a\b([^>]*\btarget\s*=\s*[""']_blank[""'][^>]*)>",
            match =>
            {
                var tagContent = match.Value;
                if (!tagContent.Contains("rel=", StringComparison.OrdinalIgnoreCase))
                {
                    return tagContent.Insert(tagContent.Length - 1, " rel=\"noopener noreferrer\"");
                }
                return tagContent;
            },
            RegexOptions.None,
            TimeSpan.FromMilliseconds(300));

        return sanitized;
    }

    /// <summary>
    /// Làm sạch chuỗi HTML và bọc trong HtmlString an toàn để render trong Razor View.
    /// </summary>
    public static HtmlString SanitizeToHtmlString(string? input)
    {
        return new HtmlString(SanitizeHtml(input));
    }
}
