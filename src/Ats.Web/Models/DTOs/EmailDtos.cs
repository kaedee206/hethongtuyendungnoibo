namespace Ats.Web.Models.DTOs;

public record SendEmailRequestDto(
    string ToEmail,
    string Subject,
    string Body
);
