namespace Ats.Web.Models.DTOs;

public record SendEmailRequestDto(
    string ToEmail,
    string Subject,
    string Body,
    Guid? CandidateId = null,
    string? EmailType = null,
    Guid? TriggeredByUserId = null
);
