using Ats.Web.Controllers;
using Ats.Web.Models.ViewModels.EvaluationCriteria;
using Ats.Web.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

namespace Ats.Web.Tests;

public class EvaluationCriteriaRubricTests
{
    private readonly Mock<ILogger<EvaluationCriteriaService>> _serviceLoggerMock = new();
    private readonly Mock<ILogger<EvaluationCriteriaController>> _controllerLoggerMock = new();

    [Fact]
    public async Task GetAllCriteriaAsync_WhenDatabaseEmpty_SeedsStandardCriteriaAndReturnsList()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var service = new EvaluationCriteriaService(context, _serviceLoggerMock.Object);

        var result = await service.GetAllCriteriaAsync();

        result.Should().NotBeNull();
        result.TotalCriteria.Should().Be(6);
        result.HardSkillsCount.Should().Be(3);
        result.SoftSkillsCount.Should().Be(2);
        result.CultureFitCount.Should().Be(1);
        result.FullyDefinedCount.Should().Be(6);
        result.CriteriaList.Should().OnlyContain(c => c.ScaleMin == 1 && c.ScaleMax == 5);
    }

    [Fact]
    public async Task GetRubricAsync_ExistingCriteria_ReturnsCompleteScaleAndLevelDescriptions()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var service = new EvaluationCriteriaService(context, _serviceLoggerMock.Object);
        var all = await service.GetAllCriteriaAsync();
        var targetCriteria = all.CriteriaList.First();

        var rubric = await service.GetRubricAsync(targetCriteria.Id);

        rubric.Should().NotBeNull();
        rubric!.CriteriaId.Should().Be(targetCriteria.Id);
        rubric.ScaleMin.Should().Be(1);
        rubric.ScaleMax.Should().Be(5);
        rubric.Levels.Should().HaveCount(5);
        rubric.Levels.Should().OnlyContain(l => !string.IsNullOrWhiteSpace(l.BehavioralDescription));
        rubric.IsFullyDefined.Should().BeTrue();
    }

    [Fact]
    public async Task SaveRubricAsync_ValidCustomRubric_UpdatesCriteriaAndPersistsChanges()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var service = new EvaluationCriteriaService(context, _serviceLoggerMock.Object);
        var all = await service.GetAllCriteriaAsync();
        var targetCriteria = all.CriteriaList.First();

        var input = new CriteriaRubricSaveInputModel
        {
            CriteriaId = targetCriteria.Id,
            ScaleMin = 1,
            ScaleMax = 4,
            Summary = "Mục tiêu đánh giá đã cập nhật",
            Levels =
            [
                new() { Score = 1, LevelName = "Yếu", BehavioralDescription = "Chưa thể thực hiện được các thao tác lập trình cơ bản.", IsRequired = true },
                new() { Score = 2, LevelName = "Trung bình", BehavioralDescription = "Hiểu các thao tác cơ bản nhưng cần người hỗ trợ liên tục.", IsRequired = true },
                new() { Score = 3, LevelName = "Khá", BehavioralDescription = "Làm việc độc lập tốt và đáp ứng đủ các tiêu chuẩn kỹ thuật.", IsRequired = true },
                new() { Score = 4, LevelName = "Giỏi", BehavioralDescription = "Giải quyết các bài toán tối ưu phức tạp xuất sắc và phản biện tốt.", IsRequired = true }
            ]
        };

        var (success, message) = await service.SaveRubricAsync(input);

        success.Should().BeTrue();
        message.Should().Contain("thành công");

        var updatedRubric = await service.GetRubricAsync(targetCriteria.Id);
        updatedRubric.Should().NotBeNull();
        updatedRubric!.ScaleMax.Should().Be(4);
        updatedRubric.Levels.Should().HaveCount(4);
        updatedRubric.Levels.First(l => l.Score == 4).LevelName.Should().Be("Giỏi");
    }

    [Fact]
    public async Task SaveRubricAsync_MissingBehavioralDescriptionAtRequiredLevel_FailsValidation()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var service = new EvaluationCriteriaService(context, _serviceLoggerMock.Object);
        var all = await service.GetAllCriteriaAsync();
        var targetCriteria = all.CriteriaList.First();

        var input = new CriteriaRubricSaveInputModel
        {
            CriteriaId = targetCriteria.Id,
            ScaleMin = 1,
            ScaleMax = 5,
            Levels =
            [
                new() { Score = 1, LevelName = "Chưa đạt", BehavioralDescription = "Mô tả chi tiết mức 1 đạt chuẩn quy định kiểm thử.", IsRequired = true },
                new() { Score = 2, LevelName = "Dưới kỳ vọng", BehavioralDescription = "", IsRequired = true },
                new() { Score = 3, LevelName = "Đạt chuẩn", BehavioralDescription = "Mô tả chi tiết mức 3 đạt chuẩn quy định kiểm thử.", IsRequired = true },
                new() { Score = 4, LevelName = "Tốt", BehavioralDescription = "Mô tả chi tiết mức 4 đạt chuẩn quy định kiểm thử.", IsRequired = true },
                new() { Score = 5, LevelName = "Xuất sắc", BehavioralDescription = "Mô tả chi tiết mức 5 đạt chuẩn quy định kiểm thử.", IsRequired = true }
            ]
        };

        var (success, message) = await service.SaveRubricAsync(input);

        success.Should().BeFalse();
        message.Should().Contain("Mức 2");
        message.Should().Contain("không được để trống");
    }

    [Fact]
    public async Task SaveRubricAsync_DescriptionTooShort_FailsValidation()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var service = new EvaluationCriteriaService(context, _serviceLoggerMock.Object);
        var all = await service.GetAllCriteriaAsync();
        var targetCriteria = all.CriteriaList.First();

        var input = new CriteriaRubricSaveInputModel
        {
            CriteriaId = targetCriteria.Id,
            ScaleMin = 1,
            ScaleMax = 3,
            Levels =
            [
                new() { Score = 1, LevelName = "Mức 1", BehavioralDescription = "Ngắn", IsRequired = true },
                new() { Score = 2, LevelName = "Mức 2", BehavioralDescription = "Mô tả chi tiết mức 2 đạt chuẩn quy định kiểm thử.", IsRequired = true },
                new() { Score = 3, LevelName = "Mức 3", BehavioralDescription = "Mô tả chi tiết mức 3 đạt chuẩn quy định kiểm thử.", IsRequired = true }
            ]
        };

        var (success, message) = await service.SaveRubricAsync(input);

        success.Should().BeFalse();
        message.Should().Contain("quá ngắn");
        message.Should().Contain("tối thiểu 10 ký tự");
    }

    [Fact]
    public async Task SaveRubricAsync_InvalidScaleRange_FailsValidation()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var service = new EvaluationCriteriaService(context, _serviceLoggerMock.Object);
        var all = await service.GetAllCriteriaAsync();
        var targetCriteria = all.CriteriaList.First();

        var input = new CriteriaRubricSaveInputModel
        {
            CriteriaId = targetCriteria.Id,
            ScaleMin = 1,
            ScaleMax = 6,
            Levels = []
        };

        var (success, message) = await service.SaveRubricAsync(input);

        success.Should().BeFalse();
        message.Should().Contain("Thang điểm phải trong khoảng");
    }

    [Fact]
    public async Task GetInterviewSheetRubricsAsync_ReturnsAllCriteriaWithRubrics()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var service = new EvaluationCriteriaService(context, _serviceLoggerMock.Object);

        var sheet = await service.GetInterviewSheetRubricsAsync();

        sheet.Should().NotBeNull();
        sheet.TotalCriteria.Should().Be(6);
        sheet.TotalWeight.Should().BeGreaterThan(0);
        sheet.CriteriaRubrics.Should().OnlyContain(r => r.Levels.Count >= 3);
    }

    [Fact]
    public async Task EvaluationCriteriaController_Index_ReturnsViewWithCriteriaList()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var service = new EvaluationCriteriaService(context, _serviceLoggerMock.Object);
        var controller = new EvaluationCriteriaController(service, _controllerLoggerMock.Object);

        var actionResult = await controller.Index();

        var viewResult = actionResult.Should().BeOfType<ViewResult>().Subject;
        var model = viewResult.Model.Should().BeOfType<EvaluationCriteriaListViewModel>().Subject;
        model.TotalCriteria.Should().Be(6);
    }

    [Fact]
    public async Task EvaluationCriteriaController_GetRubricApi_ReturnsOkWithRubric()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var service = new EvaluationCriteriaService(context, _serviceLoggerMock.Object);
        var all = await service.GetAllCriteriaAsync();
        var targetCriteria = all.CriteriaList.First();
        var controller = new EvaluationCriteriaController(service, _controllerLoggerMock.Object);

        var actionResult = await controller.GetRubric(targetCriteria.Id);

        var okResult = actionResult.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task EvaluationCriteriaController_GetInterviewSheetApi_ReturnsOkWithSheetData()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var service = new EvaluationCriteriaService(context, _serviceLoggerMock.Object);
        var controller = new EvaluationCriteriaController(service, _controllerLoggerMock.Object);

        var actionResult = await controller.GetInterviewSheetApi();

        var okResult = actionResult.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task EvaluationCriteriaController_SaveRubric_ValidationFails_ReturnsBadRequest()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var service = new EvaluationCriteriaService(context, _serviceLoggerMock.Object);
        var all = await service.GetAllCriteriaAsync();
        var targetCriteria = all.CriteriaList.First();
        var controller = new EvaluationCriteriaController(service, _controllerLoggerMock.Object);

        var input = new CriteriaRubricSaveInputModel
        {
            CriteriaId = targetCriteria.Id,
            ScaleMin = 1,
            ScaleMax = 5,
            Levels =
            [
                new() { Score = 1, LevelName = "Mức 1", BehavioralDescription = "", IsRequired = true }
            ]
        };

        var actionResult = await controller.SaveRubric(input);

        var badRequestResult = actionResult.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequestResult.Value.Should().NotBeNull();
    }
}
