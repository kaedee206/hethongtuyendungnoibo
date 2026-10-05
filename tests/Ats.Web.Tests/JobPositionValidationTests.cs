using System.ComponentModel.DataAnnotations;
using Ats.Web.Models.ViewModels.JobPositions;
using Xunit;

namespace Ats.Web.Tests;

public class JobPositionValidationTests
{
    private static IList<ValidationResult> ValidateModel(object model)
    {
        var validationResults = new List<ValidationResult>();
        var ctx = new ValidationContext(model, null, null);
        Validator.TryValidateObject(model, ctx, validationResults, true);

        if (model is IValidatableObject validatable)
        {
            var customResults = validatable.Validate(ctx);
            validationResults.AddRange(customResults);
        }

        return validationResults;
    }

    [Fact]
    public void ValidModel_ShouldPassValidation()
    {
        var model = new JobPositionFormViewModel
        {
            Code = "SWE-SR-01",
            Title = "Senior Software Engineer",
            DepartmentId = Guid.NewGuid(),
            JobLevel = "SENIOR",
            MinSalary = 25_000_000,
            MaxSalary = 45_000_000
        };

        var results = ValidateModel(model);

        Assert.Empty(results);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Code_WhenMissing_ShouldFailValidation(string? code)
    {
        var model = new JobPositionFormViewModel
        {
            Code = code!,
            Title = "Senior Software Engineer",
            DepartmentId = Guid.NewGuid(),
            JobLevel = "SENIOR",
            MinSalary = 20_000_000,
            MaxSalary = 30_000_000
        };

        var results = ValidateModel(model);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(model.Code)));
    }

    [Theory]
    [InlineData("AB")]
    [InlineData("SWE SR 01")]
    [InlineData("SWE@SR#")]
    public void Code_WhenInvalidFormat_ShouldFailValidation(string invalidCode)
    {
        var model = new JobPositionFormViewModel
        {
            Code = invalidCode,
            Title = "Senior Software Engineer",
            DepartmentId = Guid.NewGuid(),
            JobLevel = "SENIOR",
            MinSalary = 20_000_000,
            MaxSalary = 30_000_000
        };

        var results = ValidateModel(model);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(model.Code)));
    }

    [Fact]
    public void Salaries_WhenMaxSalaryLessThanMinSalary_ShouldFailRangeValidation()
    {
        var model = new JobPositionFormViewModel
        {
            Code = "POS-DEV-01",
            Title = "Software Developer",
            DepartmentId = Guid.NewGuid(),
            JobLevel = "MIDDLE",
            MinSalary = 35_000_000,
            MaxSalary = 20_000_000
        };

        var results = ValidateModel(model);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(model.MaxSalary)) &&
                                      r.ErrorMessage!.Contains("lớn hơn hoặc bằng"));
    }

    [Fact]
    public void Salaries_WhenEqual_ShouldPassValidation()
    {
        var model = new JobPositionFormViewModel
        {
            Code = "POS-DEV-02",
            Title = "Junior Developer",
            DepartmentId = Guid.NewGuid(),
            JobLevel = "JUNIOR",
            MinSalary = 15_000_000,
            MaxSalary = 15_000_000
        };

        var results = ValidateModel(model);

        Assert.Empty(results);
    }
}
