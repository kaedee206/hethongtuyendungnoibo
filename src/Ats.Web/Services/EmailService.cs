using Ats.Web.Models.DTOs;
using Ats.Web.Services.Interfaces;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Ats.Web.Services;

public class EmailService : IEmailService
{
    public async Task<bool> SendEmailAsync(SendEmailRequestDto request, CancellationToken cancellationToken = default)
    {
        try
        {
            var smtpHost = Environment.GetEnvironmentVariable("SMTP_HOST") ?? "smtp.gmail.com";
            var smtpPort = int.Parse(Environment.GetEnvironmentVariable("SMTP_PORT") ?? "587");
            var smtpEmail = Environment.GetEnvironmentVariable("SMTP_EMAIL") ?? "";
            var smtpPassword = Environment.GetEnvironmentVariable("SMTP_PASSWORD") ?? "";

            var email = new MimeMessage();
            email.From.Add(new MailboxAddress("Hệ thống tuyển dụng ATS", smtpEmail));
            email.To.Add(MailboxAddress.Parse(request.ToEmail));
            email.Subject = request.Subject;

            var builder = new BodyBuilder { HtmlBody = request.Body };
            email.Body = builder.ToMessageBody();

            using var smtp = new SmtpClient();
            await smtp.ConnectAsync(smtpHost, smtpPort, SecureSocketOptions.StartTls, cancellationToken);
            await smtp.AuthenticateAsync(smtpEmail, smtpPassword, cancellationToken);
            await smtp.SendAsync(email, cancellationToken);
            await smtp.DisconnectAsync(true, cancellationToken);

            return true;
        }
        catch
        {
            // Ghi log nếu gửi mail thất bại nhưng không làm gián đoạn luồng chính
            return false;
        }
    }
}