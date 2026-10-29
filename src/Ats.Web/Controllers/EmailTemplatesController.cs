using System.Text;
using Ats.Web.Constants;
using Ats.Web.Data;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Models.ViewModels.EmailTemplates;
using Ats.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ats.Web.Controllers;

[Authorize(Roles = $"{UserRoles.Admin},{UserRoles.HRManager},Quản trị viên,Trưởng phòng nhân sự,HRManager,Admin")]
[Route("email-templates")]
public class EmailTemplatesController : Controller
{
    private readonly ApplicationDbContext _dbContext;

    public EmailTemplatesController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    [HttpGet("index")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var model = await BuildManagementModelAsync(cancellationToken);
        return View(model);
    }

    [HttpPost("save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([FromForm] EmailTemplateManagementViewModel model, CancellationToken cancellationToken)
    {
        if (model.Templates is null || model.Templates.Count == 0)
        {
            TempData["ErrorMessage"] = "Không có mẫu email nào để lưu.";
            return RedirectToAction(nameof(Index));
        }

        foreach (var template in model.Templates)
        {
            if (string.IsNullOrWhiteSpace(template.TemplateType) || !Enum.TryParse<EmailTemplateType>(template.TemplateType, true, out var templateType))
            {
                continue;
            }

            var subject = template.Subject?.Trim() ?? string.Empty;
            var body = template.Body?.Trim() ?? string.Empty;
            var validation = EmailTemplateRenderer.Validate(templateType, subject, body);
            if (!validation.IsValid)
            {
                TempData["ErrorMessage"] = $"Mẫu {GetDisplayName(templateType)} không hợp lệ: {validation.Message}";
                return RedirectToAction(nameof(Index));
            }

            var entity = await _dbContext.EmailTemplates.FirstOrDefaultAsync(x => x.TemplateType == templateType, cancellationToken);
            if (entity is null)
            {
                entity = new EmailTemplate
                {
                    Id = Guid.NewGuid(),
                    TemplateType = templateType,
                    Name = GetDisplayName(templateType),
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                _dbContext.EmailTemplates.Add(entity);
            }

            entity.Name = GetDisplayName(templateType);
            entity.Subject = subject;
            entity.Body = body;
            entity.IsEnabled = template.IsEnabled;
            entity.IsHtml = template.IsHtml;
            entity.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        TempData["SuccessMessage"] = "Cập nhật mẫu email tự động thành công.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("preview")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Preview([FromForm] EmailTemplatePreviewRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TemplateType) || !Enum.TryParse<EmailTemplateType>(request.TemplateType, true, out var templateType))
        {
            return Json(new EmailTemplatePreviewResponse { Success = false, Error = "Loại mẫu email không hợp lệ." });
        }

        var subject = request.Subject?.Trim() ?? string.Empty;
        var body = request.Body?.Trim() ?? string.Empty;

        var values = await BuildPlaceholderValuesAsync(templateType, request, cancellationToken);
        try
        {
            var rendered = EmailTemplateRenderer.Render(templateType, subject, body, values);
            var segments = rendered.Split("\n", 2, StringSplitOptions.None);
            var renderedSubject = segments.Length > 0 ? segments[0] : string.Empty;
            var renderedBody = segments.Length > 1 ? segments[1] : string.Empty;

            return Json(new EmailTemplatePreviewResponse
            {
                Success = true,
                Subject = renderedSubject,
                Body = renderedBody,
                Html = BuildPreviewHtml(renderedSubject, renderedBody)
            });
        }
        catch (InvalidOperationException ex)
        {
            return Json(new EmailTemplatePreviewResponse
            {
                Success = false,
                Error = ex.Message
            });
        }
    }

    private async Task<EmailTemplateManagementViewModel> BuildManagementModelAsync(CancellationToken cancellationToken)
    {
        foreach (var templateType in Enum.GetValues<EmailTemplateType>())
        {
            var existing = await _dbContext.EmailTemplates.FirstOrDefaultAsync(x => x.TemplateType == templateType, cancellationToken);
            if (existing is null)
            {
                var defaultTemplate = new EmailTemplate
                {
                    Id = Guid.NewGuid(),
                    TemplateType = templateType,
                    Name = GetDisplayName(templateType),
                    Subject = GetDefaultSubject(templateType),
                    Body = GetDefaultBody(templateType),
                    IsEnabled = true,
                    IsHtml = true,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                _dbContext.EmailTemplates.Add(defaultTemplate);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var templatesDb = await _dbContext.EmailTemplates
            .OrderBy(x => x.TemplateType)
            .ToListAsync(cancellationToken);

        var candidates = await _dbContext.Candidates
            .OrderBy(c => c.FirstName)
            .ThenBy(c => c.LastName)
            .Select(c => new CandidateTemplateOption
            {
                Id = c.Id,
                DisplayName = c.FirstName + " " + c.LastName + " (" + c.Email + ")"
            })
            .ToListAsync(cancellationToken);

        var jobPostings = await _dbContext.JobPostings
            .OrderBy(j => j.Title)
            .Take(50)
            .Select(j => new JobTemplateOption
            {
                Id = j.Id,
                DisplayName = j.Title
            })
            .ToListAsync(cancellationToken);

        var interviews = await _dbContext.Interviews
            .Include(i => i.Application)
            .ThenInclude(a => a.Candidate)
            .OrderByDescending(i => i.StartTime)
            .Take(50)
            .Select(i => new InterviewTemplateOption
            {
                Id = i.Id,
                DisplayName = i.Application.Candidate.FirstName + " " + i.Application.Candidate.LastName + " - " + (i.Title ?? "Buổi phỏng vấn") + " - " + i.StartTime!.Value.ToString("dd/MM/yyyy HH:mm")
            })
            .ToListAsync(cancellationToken);

        var model = new EmailTemplateManagementViewModel
        {
            Candidates = candidates,
            Jobs = jobPostings,
            Interviews = interviews
        };

        model.Templates = Enum.GetValues<EmailTemplateType>()
            .Select(type =>
            {
                var template = templatesDb.FirstOrDefault(x => x.TemplateType == type) ?? new EmailTemplate
                {
                    TemplateType = type,
                    Subject = GetDefaultSubject(type),
                    Body = GetDefaultBody(type),
                    IsEnabled = true,
                    IsHtml = true
                };

                return new EmailTemplateItemViewModel
                {
                    TemplateType = type.ToString(),
                    DisplayName = GetDisplayName(type),
                    Subject = template.Subject,
                    Body = template.Body,
                    IsEnabled = template.IsEnabled,
                    IsHtml = template.IsHtml,
                    SupportedVariables = EmailTemplateRenderer.GetSupportedVariables(type).ToList(),
                    RequiredVariables = EmailTemplateRenderer.GetRequiredVariables(type).ToList()
                };
            })
            .ToList();

        return model;
    }

    private async Task<Dictionary<string, string?>> BuildPlaceholderValuesAsync(EmailTemplateType templateType, EmailTemplatePreviewRequest request, CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        if (request.CandidateId.HasValue)
        {
            var candidate = await _dbContext.Candidates.FirstOrDefaultAsync(c => c.Id == request.CandidateId.Value, cancellationToken);
            if (candidate is not null)
            {
                values["{{CandidateName}}"] = (candidate.FirstName + " " + candidate.LastName).Trim();
            }
        }

        if (request.JobId.HasValue)
        {
            var job = await _dbContext.JobPostings.FirstOrDefaultAsync(j => j.Id == request.JobId.Value, cancellationToken);
            if (job is not null)
            {
                values["{{JobTitle}}"] = job.Title;
            }
        }

        if (request.InterviewId.HasValue)
        {
            var interview = await _dbContext.Interviews.FirstOrDefaultAsync(i => i.Id == request.InterviewId.Value, cancellationToken);
            if (interview is not null)
            {
                values["{{InterviewTime}}"] = interview.StartTime.HasValue ? interview.StartTime.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm") : string.Empty;
                values["{{InterviewRound}}"] = interview.Title;
                values["{{InterviewLocation}}"] = interview.LocationOrLink ?? "Hội trường/Link phỏng vấn";
            }
        }

        if (string.IsNullOrWhiteSpace(values.GetValueOrDefault("{{CandidateName}}")))
        {
            values["{{CandidateName}}"] = "Ứng viên";
        }

        if (string.IsNullOrWhiteSpace(values.GetValueOrDefault("{{JobTitle}}")))
        {
            values["{{JobTitle}}"] = "Vị trí tuyển dụng";
        }

        if (string.IsNullOrWhiteSpace(values.GetValueOrDefault("{{InterviewTime}}")))
        {
            values["{{InterviewTime}}"] = "thời gian phỏng vấn";
        }

        if (string.IsNullOrWhiteSpace(values.GetValueOrDefault("{{OfferStartDate}}")))
        {
            values["{{OfferStartDate}}"] = DateTime.Today.AddDays(14).ToString("dd/MM/yyyy");
        }

        return values;
    }

    private static string GetDisplayName(EmailTemplateType templateType) => templateType switch
    {
        EmailTemplateType.ApplicationReceived => "Application Received",
        EmailTemplateType.InterviewInvitation => "Interview Invitation",
        EmailTemplateType.InterviewThankYou => "Interview Thank You",
        EmailTemplateType.Rejection => "Rejection",
        EmailTemplateType.JobOffer => "Job Offer",
        _ => templateType.ToString()
    };

    private static string GetDefaultSubject(EmailTemplateType templateType) => templateType switch
    {
        EmailTemplateType.ApplicationReceived => "[NoveraTech Careers] Xác nhận đã nhận hồ sơ ứng tuyển: {{JobTitle}}",
        EmailTemplateType.InterviewInvitation => "Thư mời phỏng vấn vị trí {{JobTitle}}",
        EmailTemplateType.InterviewThankYou => "Cảm ơn bạn đã tham gia phỏng vấn vị trí {{JobTitle}}",
        EmailTemplateType.Rejection => "Kết quả hồ sơ vị trí {{JobTitle}}",
        EmailTemplateType.JobOffer => "Chúc mừng! Thư mời nhận việc cho vị trí {{JobTitle}}",
        _ => string.Empty
    };

    private static string GetDefaultBody(EmailTemplateType templateType) => templateType switch
    {
        EmailTemplateType.ApplicationReceived => "<p>Xin chào {{CandidateName}},</p><p>Cảm ơn bạn đã nộp hồ sơ ứng tuyển cho vị trí {{JobTitle}}. Hồ sơ của bạn đã được ghi nhận.</p>",
        EmailTemplateType.InterviewInvitation => "<p>Xin chào {{CandidateName}},</p><p>Chúng tôi trân trọng mời bạn tham gia phỏng vấn vị trí {{JobTitle}} vào lúc {{InterviewTime}}.</p>",
        EmailTemplateType.InterviewThankYou => "<p>Xin chào {{CandidateName}},</p><p>Cảm ơn bạn đã dành thời gian trao đổi với đội ngũ tuyển dụng cho vị trí {{JobTitle}}.</p>",
        EmailTemplateType.Rejection => "<p>Xin chào {{CandidateName}},</p><p>Chúng tôi cảm ơn bạn đã quan tâm đến vị trí {{JobTitle}}. Sau khi đánh giá, chúng tôi xin thông báo không tiếp tục tiến hành với hồ sơ của bạn.</p>",
        EmailTemplateType.JobOffer => "<p>Xin chào {{CandidateName}},</p><p>Chúng tôi vui mừng thông báo bạn đã nhận được lời mời làm việc cho vị trí {{JobTitle}} với ngày bắt đầu dự kiến {{OfferStartDate}}.</p>",
        _ => string.Empty
    };

    private static string BuildPreviewHtml(string subject, string body)
    {
        var safeBody = body;
        if (!string.IsNullOrWhiteSpace(safeBody) && !safeBody.Contains("<html", StringComparison.OrdinalIgnoreCase))
        {
            safeBody = $"<div style=\"padding: 20px; background: #f8fafc; border: 1px solid #e2e8f0; border-radius: 10px;\">{safeBody}</div>";
        }

        var builder = new StringBuilder();
        builder.Append("<div style='font-family: Arial, sans-serif; max-width: 680px; margin: 0 auto;'>");
        builder.Append($"<div style='background: #0f172a; color: white; padding: 16px 20px; border-radius: 10px 10px 0 0; font-weight: 700;'>{System.Net.WebUtility.HtmlEncode(subject)}</div>");
        builder.Append(safeBody);
        builder.Append("</div>");
        return builder.ToString();
    }
}
