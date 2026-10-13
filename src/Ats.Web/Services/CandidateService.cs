using Ats.Web.Constants;
using Ats.Web.Data;
using Ats.Web.Models.Entities;
using Ats.Web.Models.ViewModels.Candidates;
using Ats.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ats.Web.Services;

/// <summary>
/// Dịch vụ quản lý Hồ sơ Ứng viên Hợp nhất (Scrum 39 - Candidate Unified Profile / Dossier).
/// Tuân thủ quy chuẩn C# 12 Primary Constructors, bảo mật phân quyền nghiêm ngặt theo vai trò
/// và chuẩn hóa múi giờ Việt Nam UTC+7.
/// </summary>
public class CandidateService(
    ApplicationDbContext dbContext,
    IFileStorageService fileStorageService,
    ISecurityAuditService securityAuditService,
    ILogger<CandidateService> logger) : ICandidateService
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly IFileStorageService _fileStorageService = fileStorageService;
    private readonly ISecurityAuditService _securityAuditService = securityAuditService;
    private readonly ILogger<CandidateService> _logger = logger;

    public async Task<bool> CanAccessCandidateAsync(
        Guid candidateId,
        Guid currentUserId,
        string currentUserRole,
        CancellationToken cancellationToken = default)
    {
        // 1. Quản trị viên hệ thống (Admin) hoặc Trưởng phòng Nhân sự (HRManager) có quyền xem toàn bộ hồ sơ
        if (currentUserRole == UserRoles.Admin || currentUserRole == UserRoles.HRManager)
        {
            return true;
        }

        // 2. Tìm ứng viên
        var candidate = await _dbContext.Candidates
            .Include(c => c.Applications)
                .ThenInclude(a => a.JobPosting)
                    .ThenInclude(jp => jp.Requisition)
            .FirstOrDefaultAsync(c => c.Id == candidateId && !c.IsDeleted, cancellationToken);

        if (candidate == null)
        {
            return false;
        }

        // 3. Nếu là tài khoản chính chủ của ứng viên (portal)
        if (candidate.UserId.HasValue && candidate.UserId.Value == currentUserId)
        {
            return true;
        }

        // 4. "Chỉ recruiter phụ trách và hiring manager của vị trí đó mở được"
        // Kiểm tra tất cả các đơn ứng tuyển của ứng viên này (bao gồm cả các đơn ứng tuyển cùng Email)
        var allRelatedApplications = await _dbContext.Applications
            .Include(a => a.JobPosting)
                .ThenInclude(jp => jp.Requisition)
            .Where(a => (!a.IsDeleted) && (a.CandidateId == candidateId || a.Candidate.Email.ToLower() == candidate.Email.ToLower()))
            .ToListAsync(cancellationToken);

        foreach (var app in allRelatedApplications)
        {
            var req = app.JobPosting?.Requisition;
            if (req != null)
            {
                // Kiểm tra Recruiter phụ trách của vị trí
                if (req.AssignedRecruiterId.HasValue && req.AssignedRecruiterId.Value == currentUserId)
                {
                    return true;
                }

                // Kiểm tra Hiring Manager của vị trí
                if (req.HiringManagerId == currentUserId)
                {
                    return true;
                }
            }
        }

        return false;
    }

    public async Task<CandidateDetailsViewModel?> GetUnifiedProfileAsync(
        Guid candidateId,
        Guid currentUserId,
        string currentUserRole,
        CancellationToken cancellationToken = default)
    {
        var candidate = await _dbContext.Candidates
            .Include(c => c.Resumes)
            .Include(c => c.Tags.Where(t => !t.IsDeleted))
                .ThenInclude(t => t.CreatedByUser)
            .Include(c => c.Notes.Where(n => !n.IsDeleted))
                .ThenInclude(n => n.AuthorUser)
            .Include(c => c.ReferrerUser)
            .FirstOrDefaultAsync(c => c.Id == candidateId && !c.IsDeleted, cancellationToken);

        if (candidate == null)
        {
            return null;
        }

        // Kiểm tra quyền hạn
        var hasAccess = await CanAccessCandidateAsync(candidateId, currentUserId, currentUserRole, cancellationToken);
        if (!hasAccess)
        {
            _securityAuditService.LogSecurityEvent(new SecurityAuditEvent
            {
                EventType = SecurityAuditEventType.CandidateDocumentAccessDenied,
                UserId = currentUserId.ToString(),
                UserNameOrEmail = currentUserRole,
                ResourcePath = $"/ung-vien/{candidateId}",
                Action = "VIEW_UNIFIED_PROFILE_DENIED",
                IsSuccess = false,
                Details = $"Từ chối truy cập hồ sơ hợp nhất ứng viên {candidate.Email}. Chỉ Recruiter phụ trách hoặc Hiring Manager của vị trí mới có quyền xem."
            });

            return new CandidateDetailsViewModel
            {
                CandidateId = candidate.Id,
                FirstName = candidate.FirstName,
                LastName = candidate.LastName,
                Email = candidate.Email,
                HasAccess = false,
                AccessDeniedReason = "Truy cập bị từ chối: Hồ sơ ứng viên hợp nhất chỉ dành cho Chuyên viên tuyển dụng phụ trách hoặc Quản lý chuyên môn (Hiring Manager) của vị trí ứng viên đã ứng tuyển.",
                CurrentUserRole = currentUserRole
            };
        }

        // Truy vấn TẤT CẢ các đơn ứng tuyển của cùng người này (cùng CandidateId hoặc cùng Email)
        var allCandidateApplications = await _dbContext.Applications
            .Include(a => a.JobPosting)
                .ThenInclude(jp => jp.Requisition)
                    .ThenInclude(req => req.Department)
            .Include(a => a.JobPosting)
                .ThenInclude(jp => jp.Requisition)
                    .ThenInclude(req => req.AssignedRecruiter)
            .Include(a => a.JobPosting)
                .ThenInclude(jp => jp.Requisition)
                    .ThenInclude(req => req.HiringManager)
            .Include(a => a.CurrentStage)
            .Include(a => a.Resume)
            .Include(a => a.Interviews)
                .ThenInclude(i => i.Panelists)
                    .ThenInclude(ip => ip.Interviewer)
            .Include(a => a.Interviews)
                .ThenInclude(i => i.Evaluations)
                    .ThenInclude(e => e.Interviewer)
            .Include(a => a.JobOffers.Where(o => !o.IsDeleted))
            .Include(a => a.StageHistories.Where(h => !h.IsDeleted))
                .ThenInclude(h => h.ChangedByUser)
            .Where(a => (!a.IsDeleted) && (a.CandidateId == candidateId || a.Candidate.Email.ToLower() == candidate.Email.ToLower()))
            .OrderByDescending(a => a.AppliedAt ?? a.CreatedAt)
            .ToListAsync(cancellationToken);

        // Chuẩn bị danh sách Stage để hiển thị tên FromStage
        var allStages = await _dbContext.PipelineStages.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);

        // Map Resumes
        var resumesVm = candidate.Resumes
            .Where(r => !r.IsDeleted)
            .OrderByDescending(r => r.IsPrimary)
            .ThenByDescending(r => r.CreatedAt)
            .Select(r => new CandidateResumeViewModel
            {
                ResumeId = r.Id,
                FileName = r.FileName,
                FilePath = r.FilePath,
                FileSize = r.FileSize,
                MimeType = r.MimeType,
                IsPrimary = r.IsPrimary,
                UploadedAt = r.CreatedAt,
                ParsedText = r.ParsedText
            }).ToList();

        // Map Application Histories (Toàn bộ các lần ứng tuyển)
        var applicationHistoriesVm = allCandidateApplications.Select(app =>
        {
            var req = app.JobPosting?.Requisition;
            var vm = new CandidateApplicationHistoryViewModel
            {
                ApplicationId = app.Id,
                JobPostingId = app.JobPostingId,
                JobTitle = app.JobPosting?.Title ?? "Vị trí tuyển dụng",
                JobSlug = app.JobPosting?.Slug ?? string.Empty,
                RequisitionCode = req?.Code ?? "N/A",
                DepartmentName = req?.Department?.Name ?? "NoveraTech",
                WorkLocation = app.JobPosting?.WorkLocation ?? "Trụ sở chính",
                EmploymentType = app.JobPosting?.EmploymentType.ToString() ?? "FULL_TIME",
                SalaryDisplay = app.JobPosting?.SalaryDisplay,
                AppliedAt = app.AppliedAt ?? app.CreatedAt,
                CurrentStageId = app.CurrentStageId,
                CurrentStageName = app.CurrentStage?.Name ?? "Đang xét duyệt",
                StageOrder = app.CurrentStage?.StageOrder ?? 1,
                StageColor = app.CurrentStage?.ColorCode ?? "#2563EB",
                Status = app.Status.ToString(),
                Rating = app.Rating,
                RejectionReason = app.RejectionReasonId.HasValue ? "Theo danh mục chuẩn" : null,
                RejectionNote = app.RejectionNote,
                AssignedRecruiterName = req?.AssignedRecruiter?.FullName,
                AssignedRecruiterEmail = req?.AssignedRecruiter?.Email,
                HiringManagerName = req?.HiringManager?.FullName,
                HiringManagerEmail = req?.HiringManager?.Email,
                ResumeId = app.ResumeId,
                ResumeFileName = app.Resume?.FileName ?? "CV_Attached.pdf",
                ResumeFilePath = app.Resume?.FilePath ?? string.Empty,

                // Phỏng vấn
                Interviews = app.Interviews
                    .Where(i => !i.IsDeleted)
                    .OrderBy(i => i.RoundNumber)
                    .Select(i =>
                    {
                        var eval = i.Evaluations.FirstOrDefault(e => !e.IsDeleted);
                        return new CandidateInterviewHistoryItemViewModel
                        {
                            InterviewId = i.Id,
                            RoundNumber = i.RoundNumber,
                            Title = i.Title,
                            InterviewType = i.InterviewType.ToString(),
                            LocationOrLink = i.LocationOrLink ?? "Trực tuyến",
                            StartTime = i.StartTime,
                            EndTime = i.EndTime,
                            Status = i.Status.ToString(),
                            CandidateConfirmed = i.CandidateConfirmed.ToString(),
                            Panelists = i.Panelists.Select(p => p.Interviewer?.FullName ?? "Hội đồng phỏng vấn").ToList(),
                            OverallScore = eval?.OverallScore,
                            Recommendation = eval?.Recommendation.ToString(),
                            Feedback = eval?.DetailedFeedback,
                            InterviewerName = eval?.Interviewer?.FullName
                        };
                    }).ToList(),

                // Offer
                Offer = app.JobOffers.OrderByDescending(o => o.CreatedAt).Select(o => new CandidateOfferHistoryItemViewModel
                {
                    OfferId = o.Id,
                    BaseSalary = o.BaseSalary,
                    BonusAllowance = o.BonusAllowance,
                    TotalPackage = o.TotalPackage,
                    ProposedJoinDate = o.ProposedJoinDate,
                    ContractType = o.ContractType ?? string.Empty,
                    Status = o.Status.ToString()
                }).FirstOrDefault(),

                // Stage History
                StageHistories = app.StageHistories
                    .Where(h => !h.IsDeleted)
                    .OrderByDescending(h => h.CreatedAt)
                    .Select(h =>
                    {
                        string fromStageName = "Nộp hồ sơ";
                        if (h.FromStageId.HasValue)
                        {
                            if (allStages.TryGetValue(h.FromStageId.Value, out var fName))
                            {
                                fromStageName = fName;
                            }
                        }

                        string toStageName = "Giai đoạn kế tiếp";
                        if (allStages.TryGetValue(h.ToStageId, out var tName))
                        {
                            toStageName = tName;
                        }

                        return new CandidateStageHistoryItemViewModel
                        {
                            Id = h.Id,
                            FromStageName = fromStageName,
                            ToStageName = toStageName,
                            ChangedByUserName = h.ChangedByUser?.FullName ?? "Hệ thống ATS",
                            Comment = h.Comment,
                            CreatedAt = h.CreatedAt
                        };
                    }).ToList()
            };
            return vm;
        }).ToList();

        // Map Tags
        var tagsVm = candidate.Tags
            .OrderBy(t => t.TagName)
            .Select(t => new CandidateTagViewModel
            {
                Id = t.Id,
                TagName = t.TagName,
                ColorCode = t.ColorCode ?? "#059669",
                CreatedByUserName = t.CreatedByUser?.FullName,
                CreatedAt = t.CreatedAt
            }).ToList();

        // Map Notes
        var notesVm = candidate.Notes
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new CandidateNoteViewModel
            {
                Id = n.Id,
                AuthorUserId = n.AuthorUserId,
                AuthorName = n.AuthorUser?.FullName ?? "Chuyên viên tuyển dụng",
                AuthorRole = n.AuthorUser?.Role ?? "Nhân sự",
                Content = n.Content,
                NoteType = n.NoteType,
                CreatedAt = n.CreatedAt,
                CanDelete = (currentUserRole == UserRoles.Admin || n.AuthorUserId == currentUserId)
            }).ToList();

        // Kiểm tra xem user hiện tại có phải Recruiter phụ trách hay Hiring Manager của vị trí nào không
        bool isAssignedRecruiter = allCandidateApplications.Any(a => a.JobPosting?.Requisition?.AssignedRecruiterId == currentUserId);
        bool isHiringManager = allCandidateApplications.Any(a => a.JobPosting?.Requisition?.HiringManagerId == currentUserId);

        _securityAuditService.LogSecurityEvent(new SecurityAuditEvent
        {
            EventType = SecurityAuditEventType.CandidateDocumentAccessed,
            UserId = currentUserId.ToString(),
            UserNameOrEmail = currentUserRole,
            ResourcePath = $"/ung-vien/{candidateId}",
            Action = "VIEW_UNIFIED_PROFILE_SUCCESS",
            IsSuccess = true,
            Details = $"Xem hồ sơ hợp nhất của ứng viên {candidate.Email} thành công. Lịch sử gồm {applicationHistoriesVm.Count} đợt ứng tuyển."
        });

        return new CandidateDetailsViewModel
        {
            CandidateId = candidate.Id,
            UserId = candidate.UserId,
            FirstName = candidate.FirstName,
            LastName = candidate.LastName,
            Email = candidate.Email,
            Phone = candidate.Phone,
            DateOfBirth = candidate.DateOfBirth,
            Address = candidate.Address,
            CurrentCompany = candidate.CurrentCompany,
            CurrentTitle = candidate.CurrentTitle,
            LinkedinUrl = candidate.LinkedinUrl,
            Source = candidate.Source.ToString(),
            ReferrerName = candidate.ReferrerUser?.FullName,
            IsBlacklisted = candidate.IsBlacklisted,
            BlacklistReason = candidate.BlacklistReason,
            CreatedAt = candidate.CreatedAt,
            HasAccess = true,
            IsAssignedRecruiter = isAssignedRecruiter,
            IsHiringManager = isHiringManager,
            CurrentUserRole = currentUserRole,
            Resumes = resumesVm,
            ApplicationHistories = applicationHistoriesVm,
            Tags = tagsVm,
            Notes = notesVm
        };
    }

    public async Task<IReadOnlyList<CandidateListViewModel>> GetAllAsync(
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Candidates
            .Include(c => c.Tags.Where(t => !t.IsDeleted))
            .Include(c => c.Applications.Where(a => !a.IsDeleted))
                .ThenInclude(a => a.JobPosting)
            .Include(c => c.Applications.Where(a => !a.IsDeleted))
                .ThenInclude(a => a.CurrentStage)
            .Where(c => !c.IsDeleted);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(c =>
                c.FirstName.ToLower().Contains(s) ||
                c.LastName.ToLower().Contains(s) ||
                c.Email.ToLower().Contains(s) ||
                (c.Phone != null && c.Phone.Contains(s)) ||
                (c.CurrentTitle != null && c.CurrentTitle.ToLower().Contains(s)));
        }

        var candidates = await query
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(cancellationToken);

        var result = candidates.Select(c =>
        {
            var latestApp = c.Applications.OrderByDescending(a => a.AppliedAt ?? a.CreatedAt).FirstOrDefault();
            return new CandidateListViewModel
            {
                Id = c.Id,
                FullName = $"{c.FirstName} {c.LastName}".Trim(),
                Email = c.Email,
                Phone = c.Phone,
                CurrentTitle = c.CurrentTitle,
                CurrentCompany = c.CurrentCompany,
                ApplicationCount = c.Applications.Count,
                LatestJobTitle = latestApp?.JobPosting?.Title,
                LatestStageName = latestApp?.CurrentStage?.Name,
                LatestStageColor = latestApp?.CurrentStage?.ColorCode,
                LatestAppliedAt = latestApp?.AppliedAt ?? latestApp?.CreatedAt,
                IsBlacklisted = c.IsBlacklisted,
                Tags = c.Tags.Select(t => new CandidateTagViewModel
                {
                    Id = t.Id,
                    TagName = t.TagName,
                    ColorCode = t.ColorCode ?? "#059669"
                }).ToList()
            };
        }).ToList();

        return result;
    }

    public async Task<CandidateTagViewModel> AddTagAsync(
        Guid candidateId,
        string tagName,
        string? colorCode,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var cleanTag = tagName.Trim();
        if (string.IsNullOrWhiteSpace(cleanTag))
        {
            throw new ArgumentException("Tên nhãn không được để trống.", nameof(tagName));
        }

        // Kiểm tra xem đã có nhãn này chưa
        var existing = await _dbContext.CandidateTags
            .FirstOrDefaultAsync(t => t.CandidateId == candidateId && t.TagName.ToLower() == cleanTag.ToLower() && !t.IsDeleted, cancellationToken);

        if (existing != null)
        {
            return new CandidateTagViewModel
            {
                Id = existing.Id,
                TagName = existing.TagName,
                ColorCode = existing.ColorCode ?? "#059669",
                CreatedAt = existing.CreatedAt
            };
        }

        var defaultColors = new[] { "#059669", "#2563EB", "#7C3AED", "#D97706", "#DC2626", "#0D9488" };
        var randomColor = string.IsNullOrWhiteSpace(colorCode)
            ? defaultColors[Math.Abs(cleanTag.GetHashCode()) % defaultColors.Length]
            : colorCode.Trim();

        var tag = new CandidateTag
        {
            Id = Guid.NewGuid(),
            CandidateId = candidateId,
            TagName = cleanTag,
            ColorCode = randomColor,
            CreatedByUserId = currentUserId != Guid.Empty ? currentUserId : null,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await _dbContext.CandidateTags.AddAsync(tag, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == currentUserId, cancellationToken);

        return new CandidateTagViewModel
        {
            Id = tag.Id,
            TagName = tag.TagName,
            ColorCode = tag.ColorCode,
            CreatedByUserName = user?.FullName,
            CreatedAt = tag.CreatedAt
        };
    }

    public async Task<bool> RemoveTagAsync(
        Guid tagId,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var tag = await _dbContext.CandidateTags.FirstOrDefaultAsync(t => t.Id == tagId && !t.IsDeleted, cancellationToken);
        if (tag == null) return false;

        tag.IsDeleted = true;
        tag.DeletedAt = DateTimeOffset.UtcNow;
        tag.UpdatedAt = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<CandidateNoteViewModel> AddNoteAsync(
        Guid candidateId,
        string content,
        string noteType,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var cleanContent = content.Trim();
        if (string.IsNullOrWhiteSpace(cleanContent))
        {
            throw new ArgumentException("Nội dung ghi chú không được để trống.", nameof(content));
        }

        var note = new CandidateNote
        {
            Id = Guid.NewGuid(),
            CandidateId = candidateId,
            AuthorUserId = currentUserId != Guid.Empty ? currentUserId : (await _dbContext.Users.Select(u => u.Id).FirstAsync(cancellationToken)),
            Content = cleanContent,
            NoteType = string.IsNullOrWhiteSpace(noteType) ? "GENERAL" : noteType.Trim().ToUpperInvariant(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await _dbContext.CandidateNotes.AddAsync(note, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var author = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == note.AuthorUserId, cancellationToken);

        return new CandidateNoteViewModel
        {
            Id = note.Id,
            AuthorUserId = note.AuthorUserId,
            AuthorName = author?.FullName ?? "Chuyên viên tuyển dụng",
            AuthorRole = author?.Role ?? "Nhân sự",
            Content = note.Content,
            NoteType = note.NoteType,
            CreatedAt = note.CreatedAt,
            CanDelete = true
        };
    }

    public async Task<bool> DeleteNoteAsync(
        Guid noteId,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var note = await _dbContext.CandidateNotes.FirstOrDefaultAsync(n => n.Id == noteId && !n.IsDeleted, cancellationToken);
        if (note == null) return false;

        // Chỉ tác giả ghi chú hoặc Admin mới được xóa
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == currentUserId, cancellationToken);
        bool isAdmin = user?.Role == UserRoles.Admin;

        if (note.AuthorUserId != currentUserId && !isAdmin)
        {
            return false;
        }

        note.IsDeleted = true;
        note.DeletedAt = DateTimeOffset.UtcNow;
        note.UpdatedAt = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<(string PhysicalPath, string FileName, string MimeType)?> GetResumeFileAsync(
        Guid resumeId,
        Guid currentUserId,
        string currentUserRole,
        CancellationToken cancellationToken = default)
    {
        var resume = await _dbContext.Resumes
            .Include(r => r.Candidate)
            .FirstOrDefaultAsync(r => r.Id == resumeId && !r.IsDeleted, cancellationToken);

        if (resume == null) return null;

        // Kiểm tra quyền
        var hasAccess = await CanAccessCandidateAsync(resume.CandidateId, currentUserId, currentUserRole, cancellationToken);
        if (!hasAccess)
        {
            return null;
        }

        // Lấy physical path
        string physicalPath;
        try
        {
            physicalPath = _fileStorageService.GetPhysicalPath(resume.FilePath);
        }
        catch
        {
            physicalPath = string.Empty;
        }

        return (physicalPath, resume.FileName, resume.MimeType ?? "application/pdf");
    }
}
