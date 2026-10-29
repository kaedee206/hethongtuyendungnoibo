using System.Text.RegularExpressions;
using Ats.Web.Models.Enums;

namespace Ats.Web.Services;

public static partial class EmailTemplateRenderer
{
    private static readonly Dictionary<EmailTemplateType, string[]> SupportedVariables = new()
    {
        [EmailTemplateType.ApplicationReceived] = ["{{CandidateName}}", "{{JobTitle}}", "{{ApplicationCode}}"],
        [EmailTemplateType.InterviewInvitation] = ["{{CandidateName}}", "{{JobTitle}}", "{{InterviewTime}}", "{{InterviewRound}}", "{{InterviewLocation}}"],
        [EmailTemplateType.InterviewThankYou] = ["{{CandidateName}}", "{{JobTitle}}"],
        [EmailTemplateType.Rejection] = ["{{CandidateName}}", "{{JobTitle}}"],
        [EmailTemplateType.JobOffer] = ["{{CandidateName}}", "{{JobTitle}}", "{{OfferStartDate}}"],
    };

    private static readonly Dictionary<EmailTemplateType, string[]> RequiredVariables = new()
    {
        [EmailTemplateType.ApplicationReceived] = ["{{CandidateName}}", "{{JobTitle}}"],
        [EmailTemplateType.InterviewInvitation] = ["{{CandidateName}}", "{{JobTitle}}", "{{InterviewTime}}"],
        [EmailTemplateType.InterviewThankYou] = ["{{CandidateName}}", "{{JobTitle}}"],
        [EmailTemplateType.Rejection] = ["{{CandidateName}}", "{{JobTitle}}"],
        [EmailTemplateType.JobOffer] = ["{{CandidateName}}", "{{JobTitle}}"],
    };

    public static IReadOnlyList<string> GetSupportedVariables(EmailTemplateType templateType)
    {
        return SupportedVariables.TryGetValue(templateType, out var vars)
            ? vars
            : Array.Empty<string>();
    }

    public static IReadOnlyList<string> GetRequiredVariables(EmailTemplateType templateType)
    {
        return RequiredVariables.TryGetValue(templateType, out var vars)
            ? vars
            : Array.Empty<string>();
    }

    public static EmailTemplateValidationResult Validate(EmailTemplateType templateType, string subject, string body)
    {
        var content = (subject ?? string.Empty) + "\n" + (body ?? string.Empty);
        var placeholders = ExtractPlaceholders(content);
        var supported = GetSupportedVariables(templateType).Select(v => NormalizePlaceholder(v)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unsupported = placeholders
            .Where(p => !supported.Contains(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p)
            .ToList();

        var required = GetRequiredVariables(templateType)
            .Select(NormalizePlaceholder)
            .Where(p => !placeholders.Contains(p, StringComparer.OrdinalIgnoreCase))
            .ToList();

        return new EmailTemplateValidationResult
        {
            IsValid = unsupported.Count == 0 && required.Count == 0,
            UnsupportedVariables = unsupported,
            MissingRequiredVariables = required,
            Message = unsupported.Count > 0
                ? $"Biến không hỗ trợ: {string.Join(", ", unsupported)}."
                : required.Count > 0
                    ? $"Thiếu biến bắt buộc: {string.Join(", ", required)}."
                    : "Valid"
        };
    }

    public static string Render(
        EmailTemplateType templateType,
        string subject,
        string body,
        IReadOnlyDictionary<string, string?> values)
    {
        var validation = Validate(templateType, subject, body);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(validation.Message);
        }

        var normalizedValues = values.ToDictionary(kvp => NormalizePlaceholder(kvp.Key), kvp => kvp.Value ?? string.Empty, StringComparer.OrdinalIgnoreCase);

        var renderedSubject = ReplacePlaceholders(subject, normalizedValues);
        var renderedBody = ReplacePlaceholders(body, normalizedValues);

        return $"{renderedSubject}\n{renderedBody}";
    }

    private static string ReplacePlaceholders(string input, IReadOnlyDictionary<string, string> values)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        return PlaceholderRegex().Replace(input, match =>
        {
            var key = NormalizePlaceholder(match.Value);
            if (values.TryGetValue(key, out var value))
            {
                return string.IsNullOrWhiteSpace(value) ? string.Empty : value;
            }

            return match.Value;
        });
    }

    private static List<string> ExtractPlaceholders(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        return PlaceholderRegex()
            .Matches(content)
            .Select(match => NormalizePlaceholder(match.Value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizePlaceholder(string raw)
    {
        return raw.Trim().Replace("{{", string.Empty).Replace("}}", string.Empty).Trim();
    }

    [GeneratedRegex(@"\{\{\s*[^{}\s]+\s*\}\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderRegex();
}

public class EmailTemplateValidationResult
{
    public bool IsValid { get; set; }
    public List<string> UnsupportedVariables { get; set; } = [];
    public List<string> MissingRequiredVariables { get; set; } = [];
    public string Message { get; set; } = string.Empty;
}
