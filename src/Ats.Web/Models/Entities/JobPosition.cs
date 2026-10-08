using System.ComponentModel.DataAnnotations.Schema;

namespace Ats.Web.Models.Entities;

public class JobPosition : BaseEntity
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public Guid DepartmentId { get; set; }
    public string JobLevel { get; set; } = string.Empty;
    public string? Description { get; set; }
    
    [Column(TypeName = "jsonb")]
    public string? StandardCompetencies { get; set; }
    public bool IsActive { get; set; } = true;
    
    // FK đến Khung năng lực (SCRUM-222): nullable — một chức danh có thể chưa gán khung
    public Guid? CompetencyFrameworkId { get; set; }
    public CompetencyFramework? CompetencyFramework { get; set; }

    public Department Department { get; set; } = null!;

    [NotMapped]
    public decimal? MinSalary
    {
        get => GetSalaryMeta("min_salary");
        set => SetSalaryMeta("min_salary", value);
    }

    [NotMapped]
    public decimal? MaxSalary
    {
        get => GetSalaryMeta("max_salary");
        set => SetSalaryMeta("max_salary", value);
    }

    private decimal? GetSalaryMeta(string key)
    {
        if (string.IsNullOrWhiteSpace(StandardCompetencies)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(StandardCompetencies);
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object && doc.RootElement.TryGetProperty(key, out var prop))
            {
                if (prop.ValueKind == System.Text.Json.JsonValueKind.Number && prop.TryGetDecimal(out var val)) return val;
                if (prop.ValueKind == System.Text.Json.JsonValueKind.String && decimal.TryParse(prop.GetString(), out var sVal)) return sVal;
            }
        }
        catch { }
        return null;
    }

    private void SetSalaryMeta(string key, decimal? value)
    {
        var dict = new Dictionary<string, object>();
        if (!string.IsNullOrWhiteSpace(StandardCompetencies))
        {
            try
            {
                var existing = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(StandardCompetencies);
                if (existing != null) dict = existing;
            }
            catch { }
        }

        if (value.HasValue)
        {
            dict[key] = value.Value;
        }
        else
        {
            dict.Remove(key);
        }

        StandardCompetencies = System.Text.Json.JsonSerializer.Serialize(dict);
    }
}
