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
        CancellationToken cancellationToken = default)
    {
        var subject = "🔒 [Cảnh báo bảo mật] Đăng nhập thành công vào Hệ thống NoveraTech ATS";
        var timeDisplay = loginTime.ToOffset(TimeSpan.FromHours(7)).ToString("HH:mm:ss - dd/MM/yyyy") + " (Giờ Việt Nam)";

        var body = $@"
        <div style=""font-family: 'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; max-width: 600px; margin: 0 auto; background: #FFFFFF; border: 1px solid #E2E8F0; border-radius: 12px; overflow: hidden;"">
            <div style=""background: #0F172A; padding: 24px; text-align: center;"">
                <h2 style=""color: #FFFFFF; margin: 0; font-size: 1.25rem; font-weight: 700; letter-spacing: -0.02em;"">NOVERATECH CAREERS & ATS</h2>
                <div style=""color: #10B981; font-size: 0.8rem; font-weight: 600; margin-top: 4px;"">TRUNG TÂM BẢO MẬT & ĐIỀU HÀNH NHÂN SỰ</div>
            </div>
            <div style=""padding: 28px 24px;"">
                <p style=""font-size: 1rem; color: #0F172A; margin-top: 0;"">Xin chào <strong>{fullName}</strong>,</p>
                <p style=""color: #475569; font-size: 0.92rem; line-height: 1.6;"">
                    Hệ thống ghi nhận tài khoản của bạn vừa đăng nhập thành công vào Cổng thông tin Tuyển dụng & Quản trị NoveraTech với thông số chi tiết dưới đây:
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
                    </table>
                </div>
                <div style=""background: #FFFBEB; border-left: 4px solid #F59E0B; padding: 12px 16px; border-radius: 4px; margin-bottom: 24px;"">
                    <p style=""margin: 0; font-size: 0.82rem; color: #92400E; line-height: 1.5;"">
                        <strong>Lưu ý bảo mật:</strong> Nếu không phải chính bạn thực hiện đăng nhập này, vui lòng truy cập ngay vào mục Đổi mật khẩu hoặc liên hệ Ban An toàn Thông tin NoveraTech qua <em>security@noveratech.digital</em>.
                    </p>
                </div>
                <div style=""text-align: center; margin-top: 24px;"">
                    <a href=""https://noveratech.digital/dashboard"" style=""background: #059669; color: #FFFFFF; text-decoration: none; padding: 10px 24px; border-radius: 8px; font-weight: 600; font-size: 0.9rem; display: inline-block;"">Truy cập Không gian làm việc</a>
                </div>
            </div>
            <div style=""background: #F1F5F9; padding: 16px; text-align: center; font-size: 0.75rem; color: #64748B; border-top: 1px solid #E2E8F0;"">
                Tập đoàn Công nghệ NoveraTech · Hệ thống Tuyển dụng & Quản trị Nhân sự Nội bộ · Email tự động, vui lòng không phản hồi trực tiếp.
            </div>
        </div>";

        return await SendEmailAsync(new SendEmailRequestDto(email, subject, body), cancellationToken);
    }

    public async Task<bool> SendApplicationReceivedAsync(
        string candidateEmail,
        string candidateName,
        string jobTitle,
        string applicationCode,
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
            </div>
            <div style=""background: #F1F5F9; padding: 16px; text-align: center; font-size: 0.75rem; color: #64748B; border-top: 1px solid #E2E8F0;"">
                NoveraTech Careers · Phòng Tuyển dụng & Thu hút Nhân tài Công nghệ · Email: tuyendung@noveratech.com
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
        CancellationToken cancellationToken = default)
    {
        var subject = $"📅 [Thư mời phỏng vấn] Vị trí {jobTitle} — NoveraTech";
        var dateStr = startTime.ToOffset(TimeSpan.FromHours(7)).ToString("dddd, dd/MM/yyyy");
        var timeStr = $"{startTime.ToOffset(TimeSpan.FromHours(7)):HH:mm} - {endTime.ToOffset(TimeSpan.FromHours(7)):HH:mm} (GMT+7)";

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
                            <td style=""padding: 6px 0; color: #64748B; width: 140px;"">Vòng phỏng vấn:</td>
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
                            <td style=""padding: 6px 0; color: #64748B;"">Hình thức / Đường dẫn:</td>
                            <td style=""padding: 6px 0;"">
                                <a href=""{locationOrLink}"" style=""color: #2563EB; font-weight: 600; text-decoration: underline;"">{locationOrLink}</a>
                            </td>
                        </tr>
                    </table>
                </div>
                <p style=""color: #475569; font-size: 0.88rem; line-height: 1.6;"">
                    Vui lòng đăng nhập vào Cổng ứng viên để bấm <strong>Xác nhận tham gia</strong> hoặc gửi yêu cầu đổi lịch nếu khung giờ trên chưa thuận tiện.
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
}