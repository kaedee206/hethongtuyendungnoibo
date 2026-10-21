using Ats.Web.Data;
using Ats.Web.Models.DTOs.InterviewQuestions;
using Ats.Web.Models.Entities;
using Ats.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ats.Web.Services;

/// <summary>
/// Dịch vụ gợi ý bộ câu hỏi phỏng vấn theo tiêu chí và chức danh (User Story SP-7).
/// Tuân thủ quy chuẩn: Primary Constructor, clean service logic, logging chuẩn mực, null checks.
/// </summary>
public class InterviewQuestionSuggestionService(
    ApplicationDbContext dbContext,
    ILogger<InterviewQuestionSuggestionService> logger) : IInterviewQuestionSuggestionService
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly ILogger<InterviewQuestionSuggestionService> _logger = logger;

    /// <summary>
    /// Lấy danh sách câu hỏi gợi ý cho ứng viên trong buổi phỏng vấn (AC1, AC2, AC3).
    /// </summary>
    public async Task<InterviewQuestionSuggestionResponseDto> GetSuggestedQuestionsAsync(
        Guid candidateId,
        Guid interviewId,
        Guid? positionId = null,
        Guid? criteriaId = null,
        string? criteriaName = null,
        CancellationToken cancellationToken = default)
    {
        // 1. Đảm bảo có seed câu hỏi mẫu nếu ngân hàng câu hỏi còn trống
        await SeedInitialQuestionBankAsync(cancellationToken);

        // 2. Lấy thông tin Ứng viên & Buổi phỏng vấn
        var candidate = await _dbContext.Candidates.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == candidateId && !c.IsDeleted, cancellationToken)
            ?? throw new KeyNotFoundException($"Không tìm thấy ứng viên với Id: {candidateId}");

        var interview = await _dbContext.Interviews.AsNoTracking()
            .Include(i => i.Application)
                .ThenInclude(a => a.JobPosting)
                    .ThenInclude(p => p.Requisition)
                        .ThenInclude(r => r.JobPosition)
            .FirstOrDefaultAsync(i => i.Id == interviewId && !i.IsDeleted, cancellationToken)
            ?? throw new KeyNotFoundException($"Không tìm thấy buổi phỏng vấn với Id: {interviewId}");

        var candidateFullName = $"{candidate.FirstName} {candidate.LastName}".Trim();
        var currentRound = interview.RoundNumber > 0 ? interview.RoundNumber : 1;

        // 3. Xác định chức danh ứng tuyển hiệu lực
        var effectivePositionId = positionId
            ?? interview.Application?.JobPosting?.Requisition?.JobPositionId;

        string jobTitle = interview.Application?.JobPosting?.Requisition?.JobPosition?.Title
            ?? interview.Application?.JobPosting?.Title
            ?? "Chức danh phỏng vấn";

        // 4. Lấy danh sách tiêu chí đánh giá của vòng hiện tại để làm bộ lọc (AC1)
        var criteriaOptions = await GetCriteriaOptionsAsync(interviewId, effectivePositionId, cancellationToken);

        // 5. Truy vấn câu hỏi gợi ý theo chức danh & tiêu chí (AC1)
        var questionsQuery = _dbContext.InterviewQuestionBanks.AsNoTracking()
            .Include(q => q.JobPosition)
            .Include(q => q.CompetencyCriterion)
            .Where(q => !q.IsDeleted && q.IsActive);

        // 5.1. Lọc theo chức danh: Câu hỏi thuộc chức danh này HOẶC câu hỏi chung (JobPositionId == null)
        if (effectivePositionId.HasValue && effectivePositionId.Value != Guid.Empty)
        {
            questionsQuery = questionsQuery.Where(q => q.JobPositionId == null || q.JobPositionId == effectivePositionId.Value);
        }

        // 5.2. Lọc theo tiêu chí cụ thể nếu có yêu cầu
        if (criteriaId.HasValue && criteriaId.Value != Guid.Empty)
        {
            questionsQuery = questionsQuery.Where(q => q.CompetencyCriterionId == criteriaId.Value);
        }
        else if (!string.IsNullOrWhiteSpace(criteriaName))
        {
            var cleanCritName = criteriaName.Trim().ToLower();
            questionsQuery = questionsQuery.Where(q =>
                q.Competency.ToLower().Contains(cleanCritName) ||
                (q.CompetencyCriterion != null && q.CompetencyCriterion.Name.ToLower().Contains(cleanCritName)));
        }

        var matchedQuestions = await questionsQuery
            .OrderBy(q => q.Difficulty == "Cơ bản" ? 1 : q.Difficulty == "Trung bình" ? 2 : 3)
            .ThenByDescending(q => q.CreatedAt)
            .ToListAsync(cancellationToken);

        // 6. Truy vấn lịch sử câu đã hỏi của ứng viên qua tất cả các vòng (AC2)
        var askedRecords = await _dbContext.CandidateAskedQuestions.AsNoTracking()
            .Include(a => a.AskedByUser)
            .Where(a => a.CandidateId == candidateId && !a.IsDeleted)
            .ToListAsync(cancellationToken);

        var askedInCurrentRoundMap = askedRecords
            .Where(a => a.InterviewId == interviewId)
            .GroupBy(a => a.QuestionId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.AskedAt).First());

        var askedInPreviousRoundsMap = askedRecords
            .Where(a => a.InterviewId != interviewId)
            .GroupBy(a => a.QuestionId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.InterviewRound).ThenBy(x => x.AskedAt).ToList());

        // 7. Tạo danh sách DTO câu hỏi kèm thông tin trạng thái & Rubric (AC2, AC3)
        var questionDtos = new List<InterviewQuestionSuggestionItemDto>();

        foreach (var q in matchedQuestions)
        {
            var isAskedCurrent = askedInCurrentRoundMap.TryGetValue(q.Id, out var currentAsked);
            var isAskedPrev = askedInPreviousRoundsMap.TryGetValue(q.Id, out var prevAskedList);

            var prevHistory = new List<PreviousRoundAskedDetailDto>();
            string? prevBadge = null;
            string? prevInterviewer = null;
            DateTimeOffset? prevAskedAt = null;

            if (isAskedPrev && prevAskedList != null && prevAskedList.Count > 0)
            {
                prevHistory = prevAskedList.Select(p => new PreviousRoundAskedDetailDto
                {
                    RoundNumber = p.InterviewRound,
                    InterviewerName = p.AskedByUser?.FullName ?? "Giám khảo",
                    AskedAt = p.AskedAt
                }).ToList();

                var latestPrev = prevAskedList.Last();
                prevInterviewer = latestPrev.AskedByUser?.FullName ?? "Giám khảo";
                prevAskedAt = latestPrev.AskedAt;
                prevBadge = $"Đã hỏi ở Vòng {latestPrev.InterviewRound} ({prevInterviewer})";
            }

            var itemDto = new InterviewQuestionSuggestionItemDto
            {
                QuestionId = q.Id,
                Content = q.Content,
                Competency = !string.IsNullOrWhiteSpace(q.Competency) ? q.Competency : (q.CompetencyCriterion?.Name ?? "Chung"),
                CriteriaId = q.CompetencyCriterionId,
                CriteriaName = q.CompetencyCriterion?.Name ?? q.Competency,
                Difficulty = !string.IsNullOrWhiteSpace(q.Difficulty) ? q.Difficulty : "Cơ bản",
                
                // AC3: Gợi ý câu trả lời chuẩn (Rubric)
                ExpectedAnswer = !string.IsNullOrWhiteSpace(q.ExpectedAnswer) ? q.ExpectedAnswer : q.SuggestedAnswer,
                KeyPoints = q.KeyPoints,
                GoodAnswerRubric = q.GoodAnswerRubric,

                // AC2: Trạng thái hỏi trùng lặp
                IsAskedInPreviousRounds = isAskedPrev,
                PreviousRoundBadge = prevBadge,
                PreviousRoundInterviewerName = prevInterviewer,
                PreviousRoundAskedAt = prevAskedAt,
                PreviousRoundsHistory = prevHistory,

                IsAskedInCurrentRound = isAskedCurrent,
                CurrentRoundAskedByUserId = currentAsked?.AskedByUserId,
                CurrentRoundAskedByUserName = currentAsked?.AskedByUser?.FullName,

                JobPositionId = q.JobPositionId,
                JobPositionTitle = q.JobPosition?.Title
            };

            questionDtos.Add(itemDto);
        }

        // Sắp xếp ưu tiên: Chưa hỏi lên đầu -> Đã hỏi trong vòng này -> Đã hỏi ở vòng trước
        var sortedQuestions = questionDtos
            .OrderBy(q => q.IsAskedInPreviousRounds ? 2 : q.IsAskedInCurrentRound ? 1 : 0)
            .ThenBy(q => q.CriteriaName)
            .ToList();

        var totalQuestions = sortedQuestions.Count;
        var askedInCurrent = sortedQuestions.Count(q => q.IsAskedInCurrentRound);
        var askedInPrevious = sortedQuestions.Count(q => q.IsAskedInPreviousRounds);
        var unasked = sortedQuestions.Count(q => !q.IsAskedInPreviousRounds && !q.IsAskedInCurrentRound);

        return new InterviewQuestionSuggestionResponseDto
        {
            CandidateId = candidateId,
            CandidateName = candidateFullName,
            InterviewId = interviewId,
            CurrentRound = currentRound,
            JobPositionId = effectivePositionId,
            JobPositionTitle = jobTitle,
            TotalQuestions = totalQuestions,
            UnaskedCount = unasked,
            AskedInCurrentRoundCount = askedInCurrent,
            AskedInPreviousRoundsCount = askedInPrevious,
            CriteriaOptions = criteriaOptions,
            Questions = sortedQuestions
        };
    }

    /// <summary>
    /// Đánh dấu hoặc bỏ đánh dấu câu hỏi đã hỏi trong phiên phỏng vấn hiện tại (AC2).
    /// </summary>
    public async Task<ToggleQuestionAskedResultDto> ToggleQuestionAskedAsync(
        ToggleQuestionAskedInputModel input,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        if (input.CandidateId == Guid.Empty || input.InterviewId == Guid.Empty || input.QuestionId == Guid.Empty)
        {
            return new ToggleQuestionAskedResultDto
            {
                Success = false,
                Message = "Thông tin định danh không hợp lệ.",
                QuestionId = input.QuestionId,
                IsAskedInCurrentRound = false
            };
        }

        var interview = await _dbContext.Interviews.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == input.InterviewId && !i.IsDeleted, cancellationToken);

        if (interview == null)
        {
            return new ToggleQuestionAskedResultDto
            {
                Success = false,
                Message = "Không tìm thấy buổi phỏng vấn.",
                QuestionId = input.QuestionId,
                IsAskedInCurrentRound = false
            };
        }

        var currentRound = interview.RoundNumber > 0 ? interview.RoundNumber : 1;

        var existingRecord = await _dbContext.CandidateAskedQuestions
            .FirstOrDefaultAsync(a => a.CandidateId == input.CandidateId &&
                                      a.InterviewId == input.InterviewId &&
                                      a.QuestionId == input.QuestionId, cancellationToken);

        if (input.IsAsked)
        {
            if (existingRecord != null)
            {
                existingRecord.IsDeleted = false;
                existingRecord.AskedByUserId = currentUserId;
                existingRecord.AskedAt = DateTimeOffset.UtcNow;
                existingRecord.InterviewRound = currentRound;
                if (!string.IsNullOrWhiteSpace(input.Notes))
                {
                    existingRecord.Notes = input.Notes;
                }
                existingRecord.UpdatedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                var newRecord = new CandidateAskedQuestion
                {
                    Id = Guid.NewGuid(),
                    CandidateId = input.CandidateId,
                    QuestionId = input.QuestionId,
                    InterviewId = input.InterviewId,
                    InterviewRound = currentRound,
                    AskedByUserId = currentUserId,
                    AskedAt = DateTimeOffset.UtcNow,
                    Notes = input.Notes,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow,
                    IsDeleted = false
                };
                _dbContext.CandidateAskedQuestions.Add(newRecord);
            }
        }
        else
        {
            if (existingRecord != null)
            {
                existingRecord.IsDeleted = true;
                existingRecord.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Tính toán tổng số câu đã hỏi trong vòng hiện tại
        var currentRoundAskedCount = await _dbContext.CandidateAskedQuestions
            .CountAsync(a => a.CandidateId == input.CandidateId &&
                             a.InterviewId == input.InterviewId &&
                             !a.IsDeleted, cancellationToken);

        _logger.LogInformation("Người dùng {UserId} đã {Action} câu hỏi {QuestionId} cho ứng viên {CandidateId} tại Interview {InterviewId}",
            currentUserId, input.IsAsked ? "đánh dấu ĐÃ HỎI" : "HỦY đánh dấu", input.QuestionId, input.CandidateId, input.InterviewId);

        return new ToggleQuestionAskedResultDto
        {
            Success = true,
            Message = input.IsAsked ? "Đã ghi nhận câu hỏi vào phiên hiện tại." : "Đã bỏ đánh dấu câu hỏi.",
            QuestionId = input.QuestionId,
            IsAskedInCurrentRound = input.IsAsked,
            CurrentRoundAskedCount = currentRoundAskedCount
        };
    }

    /// <summary>
    /// Lấy danh sách tiêu chí của vòng hiện tại để đưa vào dropdown lọc (AC1).
    /// </summary>
    private async Task<List<CriteriaFilterOptionDto>> GetCriteriaOptionsAsync(
        Guid interviewId,
        Guid? positionId,
        CancellationToken cancellationToken)
    {
        var criteriaMap = new Dictionary<string, (Guid? Id, string Name, int Count)>(StringComparer.OrdinalIgnoreCase);

        // 1. Thử lấy từ phiếu đánh giá hiện tại (EvaluationForm)
        var evalForm = await _dbContext.EvaluationForms.AsNoTracking()
            .Include(f => f.Items)
            .FirstOrDefaultAsync(f => f.InterviewId == interviewId && !f.IsDeleted, cancellationToken);

        if (evalForm != null && evalForm.Items.Count > 0)
        {
            foreach (var item in evalForm.Items)
            {
                if (!string.IsNullOrWhiteSpace(item.CriteriaName) && !criteriaMap.ContainsKey(item.CriteriaName))
                {
                    criteriaMap[item.CriteriaName] = (item.CriteriaId, item.CriteriaName, 0);
                }
            }
        }

        // 2. Thử lấy từ CompetencyFramework gán cho JobPosition
        if (positionId.HasValue && positionId.Value != Guid.Empty)
        {
            var jobPosition = await _dbContext.JobPositions.AsNoTracking()
                .Include(p => p.CompetencyFramework)
                    .ThenInclude(cf => cf!.Criteria)
                .FirstOrDefaultAsync(p => p.Id == positionId.Value && !p.IsDeleted, cancellationToken);

            if (jobPosition?.CompetencyFramework?.Criteria != null)
            {
                foreach (var crit in jobPosition.CompetencyFramework.Criteria.Where(c => !c.IsDeleted))
                {
                    if (!criteriaMap.ContainsKey(crit.Name))
                    {
                        criteriaMap[crit.Name] = (crit.Id, crit.Name, 0);
                    }
                }
            }
        }

        // 3. Đếm số lượng câu hỏi thực tế có trong ngân hàng cho từng tiêu chí
        var bankQuestions = await _dbContext.InterviewQuestionBanks.AsNoTracking()
            .Include(q => q.CompetencyCriterion)
            .Where(q => !q.IsDeleted && q.IsActive &&
                       (positionId == null || q.JobPositionId == null || q.JobPositionId == positionId))
            .ToListAsync(cancellationToken);

        // Đếm cho các tiêu chí đã đăng ký
        var result = new List<CriteriaFilterOptionDto>();
        foreach (var entry in criteriaMap.Values)
        {
            var count = bankQuestions.Count(q =>
                (entry.Id.HasValue && q.CompetencyCriterionId == entry.Id.Value) ||
                q.Competency.Equals(entry.Name, StringComparison.OrdinalIgnoreCase) ||
                (q.CompetencyCriterion != null && q.CompetencyCriterion.Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase)));

            result.Add(new CriteriaFilterOptionDto
            {
                CriteriaId = entry.Id,
                CriteriaName = entry.Name,
                QuestionCount = count
            });
        }

        // Bổ sung các nhóm Competency khác có trong ngân hàng câu hỏi nếu chưa có
        var existingNames = new HashSet<string>(result.Select(r => r.CriteriaName), StringComparer.OrdinalIgnoreCase);
        var otherCompetencies = bankQuestions
            .Select(q => !string.IsNullOrWhiteSpace(q.Competency) ? q.Competency : q.CompetencyCriterion?.Name)
            .Where(c => !string.IsNullOrWhiteSpace(c) && !existingNames.Contains(c!))
            .Distinct()
            .ToList();

        foreach (var comp in otherCompetencies)
        {
            var count = bankQuestions.Count(q => q.Competency.Equals(comp, StringComparison.OrdinalIgnoreCase) ||
                (q.CompetencyCriterion != null && q.CompetencyCriterion.Name.Equals(comp, StringComparison.OrdinalIgnoreCase)));

            result.Add(new CriteriaFilterOptionDto
            {
                CriteriaId = null,
                CriteriaName = comp!,
                QuestionCount = count
            });
        }

        return result;
    }

    /// <summary>
    /// Khởi tạo dữ liệu câu hỏi mẫu phong phú theo tiêu chí và chức danh nếu hệ thống còn trống (AC1, AC3).
    /// </summary>
    public async Task SeedInitialQuestionBankAsync(CancellationToken cancellationToken = default)
    {
        // Kiểm tra xem đã có câu hỏi mẫu với cấu trúc SP-7 (có expected_answer hoặc good_answer_rubric) chưa
        var hasRichQuestions = await _dbContext.InterviewQuestionBanks
            .AnyAsync(q => !string.IsNullOrEmpty(q.ExpectedAnswer) || !string.IsNullOrEmpty(q.GoodAnswerRubric), cancellationToken);

        if (hasRichQuestions)
        {
            return;
        }

        // Lấy danh sách JobPosition hiện có để liên kết mẫu nếu có
        var positions = await _dbContext.JobPositions.AsNoTracking().Where(p => !p.IsDeleted).ToListAsync(cancellationToken);
        var devPosition = positions.FirstOrDefault(p => p.Title.Contains("Developer", StringComparison.OrdinalIgnoreCase) || p.Title.Contains("Kỹ sư", StringComparison.OrdinalIgnoreCase));
        var baPosition = positions.FirstOrDefault(p => p.Title.Contains("Business Analyst", StringComparison.OrdinalIgnoreCase) || p.Title.Contains("BA", StringComparison.OrdinalIgnoreCase));

        var seedList = new List<InterviewQuestionBank>
        {
            // === Nhóm 1: Năng lực chuyên môn & Kỹ thuật (Hard Skills) ===
            new()
            {
                Id = Guid.NewGuid(),
                Content = "Bạn hãy mô tả kiến trúc phân tầng (Clean Architecture / N-Tier) trong một dự án web gần đây. Việc phân tách trách nhiệm giữa Controller, Service và Repository mang lại lợi ích gì?",
                Competency = "Năng lực chuyên môn & Kỹ thuật",
                Difficulty = "Trung bình",
                JobPositionId = devPosition?.Id,
                ExpectedAnswer = "Ứng viên giải thích rõ các tầng Presentation, Business Logic Layer (Services) và Data Access Layer (Repositories). Phân tích được tính độc lập kiểm thử (testability), khả năng bảo trì và tránh viết logic nghiệp vụ vào Controller.",
                KeyPoints = "• Phân tách trách nhiệm (Separation of Concerns)\n• Tầng Controller chỉ nhận request và điều hướng ViewModel\n• Tầng Service chứa toàn bộ Business Validation\n• Độc lập với cơ sở dữ liệu và dễ dàng viết Unit Test với Mocking",
                GoodAnswerRubric = "🟢 TỐT: Nêu rõ luồng Dependency Injection, Data Transfer Objects (DTO) và minh họa bằng ví dụ thực tế đã triển khai.\n🟡 ĐẠT: Nêu được định nghĩa 3 tầng cơ bản, hiểu luồng dữ liệu thông thường.\n🔴 CHƯA ĐẠT: Không phân biệt được Controller và Service, đưa logic truy vấn DB thẳng vào View/Controller.",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            },
            new()
            {
                Id = Guid.NewGuid(),
                Content = "Làm thế nào để bạn xử lý bài toán tối ưu truy vấn Database khi bảng dữ liệu đạt hàng triệu bản ghi? Bạn sử dụng Indexing và tối ưu ORM (EF Core) như thế nào?",
                Competency = "Năng lực chuyên môn & Kỹ thuật",
                Difficulty = "Nâng cao",
                JobPositionId = devPosition?.Id,
                ExpectedAnswer = "Ứng viên nêu các chiến lược: Sử dụng AsNoTracking cho câu truy vấn chỉ đọc, phân trang qua Skip/Take hoặc Keysets pagination, tạo B-Tree/Composite Index cho các cột thường xuyên WHERE/ORDER BY, tránh N+1 Query qua Include/SplitQuery.",
                KeyPoints = "• AsNoTracking() cho Read-only queries\n• B-Tree Index, Composite Index, Covering Index\n• Tránh N+1 query (Eager Loading vs Lazy Loading)\n• Execution Plan (EXPLAIN ANALYZE) trong PostgreSQL",
                GoodAnswerRubric = "🟢 TỐT: Nêu sâu về Connection Pooling, EXPLAIN ANALYZE trên PostgreSQL, phân tích nhược điểm của Index thừa đối với write performance.\n🟡 ĐẠT: Biết dùng AsNoTracking, Include, tạo Index cho khóa ngoại.\n🔴 CHƯA ĐẠT: Chưa từng kiểm tra log SQL sinh ra từ ORM, không hiểu bản chất Index.",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            },
            new()
            {
                Id = Guid.NewGuid(),
                Content = "Quy trình bạn thực hiện để thu thập và làm rõ yêu cầu nghiệp vụ (Requirements Gathering) khi làm việc với các bên liên quan (Stakeholders) có ý kiến trái chiều nhau là gì?",
                Competency = "Năng lực chuyên môn & Kỹ thuật",
                Difficulty = "Trung bình",
                JobPositionId = baPosition?.Id,
                ExpectedAnswer = "Ứng viên trình bày kỹ thuật phỏng vấn chuyên sâu (1-on-1 Interviews), tổ chức workshop thống nhất mục tiêu cốt lõi (User Journey / Event Storming), đối chiếu với mục tiêu kinh doanh (Business Goals) và tài liệu hóa qua User Stories kèm Acceptance Criteria rõ ràng.",
                KeyPoints = "• Kỹ thuật 5 Whys tìm gốc rễ nhu cầu (Root Need vs Want)\n• Ma trận phân loại ưu tiên (MoSCoW Matrix)\n• Thống nhất định nghĩa hoàn thành (DoD) và Tiêu chí nghiệm thu (AC)",
                GoodAnswerRubric = "🟢 TỐT: Dùng dữ liệu và mục tiêu kinh doanh (ROI, Time-to-Market) để thuyết phục; có phương án giải quyết xung đột win-win.\n🟡 ĐẠT: Biết lắng nghe và ghi nhận ý kiến, biết escalate lên cấp quản lý khi bế tắc.\n🔴 CHƯA ĐẠT: Thụ động làm theo yêu cầu rời rạc mà không phân tích tính khả thi.",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            },

            // === Nhóm 2: Tư duy logic & Giải quyết vấn đề ===
            new()
            {
                Id = Guid.NewGuid(),
                Content = "Hãy kể về một lỗi nghiêm trọng (Critical Incident / Outage) từng xảy ra trong hệ thống của bạn. Bạn đã xác định nguyên nhân gốc rễ (Root Cause) và khắc phục nó như thế nào?",
                Competency = "Tư duy logic & Giải quyết vấn đề",
                Difficulty = "Trung bình",
                JobPositionId = null, // Dùng chung cho mọi vị trí
                ExpectedAnswer = "Ứng viên mô tả tình huống cụ thể (STAR method): Tình hình khẩn cấp, hành động cô lập sự cố (Mitigation), cách đọc log/metrics tìm Root cause, giải pháp xử lý triệt để (Post-mortem) và bổ sung cảnh báo ngăn ngừa tái diễn.",
                KeyPoints = "• Ưu tiên giảm thiểu tác động người dùng trước (Hotfix / Rollback / Circuit Breaker)\n• Tìm nguyên nhân gốc rễ dựa trên logs, telemetry, metrics chứ không đoán mò\n• Họp rút kinh nghiệm (Blameless Post-mortem) và xây dựng cơ chế phòng vệ",
                GoodAnswerRubric = "🟢 TỐT: Thái độ bình tĩnh, có phương pháp luận rõ ràng, biết tạo test case tái hiện lỗi và lập kế hoạch phòng ngừa lâu dài.\n🟡 ĐẠT: Kể được lỗi đã fix nhưng chưa có cơ chế phòng ngừa tự động sau sự cố.\n🔴 CHƯA ĐẠT: Đổ lỗi cho đồng nghiệp hoặc môi trường, không giải thích được vì sao lỗi lại phát sinh.",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            },
            new()
            {
                Id = Guid.NewGuid(),
                Content = "Khi nhận được một task có yêu cầu mơ hồ hoặc phạm vi quá rộng trong thời hạn gấp, các bước phân rã bài toán và ra quyết định của bạn là gì?",
                Competency = "Tư duy logic & Giải quyết vấn đề",
                Difficulty = "Cơ bản",
                JobPositionId = null,
                ExpectedAnswer = "Ứng viên chia nhỏ bài toán thành các MVP nhỏ có thể bàn giao tuần tự (Deliverable Milestones), trao đổi với Product Owner / Manager để cắt bỏ các tính năng không thiết yếu, xác định các rủi ro kỹ thuật chính trước khi code.",
                KeyPoints = "• Phân tích ranh giới MVP (Minimum Viable Product)\n• Xác định rõ ràng Definition of Done\n• Giao tiếp chủ động về rủi ro và các mốc thời gian phụ thuộc",
                GoodAnswerRubric = "🟢 TỐT: Chủ động đưa ra 2-3 phương án kèm ưu/nhược điểm để lãnh đạo chọn; cam kết chất lượng của phần cốt lõi.\n🟡 ĐẠT: Biết hỏi lại người giao việc để xin hướng dẫn cụ thể hơn.\n🔴 CHƯA ĐẠT: Im lặng làm việc theo suy đoán cá nhân dẫn đến bàn giao sai kết quả.",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            },

            // === Nhóm 3: Kỹ năng giao tiếp & Làm việc nhóm ===
            new()
            {
                Id = Guid.NewGuid(),
                Content = "Hãy chia sẻ về một lần bạn có bất đồng quan điểm kỹ thuật gay gắt với đồng nghiệp hoặc Team Lead. Bạn đã xử lý cuộc tranh luận đó như thế nào để đạt được tiếng nói chung?",
                Competency = "Kỹ năng giao tiếp & Làm việc nhóm",
                Difficulty = "Trung bình",
                JobPositionId = null,
                ExpectedAnswer = "Ứng viên giải quyết dựa trên bằng chứng dữ liệu (Proof of Concept, benchmark, best practice tài liệu chính thống) thay vì cái tôi cá nhân. Tôn trọng quyết định chung sau khi đã thảo luận và cam kết thực hiện (Disagree and Commit).",
                KeyPoints = "• Tách bạch con người và vấn đề kỹ thuật\n• Xây dựng Proof of Concept (PoC) nhỏ để kiểm chứng thực tế\n• Nguyên tắc Disagree and Commit vì mục tiêu chung của team",
                GoodAnswerRubric = "🟢 TỐT: Thể hiện trí tuệ cảm xúc (EQ) cao, biết lắng nghe phản biện, đặt lợi ích dự án lên trên sự tự tôn cá nhân.\n🟡 ĐẠT: Giữ thái độ hòa nhã, tuân theo biểu quyết của đa số.\n🔴 CHƯA ĐẠT: Cố chấp giữ quan điểm, tỏ thái độ tiêu cực hoặc né tránh hợp tác sau tranh luận.",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            },
            new()
            {
                Id = Guid.NewGuid(),
                Content = "Bạn làm thế nào để review code (Code Review) cho đồng nghiệp một cách xây dựng, giúp nâng cao chất lượng mã nguồn mà không gây cảm giác khó chịu hoặc bắt bẻ?",
                Competency = "Kỹ năng giao tiếp & Làm việc nhóm",
                Difficulty = "Cơ bản",
                JobPositionId = null,
                ExpectedAnswer = "Ứng viên nêu quy tắc review: Khen ngợi điểm làm tốt, đặt câu hỏi gợi mở thay vì ra lệnh ('Bạn nghĩ sao nếu...', 'Có thể tối ưu đoạn này bằng...'), trích dẫn conventions hoặc tài liệu, phân loại comment (Nitpick, Suggestion, Blocker).",
                KeyPoints = "• Phân biệt giữa sở thích cá nhân và lỗi quy chuẩn hệ thống\n• Góp ý mang tính giải pháp kèm code snippet gợi ý\n• Phản hồi kịp thời trong vòng 24 giờ để không nghẽn tiến độ",
                GoodAnswerRubric = "🟢 TỐT: Tư duy coi Code Review là cơ hội chia sẻ tri thức (Knowledge Sharing); biết hẹn nói chuyện trực tiếp khi comment quá dài.\n🟡 ĐẠT: Biết góp ý lịch sự theo checklist của dự án.\n🔴 CHƯA ĐẠT: Xem nhẹ review code hoặc comment cụt ngủn, mang tính chỉ trích cá nhân.",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            },

            // === Nhóm 4: Mức độ phù hợp văn hóa doanh nghiệp ===
            new()
            {
                Id = Guid.NewGuid(),
                Content = "Điều gì thúc đẩy bạn gắn bó và cống hiến lâu dài tại một tổ chức? Môi trường làm việc lý tưởng đối với bạn có những đặc điểm nào?",
                Competency = "Mức độ phù hợp văn hóa doanh nghiệp",
                Difficulty = "Cơ bản",
                JobPositionId = null,
                ExpectedAnswer = "Ứng viên thể hiện sự gắn kết giữa mục tiêu cá nhân và sự phát triển của công ty: Môi trường minh bạch, trao quyền (empowerment), đồng nghiệp tôn trọng và có cơ hội nâng cao chuyên môn.",
                KeyPoints = "• Tính minh bạch và tin cậy trong giao tiếp\n• Định hướng học tập liên tục (Growth Mindset)\n• Trách nhiệm giải trình (Accountability) và ghi nhận công bằng",
                GoodAnswerRubric = "🟢 TỐT: Tìm hiểu kỹ về giá trị cốt lõi của công ty, nêu dẫn chứng thực tế phù hợp với văn hóa tổ chức.\n🟡 ĐẠT: Trả lời chân thành về mong muốn phát triển cá nhân.\n🔴 CHƯA ĐẠT: Chỉ quan tâm duy nhất đến chế độ đãi ngộ ngắn hạn, không có định hướng nghề nghiệp rõ ràng.",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            },
            new()
            {
                Id = Guid.NewGuid(),
                Content = "Nếu một ngày công ty có sự thay đổi đột ngột về chiến lược công nghệ hoặc ưu tiên dự án, bạn sẽ thích nghi với sự thay đổi đó ra sao?",
                Competency = "Mức độ phù hợp văn hóa doanh nghiệp",
                Difficulty = "Trung bình",
                JobPositionId = null,
                ExpectedAnswer = "Ứng viên thể hiện tinh thần linh hoạt (Agility), sẵn sàng học hỏi công nghệ mới (Unlearn and Relearn), tìm hiểu lý do kinh doanh đằng sau sự thay đổi để hỗ trợ team chuyển giao mượt mà.",
                KeyPoints = "• Tinh thần đón nhận thay đổi (Embracing Change)\n• Khả năng tự học nhanh công nghệ mới\n• Duy trì năng lượng tích cực và hỗ trợ các thành viên khác",
                GoodAnswerRubric = "🟢 TỐT: Nêu được ví dụ trong quá khứ đã thích nghi thành công và tạo ra giá trị mới cho dự án sau khi pivot.\n🟡 ĐẠT: Sẵn sàng tuân thủ chỉ đạo của ban lãnh đạo.\n🔴 CHƯA ĐẠT: Kháng cự thay đổi, chán nản khi kế hoạch ban đầu không được tiếp tục.",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }
        };

        _dbContext.InterviewQuestionBanks.AddRange(seedList);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Đã khởi tạo thành công {Count} câu hỏi gợi ý chuẩn Rubric cho ngân hàng câu hỏi ATS (SP-7)", seedList.Count);
    }
}
