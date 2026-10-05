using Ats.Web.Constants;
using Ats.Web.Data;
using Ats.Web.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ats.Web.Controllers;

[Authorize(Roles = "ADMIN,Admin,HR_MANAGER,HRManager,RECRUITER,Recruiter")]
[Route("question-banks")]
[Route("ngan-hang-cau-hoi")]
public class QuestionBanksController(ApplicationDbContext dbContext) : Controller
{
    private readonly ApplicationDbContext _dbContext = dbContext;

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        await SeedInitialQuestionsIfEmptyAsync();
        return View();
    }

    [HttpGet("/api/question-banks")]
    public async Task<IActionResult> GetQuestions(
        [FromQuery] string? keyword = null,
        [FromQuery] string? competency = null,
        [FromQuery] string? difficulty = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        await SeedInitialQuestionsIfEmptyAsync();

        if (page < 1) page = 1;
        if (pageSize <= 0 || pageSize > 100) pageSize = 10;

        var query = _dbContext.InterviewQuestionBanks
            .AsNoTracking()
            .Where(q => !q.IsDeleted);

        if (!string.IsNullOrWhiteSpace(keyword) && keyword.Trim().Length >= 3)
        {
            var kw = keyword.Trim().ToLower();
            query = query.Where(q => q.Content.ToLower().Contains(kw) || 
                                     (q.SuggestedAnswer != null && q.SuggestedAnswer.ToLower().Contains(kw)));
        }

        if (!string.IsNullOrWhiteSpace(competency))
        {
            query = query.Where(q => q.Competency == competency);
        }

        if (!string.IsNullOrWhiteSpace(difficulty))
        {
            query = query.Where(q => q.Difficulty == difficulty);
        }

        var totalRecords = await query.CountAsync(cancellationToken);
        var totalPages = (int)Math.Ceiling(totalRecords / (double)pageSize);

        var questions = await query
            .OrderByDescending(q => q.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(q => new
            {
                q.Id,
                q.Content,
                q.Competency,
                q.Difficulty,
                Suggestion = q.SuggestedAnswer,
                q.IsActive,
                q.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            isSuccess = true,
            data = questions,
            totalRecords,
            currentPage = page,
            pageSize,
            totalPages
        });
    }

    [HttpPost("/api/question-banks")]
    public async Task<IActionResult> CreateQuestion([FromBody] CreateQuestionDto dto, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dto.Content))
        {
            return BadRequest(new { isSuccess = false, message = "Nội dung câu hỏi không được để trống." });
        }

        if (string.IsNullOrWhiteSpace(dto.Competency))
        {
            return BadRequest(new { isSuccess = false, message = "Vui lòng chọn khung năng lực áp dụng." });
        }

        var question = new InterviewQuestionBank
        {
            Id = Guid.NewGuid(),
            Content = dto.Content.Trim(),
            Competency = dto.Competency.Trim(),
            Difficulty = string.IsNullOrWhiteSpace(dto.Difficulty) ? "Cơ bản" : dto.Difficulty.Trim(),
            SuggestedAnswer = string.IsNullOrWhiteSpace(dto.Suggestion) ? null : dto.Suggestion.Trim(),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _dbContext.InterviewQuestionBanks.Add(question);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new { isSuccess = true, message = "Thêm câu hỏi mới thành công.", data = question.Id });
    }

    [HttpPut("/api/question-banks/{id}")]
    public async Task<IActionResult> UpdateQuestion(Guid id, [FromBody] UpdateQuestionDto dto, CancellationToken cancellationToken)
    {
        var question = await _dbContext.InterviewQuestionBanks.FirstOrDefaultAsync(q => q.Id == id && !q.IsDeleted, cancellationToken);
        if (question == null)
        {
            return NotFound(new { isSuccess = false, message = "Không tìm thấy câu hỏi." });
        }

        if (string.IsNullOrWhiteSpace(dto.Content))
        {
            return BadRequest(new { isSuccess = false, message = "Nội dung câu hỏi không được để trống." });
        }

        question.Content = dto.Content.Trim();
        if (!string.IsNullOrWhiteSpace(dto.Competency))
            question.Competency = dto.Competency.Trim();
        if (!string.IsNullOrWhiteSpace(dto.Difficulty))
            question.Difficulty = dto.Difficulty.Trim();
        question.SuggestedAnswer = string.IsNullOrWhiteSpace(dto.Suggestion) ? null : dto.Suggestion.Trim();
        question.UpdatedAt = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new { isSuccess = true, message = "Cập nhật câu hỏi thành công." });
    }

    [HttpDelete("/api/question-banks/{id}")]
    public async Task<IActionResult> DeleteQuestion(Guid id, CancellationToken cancellationToken)
    {
        var question = await _dbContext.InterviewQuestionBanks.FirstOrDefaultAsync(q => q.Id == id && !q.IsDeleted, cancellationToken);
        if (question == null)
        {
            return NotFound(new { isSuccess = false, message = "Không tìm thấy câu hỏi." });
        }

        question.IsDeleted = true;
        question.UpdatedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new { isSuccess = true, message = "Xóa câu hỏi thành công." });
    }

    private async Task SeedInitialQuestionsIfEmptyAsync()
    {
        if (!await _dbContext.InterviewQuestionBanks.AnyAsync())
        {
            _dbContext.InterviewQuestionBanks.AddRange(
                new InterviewQuestionBank
                {
                    Id = Guid.NewGuid(),
                    Content = "Bạn hãy mô tả một dự án gần đây nhất bạn tham gia. Vai trò của bạn là gì và bạn đã sử dụng công nghệ nào?",
                    Competency = "Kỹ năng chuyên môn",
                    Difficulty = "Cơ bản",
                    SuggestedAnswer = "Đánh giá khả năng nắm bắt dự án, hiểu biết về stack công nghệ.",
                    IsActive = true,
                    CreatedAt = DateTimeOffset.UtcNow
                },
                new InterviewQuestionBank
                {
                    Id = Guid.NewGuid(),
                    Content = "Hãy kể về một lần bạn gặp phải một bug khó trên production. Bạn đã phân tích và giải quyết nó như thế nào?",
                    Competency = "Giải quyết vấn đề",
                    Difficulty = "Trung bình",
                    SuggestedAnswer = "Ứng viên cần nêu rõ các bước debug, tìm root cause, cách fix tạm thời và fix triệt để.",
                    IsActive = true,
                    CreatedAt = DateTimeOffset.UtcNow
                },
                new InterviewQuestionBank
                {
                    Id = Guid.NewGuid(),
                    Content = "Làm thế nào để bạn đảm bảo hệ thống có thể scale khi lượng truy cập tăng gấp 10 lần trong tương lai gần?",
                    Competency = "Kỹ năng chuyên môn",
                    Difficulty = "Nâng cao",
                    SuggestedAnswer = "Kiến thức về caching, load balancing, database indexing, microservices...",
                    IsActive = true,
                    CreatedAt = DateTimeOffset.UtcNow
                },
                new InterviewQuestionBank
                {
                    Id = Guid.NewGuid(),
                    Content = "Bạn thường làm gì khi có bất đồng quan điểm với thành viên khác trong team về cách thiết kế hệ thống?",
                    Competency = "Làm việc nhóm",
                    Difficulty = "Trung bình",
                    SuggestedAnswer = "Đánh giá thái độ lắng nghe, cách đưa ra lập luận dựa trên data/fact thay vì cảm xúc.",
                    IsActive = true,
                    CreatedAt = DateTimeOffset.UtcNow
                },
                new InterviewQuestionBank
                {
                    Id = Guid.NewGuid(),
                    Content = "Hãy trình bày cách bạn giải thích một khái niệm kỹ thuật phức tạp (như API) cho một người không có chuyên môn (ví dụ: khách hàng)?",
                    Competency = "Giao tiếp",
                    Difficulty = "Cơ bản",
                    SuggestedAnswer = "Đánh giá khả năng sử dụng ngôn từ dễ hiểu, dùng ví dụ ẩn dụ thực tế.",
                    IsActive = true,
                    CreatedAt = DateTimeOffset.UtcNow
                }
            );
            await _dbContext.SaveChangesAsync();
        }
    }

    public class CreateQuestionDto
    {
        public string Content { get; set; } = string.Empty;
        public string Competency { get; set; } = string.Empty;
        public string Difficulty { get; set; } = "Cơ bản";
        public string? Suggestion { get; set; }
    }

    public class UpdateQuestionDto
    {
        public string Content { get; set; } = string.Empty;
        public string? Competency { get; set; }
        public string? Difficulty { get; set; }
        public string? Suggestion { get; set; }
    }
}
