using System.Text.Json;
using Ats.Web.Data;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Models.ViewModels.Evaluations;
using Ats.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ats.Web.Services;

public class EvaluationService(
    ApplicationDbContext dbContext,
    ILogger<EvaluationService> logger) : IEvaluationService
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly ILogger<EvaluationService> _logger = logger;

    public async Task<EvaluationFormGenerateViewModel> GenerateEvaluationFormAsync(
        Guid applicationId,
        Guid interviewId,
        Guid evaluatorId,
        CancellationToken cancellationToken = default)
    {
        if (applicationId == Guid.Empty && interviewId != Guid.Empty)
        {
            var intv = await _dbContext.Interviews.AsNoTracking().FirstOrDefaultAsync(i => i.Id == interviewId && !i.IsDeleted, cancellationToken);
            if (intv != null) applicationId = intv.ApplicationId;
        }

        if (interviewId == Guid.Empty && applicationId != Guid.Empty)
        {
            var latestIntv = await _dbContext.Interviews.AsNoTracking()
                .Where(i => i.ApplicationId == applicationId && !i.IsDeleted)
                .OrderByDescending(i => i.RoundNumber)
                .FirstOrDefaultAsync(cancellationToken);
            if (latestIntv != null) interviewId = latestIntv.Id;
        }

        var application = await _dbContext.Applications
            .Include(a => a.Candidate)
            .Include(a => a.JobPosting)
                .ThenInclude(p => p.Requisition)
                    .ThenInclude(r => r.Department)
            .Include(a => a.JobPosting)
                .ThenInclude(p => p.Requisition)
                    .ThenInclude(r => r.JobPosition)
                        .ThenInclude(jp => jp!.CompetencyFramework)
                            .ThenInclude(cf => cf!.Criteria)
            .FirstOrDefaultAsync(a => a.Id == applicationId && !a.IsDeleted, cancellationToken)
            ?? throw new KeyNotFoundException($"Không tìm thấy hồ sơ ứng tuyển với Id: {applicationId}");

        var interview = await _dbContext.Interviews
            .Include(i => i.Panelists)
            .FirstOrDefaultAsync(i => i.Id == interviewId && !i.IsDeleted, cancellationToken)
            ?? throw new KeyNotFoundException($"Không tìm thấy buổi phỏng vấn với Id: {interviewId}");

        // Kiểm tra xem đã có phiếu đánh giá của Evaluator này cho Interview này chưa
        var existingForm = await _dbContext.EvaluationForms
            .Include(f => f.Items)
            .FirstOrDefaultAsync(f => f.InterviewId == interviewId && f.EvaluatorId == evaluatorId && !f.IsDeleted, cancellationToken);

        var jobPosting = application.JobPosting;
        var jobPosition = jobPosting?.Requisition?.JobPosition;
        var departmentName = jobPosting?.Requisition?.Department?.Name ?? string.Empty;
        var jobTitle = jobPosition?.Title ?? jobPosting?.Title ?? "Chức danh tuyển dụng";
        var candidateName = $"{application.Candidate.FirstName} {application.Candidate.LastName}".Trim();

        // Trường hợp 1: Đã có phiếu (DRAFT hoặc SUBMITTED) -> Tải lại từ snapshot bất biến
        if (existingForm != null)
        {
            var isSubmitted = existingForm.Status == EvaluationFormStatus.SUBMITTED;

            return new EvaluationFormGenerateViewModel
            {
                EvaluationFormId = existingForm.Id,
                ApplicationId = application.Id,
                InterviewId = interview.Id,
                EvaluatorId = evaluatorId,
                CandidateId = application.CandidateId,
                JobPositionId = jobPosition?.Id,
                CandidateName = candidateName,
                CandidateEmail = application.Candidate.Email,
                JobTitle = jobTitle,
                DepartmentName = departmentName,
                InterviewRound = interview.RoundNumber,
                Status = existingForm.Status.ToString(),
                TotalScore = existingForm.TotalScore,
                OverallRecommendation = existingForm.OverallRecommendation,
                GeneralNotes = existingForm.GeneralNotes,
                IsReadOnly = isSubmitted,
                CanEdit = !isSubmitted,
                Items = existingForm.Items
                    .OrderBy(i => i.CreatedAt)
                    .Select(i => new EvaluationItemGenerateViewModel
                    {
                        CriteriaId = i.CriteriaId,
                        CriteriaName = i.CriteriaName,
                        CriteriaDescription = i.CriteriaDescription,
                        Weight = i.Weight,
                        Score = i.Score,
                        Comment = i.Comment
                    }).ToList()
            };
        }

        // Trường hợp 2: Chưa có phiếu -> Tự động tìm khung năng lực theo chức danh và nạp tiêu chí
        CompetencyFramework? framework = null;

        // 2.1. Thử lấy từ JobPosition đã gán CompetencyFramework
        if (jobPosition?.CompetencyFramework != null && !jobPosition.CompetencyFramework.IsDeleted && jobPosition.CompetencyFramework.IsActive)
        {
            framework = jobPosition.CompetencyFramework;
        }

        // 2.2. Nếu chưa có, tìm framework được gán cho PositionId
        if (framework == null && jobPosition != null)
        {
            framework = await _dbContext.CompetencyFrameworks
                .Include(cf => cf.Criteria)
                .FirstOrDefaultAsync(cf => !cf.IsDeleted && cf.IsActive && cf.AssignedPositions.Any(p => p.Id == jobPosition.Id), cancellationToken);
        }

        // 2.3. Nếu chưa có, tìm framework theo tên khớp với JobTitle
        if (framework == null)
        {
            var searchTitle = (jobPosition?.Title ?? jobPosting?.Title ?? string.Empty).Trim().ToLower();
            if (!string.IsNullOrWhiteSpace(searchTitle))
            {
                framework = await _dbContext.CompetencyFrameworks
                    .Include(cf => cf.Criteria)
                    .FirstOrDefaultAsync(cf => !cf.IsDeleted && cf.IsActive &&
                        (cf.Name.ToLower().Contains(searchTitle) || searchTitle.Contains(cf.Name.ToLower())), cancellationToken);
            }
        }

        // 2.4. Nếu vẫn chưa có, lấy khung năng lực hoạt động đầu tiên có tiêu chí
        if (framework == null)
        {
            framework = await _dbContext.CompetencyFrameworks
                .Include(cf => cf.Criteria)
                .FirstOrDefaultAsync(cf => !cf.IsDeleted && cf.IsActive && cf.Criteria.Any(c => !c.IsDeleted), cancellationToken);
        }

        var generatedItems = new List<EvaluationItemGenerateViewModel>();

        if (framework != null && framework.Criteria.Any(c => !c.IsDeleted))
        {
            var activeCriteria = framework.Criteria.Where(c => !c.IsDeleted).OrderBy(c => c.CreatedAt).ToList();
            foreach (var crit in activeCriteria)
            {
                generatedItems.Add(new EvaluationItemGenerateViewModel
                {
                    CriteriaId = crit.Id,
                    CriteriaName = crit.Name,
                    CriteriaDescription = crit.Description,
                    Weight = crit.Weight > 0 ? crit.Weight : 1m,
                    Score = 0,
                    Comment = null,
                    Rubric1 = crit.Rubric1,
                    Rubric2 = crit.Rubric2,
                    Rubric3 = crit.Rubric3,
                    Rubric4 = crit.Rubric4,
                    Rubric5 = crit.Rubric5
                });
            }
        }
        else
        {
            // Dự phòng tiêu chuẩn nếu hệ thống chưa có khung năng lực nào được cấu hình
            generatedItems =
            [
                new() { CriteriaName = "Năng lực chuyên môn & Kỹ thuật", CriteriaDescription = "Kiến thức chuyên môn và năng lực giải quyết công việc thực tế", Weight = 40, Score = 0 },
                new() { CriteriaName = "Tư duy logic & Giải quyết vấn đề", CriteriaDescription = "Khả năng phân tích vấn đề và đưa ra giải pháp hiệu quả", Weight = 30, Score = 0 },
                new() { CriteriaName = "Kỹ năng giao tiếp & Làm việc nhóm", CriteriaDescription = "Khả năng trao đổi, phối hợp và phản biện mang tính xây dựng", Weight = 15, Score = 0 },
                new() { CriteriaName = "Mức độ phù hợp văn hóa doanh nghiệp", CriteriaDescription = "Giá trị cốt lõi, thái độ và định hướng gắn bó lâu dài", Weight = 15, Score = 0 }
            ];
        }

        return new EvaluationFormGenerateViewModel
        {
            EvaluationFormId = null,
            ApplicationId = application.Id,
            InterviewId = interview.Id,
            EvaluatorId = evaluatorId,
            CandidateId = application.CandidateId,
            JobPositionId = jobPosition?.Id,
            CandidateName = candidateName,
            CandidateEmail = application.Candidate.Email,
            JobTitle = jobTitle,
            DepartmentName = departmentName,
            InterviewRound = interview.RoundNumber,
            FrameworkId = framework?.Id,
            FrameworkName = framework?.Name ?? "Khung năng lực tiêu chuẩn chức danh",
            Status = "NEW",
            TotalScore = 0m,
            IsReadOnly = false,
            CanEdit = true,
            Items = generatedItems
        };
    }

    public async Task<Guid> SubmitEvaluationAsync(
        EvaluationFormSubmitInputModel input,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.Items == null || input.Items.Count == 0)
        {
            throw new ArgumentException("Phiếu đánh giá phải có ít nhất một tiêu chí đánh giá.");
        }

        // 1. Kiểm tra sự tồn tại của buổi phỏng vấn & phân quyền Interviewer
        var interview = await _dbContext.Interviews
            .Include(i => i.Panelists)
            .FirstOrDefaultAsync(i => i.Id == input.InterviewId && !i.IsDeleted, cancellationToken)
            ?? throw new KeyNotFoundException($"Không tìm thấy buổi phỏng vấn với Id: {input.InterviewId}");

        var isPanelist = interview.Panelists.Any(p => p.InterviewerId == currentUserId);

        var currentUser = await _dbContext.Users
            .Include(u => u.RoleEntity)
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == currentUserId, cancellationToken);

        var isAdminOrHRManager = currentUser != null && (
            currentUser.RoleEntity?.Code is RoleCode.ADMIN or RoleCode.HR_MANAGER ||
            currentUser.UserRoles.Any(ur => ur.Role.Code is RoleCode.ADMIN or RoleCode.HR_MANAGER)
        );

        if (!isPanelist && !isAdminOrHRManager)
        {
            throw new UnauthorizedAccessException("Người dùng không có quyền đánh giá buổi phỏng vấn này.");
        }

        // 2. Kiểm tra nếu phiếu đã nộp trước đó thì KHÔNG cho chỉnh sửa
        var existingForm = await _dbContext.EvaluationForms
            .Include(f => f.Items)
            .FirstOrDefaultAsync(f => f.InterviewId == input.InterviewId && f.EvaluatorId == currentUserId && !f.IsDeleted, cancellationToken);

        if (existingForm != null && existingForm.Status == EvaluationFormStatus.SUBMITTED)
        {
            throw new InvalidOperationException("Phiếu đánh giá đã được nộp và không thể chỉnh sửa.");
        }

        // 3. Validation điểm và trọng số
        decimal totalWeight = 0m;
        decimal weightedSum = 0m;

        foreach (var item in input.Items)
        {
            if (string.IsNullOrWhiteSpace(item.CriteriaName))
            {
                throw new ArgumentException("Tên tiêu chí không được để trống.");
            }

            if (item.Weight <= 0)
            {
                throw new ArgumentException($"Trọng số của tiêu chí '{item.CriteriaName}' phải lớn hơn 0.");
            }

            if (!input.IsDraft)
            {
                // Khi nộp chính thức, điểm bắt buộc phải từ 1 đến 5
                if (item.Score < 1 || item.Score > 5)
                {
                    throw new ArgumentOutOfRangeException(nameof(item.Score), $"Điểm đánh giá cho tiêu chí '{item.CriteriaName}' phải nằm trong khoảng từ 1 đến 5.");
                }
            }
            else
            {
                // Khi lưu nháp, nếu đã nhập điểm thì điểm cũng phải từ 1 đến 5 (chấp nhận 0 nếu chưa chấm)
                if (item.Score != 0 && (item.Score < 1 || item.Score > 5))
                {
                    throw new ArgumentOutOfRangeException(nameof(item.Score), $"Điểm đánh giá cho tiêu chí '{item.CriteriaName}' phải nằm trong khoảng từ 1 đến 5.");
                }
            }

            totalWeight += item.Weight;
            weightedSum += (decimal)item.Score * item.Weight;
        }

        if (totalWeight <= 0)
        {
            throw new ArgumentException("Tổng trọng số của các tiêu chí phải lớn hơn 0.");
        }

        // Công thức bình quân gia quyền: TotalScore = Sum(Score * Weight) / Sum(Weight)
        var totalScore = totalWeight > 0 ? Math.Round(weightedSum / totalWeight, 2, MidpointRounding.AwayFromZero) : 0m;

        // 4. Mở Database Transaction tường minh bảo đảm tính toàn vẹn và bất biến
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var form = existingForm;
            if (form == null)
            {
                form = new EvaluationForm
                {
                    Id = Guid.NewGuid(),
                    ApplicationId = input.ApplicationId,
                    InterviewId = input.InterviewId,
                    EvaluatorId = currentUserId,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                await _dbContext.EvaluationForms.AddAsync(form, cancellationToken);
            }

            form.Status = input.IsDraft ? EvaluationFormStatus.DRAFT : EvaluationFormStatus.SUBMITTED;
            form.TotalScore = totalScore;
            form.OverallRecommendation = input.OverallRecommendation;
            form.GeneralNotes = input.GeneralNotes;
            form.UpdatedAt = DateTimeOffset.UtcNow;
            form.SnapshotVersion = "1.0";

            if (!input.IsDraft)
            {
                form.SubmittedAt = DateTimeOffset.UtcNow;
            }

            // Xóa các items cũ nếu đang cập nhật bản nháp
            var oldItems = form.Items.ToList();
            if (oldItems.Count > 0)
            {
                foreach (var oldItem in oldItems)
                {
                    _dbContext.EvaluationItems.Remove(oldItem);
                }
            }

            // Snapshot bất biến toàn bộ tiêu chí, mô tả, trọng số và điểm số
            foreach (var itemInput in input.Items)
            {
                var evalItem = new EvaluationItem
                {
                    Id = Guid.NewGuid(),
                    EvaluationFormId = form.Id,
                    CriteriaId = itemInput.CriteriaId,
                    CriteriaName = itemInput.CriteriaName.Trim(),
                    CriteriaDescription = itemInput.CriteriaDescription,
                    Weight = itemInput.Weight,
                    Score = itemInput.Score,
                    Comment = itemInput.Comment,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                await _dbContext.EvaluationItems.AddAsync(evalItem, cancellationToken);
            }

            // 5. Ghi AuditLog chi tiết hành động
            var auditAction = input.IsDraft ? "SAVE_DRAFT_EVALUATION" : "SUBMIT_EVALUATION";
            var auditLog = new AuditLog
            {
                Id = Guid.NewGuid(),
                UserId = currentUserId,
                Action = auditAction,
                EntityName = nameof(EvaluationForm),
                EntityId = form.Id.ToString(),
                NewValues = JsonSerializer.Serialize(new
                {
                    form.Id,
                    form.ApplicationId,
                    form.InterviewId,
                    form.EvaluatorId,
                    Status = form.Status.ToString(),
                    form.TotalScore,
                    form.SubmittedAt,
                    ItemsCount = form.Items.Count
                }),
                CreatedAt = DateTimeOffset.UtcNow
            };
            await _dbContext.AuditLogs.AddAsync(auditLog, cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation("Người dùng {UserId} đã thực hiện {Action} cho phiếu {FormId} với điểm tổng {TotalScore}",
                currentUserId, auditAction, form.Id, totalScore);

            return form.Id;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Lỗi xảy ra khi lưu phiếu đánh giá cho Interview {InterviewId}", input.InterviewId);
            throw;
        }
    }

    public async Task<EvaluationDetailViewModel?> GetEvaluationDetailAsync(
        Guid evaluationFormId,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var form = await _dbContext.EvaluationForms
            .Include(f => f.Items)
            .Include(f => f.Application)
                .ThenInclude(a => a.Candidate)
            .Include(f => f.Application)
                .ThenInclude(a => a.JobPosting)
            .Include(f => f.Interview)
            .Include(f => f.Evaluator)
            .FirstOrDefaultAsync(f => f.Id == evaluationFormId && !f.IsDeleted, cancellationToken);

        if (form == null) return null;

        var currentUser = await _dbContext.Users
            .Include(u => u.RoleEntity)
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == currentUserId, cancellationToken);

        var isAdminOrHR = currentUser != null && (
            currentUser.RoleEntity?.Code is RoleCode.ADMIN or RoleCode.HR_MANAGER ||
            currentUser.UserRoles.Any(ur => ur.Role.Code is RoleCode.ADMIN or RoleCode.HR_MANAGER)
        );

        // Interviewer chỉ được xem phiếu của mình; Admin/HRManager được xem tất cả
        if (!isAdminOrHR && form.EvaluatorId != currentUserId)
        {
            throw new UnauthorizedAccessException("Bạn không có quyền xem chi tiết phiếu đánh giá này.");
        }

        var candidate = form.Application.Candidate;
        var candidateName = $"{candidate.FirstName} {candidate.LastName}".Trim();

        return new EvaluationDetailViewModel
        {
            EvaluationFormId = form.Id,
            ApplicationId = form.ApplicationId,
            InterviewId = form.InterviewId,
            EvaluatorId = form.EvaluatorId,
            EvaluatorName = form.Evaluator.FullName,
            EvaluatorEmail = form.Evaluator.Email,
            CandidateName = candidateName,
            CandidateEmail = form.Application.Candidate.Email,
            JobTitle = form.Application.JobPosting.Title,
            InterviewRound = form.Interview.RoundNumber,
            Status = form.Status.ToString(),
            TotalScore = form.TotalScore,
            SubmittedAt = form.SubmittedAt,
            SnapshotVersion = form.SnapshotVersion,
            OverallRecommendation = form.OverallRecommendation,
            GeneralNotes = form.GeneralNotes,
            Items = form.Items
                .OrderBy(i => i.CreatedAt)
                .Select(i => new EvaluationItemDetailViewModel
                {
                    Id = i.Id,
                    CriteriaId = i.CriteriaId,
                    CriteriaName = i.CriteriaName,
                    CriteriaDescription = i.CriteriaDescription,
                    Weight = i.Weight,
                    Score = i.Score,
                    Comment = i.Comment
                }).ToList()
        };
    }

    public async Task<EvaluationSummaryComparisonViewModel> GetComparisonByJobAsync(
        Guid jobPostingId,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var currentUser = await _dbContext.Users
            .Include(u => u.RoleEntity)
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == currentUserId, cancellationToken);

        var isAdminOrHR = currentUser != null && (
            currentUser.RoleEntity?.Code is RoleCode.ADMIN or RoleCode.HR_MANAGER or RoleCode.HIRING_MANAGER or RoleCode.RECRUITER ||
            currentUser.UserRoles.Any(ur => ur.Role.Code is RoleCode.ADMIN or RoleCode.HR_MANAGER or RoleCode.HIRING_MANAGER or RoleCode.RECRUITER)
        );

        if (!isAdminOrHR)
        {
            throw new UnauthorizedAccessException("Chỉ Trưởng phòng Nhân sự, Quản lý tuyển dụng, Chuyên viên tuyển dụng và Quản trị viên mới có quyền xem bảng so sánh ứng viên.");
        }

        var jobPosting = await _dbContext.JobPostings
            .Include(p => p.Requisition)
                .ThenInclude(r => r.Department)
            .Include(p => p.Requisition)
                .ThenInclude(r => r.JobPosition)
                    .ThenInclude(jp => jp!.CompetencyFramework)
            .FirstOrDefaultAsync(p => p.Id == jobPostingId && !p.IsDeleted, cancellationToken)
            ?? throw new KeyNotFoundException($"Không tìm thấy tin tuyển dụng với Id: {jobPostingId}");

        var submittedForms = await _dbContext.EvaluationForms
            .Include(f => f.Items)
            .Include(f => f.Application)
                .ThenInclude(a => a.Candidate)
            .Include(f => f.Interview)
            .Include(f => f.Evaluator)
            .Where(f => f.Application.JobPostingId == jobPostingId &&
                        f.Status == EvaluationFormStatus.SUBMITTED &&
                        !f.IsDeleted)
            .ToListAsync(cancellationToken);

        // Lấy danh sách tiêu chí chuẩn hóa từ các snapshot của phiếu đã nộp
        var allCriteria = submittedForms
            .SelectMany(f => f.Items)
            .GroupBy(i => i.CriteriaName)
            .Select(g => new CriteriaComparisonHeaderDto
            {
                CriteriaName = g.Key,
                Weight = g.FirstOrDefault()?.Weight ?? 1m
            })
            .OrderByDescending(c => c.Weight)
            .ToList();

        var candidateRows = submittedForms
            .Select(f =>
            {
                var cand = f.Application.Candidate;
                var candName = $"{cand.FirstName} {cand.LastName}".Trim();
                return new CandidateEvaluationSummaryRow
                {
                    ApplicationId = f.ApplicationId,
                    EvaluationFormId = f.Id,
                    CandidateName = candName,
                    CandidateEmail = cand.Email,
                    InterviewRound = f.Interview.RoundNumber,
                    EvaluatorName = f.Evaluator.FullName,
                    TotalScore = f.TotalScore,
                    Recommendation = f.OverallRecommendation,
                    SubmittedAt = f.SubmittedAt,
                    CriteriaScores = f.Items.Select(i => new CandidateCriteriaScoreDto
                    {
                        CriteriaName = i.CriteriaName,
                        Score = i.Score,
                        Weight = i.Weight,
                        Comment = i.Comment
                    }).ToList()
                };
            })
            .OrderByDescending(c => c.TotalScore)
            .ToList();

        var scores = candidateRows.Select(c => c.TotalScore).ToList();

        return new EvaluationSummaryComparisonViewModel
        {
            JobPostingId = jobPosting.Id,
            JobTitle = jobPosting.Title,
            DepartmentName = jobPosting.Requisition?.Department?.Name ?? string.Empty,
            FrameworkName = jobPosting.Requisition?.JobPosition?.CompetencyFramework?.Name ?? "Khung năng lực tiêu chuẩn",
            CriteriaHeaders = allCriteria,
            Candidates = candidateRows,
            HighestScore = scores.Count > 0 ? scores.Max() : 0m,
            LowestScore = scores.Count > 0 ? scores.Min() : 0m,
            AverageScore = scores.Count > 0 ? Math.Round(scores.Average(), 2, MidpointRounding.AwayFromZero) : 0m
        };
    }
}
