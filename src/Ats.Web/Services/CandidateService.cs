using System.Text.Encodings.Web;
using System.Text.Json;
using Ats.Web.Constants;
using Ats.Web.Data;
using Ats.Web.Models.DTOs;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Models.ViewModels.Candidates;
using Ats.Web.Services.Interfaces;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ClosedXML.Excel;
using System.Text.RegularExpressions;

namespace Ats.Web.Services;

/// <summary>
/// Dịch vụ quản lý Hồ sơ Ứng viên Hợp nhất (Scrum 39 - Candidate Unified Profile / Dossier)
/// và Thêm ứng viên thủ công đa nguồn (SCRUM-47).
/// Tuân thủ quy chuẩn C# 12 Primary Constructors, bảo mật phân quyền nghiêm ngặt theo vai trò
/// và chuẩn hóa múi giờ Việt Nam UTC+7.
/// </summary>
public class CandidateService(
    ApplicationDbContext dbContext,
    IFileStorageService fileStorageService,
    ISecurityAuditService securityAuditService,
    IApplicationTrackingService trackingService,
    ILogger<CandidateService> logger) : ICandidateService
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly IFileStorageService _fileStorageService = fileStorageService;
    private readonly ISecurityAuditService _securityAuditService = securityAuditService;
    private readonly IApplicationTrackingService _trackingService = trackingService;
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
            .Include(c => c.User)
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
            AvatarUrl = candidate.User?.AvatarUrl,
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
            .Include(c => c.User)
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
                AvatarUrl = c.User?.AvatarUrl,
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

    public async Task<CandidateSearchIndexViewModel> GetFilteredCandidatesAsync(
        CandidateFilterCriteria criteria,
        Guid currentUserId,
        string currentUserRole,
        CancellationToken cancellationToken = default)
    {
        var vm = new CandidateSearchIndexViewModel
        {
            Criteria = criteria
        };

        // 1. Tải danh mục chọn lựa cho bộ lọc
        vm.AvailableJobPostings = await _dbContext.JobPostings
            .AsNoTracking()
            .Where(j => !j.IsDeleted)
            .OrderByDescending(j => j.CreatedAt)
            .Select(j => new SelectListItem
            {
                Value = j.Id.ToString(),
                Text = $"{j.Title} ({j.Code})",
                Selected = criteria.JobPostingId.HasValue && criteria.JobPostingId.Value == j.Id
            })
            .ToListAsync(cancellationToken);

        vm.AvailableTags = await _dbContext.CandidateTags
            .AsNoTracking()
            .Where(t => !t.IsDeleted)
            .Select(t => t.TagName)
            .Distinct()
            .OrderBy(t => t)
            .ToListAsync(cancellationToken);

        vm.AvailableStages = Enum.GetValues<CandidateStage>()
            .Select(s => new SelectListItem
            {
                Value = s.ToString(),
                Text = s.GetDisplayName(),
                Selected = criteria.Stage.HasValue && criteria.Stage.Value == s
            })
            .ToList();

        vm.AvailableSources = Enum.GetValues<CandidateSource>()
            .Select(s => new SelectListItem
            {
                Value = s.ToString(),
                Text = s.GetDisplayName(),
                Selected = criteria.Source.HasValue && criteria.Source.Value == s
            })
            .ToList();

        // 2. Tính toán khoảng thời gian theo Preset hoặc FromDate / ToDate
        DateTimeOffset? filterStartUtc = null;
        DateTimeOffset? filterEndUtc = null;

        var nowVn = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7));

        if (!string.IsNullOrWhiteSpace(criteria.DatePreset) && criteria.DatePreset != "all")
        {
            switch (criteria.DatePreset.ToLowerInvariant())
            {
                case "last_month":
                    // Tháng trước: Ngày đầu tiên đến ngày cuối cùng của tháng trước
                    var currentMonthFirstDay = new DateTime(nowVn.Year, nowVn.Month, 1);
                    var lastMonthFirstDay = currentMonthFirstDay.AddMonths(-1);
                    var lastMonthLastDay = currentMonthFirstDay.AddDays(-1);

                    filterStartUtc = new DateTimeOffset(lastMonthFirstDay, TimeSpan.FromHours(7)).ToUniversalTime();
                    filterEndUtc = new DateTimeOffset(lastMonthLastDay.Year, lastMonthLastDay.Month, lastMonthLastDay.Day, 23, 59, 59, 999, TimeSpan.FromHours(7)).ToUniversalTime();
                    break;

                case "this_month":
                    // Tháng này: Từ ngày 1 của tháng hiện tại đến hiện tại
                    var thisMonthFirstDay = new DateTime(nowVn.Year, nowVn.Month, 1);
                    filterStartUtc = new DateTimeOffset(thisMonthFirstDay, TimeSpan.FromHours(7)).ToUniversalTime();
                    filterEndUtc = DateTimeOffset.UtcNow;
                    break;

                case "last_30_days":
                    filterStartUtc = DateTimeOffset.UtcNow.AddDays(-30);
                    filterEndUtc = DateTimeOffset.UtcNow;
                    break;

                case "last_7_days":
                    filterStartUtc = DateTimeOffset.UtcNow.AddDays(-7);
                    filterEndUtc = DateTimeOffset.UtcNow;
                    break;
            }
        }
        else
        {
            if (criteria.FromDate.HasValue)
            {
                filterStartUtc = new DateTimeOffset(criteria.FromDate.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7)).ToUniversalTime();
            }
            if (criteria.ToDate.HasValue)
            {
                filterEndUtc = new DateTimeOffset(criteria.ToDate.Value.ToDateTime(TimeOnly.MaxValue), TimeSpan.FromHours(7)).ToUniversalTime();
            }
        }

        // 3. Xây dựng truy vấn lọc ứng viên
        var query = _dbContext.Candidates
            .Include(c => c.User)
            .Include(c => c.Tags.Where(t => !t.IsDeleted))
            .Include(c => c.Applications.Where(a => !a.IsDeleted))
                .ThenInclude(a => a.JobPosting)
            .Include(c => c.Applications.Where(a => !a.IsDeleted))
                .ThenInclude(a => a.CurrentStage)
            .Include(c => c.Applications.Where(a => !a.IsDeleted))
                .ThenInclude(a => a.StageHistories)
            .Where(c => !c.IsDeleted);

        // 3.1. Tìm kiếm tổng hợp (Search)
        if (!string.IsNullOrWhiteSpace(criteria.Search))
        {
            var s = criteria.Search.Trim().ToLower();
            query = query.Where(c =>
                c.FirstName.ToLower().Contains(s) ||
                c.LastName.ToLower().Contains(s) ||
                c.Email.ToLower().Contains(s) ||
                (c.Phone != null && c.Phone.Contains(s)) ||
                (c.CurrentTitle != null && c.CurrentTitle.ToLower().Contains(s)) ||
                c.Applications.Any(a => !a.IsDeleted && (
                    (a.CoverLetter != null && a.CoverLetter.ToLower().Contains(s)) ||
                    (a.ReferralNote != null && a.ReferralNote.ToLower().Contains(s)) ||
                    a.StageHistories.Any(h => !h.IsDeleted && h.Comment != null && h.Comment.ToLower().Contains(s))
                ))
            );
        }

        // 3.2. Tìm kiếm từ khóa riêng trong thư giới thiệu (CoverLetterKeyword)
        if (!string.IsNullOrWhiteSpace(criteria.CoverLetterKeyword))
        {
            var kw = criteria.CoverLetterKeyword.Trim().ToLower();
            query = query.Where(c => c.Applications.Any(a => !a.IsDeleted && (
                (a.CoverLetter != null && a.CoverLetter.ToLower().Contains(kw)) ||
                (a.ReferralNote != null && a.ReferralNote.ToLower().Contains(kw)) ||
                a.StageHistories.Any(h => !h.IsDeleted && h.Comment != null && h.Comment.ToLower().Contains(kw))
            )));
        }

        // 3.3. Lọc theo vị trí tin tuyển dụng (JobPostingId)
        if (criteria.JobPostingId.HasValue && criteria.JobPostingId.Value != Guid.Empty)
        {
            query = query.Where(c => c.Applications.Any(a => !a.IsDeleted && a.JobPostingId == criteria.JobPostingId.Value));
        }

        // 3.4. Lọc theo giai đoạn (Stage)
        if (criteria.Stage.HasValue)
        {
            query = query.Where(c => c.Applications.Any(a => !a.IsDeleted && a.Stage == criteria.Stage.Value));
        }

        // 3.5. Lọc theo nguồn (Source)
        if (criteria.Source.HasValue)
        {
            query = query.Where(c => c.Source == criteria.Source.Value);
        }

        // 3.6. Lọc theo nhãn (Tag)
        if (!string.IsNullOrWhiteSpace(criteria.Tag))
        {
            var tagClean = criteria.Tag.Trim().ToLower();
            query = query.Where(c => c.Tags.Any(t => !t.IsDeleted && t.TagName.ToLower() == tagClean));
        }

        // 3.7. Lọc theo thời gian nộp hồ sơ
        if (filterStartUtc.HasValue)
        {
            query = query.Where(c => c.Applications.Any(a => !a.IsDeleted && (a.AppliedAt ?? a.CreatedAt) >= filterStartUtc.Value));
        }
        if (filterEndUtc.HasValue)
        {
            query = query.Where(c => c.Applications.Any(a => !a.IsDeleted && (a.AppliedAt ?? a.CreatedAt) <= filterEndUtc.Value));
        }

        // 3.8. Lọc theo mức đánh giá (MinRating)
        if (criteria.MinRating.HasValue && criteria.MinRating.Value > 0)
        {
            query = query.Where(c => c.Applications.Any(a => !a.IsDeleted && a.Rating.HasValue && a.Rating.Value >= criteria.MinRating.Value));
        }

        // 4. Sắp xếp & Phân trang
        var totalCount = await query.CountAsync(cancellationToken);

        var page = Math.Max(1, criteria.Page);
        var pageSize = criteria.PageSize is > 0 and <= 100 ? criteria.PageSize : 15;

        var candidates = await query
            .OrderByDescending(c => c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        // 5. Map sang CandidateListViewModel
        var items = candidates.Select(c =>
        {
            var apps = c.Applications.Where(a => !a.IsDeleted).ToList();
            var latestApp = apps.OrderByDescending(a => a.AppliedAt ?? a.CreatedAt).FirstOrDefault();

            // Tìm trích đoạn thư giới thiệu
            string? coverSnippet = null;
            if (latestApp != null)
            {
                if (!string.IsNullOrWhiteSpace(latestApp.CoverLetter))
                {
                    coverSnippet = latestApp.CoverLetter;
                }
                else if (!string.IsNullOrWhiteSpace(latestApp.ReferralNote))
                {
                    coverSnippet = latestApp.ReferralNote;
                }
                else
                {
                    var commentWithLetter = latestApp.StageHistories
                        .Where(h => !h.IsDeleted && !string.IsNullOrWhiteSpace(h.Comment) && (h.Comment.Contains("Cover letter") || h.Comment.Contains("Lời nhắn")))
                        .Select(h => h.Comment)
                        .FirstOrDefault();

                    if (!string.IsNullOrWhiteSpace(commentWithLetter))
                    {
                        coverSnippet = commentWithLetter.Replace("Cover letter:", "").Replace("Lời nhắn ứng viên:", "").Trim();
                    }
                }
            }

            return new CandidateListViewModel
            {
                Id = c.Id,
                FullName = $"{c.FirstName} {c.LastName}".Trim(),
                Email = c.Email,
                Phone = c.Phone,
                AvatarUrl = c.User?.AvatarUrl,
                CurrentTitle = c.CurrentTitle,
                CurrentCompany = c.CurrentCompany,
                ApplicationCount = apps.Count,
                LatestJobTitle = latestApp?.JobPosting?.Title,
                LatestJobPostingId = latestApp?.JobPostingId,
                LatestStage = latestApp?.Stage,
                LatestStageName = latestApp?.CurrentStage?.Name ?? (latestApp != null ? latestApp.Stage.GetDisplayName() : null),
                LatestStageColor = latestApp?.CurrentStage?.ColorCode ?? "#2563EB",
                Source = c.Source,
                Rating = latestApp?.Rating,
                CoverLetterSnippet = coverSnippet,
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

        vm.PagedResult = new PagedResult<CandidateListViewModel>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };

        // 6. Thống kê KPI phục vụ Recruiter
        var baseCandidatesQuery = _dbContext.Candidates.Where(c => !c.IsDeleted);

        var firstDayCurrentMonthVn = new DateTime(nowVn.Year, nowVn.Month, 1);
        var firstDayLastMonthVn = firstDayCurrentMonthVn.AddMonths(-1);
        var lastDayLastMonthVn = firstDayCurrentMonthVn.AddDays(-1);
        var startLastMonthUtc = new DateTimeOffset(firstDayLastMonthVn, TimeSpan.FromHours(7)).ToUniversalTime();
        var endLastMonthUtc = new DateTimeOffset(lastDayLastMonthVn.Year, lastDayLastMonthVn.Month, lastDayLastMonthVn.Day, 23, 59, 59, 999, TimeSpan.FromHours(7)).ToUniversalTime();

        vm.LastMonthCandidatesCount = await baseCandidatesQuery
            .CountAsync(c => c.Applications.Any(a => !a.IsDeleted && (a.AppliedAt ?? a.CreatedAt) >= startLastMonthUtc && (a.AppliedAt ?? a.CreatedAt) <= endLastMonthUtc), cancellationToken);

        vm.HighRatingCandidatesCount = await baseCandidatesQuery
            .CountAsync(c => c.Applications.Any(a => !a.IsDeleted && a.Rating.HasValue && a.Rating.Value >= 4), cancellationToken);

        vm.MultiAppCandidatesCount = await baseCandidatesQuery
            .CountAsync(c => c.Applications.Count(a => !a.IsDeleted) > 1, cancellationToken);

        vm.TaggedCandidatesCount = await baseCandidatesQuery
            .CountAsync(c => c.Tags.Any(t => !t.IsDeleted), cancellationToken);

        // 7. Lấy danh sách bộ lọc đã lưu của Recruiter
        if (currentUserId != Guid.Empty)
        {
            vm.SavedFilters = (await GetSavedFiltersAsync(currentUserId, cancellationToken)).ToList();
        }

        return vm;
    }

    public async Task<SavedCandidateFilterDto> SaveFilterAsync(
        Guid currentUserId,
        SaveCandidateFilterInputModel model,
        CancellationToken cancellationToken = default)
    {
        var cleanName = model.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(cleanName))
        {
            throw new ArgumentException("Tên bộ lọc không được để trống.", nameof(model));
        }

        if (currentUserId == Guid.Empty)
        {
            throw new InvalidOperationException("Không xác định được danh tính người dùng.");
        }

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };

        var criteriaJson = JsonSerializer.Serialize(model.Criteria, jsonOptions);

        // Nếu đặt làm mặc định thì bỏ cờ default của các filter khác
        if (model.IsDefault)
        {
            var oldDefaults = await _dbContext.SavedCandidateFilters
                .Where(f => f.UserId == currentUserId && f.IsDefault && !f.IsDeleted)
                .ToListAsync(cancellationToken);

            foreach (var od in oldDefaults)
            {
                od.IsDefault = false;
                od.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        // Kiểm tra xem đã có bộ lọc trùng tên của user chưa
        var existing = await _dbContext.SavedCandidateFilters
            .FirstOrDefaultAsync(f => f.UserId == currentUserId && f.Name.ToLower() == cleanName.ToLower() && !f.IsDeleted, cancellationToken);

        if (existing != null)
        {
            existing.CriteriaJson = criteriaJson;
            existing.Description = model.Description?.Trim();
            existing.IsDefault = model.IsDefault;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            existing.UpdatedById = currentUserId;

            await _dbContext.SaveChangesAsync(cancellationToken);

            return new SavedCandidateFilterDto
            {
                Id = existing.Id,
                UserId = existing.UserId,
                Name = existing.Name,
                Description = existing.Description,
                Criteria = model.Criteria,
                IsDefault = existing.IsDefault,
                CreatedAt = existing.CreatedAt
            };
        }

        var savedFilter = new SavedCandidateFilter
        {
            Id = Guid.NewGuid(),
            UserId = currentUserId,
            Name = cleanName,
            Description = model.Description?.Trim(),
            CriteriaJson = criteriaJson,
            IsDefault = model.IsDefault,
            CreatedById = currentUserId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await _dbContext.SavedCandidateFilters.AddAsync(savedFilter, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Recruiter {UserId} đã lưu bộ lọc tìm kiếm mới '{FilterName}' (Id: {FilterId})",
            currentUserId, cleanName, savedFilter.Id);

        return new SavedCandidateFilterDto
        {
            Id = savedFilter.Id,
            UserId = savedFilter.UserId,
            Name = savedFilter.Name,
            Description = savedFilter.Description,
            Criteria = model.Criteria,
            IsDefault = savedFilter.IsDefault,
            CreatedAt = savedFilter.CreatedAt
        };
    }

    public async Task<IReadOnlyList<SavedCandidateFilterDto>> GetSavedFiltersAsync(
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        if (currentUserId == Guid.Empty) return [];

        var list = await _dbContext.SavedCandidateFilters
            .AsNoTracking()
            .Where(f => f.UserId == currentUserId && !f.IsDeleted)
            .OrderByDescending(f => f.IsDefault)
            .ThenByDescending(f => f.CreatedAt)
            .ToListAsync(cancellationToken);

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        return list.Select(f =>
        {
            CandidateFilterCriteria criteria;
            try
            {
                criteria = JsonSerializer.Deserialize<CandidateFilterCriteria>(f.CriteriaJson, jsonOptions) ?? new CandidateFilterCriteria();
            }
            catch
            {
                criteria = new CandidateFilterCriteria();
            }

            return new SavedCandidateFilterDto
            {
                Id = f.Id,
                UserId = f.UserId,
                Name = f.Name,
                Description = f.Description,
                Criteria = criteria,
                IsDefault = f.IsDefault,
                CreatedAt = f.CreatedAt
            };
        }).ToList();
    }

    public async Task<bool> DeleteSavedFilterAsync(
        Guid filterId,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var filter = await _dbContext.SavedCandidateFilters
            .FirstOrDefaultAsync(f => f.Id == filterId && !f.IsDeleted, cancellationToken);

        if (filter == null) return false;

        // Chỉ chủ nhân hoặc Admin mới được xóa
        if (filter.UserId != currentUserId)
        {
            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == currentUserId, cancellationToken);
            if (user?.Role != UserRoles.Admin)
            {
                return false;
            }
        }

        filter.IsDeleted = true;
        filter.DeletedAt = DateTimeOffset.UtcNow;
        filter.UpdatedAt = DateTimeOffset.UtcNow;
        filter.UpdatedById = currentUserId;

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Đã xóa bộ lọc tìm kiếm {FilterId} của Recruiter {UserId}", filterId, currentUserId);
        return true;
    }

    public async Task<byte[]> GenerateCandidateExcelTemplateAsync(CancellationToken cancellationToken = default)
    {
        using var workbook = new XLWorkbook();

        var openJobs = await _dbContext.JobPostings
            .Where(j => !j.IsDeleted && j.Status == JobPostingStatus.PUBLISHED)
            .OrderBy(j => j.Title)
            .Select(j => j.Title)
            .ToListAsync(cancellationToken);
        if (!openJobs.Any())
        {
            openJobs = new List<string> { "Senior Software Engineer (.NET & React)", "AI Engineer / MLOps", "Product Owner", "QA Automation Engineer" };
        }

        var departments = await _dbContext.Departments
            .Where(d => d.IsActive)
            .OrderBy(d => d.Name)
            .Select(d => d.Name)
            .ToListAsync(cancellationToken);
        if (!departments.Any())
        {
            departments = new List<string> { "Phòng Công Nghệ Thông Tin", "Phòng Nhân Sự", "Phòng Kinh Doanh" };
        }

        var headerBgColor = XLColor.FromArgb(5, 150, 105); // #059669 (Emerald Green NoveraTech)
        var borderColor = XLColor.FromArgb(209, 213, 219);

        // Sheet 1: Hướng dẫn & Quy định
        var instructionSheet = workbook.Worksheets.Add("Hướng dẫn & Quy định");
        instructionSheet.ShowGridLines = true;

        instructionSheet.Cell(1, 1).Value = "HƯỚNG DẪN NHẬP DANH SÁCH ỨNG VIÊN HÀNG LOẠT (HỆ THỐNG ATS NOVERATECH)";
        instructionSheet.Cell(1, 1).Style.Font.Bold = true;
        instructionSheet.Cell(1, 1).Style.Font.FontSize = 14;
        instructionSheet.Cell(1, 1).Style.Font.FontColor = headerBgColor;

        instructionSheet.Cell(3, 1).Value = "Cột dữ liệu";
        instructionSheet.Cell(3, 2).Value = "Bắt buộc?";
        instructionSheet.Cell(3, 3).Value = "Quy cách chuẩn";
        instructionSheet.Cell(3, 4).Value = "Ví dụ mẫu";

        var ruleHeaders = instructionSheet.Range(3, 1, 3, 4);
        ruleHeaders.Style.Font.Bold = true;
        ruleHeaders.Style.Fill.BackgroundColor = headerBgColor;
        ruleHeaders.Style.Font.FontColor = XLColor.White;
        ruleHeaders.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        instructionSheet.Row(3).Height = 24;

        var rules = new[]
        {
            ("Họ và tên (*)", "Bắt buộc (*)", "Độ dài từ 2 đến 100 ký tự. Tên đầy đủ ứng viên.", "Nguyễn Văn An"),
            ("Email (*)", "Bắt buộc (*)", "Email liên hệ của ứng viên. Không được trùng lặp trong tệp.", "an.nguyen.candidate@gmail.com"),
            ("Số điện thoại (*)", "Bắt buộc (*)", "Định dạng 10 số điện thoại di động Việt Nam (vd: 0912345678 hoặc +84912345678).", "0912345678"),
            ("Vị trí ứng tuyển", "Tùy chọn", "Tên vị trí tuyển dụng có trong hệ thống (Xem sheet 'Danh mục tham chiếu').", openJobs.First()),
            ("Năm kinh nghiệm", "Tùy chọn", "Số nguyên từ 0 đến 40.", "3"),
            ("Kỹ năng chính", "Tùy chọn", "Các từ khóa kỹ năng cách nhau dấu phẩy.", "C#, .NET 9, Docker, PostgreSQL"),
            ("Ghi chú", "Tùy chọn", "Ghi chú chuyên môn hoặc nguồn ứng viên.", "Ứng viên có kinh nghiệm thực chiến tốt")
        };

        for (int r = 0; r < rules.Length; r++)
        {
            int rowIdx = 4 + r;
            instructionSheet.Cell(rowIdx, 1).Value = rules[r].Item1;
            instructionSheet.Cell(rowIdx, 1).Style.Font.Bold = true;
            instructionSheet.Cell(rowIdx, 2).Value = rules[r].Item2;
            instructionSheet.Cell(rowIdx, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            if (rules[r].Item2.Contains('*')) instructionSheet.Cell(rowIdx, 2).Style.Font.FontColor = XLColor.Red;
            instructionSheet.Cell(rowIdx, 3).Value = rules[r].Item3;
            instructionSheet.Cell(rowIdx, 4).Value = rules[r].Item4;
        }

        var ruleRange = instructionSheet.Range(3, 1, 3 + rules.Length, 4);
        ruleRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        ruleRange.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
        ruleRange.Style.Border.InsideBorderColor = borderColor;
        instructionSheet.Columns().AdjustToContents();

        // Sheet 2: Dữ liệu mẫu & nhập liệu
        var dataSheet = workbook.Worksheets.Add("Dữ liệu");
        dataSheet.ShowGridLines = true;

        var headers = new string[] { "Họ và tên (*)", "Email (*)", "Số điện thoại (*)", "Vị trí ứng tuyển", "Năm kinh nghiệm", "Kỹ năng chính", "Ghi chú" };
        for (int i = 0; i < headers.Length; i++)
        {
            var cell = dataSheet.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = headerBgColor;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.OutsideBorderColor = borderColor;
        }
        dataSheet.Row(1).Height = 28;

        dataSheet.Column(3).Style.NumberFormat.Format = "@"; // SĐT dạng Text

        var sampleRows = new[]
        {
            new { Name = "Nguyễn Văn An", Email = "an.nguyen.candidate@gmail.com", Phone = "0912345678", Job = openJobs.ElementAtOrDefault(0) ?? "Senior Software Engineer", Exp = "4", Skills = ".NET, C#, SQL, Azure", Notes = "Kinh nghiệm 4 năm fintech" },
            new { Name = "Trần Thị Mai", Email = "mai.tran.dev@gmail.com", Phone = "0987654321", Job = openJobs.ElementAtOrDefault(1) ?? "AI Engineer", Exp = "2", Skills = "Python, PyTorch, FastAPI", Notes = "Tốt nghiệp ĐHBK" },
            new { Name = "Lê Hoàng Nam", Email = "nam.le.candidate@outlook.com", Phone = "0933445566", Job = openJobs.ElementAtOrDefault(0) ?? "QA Engineer", Exp = "3", Skills = "Selenium, Playwright, C#", Notes = "Tiếng Anh giao tiếp tốt" }
        };

        for (int i = 0; i < sampleRows.Length; i++)
        {
            var row = sampleRows[i];
            int r = i + 2;
            dataSheet.Cell(r, 1).Value = row.Name;
            dataSheet.Cell(r, 2).Value = row.Email;
            dataSheet.Cell(r, 3).SetValue(row.Phone);
            dataSheet.Cell(r, 4).Value = row.Job;
            dataSheet.Cell(r, 5).Value = row.Exp;
            dataSheet.Cell(r, 6).Value = row.Skills;
            dataSheet.Cell(r, 7).Value = row.Notes;

            for (int c = 1; c <= headers.Length; c++)
            {
                dataSheet.Cell(r, c).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                dataSheet.Cell(r, c).Style.Border.OutsideBorderColor = borderColor;
            }
        }

        dataSheet.Columns().AdjustToContents();

        // Sheet 3: Danh mục tham chiếu
        var refSheet = workbook.Worksheets.Add("Danh mục tham chiếu");
        refSheet.ShowGridLines = true;
        refSheet.Cell(1, 1).Value = "Vị trí tuyển dụng đang mở";
        refSheet.Cell(1, 2).Value = "Phòng ban";
        var refHeaders = refSheet.Range(1, 1, 1, 2);
        refHeaders.Style.Font.Bold = true;
        refHeaders.Style.Fill.BackgroundColor = headerBgColor;
        refHeaders.Style.Font.FontColor = XLColor.White;

        for (int i = 0; i < openJobs.Count; i++)
        {
            refSheet.Cell(i + 2, 1).Value = openJobs[i];
        }
        for (int i = 0; i < departments.Count; i++)
        {
            refSheet.Cell(i + 2, 2).Value = departments[i];
        }
        refSheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        var fileBytes = stream.ToArray();

        // Best effort lưu bản sao tĩnh vào wwwroot/templates/Mau_Nhap_Ung_Vien.xlsx
        try
        {
            var contentRoot = Directory.GetCurrentDirectory();
            var templatesDir = Path.Combine(contentRoot, "wwwroot", "templates");
            if (!Directory.Exists(templatesDir))
            {
                templatesDir = Path.Combine(contentRoot, "src", "Ats.Web", "wwwroot", "templates");
            }
            if (Directory.Exists(templatesDir))
            {
                var physicalFilePath = Path.Combine(templatesDir, "Mau_Nhap_Ung_Vien.xlsx");
                File.WriteAllBytes(physicalFilePath, fileBytes);
            }
        }
        catch { /* best effort */ }

        return fileBytes;
    }

    public async Task<ImportCandidateResultDto> ValidateCandidateExcelImportAsync(Stream excelStream, CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        var result = new ImportCandidateResultDto();

        using var workbook = new XLWorkbook(excelStream);
        var worksheet = workbook.Worksheets.FirstOrDefault(ws => ws.Name == "Dữ liệu") ?? workbook.Worksheet(1);

        static string NormalizeHeader(string header) => header.Replace("(*)", "").Replace("*", "").Trim().ToLowerInvariant();

        var expectedHeaders = new[] { "Họ và tên (*)", "Email (*)", "Số điện thoại (*)" };
        var expectedNormalized = new[] { "họ và tên", "email", "số điện thoại" };
        var headerRow = worksheet.Row(1);
        for (int i = 0; i < expectedNormalized.Length; i++)
        {
            var cellValue = headerRow.Cell(i + 1).GetString().Trim();
            var normalizedVal = NormalizeHeader(cellValue);
            if (!normalizedVal.Equals(expectedNormalized[i], StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Cấu trúc cột không hợp lệ. Cột thứ {i + 1} phải là '{expectedHeaders[i]}' (Tìm thấy '{cellValue}'). Vui lòng sử dụng tệp mẫu chuẩn.");
            }
        }

        var rows = worksheet.RowsUsed().Skip(1); // Bỏ qua header
        var emailRegex = new Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.IgnoreCase);
        var phoneRegex = new Regex(@"^(?:\+?84|0)(?:3|5|7|8|9)\d{8}$");

        var emailsInFile = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            int rowIndex = row.RowNumber();
            var errors = new List<ImportCandidateErrorDetailDto>();

            var name = row.Cell(1).GetString().Trim();
            var email = row.Cell(2).GetString().Trim();
            var phone = row.Cell(3).GetString().Trim();
            var jobTitle = row.Cell(4).GetString().Trim();
            var expStr = row.Cell(5).GetString().Trim();
            var skills = row.Cell(6).GetString().Trim();
            var notes = row.Cell(7).GetString().Trim();

            // Nếu toàn bộ dòng rỗng thì bỏ qua
            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(phone))
            {
                continue;
            }

            // 1. Kiểm tra Họ và tên
            if (string.IsNullOrWhiteSpace(name))
            {
                errors.Add(new ImportCandidateErrorDetailDto { ColumnName = "Họ và tên", ErrorMessage = "Họ và tên không được để trống." });
            }
            else if (name.Length < 2 || name.Length > 100)
            {
                errors.Add(new ImportCandidateErrorDetailDto { ColumnName = "Họ và tên", ErrorMessage = "Độ dài họ và tên phải từ 2 đến 100 ký tự." });
            }

            // 2. Kiểm tra Email
            if (string.IsNullOrWhiteSpace(email))
            {
                errors.Add(new ImportCandidateErrorDetailDto { ColumnName = "Email", ErrorMessage = "Email không được để trống." });
            }
            else if (!emailRegex.IsMatch(email))
            {
                errors.Add(new ImportCandidateErrorDetailDto { ColumnName = "Email", ErrorMessage = "Email không đúng định dạng." });
            }
            else if (emailsInFile.TryGetValue(email, out var duplicateRowIndex))
            {
                errors.Add(new ImportCandidateErrorDetailDto { ColumnName = "Email", ErrorMessage = $"Email bị trùng lặp với dòng số {duplicateRowIndex} trong tệp." });
            }
            else
            {
                emailsInFile[email] = rowIndex;
            }

            // 3. Kiểm tra Số điện thoại
            var cleanedPhone = new string(phone.Where(char.IsDigit).ToArray());
            if (cleanedPhone.StartsWith("84") && cleanedPhone.Length == 11)
            {
                cleanedPhone = "0" + cleanedPhone[2..];
            }

            if (string.IsNullOrWhiteSpace(phone))
            {
                errors.Add(new ImportCandidateErrorDetailDto { ColumnName = "Số điện thoại", ErrorMessage = "Số điện thoại không được để trống." });
            }
            else if (!phoneRegex.IsMatch(cleanedPhone))
            {
                errors.Add(new ImportCandidateErrorDetailDto { ColumnName = "Số điện thoại", ErrorMessage = "Số điện thoại không đúng định dạng di động Việt Nam (10 chữ số, đầu số 03, 05, 07, 08, 09)." });
            }

            // 4. Kiểm tra Năm kinh nghiệm
            int? expYears = null;
            if (!string.IsNullOrWhiteSpace(expStr))
            {
                if (int.TryParse(expStr, out var parsedExp) && parsedExp is >= 0 and <= 40)
                {
                    expYears = parsedExp;
                }
                else
                {
                    errors.Add(new ImportCandidateErrorDetailDto { ColumnName = "Năm kinh nghiệm", ErrorMessage = "Năm kinh nghiệm phải là số nguyên từ 0 đến 40." });
                }
            }

            if (errors.Any())
            {
                result.ErrorRows.Add(new ImportCandidateErrorRowDto { RowIndex = rowIndex, Errors = errors });
            }
            else
            {
                result.ValidRows.Add(new ImportCandidateRowDto
                {
                    RowIndex = rowIndex,
                    FullName = name,
                    Email = email,
                    PhoneNumber = cleanedPhone,
                    TargetJobTitle = string.IsNullOrWhiteSpace(jobTitle) ? null : jobTitle,
                    ExperienceYears = expYears,
                    Skills = string.IsNullOrWhiteSpace(skills) ? null : skills,
                    Notes = string.IsNullOrWhiteSpace(notes) ? null : notes
                });
            }
        }

        return result;
    }

    public async Task<int> ExecuteCandidateImportAsync(ExecuteCandidateImportRequestDto request, Guid currentUserId, CancellationToken cancellationToken = default)
    {
        if (request.ValidRows == null || !request.ValidRows.Any())
        {
            return 0;
        }

        var successCount = 0;
        var initialStage = await _dbContext.PipelineStages.OrderBy(s => s.StageOrder).FirstOrDefaultAsync(cancellationToken);

        foreach (var r in request.ValidRows)
        {
            var emailClean = r.Email.Trim().ToLowerInvariant();
            var candidate = await _dbContext.Candidates
                .Include(c => c.Applications)
                .FirstOrDefaultAsync(c => c.Email.ToLower() == emailClean && !c.IsDeleted, cancellationToken);

            if (candidate == null)
            {
                candidate = new Candidate
                {
                    Id = Guid.NewGuid(),
                    FirstName = r.FullName.Trim(),
                    LastName = string.Empty,
                    Email = emailClean,
                    Phone = r.PhoneNumber.Trim(),
                    Source = CandidateSource.OTHER,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                await _dbContext.Candidates.AddAsync(candidate, cancellationToken);
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(r.PhoneNumber)) candidate.Phone = r.PhoneNumber.Trim();
                candidate.UpdatedAt = DateTimeOffset.UtcNow;
            }

            // Gán vào vị trí tuyển dụng nếu có
            Guid? targetJobId = request.JobPostingId;
            if (!targetJobId.HasValue && !string.IsNullOrWhiteSpace(r.TargetJobTitle))
            {
                var matchedJob = await _dbContext.JobPostings
                    .FirstOrDefaultAsync(j => j.Title.ToLower() == r.TargetJobTitle.ToLower().Trim() && !j.IsDeleted, cancellationToken);
                targetJobId = matchedJob?.Id;
            }

            if (targetJobId.HasValue && initialStage != null)
            {
                var hasApp = await _dbContext.Applications
                    .AnyAsync(a => a.CandidateId == candidate.Id && a.JobPostingId == targetJobId.Value && !a.IsDeleted, cancellationToken);

                if (!hasApp)
                {
                    var resume = await _dbContext.Resumes.FirstOrDefaultAsync(rm => rm.CandidateId == candidate.Id && !rm.IsDeleted, cancellationToken);
                    if (resume == null)
                    {
                        resume = new Resume
                        {
                            Id = Guid.NewGuid(),
                            CandidateId = candidate.Id,
                            FileName = "ho_so_excel.txt",
                            FilePath = "uploads/resumes/excel_import.txt",
                            FileSize = 0,
                            MimeType = "text/plain",
                            ParsedText = $"Dữ liệu ứng viên nhập từ tệp Excel: {r.FullName} - {r.Email} - {r.PhoneNumber}. Kỹ năng: {r.Skills}. Ghi chú: {r.Notes}",
                            IsPrimary = true,
                            CreatedAt = DateTimeOffset.UtcNow,
                            UpdatedAt = DateTimeOffset.UtcNow
                        };
                        await _dbContext.Resumes.AddAsync(resume, cancellationToken);
                    }

                    var application = new Application
                    {
                        Id = Guid.NewGuid(),
                        CandidateId = candidate.Id,
                        JobPostingId = targetJobId.Value,
                        ResumeId = resume.Id,
                        CurrentStageId = initialStage.Id,
                        Status = ApplicationStatus.IN_PROCESS,
                        Stage = CandidateStage.APPLIED,
                        TrackingCode = $"#EXCEL-{Guid.NewGuid().ToString("N")[..6].ToUpper()}",
                        ReferralNote = r.Notes,
                        AppliedAt = DateTimeOffset.UtcNow,
                        CreatedAt = DateTimeOffset.UtcNow,
                        UpdatedAt = DateTimeOffset.UtcNow
                    };
                    await _dbContext.Applications.AddAsync(application, cancellationToken);
                }
            }

            // Thêm tags kỹ năng
            if (!string.IsNullOrWhiteSpace(r.Skills))
            {
                var skillsList = r.Skills.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var sk in skillsList.Take(3))
                {
                    var tagClean = sk.Trim();
                    if (tagClean.Length > 0 && tagClean.Length <= 30)
                    {
                        var hasTag = await _dbContext.CandidateTags
                            .AnyAsync(t => t.CandidateId == candidate.Id && t.TagName.ToLower() == tagClean.ToLower(), cancellationToken);
                        if (!hasTag)
                        {
                            await _dbContext.CandidateTags.AddAsync(new CandidateTag
                            {
                                Id = Guid.NewGuid(),
                                CandidateId = candidate.Id,
                                TagName = tagClean,
                                ColorCode = "#059669",
                                CreatedByUserId = currentUserId != Guid.Empty ? currentUserId : (await _dbContext.Users.Select(u => u.Id).FirstAsync(cancellationToken)),
                                CreatedAt = DateTimeOffset.UtcNow
                            }, cancellationToken);
                        }
                    }
                }
            }

            // Thêm ghi chú
            if (!string.IsNullOrWhiteSpace(r.Notes))
            {
                await _dbContext.CandidateNotes.AddAsync(new CandidateNote
                {
                    Id = Guid.NewGuid(),
                    CandidateId = candidate.Id,
                    AuthorUserId = currentUserId != Guid.Empty ? currentUserId : (await _dbContext.Users.Select(u => u.Id).FirstAsync(cancellationToken)),
                    Content = $"[Nhập Excel]: {r.Notes.Trim()}",
                    NoteType = "GENERAL",
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                }, cancellationToken);
            }

            successCount++;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return successCount;
    }

    // ==========================================
    // SCRUM-47: THÊM ỨNG VIÊN THỦ CÔNG VÀO VỊ TRÍ
    // ==========================================

    /// <inheritdoc />
    public async Task<CandidateDuplicateCheckResultViewModel> CheckDuplicateCandidateAsync(
        string email,
        string? phoneNumber,
        Guid jobPostingId,
        CancellationToken cancellationToken = default)
    {
        var result = new CandidateDuplicateCheckResultViewModel();
        var cleanEmail = (email ?? string.Empty).Trim().ToLowerInvariant();
        var cleanPhone = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim();
        var normalizedPhone = NormalizePhoneNumber(cleanPhone);

        if (string.IsNullOrWhiteSpace(cleanEmail) && string.IsNullOrWhiteSpace(cleanPhone))
        {
            result.IsDuplicate = false;
            result.Message = "Không có thông tin email hoặc số điện thoại để kiểm tra trùng lặp.";
            return result;
        }

        // 1. Tìm kiếm ứng viên trong cơ sở dữ liệu theo Email hoặc Số điện thoại
        var candidates = await _dbContext.Candidates
            .AsNoTracking()
            .Where(c => !c.IsDeleted && (
                (!string.IsNullOrEmpty(cleanEmail) && c.Email.ToLower() == cleanEmail) ||
                (!string.IsNullOrEmpty(cleanPhone) && c.Phone != null && c.Phone != "")))
            .ToListAsync(cancellationToken);

        Candidate? matchedCandidate = null;
        var matchedByEmail = false;
        var matchedByPhone = false;

        if (!string.IsNullOrEmpty(cleanEmail))
        {
            matchedCandidate = candidates.FirstOrDefault(c => c.Email.Trim().ToLowerInvariant() == cleanEmail);
            if (matchedCandidate != null)
            {
                matchedByEmail = true;
            }
        }

        if (!string.IsNullOrEmpty(normalizedPhone))
        {
            var phoneCandidate = candidates.FirstOrDefault(c => NormalizePhoneNumber(c.Phone) == normalizedPhone);
            if (phoneCandidate != null)
            {
                matchedByPhone = true;
                matchedCandidate ??= phoneCandidate;
            }
        }

        if (matchedCandidate == null)
        {
            result.IsDuplicate = false;
            result.Message = "Hồ sơ mới, không phát hiện trùng lặp thông tin với ứng viên nào trong hệ thống.";
            return result;
        }

        // Nếu ứng viên đã bị gộp vào hồ sơ chính (Scrum #40), lấy hồ sơ chính
        if (matchedCandidate.IsMerged && matchedCandidate.MergedIntoCandidateId.HasValue)
        {
            var primaryCandidate = await _dbContext.Candidates
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == matchedCandidate.MergedIntoCandidateId.Value && !c.IsDeleted, cancellationToken);
            if (primaryCandidate != null)
            {
                matchedCandidate = primaryCandidate;
            }
        }

        result.IsDuplicate = true;
        result.ExistingCandidateId = matchedCandidate.Id;
        result.CandidateFullName = $"{matchedCandidate.FirstName} {matchedCandidate.LastName}".Trim();
        result.CandidateEmail = matchedCandidate.Email;
        result.CandidatePhone = matchedCandidate.Phone;

        if (matchedByEmail && matchedByPhone)
        {
            result.MatchedBy = "Email và Số điện thoại";
        }
        else if (matchedByEmail)
        {
            result.MatchedBy = "Email";
        }
        else
        {
            result.MatchedBy = "Số điện thoại";
        }

        // 2. Kiểm tra xem ứng viên này đã có đơn ứng tuyển (Application) ở chính JobPostingId này chưa
        if (jobPostingId != Guid.Empty)
        {
            var existingApplications = await _dbContext.Applications
                .AsNoTracking()
                .Include(a => a.JobPosting)
                .Where(a => !a.IsDeleted && a.CandidateId == matchedCandidate.Id && a.JobPostingId == jobPostingId)
                .OrderByDescending(a => a.CreatedAt)
                .ToListAsync(cancellationToken);

            var activeApp = existingApplications.FirstOrDefault(a => a.Status == ApplicationStatus.IN_PROCESS);
            if (activeApp != null)
            {
                result.ExistingApplicationId = activeApp.Id;
                result.HasActiveApplicationInSameJob = true;
                result.Message = $"Ứng viên '{result.CandidateFullName}' ({result.CandidateEmail}) đã có hồ sơ đang xử lý (IN_PROCESS) cho vị trí này (Mã đơn: {activeApp.TrackingCode ?? activeApp.Id.ToString()}). Không được phép thêm hồ sơ trùng lặp.";
                return result;
            }

            if (existingApplications.Count > 0)
            {
                var pastApp = existingApplications.First();
                result.ExistingApplicationId = pastApp.Id;
                result.HasActiveApplicationInSameJob = false;
                result.Message = $"Ứng viên '{result.CandidateFullName}' đã có lịch sử ứng tuyển vị trí này trước đây (Trạng thái: {pastApp.Status}). Hệ thống sẽ liên kết và tạo đơn ứng tuyển mới cho đợt này.";
                return result;
            }
        }

        result.HasActiveApplicationInSameJob = false;
        result.Message = $"Phát hiện ứng viên '{result.CandidateFullName}' đã tồn tại trong hệ thống (trùng khớp qua {result.MatchedBy}). Hệ thống sẽ tự động liên kết hồ sơ ứng viên này vào vị trí tuyển dụng mới.";
        return result;
    }

    /// <inheritdoc />
    public async Task<Guid> AddManualCandidateAsync(
        CandidateManualCreateInputModel input,
        string recruiterUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (string.IsNullOrWhiteSpace(recruiterUserId) || !Guid.TryParse(recruiterUserId, out var recruiterGuid) || recruiterGuid == Guid.Empty)
        {
            throw new ArgumentException("Mã định danh người dùng thực hiện không hợp lệ.", nameof(recruiterUserId));
        }

        // 1. Kiểm tra quyền Recruiter / Admin / HRManager
        var user = await _dbContext.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .Include(u => u.RoleEntity)
            .FirstOrDefaultAsync(u => u.Id == recruiterGuid && u.Status == "ACTIVE", cancellationToken)
            ?? throw new KeyNotFoundException($"Không tìm thấy tài khoản người dùng với mã: {recruiterUserId}");

        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(user.Role)) roles.Add(user.Role);
        if (user.RoleEntity != null) roles.Add(user.RoleEntity.Code.ToString());
        foreach (var ur in user.UserRoles.Where(x => x.Role != null))
        {
            roles.Add(ur.Role.Code.ToString());
            if (!string.IsNullOrWhiteSpace(ur.Role.Name)) roles.Add(ur.Role.Name);
        }

        var isAuthorized = roles.Any(r =>
            string.Equals(r, UserRoles.Recruiter, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(r, UserRoles.Admin, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(r, UserRoles.HRManager, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(r, "RECRUITER", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(r, "ADMIN", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(r, "HRMANAGER", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(r, "HR_MANAGER", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(r, "Chuyên viên tuyển dụng", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(r, "Quản lý nhân sự", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(r, "Quản trị viên", StringComparison.OrdinalIgnoreCase));

        if (!isAuthorized)
        {
            throw new UnauthorizedAccessException("Bạn không có quyền thêm ứng viên thủ công. Chức năng chỉ dành cho Nhân viên tuyển dụng (Recruiter), Quản lý nhân sự (HRManager) hoặc Quản trị viên (Admin).");
        }

        // 2. Kiểm tra JobPosting tồn tại và đang mở (PUBLISHED)
        var jobPosting = await _dbContext.JobPostings
            .Include(jp => jp.Requisition)
            .FirstOrDefaultAsync(jp => jp.Id == input.JobPostingId && !jp.IsDeleted, cancellationToken)
            ?? throw new KeyNotFoundException($"Không tìm thấy tin tuyển dụng với mã: {input.JobPostingId}");

        if (jobPosting.Status != JobPostingStatus.PUBLISHED)
        {
            throw new InvalidOperationException($"Tin tuyển dụng '{jobPosting.Title}' hiện không ở trạng thái mở nhận hồ sơ (PUBLISHED). Trạng thái hiện tại: {jobPosting.Status}.");
        }

        // 3. Chạy luồng kiểm tra trùng
        var duplicateCheck = await CheckDuplicateCandidateAsync(input.Email, input.PhoneNumber, input.JobPostingId, cancellationToken);
        if (duplicateCheck.HasActiveApplicationInSameJob)
        {
            throw new InvalidOperationException(
                $"Ứng viên '{input.FullName}' ({input.Email}) đã có hồ sơ đang xử lý trong vị trí này (Mã hồ sơ: {duplicateCheck.ExistingApplicationId}). Không được phép thêm hồ sơ trùng lặp.");
        }

        // 4. Mở Database Transaction tường minh
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var nowUtc = DateTimeOffset.UtcNow;
            Candidate candidate;
            var isExistingCandidate = false;

            if (duplicateCheck.IsDuplicate && duplicateCheck.ExistingCandidateId.HasValue)
            {
                candidate = await _dbContext.Candidates
                    .Include(c => c.Resumes)
                    .FirstOrDefaultAsync(c => c.Id == duplicateCheck.ExistingCandidateId.Value, cancellationToken)
                    ?? throw new InvalidOperationException("Không thể tìm thấy thông tin ứng viên trùng khớp.");

                isExistingCandidate = true;

                // Bổ sung thông tin nếu còn thiếu
                if (string.IsNullOrWhiteSpace(candidate.Phone) && !string.IsNullOrWhiteSpace(input.PhoneNumber))
                {
                    candidate.Phone = input.PhoneNumber.Trim();
                }
                if (string.IsNullOrWhiteSpace(candidate.LinkedinUrl) && !string.IsNullOrWhiteSpace(input.LinkedInUrl))
                {
                    candidate.LinkedinUrl = input.LinkedInUrl.Trim();
                }
                candidate.Source = input.Source;
                candidate.UpdatedAt = nowUtc;
                candidate.UpdatedById = recruiterGuid;
            }
            else
            {
                // Tách Họ và Tên từ FullName
                var trimmedName = input.FullName.Trim();
                var lastSpaceIdx = trimmedName.LastIndexOf(' ');
                string firstName, lastName;
                if (lastSpaceIdx > 0)
                {
                    firstName = trimmedName[..lastSpaceIdx].Trim();
                    lastName = trimmedName[(lastSpaceIdx + 1)..].Trim();
                }
                else
                {
                    firstName = trimmedName;
                    lastName = string.Empty;
                }

                candidate = new Candidate
                {
                    Id = Guid.NewGuid(),
                    FirstName = firstName,
                    LastName = lastName,
                    Email = input.Email.Trim().ToLowerInvariant(),
                    Phone = input.PhoneNumber?.Trim(),
                    LinkedinUrl = input.LinkedInUrl?.Trim(),
                    Source = input.Source,
                    CreatedAt = nowUtc,
                    UpdatedAt = nowUtc,
                    CreatedById = recruiterGuid,
                    UpdatedById = recruiterGuid
                };

                await _dbContext.Candidates.AddAsync(candidate, cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            // 5. Lưu file CV an toàn
            Resume? resume = null;
            if (input.CvFile != null && input.CvFile.Length > 0)
            {
                var originalFileName = Path.GetFileName(input.CvFile.FileName);
                var savedPath = await _fileStorageService.SaveCvAsync(input.CvFile, $"cv_manual_{candidate.Id:N}", cancellationToken);

                var existingResumes = await _dbContext.Resumes
                    .Where(r => r.CandidateId == candidate.Id)
                    .ToListAsync(cancellationToken);

                foreach (var r in existingResumes)
                {
                    r.IsPrimary = false;
                }

                resume = new Resume
                {
                    Id = Guid.NewGuid(),
                    CandidateId = candidate.Id,
                    FileName = originalFileName,
                    FilePath = savedPath,
                    FileSize = input.CvFile.Length,
                    MimeType = input.CvFile.ContentType ?? "application/pdf",
                    IsPrimary = true,
                    CreatedAt = nowUtc,
                    UpdatedAt = nowUtc,
                    CreatedById = recruiterGuid,
                    UpdatedById = recruiterGuid
                };

                await _dbContext.Resumes.AddAsync(resume, cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            else
            {
                resume = await _dbContext.Resumes
                    .Where(r => r.CandidateId == candidate.Id && !r.IsDeleted)
                    .OrderByDescending(r => r.IsPrimary)
                    .ThenByDescending(r => r.CreatedAt)
                    .FirstOrDefaultAsync(cancellationToken);

                if (resume == null)
                {
                    throw new InvalidOperationException("Vui lòng đính kèm tệp CV cho ứng viên khi tạo mới hồ sơ.");
                }
            }

            // 6. Tạo Application mới trỏ vào JobPosting với trạng thái vòng đầu (APPLIED / SCREENING)
            var initialPipelineStage = await _dbContext.PipelineStages
                .OrderBy(s => s.StageOrder)
                .FirstOrDefaultAsync(cancellationToken)
                ?? await _dbContext.PipelineStages.FirstAsync(cancellationToken);

            var trackingCode = await _trackingService.GenerateUniqueTrackingCodeAsync(cancellationToken);

            var application = new Application
            {
                Id = Guid.NewGuid(),
                JobPostingId = jobPosting.Id,
                CandidateId = candidate.Id,
                ResumeId = resume.Id,
                CurrentStageId = initialPipelineStage.Id,
                Stage = CandidateStage.APPLIED,
                Status = ApplicationStatus.IN_PROCESS,
                TrackingCode = trackingCode,
                AppliedAt = nowUtc,
                StageEnteredAt = nowUtc,
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc,
                CreatedById = recruiterGuid,
                UpdatedById = recruiterGuid
            };

            await _dbContext.Applications.AddAsync(application, cancellationToken);

            // Lưu bản ghi lịch sử ApplicationStageHistory
            var appStageHistory = new ApplicationStageHistory
            {
                Id = Guid.NewGuid(),
                ApplicationId = application.Id,
                FromStageId = null,
                ToStageId = initialPipelineStage.Id,
                ChangedByUserId = recruiterGuid,
                Comment = $"Nhân viên tuyển dụng thêm ứng viên thủ công từ nguồn: {input.Source.GetDisplayName()}",
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc,
                CreatedById = recruiterGuid,
                UpdatedById = recruiterGuid
            };
            await _dbContext.ApplicationStageHistories.AddAsync(appStageHistory, cancellationToken);

            // Lưu bản ghi CandidateStageHistory (SCRUM-42)
            var candStageHistory = new CandidateStageHistory
            {
                Id = Guid.NewGuid(),
                ApplicationId = application.Id,
                FromStage = null,
                ToStage = CandidateStage.APPLIED,
                ChangedByUserId = recruiterGuid,
                Note = $"Thêm ứng viên thủ công từ nguồn: {input.Source.GetDisplayName()}",
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc,
                CreatedById = recruiterGuid,
                UpdatedById = recruiterGuid
            };
            await _dbContext.CandidateStageHistories.AddAsync(candStageHistory, cancellationToken);

            // 7. Thêm CandidateNote nếu có ghi chú sơ bộ ban đầu
            if (!string.IsNullOrWhiteSpace(input.Note))
            {
                var note = new CandidateNote
                {
                    Id = Guid.NewGuid(),
                    CandidateId = candidate.Id,
                    AuthorUserId = recruiterGuid,
                    Content = input.Note.Trim(),
                    NoteType = "RECRUITER_CALL",
                    CreatedAt = nowUtc,
                    UpdatedAt = nowUtc,
                    CreatedById = recruiterGuid,
                    UpdatedById = recruiterGuid
                };
                await _dbContext.CandidateNotes.AddAsync(note, cancellationToken);
            }

            // 8. Ghi AuditLog đầy đủ
            var jsonOptions = new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            var auditLog = new AuditLog
            {
                Id = Guid.NewGuid(),
                UserId = recruiterGuid,
                Action = "CANDIDATE_MANUAL_CREATE",
                EntityName = "Candidate",
                EntityId = candidate.Id.ToString(),
                OldValues = isExistingCandidate 
                    ? JsonSerializer.Serialize(new { CandidateId = candidate.Id, Status = "EXISTING_CANDIDATE_REUSED" }, jsonOptions) 
                    : null,
                NewValues = JsonSerializer.Serialize(new
                {
                    CandidateId = candidate.Id,
                    FullName = $"{candidate.FirstName} {candidate.LastName}".Trim(),
                    Email = candidate.Email,
                    Phone = candidate.Phone,
                    LinkedinUrl = candidate.LinkedinUrl,
                    Source = candidate.Source.ToString(),
                    ApplicationId = application.Id,
                    JobPostingId = jobPosting.Id,
                    JobTitle = jobPosting.Title,
                    ResumeId = resume.Id,
                    ResumeFileName = resume.FileName,
                    TrackingCode = trackingCode,
                    Stage = CandidateStage.APPLIED.ToString(),
                    Status = ApplicationStatus.IN_PROCESS.ToString(),
                    RecruiterUserId = recruiterGuid,
                    RecruiterName = user.FullName,
                    InitialNote = input.Note,
                    CreatedAt = nowUtc
                }, jsonOptions),
                CreatedAt = nowUtc
            };
            await _dbContext.AuditLogs.AddAsync(auditLog, cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            input.CreatedApplicationId = application.Id;

            _logger.LogInformation(
                "Thêm ứng viên thủ công thành công (SCRUM-47): CandidateId={CandidateId}, ApplicationId={ApplicationId}, JobPostingId={JobPostingId}, Recruiter={RecruiterName}",
                candidate.Id, application.Id, jobPosting.Id, user.FullName);

            return candidate.Id;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Lỗi xảy ra trong quá trình thêm ứng viên thủ công cho vị trí {JobPostingId}", input.JobPostingId);
            throw;
        }
    }

    private static string NormalizePhoneNumber(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return string.Empty;
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("84") && digits.Length >= 11)
        {
            digits = "0" + digits[2..];
        }
        return digits;
    }
}

