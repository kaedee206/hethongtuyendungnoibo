using Ats.Web.Data;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Models.ViewModels.TalentPool;
using Ats.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Ats.Web.Services;

/// <summary>
/// Dịch vụ quản lý Kho Ứng viên Tiềm năng (Scrum 45 - Talent Pool Management).
/// Áp dụng C# 12 Primary Constructors, tuân thủ Clean Architecture và tối ưu thuật toán Matching.
/// </summary>
public class TalentPoolService(
    ApplicationDbContext dbContext,
    ISecurityAuditService securityAuditService,
    ILogger<TalentPoolService> logger) : ITalentPoolService
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly ISecurityAuditService _securityAuditService = securityAuditService;
    private readonly ILogger<TalentPoolService> _logger = logger;

    public async Task<TalentPoolListViewModel> GetTalentPoolListAsync(
        string? search = null,
        string? jobTitle = null,
        string? skill = null,
        string? status = null,
        int? minRating = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.TalentPoolCandidates
            .Include(tp => tp.Candidate)
                .ThenInclude(c => c.Resumes)
            .Include(tp => tp.Candidate)
                .ThenInclude(c => c.Tags.Where(t => !t.IsDeleted))
                    .ThenInclude(t => t.Catalog)
            .Include(tp => tp.AddedByUser)
            .Include(tp => tp.SourceApplication)
                .ThenInclude(a => a!.JobPosting)
            .Where(tp => !tp.IsDeleted);

        // 1. Lọc theo từ khóa tìm kiếm tổng quát
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(tp =>
                tp.Candidate.FirstName.ToLower().Contains(s) ||
                tp.Candidate.LastName.ToLower().Contains(s) ||
                tp.Candidate.Email.ToLower().Contains(s) ||
                (tp.Candidate.Phone != null && tp.Candidate.Phone.Contains(s)) ||
                tp.TargetJobTitle.ToLower().Contains(s) ||
                (tp.Skills != null && tp.Skills.ToLower().Contains(s)) ||
                tp.Reason.ToLower().Contains(s));
        }

        // 2. Lọc theo Chức danh quan tâm (Target Job Title)
        if (!string.IsNullOrWhiteSpace(jobTitle))
        {
            var jt = jobTitle.Trim().ToLower();
            query = query.Where(tp => tp.TargetJobTitle.ToLower().Contains(jt));
        }

        // 3. Lọc theo Kỹ năng (Skill)
        if (!string.IsNullOrWhiteSpace(skill))
        {
            var sk = skill.Trim().ToLower();
            query = query.Where(tp =>
                (tp.Skills != null && tp.Skills.ToLower().Contains(sk)) ||
                tp.Candidate.Tags.Any(t => t.Catalog.Name.ToLower().Contains(sk)));
        }

        // 4. Lọc theo Trạng thái (Status)
        if (!string.IsNullOrWhiteSpace(status) && status != "ALL")
        {
            query = query.Where(tp => tp.Status == status);
        }

        // 5. Lọc theo Đánh giá tối thiểu
        if (minRating.HasValue && minRating.Value > 0)
        {
            query = query.Where(tp => tp.Rating >= minRating.Value);
        }

        var candidatesInPool = await query
            .OrderByDescending(tp => tp.AddedAt)
            .ToListAsync(cancellationToken);

        // Chuẩn bị danh sách Job Titles và Skills có trong kho để hiển thị gợi ý bộ lọc
        var allEntries = await _dbContext.TalentPoolCandidates
            .Where(tp => !tp.IsDeleted)
            .Select(tp => new { tp.TargetJobTitle, tp.Skills })
            .ToListAsync(cancellationToken);

        var distinctTitles = allEntries
            .Select(x => x.TargetJobTitle.Trim())
            .Where(x => !string.IsNullOrEmpty(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

        var distinctSkills = allEntries
            .Where(x => !string.IsNullOrEmpty(x.Skills))
            .SelectMany(x => x.Skills!.Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .Take(25)
            .ToList();

        // Lấy danh sách việc làm đang mở (Published)
        var openJobs = await _dbContext.JobPostings
            .Include(j => j.Requisition)
                .ThenInclude(r => r.Department)
            .Where(j => !j.IsDeleted && j.Status == JobPostingStatus.PUBLISHED)
            .OrderByDescending(j => j.PublishedAt ?? j.CreatedAt)
            .Select(j => new OpenJobOptionViewModel
            {
                Id = j.Id,
                Title = j.Title,
                Code = j.Code ?? j.Requisition.Code,
                DepartmentName = j.Requisition.Department != null ? j.Requisition.Department.Name : "NoveraTech",
                WorkLocation = j.WorkLocation,
                Status = j.Status.ToString()
            })
            .ToListAsync(cancellationToken);

        var itemsVm = candidatesInPool.Select(tp =>
        {
            var primaryResume = tp.Candidate.Resumes.FirstOrDefault(r => r.IsPrimary) ?? tp.Candidate.Resumes.FirstOrDefault();
            return new TalentPoolItemViewModel
            {
                Id = tp.Id,
                CandidateId = tp.CandidateId,
                FullName = $"{tp.Candidate.FirstName} {tp.Candidate.LastName}".Trim(),
                Email = tp.Candidate.Email,
                Phone = tp.Candidate.Phone,
                TargetJobTitle = tp.TargetJobTitle,
                SkillsRaw = tp.Skills,
                ExperienceYears = tp.ExperienceYears,
                Rating = tp.Rating,
                LastStageReached = tp.LastStageReached,
                Reason = tp.Reason,
                Notes = tp.Notes,
                Status = tp.Status,
                AddedByUserName = tp.AddedByUser?.FullName ?? "Ban Nhân Sự",
                AddedAt = tp.AddedAt,
                SourceApplicationId = tp.SourceApplicationId,
                SourceJobTitle = tp.SourceApplication?.JobPosting?.Title,
                HasResume = primaryResume != null,
                ResumeId = primaryResume?.Id,
                ResumeFileName = primaryResume?.FileName,
                ResumeFilePath = primaryResume?.FilePath
            };
        }).ToList();

        return new TalentPoolListViewModel
        {
            Items = itemsVm,
            SearchQuery = search,
            SelectedJobTitle = jobTitle,
            SelectedSkill = skill,
            SelectedStatus = status,
            MinRating = minRating,
            AvailableJobTitles = distinctTitles,
            AvailableSkills = distinctSkills,
            OpenJobs = openJobs
        };
    }

    public async Task<TalentPoolSuggestionListViewModel?> GetSuggestedCandidatesForJobAsync(
        Guid jobPostingId,
        CancellationToken cancellationToken = default)
    {
        var jobPosting = await _dbContext.JobPostings
            .Include(j => j.Requisition)
                .ThenInclude(r => r.Department)
            .Include(j => j.Requisition)
                .ThenInclude(r => r.JobPosition)
            .FirstOrDefaultAsync(j => j.Id == jobPostingId && !j.IsDeleted, cancellationToken);

        if (jobPosting == null)
        {
            return null;
        }

        // Lấy tất cả ứng viên hiện có trong kho tiềm năng
        var poolCandidates = await _dbContext.TalentPoolCandidates
            .Include(tp => tp.Candidate)
                .ThenInclude(c => c.Resumes)
            .Include(tp => tp.Candidate)
                .ThenInclude(c => c.Tags.Where(t => !t.IsDeleted))
                    .ThenInclude(t => t.Catalog)
            .Include(tp => tp.AddedByUser)
            .Include(tp => tp.SourceApplication)
                .ThenInclude(a => a!.JobPosting)
            .Where(tp => !tp.IsDeleted && tp.Status != "ARCHIVED")
            .ToListAsync(cancellationToken);

        // Lấy danh sách CandidateId đã nộp đơn vào vị trí này
        var appliedCandidateIds = await _dbContext.Applications
            .Where(a => a.JobPostingId == jobPostingId && !a.IsDeleted)
            .Select(a => a.CandidateId)
            .ToHashSetAsync(cancellationToken);

        // Thuật toán Matching (Matching Engine)
        var suggestions = new List<TalentPoolCandidateSuggestionViewModel>();

        // Chuẩn hóa văn bản tin tuyển dụng để trích xuất từ khóa
        var jobTitleTokens = TokenizeKeywords(jobPosting.Title);
        var jobRequirementsText = $"{jobPosting.Requirements} {jobPosting.JobDescription} {jobPosting.Title}".ToLower();
        var departmentTokens = TokenizeKeywords(jobPosting.Requisition?.Department?.Name ?? string.Empty);

        foreach (var tp in poolCandidates)
        {
            int score = 0;
            var matchingSkills = new List<string>();
            var matchReasons = new List<string>();

            // 1. So khớp chức danh quan tâm (Target Job Title)
            var targetTitle = tp.TargetJobTitle.Trim();
            var targetTokens = TokenizeKeywords(targetTitle);

            bool titleExactMatch = jobPosting.Title.Contains(targetTitle, StringComparison.OrdinalIgnoreCase) ||
                                   targetTitle.Contains(jobPosting.Title, StringComparison.OrdinalIgnoreCase);

            var commonTitleTokens = targetTokens.Intersect(jobTitleTokens, StringComparer.OrdinalIgnoreCase).ToList();

            if (titleExactMatch)
            {
                score += 45;
                matchReasons.Add($"Chức danh quan tâm '{targetTitle}' hoàn toàn phù hợp với vị trí");
            }
            else if (commonTitleTokens.Count > 0)
            {
                score += Math.Min(35, commonTitleTokens.Count * 15);
                matchReasons.Add($"Chức danh có từ khóa tương đồng: {string.Join(", ", commonTitleTokens)}");
            }

            // 2. So khớp kỹ năng chuyên môn (Skills & Tags & CV)
            var candidateSkills = (tp.Skills ?? string.Empty)
                .Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            // Bổ sung các nhãn từ CandidateTag
            var tagNames = tp.Candidate.Tags.Select(t => t.TagName).ToList();
            var allCandidateSkillTokens = candidateSkills.Concat(tagNames).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            int matchedSkillCount = 0;
            foreach (var sk in allCandidateSkillTokens)
            {
                if (sk.Length >= 2 && jobRequirementsText.Contains(sk.ToLower()))
                {
                    matchedSkillCount++;
                    matchingSkills.Add(sk);
                }
            }

            if (matchedSkillCount > 0)
            {
                int skillScore = Math.Min(35, matchedSkillCount * 10);
                score += skillScore;
                matchReasons.Add($"Khớp {matchedSkillCount} kỹ năng chuyên môn trong bản mô tả công việc: {string.Join(", ", matchingSkills.Take(4))}");
            }

            // 3. Ưu tiên ứng viên từng vào vòng cuối (Theo Scrum 45: trượt vòng chuyên môn/offer)
            if (!string.IsNullOrWhiteSpace(tp.LastStageReached))
            {
                if (tp.LastStageReached.Contains("Offer", StringComparison.OrdinalIgnoreCase) ||
                    tp.LastStageReached.Contains("Đề nghị", StringComparison.OrdinalIgnoreCase))
                {
                    score += 15;
                    matchReasons.Add($"Từng đạt vòng Đề nghị tuyển dụng (Offer) trong đợt tuyển trước");
                }
                else if (tp.LastStageReached.Contains("chuyên môn", StringComparison.OrdinalIgnoreCase) ||
                         tp.LastStageReached.Contains("kỹ thuật", StringComparison.OrdinalIgnoreCase) ||
                         tp.LastStageReached.Contains("Vòng 4", StringComparison.OrdinalIgnoreCase))
                {
                    score += 10;
                    matchReasons.Add($"Đã từng vượt qua các vòng sơ loại để vào vòng Phỏng vấn chuyên môn");
                }
            }

            // 4. Ưu tiên đánh giá năng lực sao cao
            if (tp.Rating.HasValue)
            {
                if (tp.Rating.Value >= 4)
                {
                    score += 10;
                    matchReasons.Add($"Đánh giá năng lực cao từ hội đồng tuyển dụng ({tp.Rating.Value}/5 sao)");
                }
                else if (tp.Rating.Value == 3)
                {
                    score += 5;
                }
            }

            // Giới hạn điểm số tối đa 98%
            score = Math.Min(98, score);

            // Chỉ gợi ý nếu có điểm tương quan tối thiểu (>= 20)
            if (score >= 20 || titleExactMatch || matchedSkillCount > 0)
            {
                var primaryResume = tp.Candidate.Resumes.FirstOrDefault(r => r.IsPrimary) ?? tp.Candidate.Resumes.FirstOrDefault();

                suggestions.Add(new TalentPoolCandidateSuggestionViewModel
                {
                    Candidate = new TalentPoolItemViewModel
                    {
                        Id = tp.Id,
                        CandidateId = tp.CandidateId,
                        FullName = $"{tp.Candidate.FirstName} {tp.Candidate.LastName}".Trim(),
                        Email = tp.Candidate.Email,
                        Phone = tp.Candidate.Phone,
                        TargetJobTitle = tp.TargetJobTitle,
                        SkillsRaw = tp.Skills,
                        ExperienceYears = tp.ExperienceYears,
                        Rating = tp.Rating,
                        LastStageReached = tp.LastStageReached,
                        Reason = tp.Reason,
                        Notes = tp.Notes,
                        Status = tp.Status,
                        AddedByUserName = tp.AddedByUser?.FullName ?? "Ban Nhân Sự",
                        AddedAt = tp.AddedAt,
                        SourceApplicationId = tp.SourceApplicationId,
                        SourceJobTitle = tp.SourceApplication?.JobPosting?.Title,
                        HasResume = primaryResume != null,
                        ResumeId = primaryResume?.Id,
                        ResumeFileName = primaryResume?.FileName,
                        ResumeFilePath = primaryResume?.FilePath
                    },
                    MatchScore = Math.Max(25, score),
                    MatchingSkills = matchingSkills.Distinct().ToList(),
                    MatchReasons = matchReasons,
                    IsAlreadyApplied = appliedCandidateIds.Contains(tp.CandidateId)
                });
            }
        }

        // Sắp xếp thứ tự: Chưa nộp trước, Điểm phù hợp từ cao xuống thấp
        suggestions = suggestions
            .OrderBy(s => s.IsAlreadyApplied)
            .ThenByDescending(s => s.MatchScore)
            .ToList();

        // Lấy danh sách việc làm đang mở khác để hỗ trợ dropdown chuyển đổi
        var openJobsList = await _dbContext.JobPostings
            .Include(j => j.Requisition)
                .ThenInclude(r => r.Department)
            .Where(j => !j.IsDeleted && j.Status == JobPostingStatus.PUBLISHED)
            .OrderByDescending(j => j.PublishedAt ?? j.CreatedAt)
            .Select(j => new OpenJobOptionViewModel
            {
                Id = j.Id,
                Title = j.Title,
                Code = j.Code ?? j.Requisition.Code,
                DepartmentName = j.Requisition.Department != null ? j.Requisition.Department.Name : "NoveraTech",
                WorkLocation = j.WorkLocation,
                Status = j.Status.ToString()
            })
            .ToListAsync(cancellationToken);

        // Thu thập các kỹ năng công nghệ thực tế xuất hiện trong yêu cầu tuyển dụng
        var allKnownSkills = poolCandidates
            .SelectMany(tp => (tp.Skills ?? string.Empty)
                .Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Concat(tp.Candidate.Tags.Select(t => t.TagName)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(sk => sk.Length >= 2 && jobRequirementsText.Contains(sk.ToLower()))
            .OrderBy(sk => sk)
            .ToList();

        return new TalentPoolSuggestionListViewModel
        {
            JobPostingId = jobPosting.Id,
            JobTitle = jobPosting.Title,
            JobSlug = jobPosting.Slug,
            JobCode = jobPosting.Code ?? jobPosting.Requisition?.Code ?? "JOB",
            DepartmentName = jobPosting.Requisition?.Department?.Name ?? "NoveraTech",
            WorkLocation = jobPosting.WorkLocation,
            SalaryDisplay = jobPosting.SalaryDisplay,
            Requirements = jobPosting.Requirements,
            JobDescription = jobPosting.JobDescription,
            PublishedAt = jobPosting.PublishedAt,
            TotalPoolCandidates = poolCandidates.Count,
            SkillsRequired = allKnownSkills.Take(8).ToList(),
            AvailableOpenJobs = openJobsList,
            Suggestions = suggestions
        };
    }

    public async Task<Guid?> GetFirstActiveJobPostingIdAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.JobPostings
            .AsNoTracking()
            .Where(j => !j.IsDeleted && j.Status == JobPostingStatus.PUBLISHED)
            .OrderByDescending(j => j.PublishedAt)
            .Select(j => (Guid?)j.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<(bool Success, string Message, Guid? Id)> AddCandidateToPoolAsync(
        AddTalentPoolCandidateInputModel model,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        if (model.CandidateId == Guid.Empty)
        {
            return (false, "Thông tin ứng viên không hợp lệ.", null);
        }

        if (string.IsNullOrWhiteSpace(model.Reason))
        {
            return (false, "Vui lòng nhập lý do đưa ứng viên vào kho tiềm năng.", null);
        }

        if (string.IsNullOrWhiteSpace(model.TargetJobTitle))
        {
            return (false, "Vui lòng chỉ định chức danh quan tâm / phù hợp cho ứng viên.", null);
        }

        var candidate = await _dbContext.Candidates
            .Include(c => c.Tags)
                .ThenInclude(t => t.Catalog)
            .FirstOrDefaultAsync(c => c.Id == model.CandidateId && !c.IsDeleted, cancellationToken);

        if (candidate == null)
        {
            return (false, "Không tìm thấy hồ sơ ứng viên trong hệ thống.", null);
        }

        // Kiểm tra xem ứng viên đã có trong kho chưa
        var existing = await _dbContext.TalentPoolCandidates
            .FirstOrDefaultAsync(tp => tp.CandidateId == model.CandidateId && !tp.IsDeleted, cancellationToken);

        if (existing != null)
        {
            // Cập nhật lại thông tin mới nhất
            existing.Reason = model.Reason.Trim();
            existing.TargetJobTitle = model.TargetJobTitle.Trim();
            if (!string.IsNullOrWhiteSpace(model.Skills))
            {
                existing.Skills = model.Skills.Trim();
            }
            if (!string.IsNullOrWhiteSpace(model.LastStageReached))
            {
                existing.LastStageReached = model.LastStageReached.Trim();
            }
            if (model.Rating.HasValue)
            {
                existing.Rating = model.Rating.Value;
            }
            if (!string.IsNullOrWhiteSpace(model.Notes))
            {
                existing.Notes = model.Notes.Trim();
            }
            existing.Status = "AVAILABLE"; // Đặt lại trạng thái sẵn sàng
            existing.UpdatedAt = DateTimeOffset.UtcNow;

            await _dbContext.SaveChangesAsync(cancellationToken);
            return (true, "Ứng viên đã có trong kho và vừa được cập nhật thông tin tiềm năng thành công!", existing.Id);
        }

        // Tự động lấy thêm kỹ năng từ Tags nếu người dùng không nhập
        var skills = model.Skills?.Trim();
        if (string.IsNullOrWhiteSpace(skills) && candidate.Tags.Any())
        {
            skills = string.Join(", ", candidate.Tags.Select(t => t.TagName));
        }

        var newEntry = new TalentPoolCandidate
        {
            Id = Guid.NewGuid(),
            CandidateId = model.CandidateId,
            SourceApplicationId = model.SourceApplicationId,
            LastStageReached = model.LastStageReached?.Trim(),
            Reason = model.Reason.Trim(),
            TargetJobTitle = model.TargetJobTitle.Trim(),
            Skills = skills,
            ExperienceYears = model.ExperienceYears,
            Rating = model.Rating ?? 4,
            Notes = model.Notes?.Trim(),
            Status = "AVAILABLE",
            AddedByUserId = currentUserId,
            AddedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await _dbContext.TalentPoolCandidates.AddAsync(newEntry, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _securityAuditService.LogSecurityEvent(new SecurityAuditEvent
        {
            EventType = SecurityAuditEventType.CandidateDocumentAccessed,
            UserId = currentUserId.ToString(),
            ResourcePath = $"/kho-tiem-nang/{newEntry.Id}",
            Action = "TALENT_POOL_ADD_CANDIDATE",
            IsSuccess = true,
            Details = $"Đưa ứng viên {candidate.Email} vào kho tiềm năng cho chức danh '{newEntry.TargetJobTitle}'. Lý do: {newEntry.Reason}"
        });

        return (true, "Đã lưu ứng viên vào Kho Tiềm Năng thành công!", newEntry.Id);
    }

    public async Task<(bool Success, string Message)> UpdateCandidateInPoolAsync(
        UpdateTalentPoolCandidateInputModel model,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var entry = await _dbContext.TalentPoolCandidates
            .FirstOrDefaultAsync(tp => tp.Id == model.Id && !tp.IsDeleted, cancellationToken);

        if (entry == null)
        {
            return (false, "Không tìm thấy ứng viên trong kho tiềm năng.");
        }

        if (string.IsNullOrWhiteSpace(model.TargetJobTitle))
        {
            return (false, "Chức danh quan tâm không được để trống.");
        }

        entry.TargetJobTitle = model.TargetJobTitle.Trim();
        entry.Skills = model.Skills?.Trim();
        entry.Rating = model.Rating;
        entry.Status = model.Status ?? "AVAILABLE";
        entry.Notes = model.Notes?.Trim();
        entry.UpdatedAt = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return (true, "Đã cập nhật thông tin ứng viên trong kho tiềm năng thành công!");
    }

    public async Task<(bool Success, string Message)> RemoveCandidateFromPoolAsync(
        Guid id,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var entry = await _dbContext.TalentPoolCandidates
            .Include(tp => tp.Candidate)
            .FirstOrDefaultAsync(tp => tp.Id == id && !tp.IsDeleted, cancellationToken);

        if (entry == null)
        {
            return (false, "Không tìm thấy bản ghi ứng viên tiềm năng.");
        }

        entry.IsDeleted = true;
        entry.DeletedAt = DateTimeOffset.UtcNow;
        entry.Status = "ARCHIVED";

        await _dbContext.SaveChangesAsync(cancellationToken);

        _securityAuditService.LogSecurityEvent(new SecurityAuditEvent
        {
            EventType = SecurityAuditEventType.CandidateDocumentAccessed,
            UserId = currentUserId.ToString(),
            ResourcePath = $"/kho-tiem-nang/{entry.Id}",
            Action = "TALENT_POOL_REMOVE_CANDIDATE",
            IsSuccess = true,
            Details = $"Đã gỡ ứng viên {entry.Candidate?.Email} khỏi kho tiềm năng."
        });

        return (true, "Đã xóa ứng viên khỏi kho tiềm năng thành công.");
    }

    public async Task<(bool Success, string Message, Guid? NewApplicationId)> InviteCandidateToJobAsync(
        InviteTalentPoolCandidateInputModel model,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var poolEntry = await _dbContext.TalentPoolCandidates
            .Include(tp => tp.Candidate)
                .ThenInclude(c => c.Resumes)
            .FirstOrDefaultAsync(tp => tp.Id == model.TalentPoolCandidateId && !tp.IsDeleted, cancellationToken);

        if (poolEntry == null)
        {
            return (false, "Không tìm thấy ứng viên trong kho tiềm năng.", null);
        }

        var jobPosting = await _dbContext.JobPostings
            .Include(j => j.Requisition)
            .FirstOrDefaultAsync(j => j.Id == model.JobPostingId && !j.IsDeleted, cancellationToken);

        if (jobPosting == null)
        {
            return (false, "Vị trí tuyển dụng không tồn tại hoặc đã đóng.", null);
        }

        // Kiểm tra xem ứng viên đã có đơn ứng tuyển cho vị trí này chưa
        var alreadyApplied = await _dbContext.Applications
            .AnyAsync(a => a.CandidateId == poolEntry.CandidateId && a.JobPostingId == model.JobPostingId && !a.IsDeleted, cancellationToken);

        if (alreadyApplied)
        {
            return (false, $"Ứng viên {poolEntry.Candidate.Email} đã có đơn ứng tuyển cho vị trí '{jobPosting.Title}'.", null);
        }

        // Lấy CV chính của ứng viên
        var resume = poolEntry.Candidate.Resumes.FirstOrDefault(r => r.IsPrimary) ?? poolEntry.Candidate.Resumes.FirstOrDefault();
        if (resume == null)
        {
            // Nếu chưa có file CV, tạo 1 dummy Resume record để liên kết
            resume = new Resume
            {
                Id = Guid.NewGuid(),
                CandidateId = poolEntry.CandidateId,
                FileName = $"CV_{poolEntry.Candidate.LastName}.pdf",
                FilePath = "#",
                IsPrimary = true,
                CreatedAt = DateTimeOffset.UtcNow
            };
            await _dbContext.Resumes.AddAsync(resume, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        // Lấy Stage: ưu tiên Stage 2 ("Sàng lọc CV") hoặc Stage 3 ("Phỏng vấn sơ loại") để ứng viên tiềm năng được ưu tiên xét tuyển nhanh
        var stages = await _dbContext.PipelineStages.OrderBy(s => s.StageOrder).ToListAsync(cancellationToken);
        var initialStage = stages.FirstOrDefault(s => s.StageOrder == 2) ?? stages.FirstOrDefault(s => s.StageOrder == 1) ?? stages.First();

        var newApplication = new Application
        {
            Id = Guid.NewGuid(),
            CandidateId = poolEntry.CandidateId,
            JobPostingId = jobPosting.Id,
            ResumeId = resume.Id,
            CurrentStageId = initialStage.Id,
            Status = ApplicationStatus.IN_PROCESS,
            Rating = poolEntry.Rating,
            AppliedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await _dbContext.Applications.AddAsync(newApplication, cancellationToken);

        // Ghi nhận lịch sử chuyển giai đoạn (Audit trail)
        var stageHistory = new ApplicationStageHistory
        {
            Id = Guid.NewGuid(),
            ApplicationId = newApplication.Id,
            FromStageId = null,
            ToStageId = initialStage.Id,
            ChangedByUserId = currentUserId,
            Comment = $"Ứng viên được Trưởng phòng Nhân sự mời ứng tuyển từ Kho Tiềm Năng (Scrum 45). {model.Note?.Trim()}",
            CreatedAt = DateTimeOffset.UtcNow
        };
        await _dbContext.ApplicationStageHistories.AddAsync(stageHistory, cancellationToken);

        // Cập nhật trạng thái trong Kho Tiềm Năng
        poolEntry.Status = "REAPPLIED";
        poolEntry.UpdatedAt = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _securityAuditService.LogSecurityEvent(new SecurityAuditEvent
        {
            EventType = SecurityAuditEventType.CandidateDocumentAccessed,
            UserId = currentUserId.ToString(),
            ResourcePath = $"/ung-vien/{poolEntry.CandidateId}",
            Action = "TALENT_POOL_INVITE_CANDIDATE",
            IsSuccess = true,
            Details = $"Mời ứng viên {poolEntry.Candidate.Email} từ kho tiềm năng vào vị trí '{jobPosting.Title}' (ApplicationId: {newApplication.Id})"
        });

        return (true, $"Đã đưa ứng viên vào quy trình tuyển dụng vị trí '{jobPosting.Title}' thành công!", newApplication.Id);
    }

    public async Task<List<OpenJobOptionViewModel>> GetOpenJobsAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.JobPostings
            .Include(j => j.Requisition)
                .ThenInclude(r => r.Department)
            .Where(j => !j.IsDeleted && j.Status == JobPostingStatus.PUBLISHED)
            .OrderByDescending(j => j.PublishedAt ?? j.CreatedAt)
            .Select(j => new OpenJobOptionViewModel
            {
                Id = j.Id,
                Title = j.Title,
                Code = j.Code ?? j.Requisition.Code,
                DepartmentName = j.Requisition.Department != null ? j.Requisition.Department.Name : "NoveraTech",
                WorkLocation = j.WorkLocation,
                Status = j.Status.ToString()
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> IsCandidateInPoolAsync(Guid candidateId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.TalentPoolCandidates
            .AnyAsync(tp => tp.CandidateId == candidateId && !tp.IsDeleted, cancellationToken);
    }

    public async Task<TalentPoolCandidate?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.TalentPoolCandidates
            .Include(tp => tp.Candidate)
            .Include(tp => tp.SourceApplication)
            .FirstOrDefaultAsync(tp => tp.Id == id && !tp.IsDeleted, cancellationToken);
    }

    private static List<string> TokenizeKeywords(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        char[] separators = [' ', ',', '.', '/', '-', '(', ')', '[', ']', ':', '+'];
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "và", "hoặc", "của", "cho", "các", "những", "trong", "về", "với", "tại", "là", "được", "có", "theo",
            "kèm", "yêu", "cầu", "tuyển", "dụng", "vị", "trí", "nhân", "viên", "bộ", "phận", "phòng", "ban", "level",
            "senior", "junior", "middle", "lead", "chính", "thức", "kinh", "nghiệm", "năm",
            "chuyên", "cao", "cấp", "bảo", "mật", "hệ", "thống", "đã", "đóng", "mở", "mới"
        };

        return text.Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(w => w.Length >= 2 && !stopWords.Contains(w))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
