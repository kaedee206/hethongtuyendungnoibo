using Ats.Web.Data;
using Ats.Web.Models.DTOs;
using Ats.Web.Models.Entities;
using Ats.Web.Services.Interfaces;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Ats.Web.Services;

public class EmailService : IEmailService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IServiceScopeFactory scopeFactory, ILogger<EmailService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<bool> SendEmailAsync(SendEmailRequestDto request, CancellationToken cancellationToken = default)
    {
        var log = new EmailLog
        {
            Id = Guid.NewGuid(),
            RecipientEmail = request.ToEmail,
            Subject = request.Subject,
            Status = "PENDING",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        try
        {
            var smtpHost = Environment.GetEnvironmentVariable("SMTP_HOST") ?? "smtp.gmail.com";
            var smtpPortStr = Environment.GetEnvironmentVariable("SMTP_PORT") ?? "587";
            var smtpPort = int.TryParse(smtpPortStr, out var port) ? port : 587;
            var smtpEmail = Environment.GetEnvironmentVariable("SMTP_EMAIL") ?? "";
            var smtpPassword = Environment.GetEnvironmentVariable("SMTP_PASSWORD") ?? "";

            if (!string.IsNullOrWhiteSpace(smtpEmail) && !string.IsNullOrWhiteSpace(smtpPassword))
            {
                var email = new MimeMessage();
                email.From.Add(new MailboxAddress("NoveraTech Careers & ATS", smtpEmail));
                email.To.Add(MailboxAddress.Parse(request.ToEmail));
                email.Subject = request.Subject;

                var builder = new BodyBuilder { HtmlBody = request.Body };
                email.Body = builder.ToMessageBody();

                using var smtp = new SmtpClient();
                // Timeout 5 giây để tránh chặn luồng web
                smtp.Timeout = 5000;
                await smtp.ConnectAsync(smtpHost, smtpPort, SecureSocketOptions.StartTls, cancellationToken);
                await smtp.AuthenticateAsync(smtpEmail, smtpPassword, cancellationToken);
                await smtp.SendAsync(email, cancellationToken);
                await smtp.DisconnectAsync(true, cancellationToken);

                log.Status = "SENT";
                log.SentAt = DateTimeOffset.UtcNow;
                _logger.LogInformation("Đã gửi email thành công tới {ToEmail} (Tiêu đề: {Subject})", request.ToEmail, request.Subject);
            }
            else
            {
                log.Status = "SKIPPED_NO_CREDENTIALS";
                log.ErrorMessage = "Chưa cấu hình tài khoản SMTP gửi mail.";
                _logger.LogWarning("Bỏ qua gửi email do chưa cấu hình SMTP: {Subject} -> {ToEmail}", request.Subject, request.ToEmail);
            }
        }
        catch (Exception ex)
        {
            log.Status = "FAILED";
            log.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Lỗi khi gửi email tới {ToEmail}", request.ToEmail);
        }
        finally
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await db.EmailLogs.AddAsync(log, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception dbEx)
            {
                _logger.LogWarning(dbEx, "Không thể lưu nhật ký EmailLog vào CSDL");
            }
        }

        return log.Status == "SENT";
    }

    public async Task<bool> SendLoginSuccessAlertAsync(
        string email,
        string fullName,
        string ipAddress,
        string location,
        string userAgent,
        DateTimeOffset loginTime,
        bool isCandidate = false,
        string? roleName = null,
        CancellationToken cancellationToken = default)
    {
        var timeDisplay = loginTime.ToOffset(TimeSpan.FromHours(7)).ToString("HH:mm:ss - dd/MM/yyyy") + " (Giờ Việt Nam)";

        string subject;
        string badgeText;
        string portalTitle;
        string greetingIntro;
        string roleRowHtml;
        string securityNoticeHtml;
        string ctaButtonText;
        string ctaButtonUrl;
        string footerText;

        if (isCandidate)
        {
            subject = "✨ [NoveraTech Careers] Đăng nhập thành công Cổng Ứng viên";
            badgeText = "CỔNG ỨNG VIÊN TUYỂN DỤNG";
            portalTitle = "NOVERATECH CAREERS";
            greetingIntro = "Hệ thống ghi nhận tài khoản của bạn vừa đăng nhập thành công vào <strong>Cổng thông tin Ứng viên NoveraTech Careers</strong> với thông số chi tiết dưới đây:";
            roleRowHtml = @"
                        <tr>
                            <td style=""padding: 6px 0; color: #64748B;"">Phân hệ:</td>
                            <td style=""padding: 6px 0; color: #059669; font-weight: 600;"">Cổng Ứng viên (Candidate Portal)</td>
                        </tr>";
            securityNoticeHtml = @"
                        <strong>Lưu ý an toàn:</strong> Đây là cổng thông tin dành riêng cho ứng viên theo dõi tiến trình hồ sơ, cập nhật CV và ứng tuyển các cơ hội việc làm tại NoveraTech. Nếu không phải bạn thực hiện đăng nhập này, vui lòng truy cập ngay vào mục Đổi mật khẩu hoặc liên hệ Ban Tuyển dụng qua <em>tuyendung@noveratech.digital</em>.";
            ctaButtonText = "Truy cập Cổng Ứng viên";
            ctaButtonUrl = "https://tuyendung.noveratech.digital/candidate/ho-so-cua-toi";
            footerText = "NoveraTech Careers · Cổng Thông tin Tuyển dụng Trực tiếp NoveraTech · Email thông báo tự động.";
        }
        else
        {
            var displayRole = string.IsNullOrWhiteSpace(roleName) ? "Cán bộ nhân sự" : roleName;
            subject = "🔒 [Bảo mật Nội bộ] Đăng nhập thành công - Hệ thống Quản trị ATS NoveraTech";
            badgeText = "HỆ THỐNG QUẢN TRỊ NỘI BỘ";
            portalTitle = "NOVERATECH INTERNAL ATS";
            greetingIntro = "Hệ thống xác nhận tài khoản nhân sự của bạn vừa đăng nhập thành công vào <strong>Hệ thống Quản trị Tuyển dụng Nội bộ (NoveraTech ATS)</strong> với thông số chi tiết dưới đây:";
            roleRowHtml = $@"
                        <tr>
                            <td style=""padding: 6px 0; color: #64748B;"">Vai trò nội bộ:</td>
                            <td style=""padding: 6px 0; color: #059669; font-weight: 700;"">{displayRole}</td>
                        </tr>
                        <tr>
                            <td style=""padding: 6px 0; color: #64748B;"">Phân hệ:</td>
                            <td style=""padding: 6px 0; color: #0F172A; font-weight: 600;"">Không gian Làm việc Quản trị Nội bộ (Internal Staff Workspace)</td>
                        </tr>";
            securityNoticeHtml = @"
                        <strong>Cảnh báo an ninh thông tin:</strong> Đây là hệ thống phân quyền nội bộ dành riêng cho cán bộ nhân sự và hội đồng tuyển dụng NoveraTech. Mọi thao tác đều được ghi nhật ký bảo mật (Audit Log). Nếu phát hiện bất thường, vui lòng báo ngay cho Ban An toàn Thông tin qua <em>security@noveratech.digital</em>.";
            ctaButtonText = "Truy cập Không gian Làm việc Nội bộ";
            ctaButtonUrl = "https://tuyendung.noveratech.digital/dashboard";
            footerText = "Tập đoàn Công nghệ NoveraTech · Hệ thống Quản trị Tuyển dụng & Nhân sự Nội bộ (NoveraTech ATS) · Bảo mật nội bộ.";
        }

        var body = $@"
        <div style=""font-family: 'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; max-width: 600px; margin: 0 auto; background: #FFFFFF; border: 1px solid #E2E8F0; border-radius: 12px; overflow: hidden;"">
            <div style=""background: #0F172A; padding: 24px; text-align: center;"">
                <h2 style=""color: #FFFFFF; margin: 0; font-size: 1.25rem; font-weight: 700; letter-spacing: -0.02em;"">{portalTitle}</h2>
                <div style=""color: #10B981; font-size: 0.8rem; font-weight: 600; margin-top: 4px; letter-spacing: 0.05em;"">{badgeText}</div>
            </div>
            <div style=""padding: 28px 24px;"">
                <p style=""font-size: 1rem; color: #0F172A; margin-top: 0;"">Xin chào <strong>{fullName}</strong>,</p>
                <p style=""color: #475569; font-size: 0.92rem; line-height: 1.6;"">
                    {greetingIntro}
                </p>
                <div style=""background: #F8FAFC; border: 1px solid #E2E8F0; border-radius: 8px; padding: 16px; margin: 20px 0; font-size: 0.88rem;"">
                    <table style=""width: 100%; border-collapse: collapse;"">
                        <tr>
                            <td style=""padding: 6px 0; color: #64748B; width: 140px;"">Thời gian:</td>
                            <td style=""padding: 6px 0; color: #0F172A; font-weight: 600;"">{timeDisplay}</td>
                        </tr>
                        <tr>
                            <td style=""padding: 6px 0; color: #64748B;"">Địa chỉ IP:</td>
                            <td style=""padding: 6px 0; color: #059669; font-family: monospace; font-weight: 700;"">{ipAddress}</td>
                        </tr>
                        <tr>
                            <td style=""padding: 6px 0; color: #64748B;"">Vị trí đăng nhập:</td>
                            <td style=""padding: 6px 0; color: #0F172A; font-weight: 600;"">{location}</td>
                        </tr>
                        <tr>
                            <td style=""padding: 6px 0; color: #64748B;"">Thiết bị / Trình duyệt:</td>
                            <td style=""padding: 6px 0; color: #334155; font-size: 0.82rem;"">{userAgent}</td>
                        </tr>
                        {roleRowHtml}
                    </table>
                </div>
                <div style=""background: #FFFBEB; border-left: 4px solid #F59E0B; padding: 12px 16px; border-radius: 4px; margin-bottom: 24px;"">
                    <p style=""margin: 0; font-size: 0.82rem; color: #92400E; line-height: 1.5;"">
                        {securityNoticeHtml}
                    </p>
                </div>
                <div style=""text-align: center; margin-top: 24px;"">
                    <a href=""{ctaButtonUrl}"" style=""background: #059669; color: #FFFFFF; text-decoration: none; padding: 10px 24px; border-radius: 8px; font-weight: 600; font-size: 0.9rem; display: inline-block;"">{ctaButtonText}</a>
                </div>
            </div>
            <div style=""background: #F1F5F9; padding: 16px; text-align: center; font-size: 0.75rem; color: #64748B; border-top: 1px solid #E2E8F0;"">
                {footerText}
            </div>
        </div>";

        return await SendEmailAsync(new SendEmailRequestDto(email, subject, body), cancellationToken);
    }

    public async Task<bool> SendApplicationReceivedAsync(
        string candidateEmail,
        string candidateName,
        string jobTitle,
        string applicationCode,
        string? withdrawUrl = null,
        CancellationToken cancellationToken = default)
    {
        var subject = $"[NoveraTech Careers] Xác nhận tiếp nhận hồ sơ ứng tuyển: {jobTitle}";
        var body = $@"
        <div style=""font-family: 'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; max-width: 600px; margin: 0 auto; background: #FFFFFF; border: 1px solid #E2E8F0; border-radius: 12px; overflow: hidden;"">
            <div style=""background: #059669; padding: 24px; text-align: center;"">
                <h2 style=""color: #FFFFFF; margin: 0; font-size: 1.3rem; font-weight: 700;"">HỒ SƠ ỨNG TUYỂN ĐÃ ĐƯỢC TIẾP NHẬN</h2>
                <div style=""color: #D1FAE5; font-size: 0.85rem; margin-top: 4px;"">NoveraTech Engineering & Product Hiring</div>
            </div>
            <div style=""padding: 28px 24px;"">
                <p style=""font-size: 1rem; color: #0F172A; margin-top: 0;"">Xin chào <strong>{candidateName}</strong>,</p>
                <p style=""color: #475569; font-size: 0.92rem; line-height: 1.6;"">
                    Cảm ơn bạn đã quan tâm và nộp hồ sơ ứng tuyển cho vị trí <strong>{jobTitle}</strong> tại Tập đoàn Công nghệ NoveraTech. Hồ sơ của bạn đã được ghi nhận vào hệ thống ATS với mã số theo dõi:
                </p>
                <div style=""text-align: center; margin: 20px 0;"">
                    <span style=""background: #ECFDF5; border: 1.5px dashed #059669; color: #065F46; font-size: 1.15rem; font-weight: 700; font-family: monospace; padding: 8px 20px; border-radius: 8px; display: inline-block;"">
                        {applicationCode}
                    </span>
                </div>
                <div style=""background: #F8FAFC; border: 1px solid #E2E8F0; border-radius: 8px; padding: 16px; font-size: 0.88rem; color: #334155; line-height: 1.6;"">
                    <div style=""font-weight: 700; color: #0F172A; margin-bottom: 6px;"">Cam kết quy trình tuyển dụng tại NoveraTech:</div>
                    <ul style=""margin: 0; padding-left: 20px;"">
                        <li>Phản hồi sơ loại hồ sơ trong vòng <strong>48 giờ làm việc</strong>.</li>
                        <li>Đánh giá năng lực dựa trên tư duy kỹ thuật thực chiến, minh bạch và không thiên vị.</li>
                        <li>Lịch trình phỏng vấn chuyên nghiệp và linh hoạt theo thỏa thuận.</li>
                    </ul>
                </div>
                <p style=""color: #475569; font-size: 0.9rem; line-height: 1.6; margin-top: 20px;"">
                    Bạn có thể đăng nhập vào Cổng ứng viên để theo dõi tiến độ xử lý hồ sơ và cập nhật thông tin cá nhân bất cứ lúc nào.
                </p>
                {(!string.IsNullOrWhiteSpace(withdrawUrl) ? $@"
                <div style=""margin-top: 24px; padding-top: 18px; border-top: 1px dashed #E2E8F0; font-size: 0.85rem; color: #64748B;"">
                    <p style=""margin: 0 0 8px 0;"">
                        Nếu bạn đã nhận việc nơi khác hoặc vì lý do cá nhân muốn dừng quy trình tuyển dụng, bạn có thể chủ động rút hồ sơ bất cứ lúc nào qua liên kết bảo mật:
                    </p>
                    <a href=""{withdrawUrl}"" style=""display: inline-block; color: #DC2626; text-decoration: underline; font-weight: 600; font-size: 0.82rem;"">
                        &rarr; Rút hồ sơ ứng tuyển vị trí này
                    </a>
                </div>" : "")}
            </div>
            <div style=""background: #F1F5F9; padding: 16px; text-align: center; font-size: 0.75rem; color: #64748B; border-top: 1px solid #E2E8F0;"">
                NoveraTech Careers · Phòng Tuyển dụng & Thu hút Nhân tài Công nghệ · Email: tuyendung@noveratech.com
            </div>
        </div>";

        return await SendEmailAsync(new SendEmailRequestDto(candidateEmail, subject, body), cancellationToken);
    }

    public async Task<bool> SendRecruiterWithdrawalAlertAsync(
        string recruiterEmail,
        string recruiterName,
        string candidateName,
        string candidateEmail,
        string jobTitle,
        string? reason,
        DateTimeOffset withdrawnAt,
        CancellationToken cancellationToken = default)
    {
        var subject = $"[Thông báo Tuyển dụng] Ứng viên {candidateName} đã rút hồ sơ: {jobTitle}";
        var body = $@"
        <div style=""font-family: 'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; max-width: 600px; margin: 0 auto; background: #FFFFFF; border: 1px solid #E2E8F0; border-radius: 12px; overflow: hidden;"">
            <div style=""background: #0F172A; padding: 22px; text-align: center;"">
                <h2 style=""color: #F8FAFC; margin: 0; font-size: 1.25rem; font-weight: 700;"">ỨNG VIÊN RÚT HỒ SƠ ỨNG TUYỂN</h2>
                <div style=""color: #94A3B8; font-size: 0.82rem; margin-top: 4px;"">Hệ thống Tuyển dụng Nội bộ NoveraTech ATS</div>
            </div>
            <div style=""padding: 26px 22px;"">
                <p style=""font-size: 0.95rem; color: #0F172A; margin-top: 0;"">Kính gửi <strong>{recruiterName}</strong>,</p>
                <p style=""color: #475569; font-size: 0.9rem; line-height: 1.6;"">
                    Hệ thống ghi nhận ứng viên <strong>{candidateName}</strong> ({candidateEmail}) đã chủ động rút hồ sơ ứng tuyển cho vị trí <strong>{jobTitle}</strong> vào lúc <strong>{withdrawnAt:HH:mm dd/MM/yyyy}</strong>.
                </p>
                <div style=""background: #FFFBEB; border: 1px solid #FDE68A; border-left: 4px solid #D97706; border-radius: 8px; padding: 14px; margin: 18px 0; font-size: 0.88rem; color: #78350F;"">
                    <div style=""font-weight: 700; margin-bottom: 4px;"">Lý do rút hồ sơ:</div>
                    <div>{(!string.IsNullOrWhiteSpace(reason) ? reason : "Ứng viên không cung cấp lý do chi tiết.")}</div>
                </div>
                <p style=""color: #475569; font-size: 0.88rem; line-height: 1.6;"">
                    Hồ sơ này đã được tự động chuyển sang trạng thái <strong>Ứng viên rút (WITHDRAWN)</strong> và đã biến mất khỏi pipeline tuyển dụng đang hoạt động.
                </p>
                <div style=""text-align: center; margin-top: 24px;"">
                    <a href=""https://tuyendung.noveratech.digital/dashboard/ung-vien"" style=""background: #059669; color: #FFFFFF; text-decoration: none; font-weight: 600; font-size: 0.85rem; padding: 10px 20px; border-radius: 6px; display: inline-block;"">
                        Xem Danh sách Ứng viên &rarr;
                    </a>
                </div>
            </div>
            <div style=""background: #F1F5F9; padding: 14px; text-align: center; font-size: 0.75rem; color: #64748B; border-top: 1px solid #E2E8F0;"">
                Hệ thống Quản trị Tuyển dụng NoveraTech ATS · Tự động gửi thông báo
            </div>
        </div>";

        return await SendEmailAsync(new SendEmailRequestDto(recruiterEmail, subject, body), cancellationToken);
    }

    public async Task<bool> SendCandidateWithdrawalConfirmedAsync(
        string candidateEmail,
        string candidateName,
        string jobTitle,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var subject = $"[NoveraTech Careers] Xác nhận đã hoàn tất rút hồ sơ ứng tuyển: {jobTitle}";
        var body = $@"
        <div style=""font-family: 'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; max-width: 600px; margin: 0 auto; background: #FFFFFF; border: 1px solid #E2E8F0; border-radius: 12px; overflow: hidden;"">
            <div style=""background: #475569; padding: 22px; text-align: center;"">
                <h2 style=""color: #FFFFFF; margin: 0; font-size: 1.25rem; font-weight: 700;"">HỒ SƠ ỨNG TUYỂN ĐÃ ĐƯỢC RÚT</h2>
                <div style=""color: #CBD5E1; font-size: 0.82rem; margin-top: 4px;"">NoveraTech Engineering & Product Hiring</div>
            </div>
            <div style=""padding: 26px 22px;"">
                <p style=""font-size: 0.95rem; color: #0F172A; margin-top: 0;"">Xin chào <strong>{candidateName}</strong>,</p>
                <p style=""color: #475569; font-size: 0.9rem; line-height: 1.6;"">
                    Chúng tôi xác nhận bạn đã chủ động rút hồ sơ ứng tuyển cho vị trí <strong>{jobTitle}</strong> tại NoveraTech. Quá trình tuyển dụng cho vị trí này đã được dừng lại theo nguyện vọng của bạn.
                </p>
                <p style=""color: #475569; font-size: 0.9rem; line-height: 1.6;"">
                    Cảm ơn bạn đã thẳng thắn thông báo để đội ngũ tuyển dụng chủ động sắp xếp kế hoạch. NoveraTech chúc bạn gặt hái thật nhiều thành công và phát triển vượt bậc trên con đường sự nghiệp sắp tới!
                </p>
                <p style=""color: #475569; font-size: 0.9rem; line-height: 1.6;"">
                    Nếu trong tương lai bạn có mong muốn tiếp tục ứng tuyển các cơ hội nghề nghiệp khác tại NoveraTech, chúng tôi luôn chào đón bạn.
                </p>
            </div>
            <div style=""background: #F1F5F9; padding: 14px; text-align: center; font-size: 0.75rem; color: #64748B; border-top: 1px solid #E2E8F0;"">
                NoveraTech Careers · Phòng Tuyển dụng & Thu hút Nhân tài Công nghệ
            </div>
        </div>";

        return await SendEmailAsync(new SendEmailRequestDto(candidateEmail, subject, body), cancellationToken);
    }

    public async Task<bool> SendScreeningPassedAsync(
        string candidateEmail,
        string candidateName,
        string jobTitle,
        CancellationToken cancellationToken = default)
    {
        var subject = $"🎉 [NoveraTech Careers] Chúc mừng bạn đã vượt qua vòng sơ loại hồ sơ: {jobTitle}";
        var body = $@"
        <div style=""font-family: 'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; max-width: 600px; margin: 0 auto; background: #FFFFFF; border: 1px solid #E2E8F0; border-radius: 12px; overflow: hidden;"">
            <div style=""background: #047857; padding: 24px; text-align: center;"">
                <h2 style=""color: #FFFFFF; margin: 0; font-size: 1.3rem; font-weight: 700;"">HỒ SƠ ĐẠT YÊU CẦU SƠ LOẠI</h2>
                <div style=""color: #A7F3D0; font-size: 0.85rem; margin-top: 4px;"">Tiến độ tuyển dụng NoveraTech</div>
            </div>
            <div style=""padding: 28px 24px;"">
                <p style=""font-size: 1rem; color: #0F172A; margin-top: 0;"">Xin chào <strong>{candidateName}</strong>,</p>
                <p style=""color: #475569; font-size: 0.92rem; line-height: 1.6;"">
                    Đội ngũ tuyển dụng và Quản lý Kỹ thuật NoveraTech đã hoàn tất quá trình sàng lọc hồ sơ và rất ấn tượng với năng lực chuyên môn của bạn cho vị trí <strong>{jobTitle}</strong>.
                </p>
                <div style=""background: #ECFDF5; border: 1px solid #A7F3D0; border-radius: 8px; padding: 16px; margin: 20px 0;"">
                    <div style=""font-weight: 700; color: #065F46; font-size: 0.95rem; margin-bottom: 4px;"">Bước tiếp theo: Lên lịch phỏng vấn chuyên môn</div>
                    <div style=""font-size: 0.85rem; color: #047857; line-height: 1.5;"">
                        Chuyên viên tuyển dụng của NoveraTech sẽ liên hệ và gửi Thư mời phỏng vấn chính thức (kèm khung giờ và link họp trực tuyến) trong vòng 24 giờ tới.
                    </div>
                </div>
                <p style=""color: #475569; font-size: 0.9rem; line-height: 1.6;"">
                    Chúc bạn có sự chuẩn bị thật tốt và hẹn sớm gặp lại bạn tại buổi trao đổi chuyên môn cùng đội ngũ kỹ thuật NoveraTech!
                </p>
            </div>
            <div style=""background: #F1F5F9; padding: 16px; text-align: center; font-size: 0.75rem; color: #64748B; border-top: 1px solid #E2E8F0;"">
                NoveraTech Careers · Phòng Tuyển dụng Nhân tài Công nghệ NoveraTech
            </div>
        </div>";

        return await SendEmailAsync(new SendEmailRequestDto(candidateEmail, subject, body), cancellationToken);
    }

    public async Task<bool> SendInterviewInvitationAsync(
        string candidateEmail,
        string candidateName,
        string jobTitle,
        string roundTitle,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        string locationOrLink,
        string panelists,
        string? preparationGuide = null,
        string? confirmationUrl = null,
        CancellationToken cancellationToken = default)
    {
        var subject = $"📅 [Thư mời phỏng vấn] Vị trí {jobTitle} — NoveraTech";
        var dateStr = startTime.ToOffset(TimeSpan.FromHours(7)).ToString("dddd, dd/MM/yyyy");
        var timeStr = $"{startTime.ToOffset(TimeSpan.FromHours(7)):HH:mm} - {endTime.ToOffset(TimeSpan.FromHours(7)):HH:mm} (GMT+7)";

        var effectiveGuide = string.IsNullOrWhiteSpace(preparationGuide)
            ? @"• Kiểm tra camera, micro và đường truyền internet ổn định trước giờ hẹn 5 - 10 phút.
• Chuẩn bị sẵn CV, Portfolio hoặc source code dự án tiêu biểu để sẵn sàng trao đổi cùng Hội đồng.
• Trang phục lịch sự, chuyên nghiệp; lựa chọn không gian yên tĩnh."
            : preparationGuide.Trim();

        var guideHtml = effectiveGuide.Replace("\n", "<br/>");

        var confirmButtonHtml = string.IsNullOrWhiteSpace(confirmationUrl)
            ? ""
            : $@"
            <div style=""margin: 24px 0 16px 0; text-align: center;"">
                <a href=""{confirmationUrl}?action=confirm"" style=""display: inline-block; background: #059669; color: #FFFFFF; text-decoration: none; padding: 12px 24px; border-radius: 8px; font-weight: 700; font-size: 0.95rem; margin-right: 8px; box-shadow: 0 2px 4px rgba(5, 150, 105, 0.2);"">
                    ✓ Xác nhận tham dự
                </a>
                <a href=""{confirmationUrl}?action=reschedule"" style=""display: inline-block; background: #F8FAFC; color: #D97706; text-decoration: none; padding: 12px 20px; border-radius: 8px; font-weight: 600; font-size: 0.95rem; border: 1px solid #FCD34D;"">
                    📅 Đề nghị đổi lịch
                </a>
            </div>
            <div style=""text-align: center; margin-bottom: 20px;"">
                <a href=""{confirmationUrl}"" style=""color: #2563EB; font-size: 0.85rem; text-decoration: underline;"">
                    Xem chi tiết buổi phỏng vấn & Hướng dẫn chuẩn bị trực tuyến
                </a>
            </div>
            <div style=""background: #F8FAFC; border: 1px dashed #CBD5E1; border-radius: 8px; padding: 10px 14px; margin-bottom: 16px; font-size: 0.78rem; color: #64748B; text-align: center;"">
                🔒 <strong>Lưu ý bảo mật:</strong> Liên kết trên được tạo riêng cho buổi phỏng vấn này của bạn và chỉ áp dụng cho đúng lịch hẹn được mời.
            </div>";

        var body = $@"
        <div style=""font-family: 'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; max-width: 600px; margin: 0 auto; background: #FFFFFF; border: 1px solid #E2E8F0; border-radius: 12px; overflow: hidden;"">
            <div style=""background: #0F172A; padding: 24px; text-align: center;"">
                <h2 style=""color: #FFFFFF; margin: 0; font-size: 1.25rem; font-weight: 700;"">THƯ MỜI PHỎNG VẤN CHÍNH THỨC</h2>
                <div style=""color: #10B981; font-size: 0.82rem; font-weight: 600; margin-top: 4px;"">NOVERATECH CAREERS & ENGINEERING</div>
            </div>
            <div style=""padding: 28px 24px;"">
                <p style=""font-size: 1rem; color: #0F172A; margin-top: 0;"">Xin chào <strong>{candidateName}</strong>,</p>
                <p style=""color: #475569; font-size: 0.92rem; line-height: 1.6;"">
                    NoveraTech trân trọng kính mời bạn tham gia phiên phỏng vấn cho vị trí <strong>{jobTitle}</strong>. Thông tin chi tiết buổi làm việc như sau:
                </p>
                <div style=""background: #F8FAFC; border: 1px solid #E2E8F0; border-radius: 8px; padding: 16px; margin: 20px 0; font-size: 0.88rem;"">
                    <table style=""width: 100%; border-collapse: collapse;"">
                        <tr>
                            <td style=""padding: 6px 0; color: #64748B; width: 140px;"">Vị trí ứng tuyển:</td>
                            <td style=""padding: 6px 0; color: #0F172A; font-weight: 700;"">{jobTitle}</td>
                        </tr>
                        <tr>
                            <td style=""padding: 6px 0; color: #64748B;"">Vòng phỏng vấn:</td>
                            <td style=""padding: 6px 0; color: #0F172A; font-weight: 700;"">{roundTitle}</td>
                        </tr>
                        <tr>
                            <td style=""padding: 6px 0; color: #64748B;"">Ngày phỏng vấn:</td>
                            <td style=""padding: 6px 0; color: #0F172A; font-weight: 600;"">{dateStr}</td>
                        </tr>
                        <tr>
                            <td style=""padding: 6px 0; color: #64748B;"">Khung giờ:</td>
                            <td style=""padding: 6px 0; color: #059669; font-weight: 700;"">{timeStr}</td>
                        </tr>
                        <tr>
                            <td style=""padding: 6px 0; color: #64748B;"">Hội đồng phỏng vấn:</td>
                            <td style=""padding: 6px 0; color: #0F172A; font-weight: 600;"">{panelists}</td>
                        </tr>
                        <tr>
                            <td style=""padding: 6px 0; color: #64748B;"">Hình thức / Địa điểm:</td>
                            <td style=""padding: 6px 0;"">
                                <a href=""{locationOrLink}"" style=""color: #2563EB; font-weight: 600; text-decoration: underline;"">{locationOrLink}</a>
                            </td>
                        </tr>
                    </table>
                </div>

                <div style=""background: #F0FDF4; border: 1px solid #BBF7D0; border-radius: 8px; padding: 14px 16px; margin-bottom: 20px;"">
                    <div style=""color: #166534; font-weight: 700; font-size: 0.88rem; margin-bottom: 6px;"">
                        📋 Hướng dẫn chuẩn bị cho buổi phỏng vấn:
                    </div>
                    <div style=""color: #14532D; font-size: 0.84rem; line-height: 1.6;"">
                        {guideHtml}
                    </div>
                </div>

                {confirmButtonHtml}

                <p style=""color: #475569; font-size: 0.86rem; line-height: 1.6;"">
                    Vui lòng bấm vào nút <strong>Xác nhận tham dự</strong> hoặc gửi <strong>Đề nghị đổi lịch</strong> trước 24 giờ để Hội đồng phỏng vấn chuẩn bị chu đáo nhất.
                </p>
            </div>
            <div style=""background: #F1F5F9; padding: 16px; text-align: center; font-size: 0.75rem; color: #64748B; border-top: 1px solid #E2E8F0;"">
                NoveraTech Careers · Bộ phận Thu hút Nhân tài NoveraTech · tuyendung@noveratech.com
            </div>
        </div>";

        return await SendEmailAsync(new SendEmailRequestDto(candidateEmail, subject, body), cancellationToken);
    }

    public async Task<bool> SendOfferLetterNotificationAsync(
        string candidateEmail,
        string candidateName,
        string jobTitle,
        decimal baseSalary,
        DateTime? startDate,
        CancellationToken cancellationToken = default)
    {
        var subject = $"🌟 [Chúc mừng] Thư mời nhận việc (Offer Letter) từ NoveraTech — Vị trí {jobTitle}";
        var salaryStr = $"{baseSalary:N0} VNĐ/tháng";
        var dateStr = startDate.HasValue ? startDate.Value.ToString("dd/MM/yyyy") : "Theo thỏa thuận tiếp nhận";

        var body = $@"
        <div style=""font-family: 'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; max-width: 600px; margin: 0 auto; background: #FFFFFF; border: 1px solid #E2E8F0; border-radius: 12px; overflow: hidden;"">
            <div style=""background: linear-gradient(135deg, #059669 0%, #047857 100%); padding: 28px; text-align: center;"">
                <h2 style=""color: #FFFFFF; margin: 0; font-size: 1.35rem; font-weight: 800; letter-spacing: -0.02em;"">CHÚC MỪNG GIA NHẬP NOVERATECH</h2>
                <div style=""color: #D1FAE5; font-size: 0.85rem; margin-top: 4px;"">Thư Mời Nhận Việc Chính Thức (Job Offer)</div>
            </div>
            <div style=""padding: 28px 24px;"">
                <p style=""font-size: 1rem; color: #0F172A; margin-top: 0;"">Kính gửi <strong>{candidateName}</strong>,</p>
                <p style=""color: #475569; font-size: 0.92rem; line-height: 1.6;"">
                    Ban Giám Đốc và Hội đồng Tuyển dụng Tập đoàn Công nghệ NoveraTech trân trọng chúc mừng bạn đã xuất sắc vượt qua các vòng đánh giá chuyên môn và chính thức gửi tới bạn lời mời làm việc tại vị trí <strong>{jobTitle}</strong>.
                </p>
                <div style=""background: #F8FAFC; border: 1px solid #E2E8F0; border-radius: 8px; padding: 18px; margin: 20px 0; font-size: 0.9rem;"">
                    <table style=""width: 100%; border-collapse: collapse;"">
                        <tr>
                            <td style=""padding: 6px 0; color: #64748B; width: 150px;"">Chức danh bổ nhiệm:</td>
                            <td style=""padding: 6px 0; color: #0F172A; font-weight: 700;"">{jobTitle}</td>
                        </tr>
                        <tr>
                            <td style=""padding: 6px 0; color: #64748B;"">Mức lương cơ bản:</td>
                            <td style=""padding: 6px 0; color: #059669; font-weight: 800; font-size: 1.05rem;"">{salaryStr}</td>
                        </tr>
                        <tr>
                            <td style=""padding: 6px 0; color: #64748B;"">Ngày bắt đầu dự kiến:</td>
                            <td style=""padding: 6px 0; color: #0F172A; font-weight: 600;"">{dateStr}</td>
                        </tr>
                        <tr>
                            <td style=""padding: 6px 0; color: #64748B;"">Gói phúc lợi đi kèm:</td>
                            <td style=""padding: 6px 0; color: #334155; font-size: 0.85rem;"">BHXH đầy đủ, NoveraCare VIP, Lương tháng 13+, Thưởng hiệu quả, Hybrid WFH 2 ngày/tuần.</td>
                        </tr>
                    </table>
                </div>
                <p style=""color: #475569; font-size: 0.9rem; line-height: 1.6;"">
                    Chúng tôi tin tưởng rằng với tài năng và tâm huyết của bạn, bạn sẽ cùng NoveraTech tạo nên những bước tiến công nghệ đột phá.
                </p>
            </div>
            <div style=""background: #F1F5F9; padding: 16px; text-align: center; font-size: 0.75rem; color: #64748B; border-top: 1px solid #E2E8F0;"">
                Ban Giám Đốc & Khối Nhân sự Tập đoàn Công nghệ NoveraTech
            </div>
        </div>";

        return await SendEmailAsync(new SendEmailRequestDto(candidateEmail, subject, body), cancellationToken);
    }

    public async Task<bool> SendRejectionLetterAsync(
        string candidateEmail,
        string candidateName,
        string jobTitle,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var subject = $"[NoveraTech Careers] Thư cảm ơn & Kết quả ứng tuyển vị trí {jobTitle}";
        var reasonText = !string.IsNullOrWhiteSpace(reason)
            ? $"<div style=\"background: #F8FAFC; border-left: 3px solid #94A3B8; padding: 10px 14px; margin: 16px 0; font-size: 0.85rem; color: #475569;\"><strong>Ghi chú từ hội đồng:</strong> {reason}</div>"
            : "";

        var body = $@"
        <div style=""font-family: 'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; max-width: 600px; margin: 0 auto; background: #FFFFFF; border: 1px solid #E2E8F0; border-radius: 12px; overflow: hidden;"">
            <div style=""background: #334155; padding: 24px; text-align: center;"">
                <h2 style=""color: #FFFFFF; margin: 0; font-size: 1.2rem; font-weight: 700;"">THƯ CẢM ƠN ỨNG TUYỂN</h2>
                <div style=""color: #CBD5E1; font-size: 0.8rem; margin-top: 4px;"">NoveraTech Careers & Talent Network</div>
            </div>
            <div style=""padding: 28px 24px;"">
                <p style=""font-size: 1rem; color: #0F172A; margin-top: 0;"">Xin chào <strong>{candidateName}</strong>,</p>
                <p style=""color: #475569; font-size: 0.92rem; line-height: 1.6;"">
                    Lời đầu tiên, NoveraTech xin chân thành cảm ơn bạn đã dành thời gian và tâm huyết tham gia quy trình ứng tuyển cho vị trí <strong>{jobTitle}</strong>.
                </p>
                <p style=""color: #475569; font-size: 0.92rem; line-height: 1.6;"">
                    Sau khi cân nhắc kỹ lưỡng giữa hồ sơ của các ứng viên và yêu cầu trọng tâm của dự án ở thời điểm hiện tại, chúng tôi rất tiếc phải thông báo hiện tại chưa thể đồng hành cùng bạn ở vị trí này.
                </p>
                {reasonText}
                <div style=""background: #F8FAFC; border: 1px solid #E2E8F0; border-radius: 8px; padding: 14px; font-size: 0.85rem; color: #475569; line-height: 1.5; margin-top: 16px;"">
                    Hồ sơ của bạn đã được trân trọng lưu vào <strong>Mạng lưới Nhân tài (Talent Pool)</strong> của NoveraTech. Khi có các dự án và cơ hội mới phù hợp với thế mạnh của bạn trong tương lai, chúng tôi sẽ ưu tiên liên hệ lại.
                </div>
                <p style=""color: #475569; font-size: 0.9rem; line-height: 1.6; margin-top: 20px;"">
                    Kính chúc bạn luôn dồi dào sức khỏe và gặt hái được nhiều thành công rực rỡ trên con đường sự nghiệp!
                </p>
            </div>
            <div style=""background: #F1F5F9; padding: 16px; text-align: center; font-size: 0.75rem; color: #64748B; border-top: 1px solid #E2E8F0;"">
                Phòng Tuyển dụng Tập đoàn Công nghệ NoveraTech
            </div>
        </div>";

        return await SendEmailAsync(new SendEmailRequestDto(candidateEmail, subject, body), cancellationToken);
    }

    /// <summary>
    /// Gửi email nhắc việc khi hồ sơ ứng viên quá hạn SLA cho Recruiter phụ trách (SCRUM-327).
    /// </summary>
    public async Task<bool> SendSlaOverdueAlertAsync(
        string recruiterEmail,
        string recruiterName,
        string candidateName,
        string candidateEmail,
        string jobTitle,
        string stageName,
        int daysInStage,
        int slaDays,
        int overdueDays,
        CancellationToken cancellationToken = default)
    {
        var subject = $"⚠️ [Cảnh báo SLA] Hồ sơ ứng viên {candidateName} đã quá hạn {overdueDays} ngày ({stageName})";
        var baseUrl = Environment.GetEnvironmentVariable("APP_BASE_URL") ?? "http://localhost:5266";

        var body = $@"
        <div style=""font-family: 'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; max-width: 600px; margin: 0 auto; background: #FFFFFF; border: 1px solid #FECACA; border-radius: 12px; overflow: hidden; box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.1);"">
            <div style=""background: linear-gradient(135deg, #DC2626 0%, #991B1B 100%); padding: 24px; text-align: center;"">
                <div style=""display: inline-block; background: rgba(255,255,255,0.2); padding: 4px 12px; border-radius: 20px; color: #FEE2E2; font-size: 0.75rem; font-weight: 700; text-transform: uppercase; letter-spacing: 0.05em; margin-bottom: 8px;"">
                    NHẮC VIỆC TUYỂN DỤNG · SLA ALERT
                </div>
                <h2 style=""color: #FFFFFF; margin: 0; font-size: 1.25rem; font-weight: 800;"">HỒ SƠ QUÁ HẠN SLA CẦN XỬ LÝ</h2>
                <div style=""color: #FCA5A5; font-size: 0.85rem; margin-top: 4px;"">Không để hồ sơ ứng viên nằm im quá thời gian quy định</div>
            </div>
            <div style=""padding: 24px;"">
                <p style=""font-size: 0.95rem; color: #0F172A; margin-top: 0;"">Xin chào <strong>{recruiterName}</strong>,</p>
                <p style=""color: #475569; font-size: 0.9rem; line-height: 1.6;"">
                    Hệ thống ATS phát hiện hồ sơ dưới đây đang nằm ở giai đoạn <strong>{stageName}</strong> đã <strong>{daysInStage} ngày</strong>, vượt ngưỡng cam kết SLA là <strong>{slaDays} ngày</strong> (Quá hạn <strong>{overdueDays} ngày</strong>).
                </p>
                <div style=""background: #FFF5F5; border: 1px solid #FED7D7; border-radius: 8px; padding: 16px; margin: 18px 0;"">
                    <table style=""width: 100%; border-collapse: collapse; font-size: 0.88rem;"">
                        <tr>
                            <td style=""padding: 6px 0; color: #64748B; width: 140px;"">Họ tên ứng viên:</td>
                            <td style=""padding: 6px 0; color: #0F172A; font-weight: 700;"">{candidateName}</td>
                        </tr>
                        <tr>
                            <td style=""padding: 6px 0; color: #64748B;"">Email liên hệ:</td>
                            <td style=""padding: 6px 0; color: #0F172A; font-family: monospace;"">{candidateEmail}</td>
                        </tr>
                        <tr>
                            <td style=""padding: 6px 0; color: #64748B;"">Vị trí ứng tuyển:</td>
                            <td style=""padding: 6px 0; color: #0F172A; font-weight: 600;"">{jobTitle}</td>
                        </tr>
                        <tr>
                            <td style=""padding: 6px 0; color: #64748B;"">Giai đoạn hiện tại:</td>
                            <td style=""padding: 6px 0;""><span style=""background: #FEE2E2; color: #991B1B; padding: 2px 8px; border-radius: 4px; font-weight: 700; font-size: 0.8rem;"">{stageName}</span></td>
                        </tr>
                        <tr>
                            <td style=""padding: 6px 0; color: #64748B;"">Thời gian nằm im:</td>
                            <td style=""padding: 6px 0; color: #DC2626; font-weight: 800;"">{daysInStage} ngày (Ngưỡng SLA: {slaDays} ngày)</td>
                        </tr>
                    </table>
                </div>
                <div style=""text-align: center; margin: 24px 0 16px 0;"">
                    <a href=""{baseUrl}/dashboard/ung-vien"" style=""display: inline-block; background: #DC2626; color: #FFFFFF; font-weight: 700; text-decoration: none; padding: 10px 24px; border-radius: 8px; font-size: 0.88rem;"">
                        Truy cập Pipeline xử lý hồ sơ ngay →
                    </a>
                </div>
                <p style=""color: #64748B; font-size: 0.82rem; line-height: 1.5; margin-bottom: 0;"">
                    <em>* Thông báo này được gửi tự động mỗi ngày 1 lần từ Tác vụ định kỳ kiểm tra SLA nội bộ của NoveraTech ATS.</em>
                </p>
            </div>
            <div style=""background: #F8FAFC; padding: 14px; text-align: center; font-size: 0.75rem; color: #94A3B8; border-top: 1px solid #E2E8F0;"">
                Hệ thống Quản lý Tuyển dụng NoveraTech ATS
            </div>
        </div>";

        return await SendEmailAsync(new SendEmailRequestDto(recruiterEmail, subject, body), cancellationToken);
    }
}