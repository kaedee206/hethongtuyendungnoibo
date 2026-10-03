using System.Text.Json;
using Ats.Web.Data;
using Ats.Web.Models.Entities;
using Ats.Web.Models.ViewModels.EvaluationCriteria;
using Ats.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ats.Web.Services;

public class EvaluationCriteriaService(
    ApplicationDbContext dbContext,
    ILogger<EvaluationCriteriaService> logger) : IEvaluationCriteriaService
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly ILogger<EvaluationCriteriaService> _logger = logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private class RubricStoragePayload
    {
        public string? Summary { get; set; }
        public int ScaleMin { get; set; } = 1;
        public int ScaleMax { get; set; } = 5;
        public List<CriteriaRubricLevelViewModel> Levels { get; set; } = [];
    }

    public async Task<EvaluationCriteriaListViewModel> GetAllCriteriaAsync(CancellationToken cancellationToken = default)
    {
        await SeedStandardCriteriaIfEmptyAsync(cancellationToken);

        var entities = await _dbContext.EvaluationCriterias
            .Where(c => !c.IsDeleted)
            .OrderBy(c => c.CriteriaType)
            .ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);

        var items = new List<EvaluationCriteriaItemViewModel>();

        foreach (var entity in entities)
        {
            var rubric = ParseRubricFromEntity(entity);
            var definedLevelsCount = rubric.Levels.Count(l => !string.IsNullOrWhiteSpace(l.BehavioralDescription));

            items.Add(new EvaluationCriteriaItemViewModel
            {
                Id = entity.Id,
                Name = entity.Name,
                Description = rubric.CriteriaDescription,
                CriteriaType = entity.CriteriaType,
                CriteriaTypeDisplayName = GetCriteriaTypeDisplayName(entity.CriteriaType),
                Weight = entity.Weight,
                ScaleMin = rubric.ScaleMin,
                ScaleMax = rubric.ScaleMax,
                DefinedLevelsCount = definedLevelsCount,
                IsFullyDefined = rubric.IsFullyDefined,
                CreatedAt = entity.CreatedAt,
                UpdatedAt = entity.UpdatedAt
            });
        }

        return new EvaluationCriteriaListViewModel
        {
            CriteriaList = items
        };
    }

    public async Task<CriteriaRubricViewModel?> GetRubricAsync(Guid criteriaId, CancellationToken cancellationToken = default)
    {
        await SeedStandardCriteriaIfEmptyAsync(cancellationToken);

        var entity = await _dbContext.EvaluationCriterias
            .FirstOrDefaultAsync(c => c.Id == criteriaId && !c.IsDeleted, cancellationToken);

        if (entity == null)
        {
            return null;
        }

        return ParseRubricFromEntity(entity);
    }

    public async Task<(bool Success, string Message)> SaveRubricAsync(
        CriteriaRubricSaveInputModel input,
        CancellationToken cancellationToken = default)
    {
        if (input.ScaleMin != 1)
        {
            return (false, "Mức điểm tối thiểu của thang đo phải bắt đầu từ 1.");
        }

        if (input.ScaleMax < 3 || input.ScaleMax > 5)
        {
            return (false, "Thang điểm phải trong khoảng từ 1-3 đến 1-5.");
        }

        var entity = await _dbContext.EvaluationCriterias
            .FirstOrDefaultAsync(c => c.Id == input.CriteriaId && !c.IsDeleted, cancellationToken);

        if (entity == null)
        {
            return (false, "Không tìm thấy tiêu chí đánh giá yêu cầu.");
        }

        var activeLevels = input.Levels
            .Where(l => l.Score >= input.ScaleMin && l.Score <= input.ScaleMax)
            .OrderBy(l => l.Score)
            .ToList();

        for (int score = input.ScaleMin; score <= input.ScaleMax; score++)
        {
            var level = activeLevels.FirstOrDefault(l => l.Score == score);
            if (level == null)
            {
                return (false, $"Thiếu cấu hình cho Mức {score} trong thang điểm 1–{input.ScaleMax}.");
            }

            if (level.IsRequired)
            {
                if (string.IsNullOrWhiteSpace(level.BehavioralDescription))
                {
                    return (false, $"Mức {score} ({level.LevelName}) là mức bắt buộc, không được để trống mô tả hành vi.");
                }

                if (level.BehavioralDescription.Trim().Length < 10)
                {
                    return (false, $"Mô tả hành vi ở Mức {score} ({level.LevelName}) quá ngắn. Vui lòng nhập chi tiết tối thiểu 10 ký tự.");
                }
            }
        }

        var payload = new RubricStoragePayload
        {
            Summary = input.Summary ?? entity.Name,
            ScaleMin = input.ScaleMin,
            ScaleMax = input.ScaleMax,
            Levels = activeLevels
        };

        entity.Description = JsonSerializer.Serialize(payload, JsonOptions);
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Đã lưu cấu hình rubric cho tiêu chí {CriteriaId} ({CriteriaName}) với thang điểm 1-{ScaleMax}", entity.Id, entity.Name, input.ScaleMax);

        return (true, "Lưu cấu hình thang điểm và mô tả mức độ thành công.");
    }

    public async Task<InterviewEvaluationSheetViewModel> GetInterviewSheetRubricsAsync(CancellationToken cancellationToken = default)
    {
        await SeedStandardCriteriaIfEmptyAsync(cancellationToken);

        var entities = await _dbContext.EvaluationCriterias
            .Where(c => !c.IsDeleted)
            .OrderBy(c => c.CriteriaType)
            .ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);

        var rubrics = entities.Select(ParseRubricFromEntity).ToList();

        return new InterviewEvaluationSheetViewModel
        {
            CriteriaRubrics = rubrics
        };
    }

    public async Task SeedStandardCriteriaIfEmptyAsync(CancellationToken cancellationToken = default)
    {
        if (await _dbContext.EvaluationCriterias.AnyAsync(cancellationToken))
        {
            return;
        }

        var defaultCriteriaList = GetDefaultStandardCriteria();

        foreach (var c in defaultCriteriaList)
        {
            _dbContext.EvaluationCriterias.Add(c);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Đã khởi tạo {Count} tiêu chí đánh giá chuẩn hóa NoveraTech", defaultCriteriaList.Count);
    }

    private static CriteriaRubricViewModel ParseRubricFromEntity(EvaluationCriteria entity)
    {
        if (!string.IsNullOrWhiteSpace(entity.Description) && entity.Description.TrimStart().StartsWith('{'))
        {
            try
            {
                var payload = JsonSerializer.Deserialize<RubricStoragePayload>(entity.Description, JsonOptions);
                if (payload != null && payload.Levels.Count > 0)
                {
                    return new CriteriaRubricViewModel
                    {
                        CriteriaId = entity.Id,
                        CriteriaName = entity.Name,
                        CriteriaDescription = payload.Summary,
                        CriteriaType = entity.CriteriaType,
                        Weight = entity.Weight,
                        ScaleMin = payload.ScaleMin > 0 ? payload.ScaleMin : 1,
                        ScaleMax = payload.ScaleMax >= 3 ? payload.ScaleMax : 5,
                        Levels = payload.Levels.OrderBy(l => l.Score).ToList(),
                        UpdatedAt = entity.UpdatedAt
                    };
                }
            }
            catch
            {
            }
        }

        var fallbackLevels = GenerateDefaultLevels(entity.CriteriaType, entity.Name, 1, 5);

        return new CriteriaRubricViewModel
        {
            CriteriaId = entity.Id,
            CriteriaName = entity.Name,
            CriteriaDescription = entity.Description,
            CriteriaType = entity.CriteriaType,
            Weight = entity.Weight,
            ScaleMin = 1,
            ScaleMax = 5,
            Levels = fallbackLevels,
            UpdatedAt = entity.UpdatedAt
        };
    }

    private static string GetCriteriaTypeDisplayName(string criteriaType) => criteriaType switch
    {
        "HARD_SKILL" => "Chuyên môn kỹ thuật",
        "SOFT_SKILL" => "Kỹ năng mềm & Tư duy",
        "CULTURE" => "Văn hóa & Đạo đức nghề nghiệp",
        _ => "Tiêu chí chung"
    };

    private static List<CriteriaRubricLevelViewModel> GenerateDefaultLevels(string criteriaType, string criteriaName, int scaleMin, int scaleMax)
    {
        var levels = new List<CriteriaRubricLevelViewModel>();

        if (criteriaType == "HARD_SKILL")
        {
            levels.Add(new CriteriaRubricLevelViewModel
            {
                Score = 1,
                LevelName = "Chưa đạt yêu cầu",
                BehavioralDescription = "Chưa nắm vững khái niệm kỹ thuật cốt lõi, không giải thích được luồng thực thi hoặc vi phạm các nguyên lý thiết kế cơ bản.",
                IsRequired = true
            });
            levels.Add(new CriteriaRubricLevelViewModel
            {
                Score = 2,
                LevelName = "Dưới kỳ vọng",
                BehavioralDescription = "Nắm kiến thức ở mức cơ bản nhưng còn lúng túng khi gặp bài toán thực tế; thiếu nhận thức về tối ưu tài nguyên và bẫy lỗi thường gặp.",
                IsRequired = true
            });
            levels.Add(new CriteriaRubricLevelViewModel
            {
                Score = 3,
                LevelName = "Đạt yêu cầu (Chuẩn)",
                BehavioralDescription = "Nắm vững lý thuyết và thực hành, viết mã nguồn sạch sẽ, tuân thủ đúng kiến trúc chuẩn và xử lý an toàn các ngoại lệ.",
                IsRequired = true
            });
            if (scaleMax >= 4)
            {
                levels.Add(new CriteriaRubricLevelViewModel
                {
                    Score = 4,
                    LevelName = "Tốt / Vượt kỳ vọng",
                    BehavioralDescription = "Chủ động đề xuất giải pháp tối ưu hiệu năng, am hiểu sâu cơ chế bên dưới của runtime, kiến trúc chịu tải và bảo mật.",
                    IsRequired = true
                });
            }
            if (scaleMax >= 5)
            {
                levels.Add(new CriteriaRubricLevelViewModel
                {
                    Score = 5,
                    LevelName = "Xuất sắc / Chuyên gia",
                    BehavioralDescription = "Trình độ chuyên gia, tư duy thiết kế giải pháp cấp Enterprise, có khả năng dẫn dắt kỹ thuật và phản biện xuất sắc các trade-off.",
                    IsRequired = true
                });
            }
        }
        else if (criteriaType == "SOFT_SKILL")
        {
            levels.Add(new CriteriaRubricLevelViewModel
            {
                Score = 1,
                LevelName = "Chưa đạt yêu cầu",
                BehavioralDescription = "Trình bày lan man, khó diễn đạt ý tưởng kỹ thuật; phản ứng phòng thủ hoặc né tránh khi bị chất vấn.",
                IsRequired = true
            });
            levels.Add(new CriteriaRubricLevelViewModel
            {
                Score = 2,
                LevelName = "Dưới kỳ vọng",
                BehavioralDescription = "Giao tiếp được nhưng thụ động; kỹ năng lắng nghe còn hạn chế và cần người khác hướng dẫn chi tiết để làm rõ vấn đề.",
                IsRequired = true
            });
            levels.Add(new CriteriaRubricLevelViewModel
            {
                Score = 3,
                LevelName = "Đạt yêu cầu (Chuẩn)",
                BehavioralDescription = "Trình bày mạch lạc, logic, biết lắng nghe tích cực; phối hợp tốt với các thành viên khác trong nhóm.",
                IsRequired = true
            });
            if (scaleMax >= 4)
            {
                levels.Add(new CriteriaRubricLevelViewModel
                {
                    Score = 4,
                    LevelName = "Tốt / Vượt kỳ vọng",
                    BehavioralDescription = "Giao tiếp thuyết phục, truyền đạt vấn đề phức tạp thành đơn giản, có khả năng điều phối và kết nối nhóm hiệu quả.",
                    IsRequired = true
                });
            }
            if (scaleMax >= 5)
            {
                levels.Add(new CriteriaRubricLevelViewModel
                {
                    Score = 5,
                    LevelName = "Xuất sắc / Chuyên gia",
                    BehavioralDescription = "Kỹ năng truyền cảm hứng, dẫn dắt thảo luận, giải quyết xung đột xuất sắc và xây dựng sự đồng thuận cao trong đội ngũ.",
                    IsRequired = true
                });
            }
        }
        else
        {
            levels.Add(new CriteriaRubricLevelViewModel
            {
                Score = 1,
                LevelName = "Chưa đạt yêu cầu",
                BehavioralDescription = "Thiếu tinh thần trách nhiệm, đổ lỗi cho hoàn cảnh hoặc không quan tâm đến tiêu chuẩn chất lượng của công việc.",
                IsRequired = true
            });
            levels.Add(new CriteriaRubricLevelViewModel
            {
                Score = 2,
                LevelName = "Dưới kỳ vọng",
                BehavioralDescription = "Hoàn thành công việc khi được phân công nhưng thiếu tính chủ động; tinh thần cam kết chỉ ở mức trung bình.",
                IsRequired = true
            });
            levels.Add(new CriteriaRubricLevelViewModel
            {
                Score = 3,
                LevelName = "Đạt yêu cầu (Chuẩn)",
                BehavioralDescription = "Thể hiện tinh thần trách nhiệm rõ ràng với sản phẩm, tôn trọng văn hóa công ty và giữ cam kết trong công việc.",
                IsRequired = true
            });
            if (scaleMax >= 4)
            {
                levels.Add(new CriteriaRubricLevelViewModel
                {
                    Score = 4,
                    LevelName = "Tốt / Vượt kỳ vọng",
                    BehavioralDescription = "Chủ động nhận việc khó, tinh thần sở hữu cao (Ownership), luôn hướng đến chuẩn mực kỹ thuật cao nhất.",
                    IsRequired = true
                });
            }
            if (scaleMax >= 5)
            {
                levels.Add(new CriteriaRubricLevelViewModel
                {
                    Score = 5,
                    LevelName = "Xuất sắc / Chuyên gia",
                    BehavioralDescription = "Là tấm gương về văn hóa công nghệ 'Engineering-First, Zero Politics', chủ động lan tỏa năng lượng tích cực và bảo vệ uy tín tập thể.",
                    IsRequired = true
                });
            }
        }

        return levels.Where(l => l.Score >= scaleMin && l.Score <= scaleMax).ToList();
    }

    private static List<EvaluationCriteria> GetDefaultStandardCriteria()
    {
        var criteriaDefs = new[]
        {
            (
                Id: new Guid("a0000000-0000-0000-0000-000000000001"),
                Name: "Kiến trúc hệ thống & Clean Architecture",
                Type: "HARD_SKILL",
                Weight: 1.5m,
                Summary: "Đánh giá hiểu biết về Clean Architecture, phân tách module, Dependency Injection và nguyên lý SOLID trong thiết kế phần mềm."
            ),
            (
                Id: new Guid("a0000000-0000-0000-0000-000000000002"),
                Name: "Lập trình C# .NET & Xử lý bất đồng bộ (Async Concurrency)",
                Type: "HARD_SKILL",
                Weight: 1.5m,
                Summary: "Đánh giá kỹ năng viết mã C# chuẩn mực, xử lý async/await, Task, CancellationToken, memory allocation và concurrency."
            ),
            (
                Id: new Guid("a0000000-0000-0000-0000-000000000003"),
                Name: "Thiết kế Cơ sở dữ liệu & Tối ưu hóa SQL",
                Type: "HARD_SKILL",
                Weight: 1.2m,
                Summary: "Đánh giá năng lực thiết kế schema quan hệ, indexing, giải quyết bài toán N+1 trong EF Core và phân tích query execution plan."
            ),
            (
                Id: new Guid("a0000000-0000-0000-0000-000000000004"),
                Name: "Tư duy giải quyết vấn đề & Phân tích thuật toán",
                Type: "SOFT_SKILL",
                Weight: 1.0m,
                Summary: "Đánh giá tư duy phân tích bài toán, khả năng chia nhỏ vấn đề, ước lượng độ phức tạp thời gian/không gian và xử lý edge cases."
            ),
            (
                Id: new Guid("a0000000-0000-0000-0000-000000000005"),
                Name: "Giao tiếp chuyên môn & Phối hợp đội ngũ",
                Type: "SOFT_SKILL",
                Weight: 1.0m,
                Summary: "Đánh giá cách thức trình bày ý tưởng kỹ thuật, lắng nghe phản biện, khả năng phối hợp liên chức năng và chia sẻ tri thức."
            ),
            (
                Id: new Guid("a0000000-0000-0000-0000-000000000006"),
                Name: "Văn hóa Engineering-First & Tinh thần trách nhiệm (Ownership)",
                Type: "CULTURE",
                Weight: 1.0m,
                Summary: "Đánh giá mức độ phù hợp với văn hóa thực chiến của NoveraTech, cam kết chất lượng sản phẩm và thái độ chủ động giải quyết sự cố."
            )
        };

        var list = new List<EvaluationCriteria>();
        var now = DateTimeOffset.UtcNow;

        foreach (var def in criteriaDefs)
        {
            var levels = GenerateDefaultLevels(def.Type, def.Name, 1, 5);
            var payload = new RubricStoragePayload
            {
                Summary = def.Summary,
                ScaleMin = 1,
                ScaleMax = 5,
                Levels = levels
            };

            list.Add(new EvaluationCriteria
            {
                Id = def.Id,
                Name = def.Name,
                Description = JsonSerializer.Serialize(payload, JsonOptions),
                CriteriaType = def.Type,
                Weight = def.Weight,
                CreatedAt = now,
                UpdatedAt = now,
                IsDeleted = false
            });
        }

        return list;
    }
}
