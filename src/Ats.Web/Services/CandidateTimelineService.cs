using Ats.Web.Data;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Models.ViewModels.CandidateTimeline;
using Ats.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Ats.Web.Services;

public class CandidateTimelineService : ICandidateTimelineService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<CandidateTimelineService> _logger;

    public CandidateTimelineService(
        ApplicationDbContext context,
        ILogger<CandidateTimelineService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<CandidateTimelineViewModel?> GetTimelineByApplicationIdAsync(
        Guid applicationId,
        string? filterType = null,
        string? searchQuery = null)
    {
        var application = await _context.Applications
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == applicationId);

        if (application == null)
        {
            return null;
        }

        return await GetCandidateTimelineAsync(
            application.CandidateId,
            applicationId,
            filterType,
            searchQuery);
    }

    public async Task<CandidateTimelineViewModel?> GetCandidateTimelineAsync(
        Guid candidateId,
        Guid? applicationId = null,
        string? filterType = null,
        string? searchQuery = null)
    {
        var candidate = await _context.Candidates
            .AsNoTracking()
            .Include(c => c.ReferrerUser)
            .FirstOrDefaultAsync(c => c.Id == candidateId);

        if (candidate == null)
        {
            return null;
        }

        // 1. Lay danh sach cac don ung tuyen cua ung vien
        var applications = await _context.Applications
            .AsNoTracking()
            .Include(a => a.JobPosting)
                .ThenInclude(jp => jp.JobPosition)
            .Include(a => a.CurrentStage)
            .Include(a => a.ReferrerUser)
            .Include(a => a.Resume)
            .Where(a => a.CandidateId == candidateId)
            .OrderByDescending(a => a.AppliedAt ?? a.CreatedAt)
            .ToListAsync();

        var appOptions = applications.Select(a => new CandidateApplicationOptionDto
        {
            ApplicationId = a.Id,
            JobTitle = a.JobPosting?.Title ?? "Vị trí tuyển dụng",
            AppliedAt = a.AppliedAt ?? a.CreatedAt,
            CurrentStageName = a.CurrentStage?.Name ?? "Chưa xác định",
            Status = a.Status.ToString()
        }).ToList();

        var appIds = applications.Select(a => a.Id).ToList();

        // Neu co chon application cu the
        string? selectedJobTitle = null;
        if (applicationId.HasValue)
        {
            var selectedApp = applications.FirstOrDefault(a => a.Id == applicationId.Value);
            if (selectedApp != null)
            {
                selectedJobTitle = selectedApp.JobPosting?.Title;
            }
        }

        var allEvents = new List<CandidateTimelineEventDto>();

        // 2. Thu thap cac loai su kien
        // 2.1. Nop ho so (APPLIED)
        foreach (var app in applications)
        {
            var occTime = app.AppliedAt ?? app.CreatedAt;
            var isReferral = app.ReferrerUser != null;
            var actor = isReferral ? app.ReferrerUser!.FullName : $"{candidate.FirstName} {candidate.LastName}".Trim();
            var actorRole = isReferral ? "Nhân viên nội bộ giới thiệu" : "Ứng viên tự nộp hồ sơ";
            var actorEmail = isReferral ? app.ReferrerUser!.Email : candidate.Email;

            var jobName = app.JobPosting?.Title ?? "Vị trí tuyển dụng";
            var resumeName = app.Resume?.FileName ?? "Hồ sơ đính kèm";
            var refNote = !string.IsNullOrWhiteSpace(app.ReferralNote) ? $" Ghi chú giới thiệu: \"{app.ReferralNote}\"." : "";

            allEvents.Add(new CandidateTimelineEventDto
            {
                EventId = app.Id,
                EventType = "APPLIED",
                EventCategory = "Hồ sơ ứng tuyển",
                Title = $"Nộp hồ sơ ứng tuyển: {jobName}",
                OccurredAt = occTime,
                ActorName = actor,
                ActorRole = actorRole,
                ActorEmail = actorEmail,
                Summary = $"Nộp hồ sơ thành công vào vị trí {jobName}",
                Details = $"Ứng viên đã nộp hồ sơ ứng tuyển cho vị trí {jobName}. CV đính kèm: {resumeName}.{refNote} Mã hồ sơ: {app.TrackingCode ?? app.Id.ToString()[..8].ToUpperInvariant()}.",
                JobTitle = jobName,
                StageName = app.CurrentStage?.Name,
                BadgeClass = "badge-subtle-slate",
                IconClass = "bi-file-earmark-person",
                Metadata = new()
                {
                    ["ApplicationId"] = app.Id.ToString(),
                    ["TrackingCode"] = app.TrackingCode ?? "",
                    ["ResumeFileName"] = resumeName
                }
            });
        }

        // 2.2. Chuyen giai doan (STAGE_CHANGE)
        if (appIds.Any())
        {
            var stageHistories = await _context.ApplicationStageHistories
                .AsNoTracking()
                .Include(h => h.ChangedByUser)
                .Include(h => h.FromStage)
                .Include(h => h.ToStage)
                .Include(h => h.Application)
                    .ThenInclude(a => a.JobPosting)
                .Where(h => appIds.Contains(h.ApplicationId))
                .ToListAsync();

            foreach (var h in stageHistories)
            {
                var jobName = h.Application?.JobPosting?.Title ?? "Vị trí tuyển dụng";
                var fromName = h.FromStage?.Name ?? "Khởi tạo";
                var toName = h.ToStage?.Name ?? "Giai đoạn mới";
                var actor = h.ChangedByUser != null ? h.ChangedByUser.FullName : "Hệ thống ATS";
                var actorRole = h.ChangedByUser != null ? "Chuyên viên tuyển dụng / Phê duyệt" : "Tự động hóa";
                var commentDetails = !string.IsNullOrWhiteSpace(h.Comment)
                    ? $"Ghi chú chuyển vòng: \"{h.Comment}\""
                    : $"Hồ sơ được chuyển từ vòng {fromName} sang vòng {toName}.";

                allEvents.Add(new CandidateTimelineEventDto
                {
                    EventId = h.Id,
                    EventType = "STAGE_CHANGE",
                    EventCategory = "Chuyển giai đoạn",
                    Title = $"Chuyển trạng thái: {fromName} ➔ {toName}",
                    OccurredAt = h.CreatedAt,
                    ActorName = actor,
                    ActorRole = actorRole,
                    ActorEmail = h.ChangedByUser?.Email,
                    Summary = $"Chuyển vòng: {toName}",
                    Details = commentDetails,
                    JobTitle = jobName,
                    StageName = toName,
                    BadgeClass = "badge-subtle-emerald",
                    IconClass = "bi-arrow-right-circle",
                    Metadata = new()
                    {
                        ["ApplicationId"] = h.ApplicationId.ToString(),
                        ["FromStage"] = fromName,
                        ["ToStage"] = toName
                    }
                });
            }
        }

        // 2.3. Email da gui (EMAIL_SENT)
        if (!string.IsNullOrWhiteSpace(candidate.Email))
        {
            var candEmailNormalized = candidate.Email.Trim().ToLower();
            var emailLogs = await _context.EmailLogs
                .AsNoTracking()
                .Where(e => e.RecipientEmail.ToLower() == candEmailNormalized)
                .ToListAsync();

            foreach (var e in emailLogs)
            {
                var occTime = e.SentAt ?? e.CreatedAt;
                var statusText = e.Status == "SENT" ? "Gửi thành công" : (e.Status == "FAILED" ? "Gửi thất bại" : e.Status);
                var errDetail = !string.IsNullOrWhiteSpace(e.ErrorMessage) ? $" (Lỗi: {e.ErrorMessage})" : "";

                allEvents.Add(new CandidateTimelineEventDto
                {
                    EventId = e.Id,
                    EventType = "EMAIL_SENT",
                    EventCategory = "Email đã gửi",
                    Title = $"Gửi email: {e.Subject}",
                    OccurredAt = occTime,
                    ActorName = "Hệ thống Email ATS",
                    ActorRole = "Thông báo tự động",
                    ActorEmail = "recruitment@noveratech.vn",
                    Summary = $"Gửi thư: {e.Subject} ({statusText})",
                    Details = $"Đã gửi email có tiêu đề: \"{e.Subject}\" tới địa chỉ {e.RecipientEmail}. Trạng thái: {statusText}.{errDetail}",
                    BadgeClass = "badge-subtle-purple",
                    IconClass = "bi-envelope-check",
                    Metadata = new()
                    {
                        ["Subject"] = e.Subject,
                        ["Status"] = e.Status,
                        ["RecipientEmail"] = e.RecipientEmail
                    }
                });
            }
        }

        // 2.4. Lich phong van (INTERVIEW_SCHEDULED)
        if (appIds.Any())
        {
            var interviews = await _context.Interviews
                .AsNoTracking()
                .Include(i => i.Panelists)
                    .ThenInclude(p => p.User)
                .Include(i => i.Application)
                    .ThenInclude(a => a.JobPosting)
                .Where(i => appIds.Contains(i.ApplicationId))
                .ToListAsync();

            foreach (var i in interviews)
            {
                var occTime = i.StartTime ?? i.CreatedAt;
                var jobName = i.Application?.JobPosting?.Title ?? "Vị trí tuyển dụng";
                var panelNames = i.Panelists
                    .Select(p => p.User?.FullName)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .ToList();
                var panelistStr = panelNames.Any() ? string.Join(", ", panelNames) : "Hội đồng phỏng vấn";

                var meetingTypeStr = i.InterviewType == InterviewType.ONLINE_MEET ? "Phỏng vấn trực tuyến" : "Phỏng vấn trực tiếp tại văn phòng";
                var locationStr = !string.IsNullOrWhiteSpace(i.LocationOrLink) ? $" | Địa điểm/Link: {i.LocationOrLink}" : "";
                var noteStr = !string.IsNullOrWhiteSpace(i.CandidateNotes) ? $"\nGhi chú chuẩn bị cho ứng viên: \"{i.CandidateNotes}\"" : "";

                allEvents.Add(new CandidateTimelineEventDto
                {
                    EventId = i.Id,
                    EventType = "INTERVIEW_SCHEDULED",
                    EventCategory = "Lịch phỏng vấn",
                    Title = $"Lịch phỏng vấn: {i.Title} (Vòng {i.RoundNumber})",
                    OccurredAt = occTime,
                    ActorName = panelistStr,
                    ActorRole = $"Hội đồng phỏng vấn (Vòng {i.RoundNumber})",
                    Summary = $"{meetingTypeStr}{locationStr}",
                    Details = $"Tổ chức buổi phỏng vấn \"{i.Title}\" cho vị trí {jobName}.\n- Hình thức: {meetingTypeStr}{locationStr}\n- Người tham gia phỏng vấn: {panelistStr}\n- Trạng thái phản hồi của ứng viên: {i.CandidateConfirmed}.{noteStr}",
                    JobTitle = jobName,
                    BadgeClass = "badge-subtle-sky",
                    IconClass = "bi-calendar2-event",
                    Metadata = new()
                    {
                        ["ApplicationId"] = i.ApplicationId.ToString(),
                        ["InterviewType"] = i.InterviewType.ToString(),
                        ["CandidateConfirmed"] = i.CandidateConfirmed.ToString()
                    }
                });
            }
        }

        // 2.5. Danh gia phong van (INTERVIEW_EVALUATION)
        if (appIds.Any())
        {
            var interviewIds = await _context.Interviews
                .AsNoTracking()
                .Where(i => appIds.Contains(i.ApplicationId))
                .Select(i => i.Id)
                .ToListAsync();

            if (interviewIds.Any())
            {
                var evaluations = await _context.InterviewEvaluations
                    .AsNoTracking()
                    .Include(ev => ev.Interviewer)
                    .Include(ev => ev.Interview)
                        .ThenInclude(i => i.Application)
                            .ThenInclude(a => a.JobPosting)
                    .Where(ev => interviewIds.Contains(ev.InterviewId))
                    .ToListAsync();

                foreach (var ev in evaluations)
                {
                    var occTime = ev.SubmittedAt ?? ev.UpdatedAt ?? ev.CreatedAt;
                    var jobName = ev.Interview?.Application?.JobPosting?.Title ?? "Vị trí tuyển dụng";
                    var interviewerName = ev.Interviewer != null ? ev.Interviewer.FullName : "Người phỏng vấn";
                    var scoreStr = ev.OverallScore.HasValue ? $"{ev.OverallScore.Value:0.0}/5.0" : "Chưa chấm";

                    var evalDetails = $"Đánh giá kết quả phỏng vấn bởi {interviewerName}:\n" +
                                      $"- Khuyến nghị: {ev.Recommendation}\n" +
                                      $"- Điểm tổng kết: {scoreStr}\n" +
                                      $"- Điểm mạnh: {(string.IsNullOrWhiteSpace(ev.Strengths) ? "Không ghi nhận" : ev.Strengths)}\n" +
                                      $"- Điểm cần cải thiện: {(string.IsNullOrWhiteSpace(ev.Weaknesses) ? "Không ghi nhận" : ev.Weaknesses)}\n" +
                                      $"- Nhận xét chi tiết: {(string.IsNullOrWhiteSpace(ev.DetailedFeedback) ? "Không có" : ev.DetailedFeedback)}";

                    allEvents.Add(new CandidateTimelineEventDto
                    {
                        EventId = ev.Id,
                        EventType = "INTERVIEW_EVALUATION",
                        EventCategory = "Đánh giá phỏng vấn",
                        Title = $"Đánh giá phỏng vấn: {ev.Interview?.Title ?? "Buổi phỏng vấn"} (Điểm: {scoreStr})",
                        OccurredAt = occTime,
                        ActorName = interviewerName,
                        ActorRole = "Người đánh giá chuyên môn",
                        ActorEmail = ev.Interviewer?.Email,
                        Summary = $"Khuyến nghị: {ev.Recommendation} | Điểm: {scoreStr}",
                        Details = evalDetails,
                        JobTitle = jobName,
                        Score = ev.OverallScore,
                        Recommendation = ev.Recommendation.ToString(),
                        BadgeClass = "badge-subtle-amber",
                        IconClass = "bi-star-fill",
                        Metadata = new()
                        {
                            ["InterviewId"] = ev.InterviewId.ToString(),
                            ["OverallScore"] = scoreStr,
                            ["Recommendation"] = ev.Recommendation.ToString()
                        }
                    });
                }
            }
        }

        // 2.6. Cuoc goi & Ghi chu trao doi truc tiep (CALL_NOTE)
        var interactions = await _context.CandidateInteractions
            .AsNoTracking()
            .Include(ci => ci.CreatedByUser)
            .Include(ci => ci.Application)
                .ThenInclude(a => a.JobPosting)
            .Where(ci => ci.CandidateId == candidateId)
            .ToListAsync();

        foreach (var ci in interactions)
        {
            var jobName = ci.Application?.JobPosting?.Title;
            var actor = ci.CreatedByUser != null ? ci.CreatedByUser.FullName : "Nhân viên tuyển dụng";
            var categoryName = ci.InteractionType switch
            {
                CandidateInteractionType.CALL => "Cuộc gọi trao đổi",
                CandidateInteractionType.MESSAGE => "Tin nhắn trao đổi",
                CandidateInteractionType.MEETING => "Gặp mặt trực tiếp",
                _ => "Ghi chú trao đổi"
            };

            var iconName = ci.InteractionType switch
            {
                CandidateInteractionType.CALL => "bi-telephone-outbound",
                CandidateInteractionType.MESSAGE => "bi-chat-dots",
                CandidateInteractionType.MEETING => "bi-people",
                _ => "bi-journal-text"
            };

            var badgeName = ci.InteractionType == CandidateInteractionType.CALL ? "badge-subtle-teal" : "badge-subtle-indigo";
            var outcomeText = !string.IsNullOrWhiteSpace(ci.Outcome) ? $" [Kết quả: {ci.Outcome}]" : "";

            allEvents.Add(new CandidateTimelineEventDto
            {
                EventId = ci.Id,
                EventType = "CALL_NOTE",
                EventCategory = categoryName,
                Title = $"{ci.Title}{outcomeText}",
                OccurredAt = ci.InteractionDate,
                ActorName = actor,
                ActorRole = "Nhân viên tuyển dụng",
                ActorEmail = ci.CreatedByUser?.Email,
                Summary = !string.IsNullOrWhiteSpace(ci.Outcome) ? $"Kết quả: {ci.Outcome}" : ci.Title,
                Details = ci.Content,
                Outcome = ci.Outcome,
                JobTitle = jobName,
                BadgeClass = badgeName,
                IconClass = iconName,
                Metadata = new()
                {
                    ["InteractionType"] = ci.InteractionType.ToString(),
                    ["Outcome"] = ci.Outcome ?? ""
                }
            });
        }

        // 3. Tinh toan thong ke truoc khi loc
        var totalEvents = allEvents.Count;
        var appCount = allEvents.Count(e => e.EventType == "APPLIED");
        var stageCount = allEvents.Count(e => e.EventType == "STAGE_CHANGE");
        var emailCount = allEvents.Count(e => e.EventType == "EMAIL_SENT");
        var interviewCount = allEvents.Count(e => e.EventType == "INTERVIEW_SCHEDULED");
        var evalCount = allEvents.Count(e => e.EventType == "INTERVIEW_EVALUATION");
        var callCount = allEvents.Count(e => e.EventType == "CALL_NOTE");

        // 4. DINH DANG THOI GIAN VA TINH KHOANG THOI GIAN (TimeAgo)
        var now = DateTimeOffset.UtcNow;
        foreach (var ev in allEvents)
        {
            ev.OccurredAtDisplay = ev.OccurredAt.ToLocalTime().ToString("HH:mm dd/MM/yyyy");
            ev.TimeAgoDisplay = FormatTimeAgo(ev.OccurredAt, now);
        }

        // 5. QUY TAC QUAN TRONG: SAP XEP MOI NHAT TRUOC (OrderByDescending)
        var sortedEvents = allEvents.OrderByDescending(e => e.OccurredAt).ToList();

        // 6. Ap dung bo loc
        if (!string.IsNullOrWhiteSpace(filterType) && !filterType.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            sortedEvents = sortedEvents
                .Where(e => e.EventType.Equals(filterType, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            var q = searchQuery.Trim().ToLowerInvariant();
            sortedEvents = sortedEvents
                .Where(e => (e.Title != null && e.Title.ToLowerInvariant().Contains(q)) ||
                            (e.Details != null && e.Details.ToLowerInvariant().Contains(q)) ||
                            (e.ActorName != null && e.ActorName.ToLowerInvariant().Contains(q)) ||
                            (e.Summary != null && e.Summary.ToLowerInvariant().Contains(q)) ||
                            (e.Outcome != null && e.Outcome.ToLowerInvariant().Contains(q)))
                .ToList();
        }

        return new CandidateTimelineViewModel
        {
            CandidateId = candidate.Id,
            CandidateName = $"{candidate.FirstName} {candidate.LastName}".Trim(),
            CandidateEmail = candidate.Email,
            CandidatePhone = candidate.Phone,
            CurrentCompany = candidate.CurrentCompany,
            CurrentTitle = candidate.CurrentTitle,
            SelectedApplicationId = applicationId,
            SelectedJobTitle = selectedJobTitle,
            FilterType = filterType ?? "ALL",
            SearchQuery = searchQuery,
            TotalEventsCount = totalEvents,
            ApplicationsCount = appCount,
            StageChangesCount = stageCount,
            EmailsSentCount = emailCount,
            InterviewsCount = interviewCount,
            EvaluationsCount = evalCount,
            CallsAndNotesCount = callCount,
            Events = sortedEvents,
            Applications = appOptions
        };
    }

    public async Task<(bool Success, string Message, Guid? InteractionId)> AddInteractionAsync(
        AddCandidateInteractionInputModel model,
        Guid currentUserId)
    {
        try
        {
            var candidate = await _context.Candidates.FindAsync(model.CandidateId);
            if (candidate == null)
            {
                return (false, "Không tìm thấy thông tin ứng viên", null);
            }

            if (model.ApplicationId.HasValue)
            {
                var appExists = await _context.Applications.AnyAsync(a => a.Id == model.ApplicationId.Value && a.CandidateId == model.CandidateId);
                if (!appExists)
                {
                    model.ApplicationId = null; // Neu don ung tuyen khong khop, ghi nhan o muc ung vien
                }
            }

            var interaction = new CandidateInteraction
            {
                Id = Guid.NewGuid(),
                CandidateId = model.CandidateId,
                ApplicationId = model.ApplicationId,
                CreatedByUserId = currentUserId,
                InteractionType = model.InteractionType,
                Title = model.Title.Trim(),
                Content = model.Content.Trim(),
                Outcome = string.IsNullOrWhiteSpace(model.Outcome) ? null : model.Outcome.Trim(),
                InteractionDate = model.InteractionDate ?? DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            _context.CandidateInteractions.Add(interaction);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Recruiter {UserId} recorded interaction {InteractionId} for Candidate {CandidateId}",
                currentUserId, interaction.Id, model.CandidateId);

            return (true, "Đã lưu nhật ký tương tác thành công", interaction.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding candidate interaction for candidate {CandidateId}", model.CandidateId);
            return (false, $"Lỗi khi lưu nhật ký tương tác: {ex.Message}", null);
        }
    }

    private static string FormatTimeAgo(DateTimeOffset past, DateTimeOffset now)
    {
        var span = now - past;
        if (span.TotalSeconds < 0) return "Vừa xong";
        if (span.TotalMinutes < 1) return "Vừa xong";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} phút trước";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours} giờ trước";
        if (span.TotalDays < 30) return $"{(int)span.TotalDays} ngày trước";
        if (span.TotalDays < 365) return $"{(int)(span.TotalDays / 30)} tháng trước";
        return $"{(int)(span.TotalDays / 365)} năm trước";
    }
}
