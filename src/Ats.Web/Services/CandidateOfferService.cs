using System.Security.Cryptography;
using System.Text;
using Ats.Web.Data;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Models.ViewModels.Offers;
using Ats.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Ats.Web.Services;

/// <summary>
/// Triển khai nghiệp vụ cho ứng viên nhận thư mời nhận việc và phản hồi (SCRUM-342).
/// Đảm bảo tính bảo mật, cập nhật tức thì trạng thái pipeline, và tự động chuyển sang Hết hạn khi quá thời hạn.
/// </summary>
public class CandidateOfferService(
    ApplicationDbContext dbContext,
    IEmailService emailService,
    ILogger<CandidateOfferService> logger,
    IConfiguration? configuration = null) : ICandidateOfferService
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly IEmailService _emailService = emailService;
    private readonly ILogger<CandidateOfferService> _logger = logger;
    private readonly string _tokenSecret = configuration?["Tokens:OfferSecret"]
                                          ?? configuration?["Tokens:Secret"]
                                          ?? "NoveraTech_Secure_Candidate_Offer_Token_Secret_Key_2026_@ATS";

    public string GenerateOfferResponseToken(Guid offerId, string candidateEmail)
    {
        var emailNormalized = (candidateEmail ?? string.Empty).Trim().ToLowerInvariant();
        var rawData = $"{offerId:D}:{emailNormalized}";

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_tokenSecret));
        var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawData));
        var hashHex = Convert.ToHexString(hashBytes)[..16].ToLowerInvariant();

        return $"{offerId:D}_{hashHex}";
    }

    public bool ValidateOfferResponseToken(string token, out Guid offerId)
    {
        offerId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(token))
            return false;

        var parts = token.Trim().Split('_');
        if (parts.Length != 2)
            return false;

        if (!Guid.TryParse(parts[0], out var parsedId))
            return false;

        var candidateHash = parts[1].Trim().ToLowerInvariant();

        // Tìm offer tương ứng để đối chiếu chữ ký số gắn liền với email ứng viên
        var offer = _dbContext.JobOffers
            .AsNoTracking()
            .Include(o => o.Application)
                .ThenInclude(a => a.Candidate)
            .FirstOrDefault(o => o.Id == parsedId && !o.IsDeleted);

        if (offer == null || offer.Application?.Candidate == null)
            return false;

        var expectedToken = GenerateOfferResponseToken(offer.Id, offer.Application.Candidate.Email);
        var expectedParts = expectedToken.Split('_');
        var expectedHash = expectedParts[1];

        // So sánh chuỗi hash với thời gian thực thi hằng số chống timing attack
        var candidateBytes = Encoding.UTF8.GetBytes(candidateHash);
        var expectedBytes = Encoding.UTF8.GetBytes(expectedHash);

        if (candidateBytes.Length != expectedBytes.Length ||
            !CryptographicOperations.FixedTimeEquals(candidateBytes, expectedBytes))
        {
            _logger.LogWarning("Mã token thư mời nhận việc không hợp lệ hoặc đã bị can thiệp. OfferId: {Id}", parsedId);
            return false;
        }

        offerId = parsedId;
        return true;
    }

    public async Task<CandidateOfferDetailsViewModel?> GetOfferDetailsByTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        if (!ValidateOfferResponseToken(token, out var offerId))
        {
            return null;
        }

        var offer = await _dbContext.JobOffers
            .Include(o => o.Application)
                .ThenInclude(a => a.Candidate)
            .Include(o => o.Application)
                .ThenInclude(a => a.JobPosting)
            .Include(o => o.CreatedByRecruiter)
            .FirstOrDefaultAsync(o => o.Id == offerId && !o.IsDeleted, cancellationToken);

        if (offer == null || offer.Application?.Candidate == null)
        {
            return null;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        bool isPastExpiration = offer.ExpirationDate.HasValue && offer.ExpirationDate.Value < today;

        // Quá thời hạn mà không phản hồi thì offer tự chuyển sang Hết hạn (EXPIRED)
        if (isPastExpiration && (offer.Status == OfferStatus.SENT_TO_CANDIDATE || offer.Status == OfferStatus.APPROVED))
        {
            offer.Status = OfferStatus.EXPIRED;
            offer.ExpiredAt ??= DateTimeOffset.UtcNow;
            offer.UpdatedAt = DateTimeOffset.UtcNow;

            // Ghi nhận lịch sử chuyển trạng thái hết hạn
            if (offer.Application != null)
            {
                await _dbContext.ApplicationStageHistories.AddAsync(new ApplicationStageHistory
                {
                    Id = Guid.NewGuid(),
                    ApplicationId = offer.Application.Id,
                    FromStageId = offer.Application.CurrentStageId,
                    ToStageId = offer.Application.CurrentStageId,
                    ChangedByUserId = offer.CreatedByRecruiterId,
                    Comment = $"Thư mời nhận việc đã tự động chuyển sang HẾT HẠN do quá thời hạn phản hồi (Hạn chót: {offer.ExpirationDate:dd/MM/yyyy}).",
                    CreatedAt = DateTimeOffset.UtcNow
                }, cancellationToken);

                await _dbContext.CandidateInteractions.AddAsync(new CandidateInteraction
                {
                    Id = Guid.NewGuid(),
                    CandidateId = offer.Application.CandidateId,
                    ApplicationId = offer.Application.Id,
                    CreatedByUserId = offer.CreatedByRecruiterId,
                    InteractionType = CandidateInteractionType.NOTE,
                    Title = $"Thư mời nhận việc hết hạn: {offer.JobTitle}",
                    Content = $"Đề xuất nhận việc mức lương {offer.BaseSalary:N0} VNĐ đã hết hạn vào ngày {offer.ExpirationDate:dd/MM/yyyy}.",
                    InteractionDate = DateTimeOffset.UtcNow,
                    CreatedAt = DateTimeOffset.UtcNow
                }, cancellationToken);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Offer {OfferId} đã tự động chuyển sang EXPIRED do quá thời hạn {ExpirationDate}", offer.Id, offer.ExpirationDate);
        }

        var isExpiredNow = offer.Status == OfferStatus.EXPIRED || isPastExpiration;
        var canRespond = !isExpiredNow && offer.Status == OfferStatus.SENT_TO_CANDIDATE;

        // Tính toán thời gian còn lại
        var daysRemaining = 0;
        var hoursRemaining = 0;
        var remainingDisplay = "Đã hết hạn";

        if (!isExpiredNow && offer.ExpirationDate.HasValue)
        {
            var endOfExpirationDayUtc = offer.ExpirationDate.Value.ToDateTime(new TimeOnly(23, 59, 59), DateTimeKind.Utc);
            var timeDiff = endOfExpirationDayUtc - DateTime.UtcNow;
            if (timeDiff.TotalSeconds > 0)
            {
                daysRemaining = (int)timeDiff.TotalDays;
                hoursRemaining = timeDiff.Hours;
                remainingDisplay = daysRemaining > 0
                    ? $"Còn {daysRemaining} ngày {hoursRemaining} giờ"
                    : $"Còn {hoursRemaining} giờ";
            }
        }

        var cand = offer.Application.Candidate;
        var candName = $"{cand.FirstName} {cand.LastName}".Trim();
        var depName = "Khối Công nghệ Thông tin";
        if (offer.Application.JobPosting != null && offer.Application.JobPosting.RequisitionId != Guid.Empty)
        {
            var req = await _dbContext.JobRequisitions
                .Include(r => r.Department)
                .FirstOrDefaultAsync(r => r.Id == offer.Application.JobPosting.RequisitionId, cancellationToken);
            if (!string.IsNullOrWhiteSpace(req?.Department?.Name))
            {
                depName = req.Department.Name;
            }
        }
        var jobTitle = !string.IsNullOrWhiteSpace(offer.JobTitle)
            ? offer.JobTitle
            : (offer.Application.JobPosting?.Title ?? "Chuyên viên");

        var recruiterName = offer.CreatedByRecruiter != null
            ? offer.CreatedByRecruiter.FullName
            : "Ban Tuyển dụng NoveraTech";

        return new CandidateOfferDetailsViewModel
        {
            OfferId = offer.Id,
            ApplicationId = offer.ApplicationId,
            Token = token,
            CandidateUserId = cand.UserId,
            CandidateName = candName,
            CandidateEmail = cand.Email,
            CandidatePhone = cand.Phone,
            JobTitle = jobTitle,
            DepartmentName = depName,
            WorkLocation = !string.IsNullOrWhiteSpace(offer.WorkLocation)
                ? offer.WorkLocation
                : (offer.Application.JobPosting?.WorkLocation ?? "Tòa nhà Novera Tower, Cầu Giấy, Hà Nội"),
            ContractType = !string.IsNullOrWhiteSpace(offer.ContractType)
                ? offer.ContractType
                : "Chính thức (Toàn thời gian)",
            BaseSalary = offer.BaseSalary,
            BonusAllowance = offer.BonusAllowance ?? 0,
            TotalPackage = offer.TotalPackage > 0 ? offer.TotalPackage : (offer.BaseSalary + (offer.BonusAllowance ?? 0)),
            ProbationPeriodMonths = offer.ProbationPeriodMonths ?? 2,
            ProbationSalaryPercentage = 85,
            ProposedJoinDate = offer.ProposedJoinDate,
            ExpirationDate = offer.ExpirationDate,
            IsExpired = isExpiredNow,
            DaysRemaining = daysRemaining,
            HoursRemaining = hoursRemaining,
            RemainingTimeDisplay = remainingDisplay,
            Status = offer.Status,
            StatusDisplay = GetStatusDisplay(offer.Status),
            CanRespond = canRespond,
            CandidateRespondedAt = offer.CandidateRespondedAt,
            CandidateFeedback = offer.CandidateFeedback,
            OfferLetterPath = offer.OfferLetterPath,
            Notes = offer.Notes,
            RecruiterName = recruiterName,
            CompanyName = "Tập đoàn Công nghệ NoveraTech"
        };
    }

    public async Task<CandidateOfferResponseResult> SubmitOfferResponseAsync(
        CandidateOfferResponseInputModel model,
        CancellationToken cancellationToken = default)
    {
        if (!ValidateOfferResponseToken(model.Token, out var offerId))
        {
            return CandidateOfferResponseResult.Fail("Mã xác thực thư mời nhận việc không hợp lệ hoặc đã bị can thiệp.");
        }

        var offer = await _dbContext.JobOffers
            .Include(o => o.Application)
                .ThenInclude(a => a.Candidate)
            .Include(o => o.CreatedByRecruiter)
            .FirstOrDefaultAsync(o => o.Id == offerId && !o.IsDeleted, cancellationToken);

        if (offer == null || offer.Application == null || offer.Application.Candidate == null)
        {
            return CandidateOfferResponseResult.Fail("Không tìm thấy thông tin thư mời nhận việc tương ứng.");
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        bool isPastExpiration = offer.ExpirationDate.HasValue && offer.ExpirationDate.Value < today;

        // Quá thời hạn phản hồi
        if (isPastExpiration || offer.Status == OfferStatus.EXPIRED)
        {
            if (offer.Status != OfferStatus.EXPIRED)
            {
                offer.Status = OfferStatus.EXPIRED;
                offer.ExpiredAt ??= DateTimeOffset.UtcNow;
                offer.UpdatedAt = DateTimeOffset.UtcNow;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            return CandidateOfferResponseResult.Fail($"Thư mời nhận việc này đã hết thời hạn phản hồi vào ngày {offer.ExpirationDate:dd/MM/yyyy}. Đề xuất đã tự động chuyển sang trạng thái Hết hạn.");
        }

        // Đã phản hồi trước đó
        if (offer.Status == OfferStatus.ACCEPTED)
        {
            return CandidateOfferResponseResult.Fail($"Bạn đã CHẤP NHẬN thư mời nhận việc này vào ngày {offer.CandidateRespondedAt?.ToLocalTime():dd/MM/yyyy HH:mm}. Không thể gửi lại phản hồi.");
        }

        if (offer.Status == OfferStatus.REJECTED)
        {
            return CandidateOfferResponseResult.Fail($"Bạn đã TỪ CHỐI thư mời nhận việc này vào ngày {offer.CandidateRespondedAt?.ToLocalTime():dd/MM/yyyy HH:mm}. Không thể gửi lại phản hồi.");
        }

        if (offer.Status == OfferStatus.WITHDRAWN)
        {
            return CandidateOfferResponseResult.Fail("Thư mời nhận việc này đã được rút lại bởi bộ phận tuyển dụng.");
        }

        var decisionNormalized = (model.Decision ?? string.Empty).Trim().ToUpperInvariant();
        var cand = offer.Application.Candidate;
        var candName = $"{cand.FirstName} {cand.LastName}".Trim();
        var feedbackClean = !string.IsNullOrWhiteSpace(model.Feedback) ? model.Feedback.Trim() : null;

        var prevStageId = offer.Application.CurrentStageId;

        if (decisionNormalized is "ACCEPT" or "ACCEPTED" or "CHẤP NHẬN" or "CHAP NHAN")
        {
            // 1. Cập nhật Offer
            offer.Status = OfferStatus.ACCEPTED;
            offer.CandidateRespondedAt = DateTimeOffset.UtcNow;
            offer.CandidateFeedback = feedbackClean;
            offer.UpdatedAt = DateTimeOffset.UtcNow;

            // 2. Cập nhật ngay trạng thái trên Pipeline: Tuyển dụng thành công / Hired
            var hiredStage = await _dbContext.PipelineStages
                .FirstOrDefaultAsync(s => s.StageOrder == 6, cancellationToken)
                ?? await _dbContext.PipelineStages.FirstOrDefaultAsync(s => s.Name.Contains("thành công") || s.Name.Contains("Hired"), cancellationToken);

            if (hiredStage != null && offer.Application.CurrentStageId != hiredStage.Id)
            {
                offer.Application.CurrentStageId = hiredStage.Id;
                offer.Application.StageEnteredAt = DateTimeOffset.UtcNow;
            }

            offer.Application.Status = ApplicationStatus.HIRED;
            offer.Application.UpdatedAt = DateTimeOffset.UtcNow;

            // 3. Ghi lịch sử chuyển giai đoạn pipeline
            await _dbContext.ApplicationStageHistories.AddAsync(new ApplicationStageHistory
            {
                Id = Guid.NewGuid(),
                ApplicationId = offer.Application.Id,
                FromStageId = prevStageId,
                ToStageId = offer.Application.CurrentStageId,
                ChangedByUserId = offer.CreatedByRecruiterId,
                Comment = $"Ứng viên đã chính thức CHẤP NHẬN thư mời nhận việc qua liên kết phản hồi trực tuyến. " +
                          $"{(string.IsNullOrWhiteSpace(feedbackClean) ? "Không có ghi chú thêm." : $"Ý kiến của ứng viên: \"{feedbackClean}\"")}",
                CreatedAt = DateTimeOffset.UtcNow
            }, cancellationToken);

            // 4. Ghi tương tác ứng viên (CandidateInteraction)
            await _dbContext.CandidateInteractions.AddAsync(new CandidateInteraction
            {
                Id = Guid.NewGuid(),
                CandidateId = cand.Id,
                ApplicationId = offer.Application.Id,
                CreatedByUserId = offer.CreatedByRecruiterId,
                InteractionType = CandidateInteractionType.NOTE,
                Title = $"Ứng viên chấp nhận Thư mời nhận việc: {offer.JobTitle}",
                Content = $"Ứng viên {candName} đã đồng ý nhận việc vị trí {offer.JobTitle} (Lương cơ bản: {offer.BaseSalary:N0} VNĐ/tháng). {(string.IsNullOrWhiteSpace(feedbackClean) ? "" : $"Ý kiến phản hồi: {feedbackClean}")}",
                InteractionDate = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow
            }, cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Ứng viên {CandName} đã CHẤP NHẬN offer {OfferId}. Pipeline cập nhật sang HIRED.", candName, offer.Id);

            return CandidateOfferResponseResult.Ok(
                $"🎉 Chúc mừng bạn đã hoàn tất chấp nhận Thư mời nhận việc vị trí {offer.JobTitle} tại NoveraTech! " +
                $"Bộ phận nhân sự sẽ liên hệ với bạn để hoàn thiện các thủ tục tiếp nhận.",
                OfferStatus.ACCEPTED,
                feedbackClean);
        }
        else if (decisionNormalized is "DECLINE" or "REJECT" or "REJECTED" or "TỪ CHỐI" or "TU CHOI")
        {
            // 1. Cập nhật Offer
            offer.Status = OfferStatus.REJECTED;
            offer.CandidateRespondedAt = DateTimeOffset.UtcNow;
            offer.CandidateFeedback = feedbackClean;
            offer.UpdatedAt = DateTimeOffset.UtcNow;

            // 2. Cập nhật trạng thái trên pipeline: REJECTED
            offer.Application.Status = ApplicationStatus.REJECTED;
            offer.Application.UpdatedAt = DateTimeOffset.UtcNow;

            // 3. Ghi lịch sử chuyển giai đoạn pipeline
            await _dbContext.ApplicationStageHistories.AddAsync(new ApplicationStageHistory
            {
                Id = Guid.NewGuid(),
                ApplicationId = offer.Application.Id,
                FromStageId = prevStageId,
                ToStageId = prevStageId,
                ChangedByUserId = offer.CreatedByRecruiterId,
                Comment = $"Ứng viên TỪ CHỐI thư mời nhận việc qua liên kết phản hồi trực tuyến. " +
                          $"{(string.IsNullOrWhiteSpace(feedbackClean) ? "Không có lý do ghi kèm." : $"Ý kiến/Lý do từ chối: \"{feedbackClean}\"")}",
                CreatedAt = DateTimeOffset.UtcNow
            }, cancellationToken);

            // 4. Ghi tương tác ứng viên (CandidateInteraction)
            await _dbContext.CandidateInteractions.AddAsync(new CandidateInteraction
            {
                Id = Guid.NewGuid(),
                CandidateId = cand.Id,
                ApplicationId = offer.Application.Id,
                CreatedByUserId = offer.CreatedByRecruiterId,
                InteractionType = CandidateInteractionType.NOTE,
                Title = $"Ứng viên từ chối Thư mời nhận việc: {offer.JobTitle}",
                Content = $"Ứng viên {candName} từ chối đề xuất nhận việc vị trí {offer.JobTitle}. Lý do/Ý kiến phản hồi: {feedbackClean}",
                InteractionDate = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow
            }, cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Ứng viên {CandName} đã TỪ CHỐI offer {OfferId}. Pipeline cập nhật sang REJECTED.", candName, offer.Id);

            return CandidateOfferResponseResult.Ok(
                $"Cảm ơn bạn đã phản hồi thư mời nhận việc. Chúng tôi đã ghi nhận quyết định của bạn và chúc bạn luôn thành công trên con đường sự nghiệp.",
                OfferStatus.REJECTED,
                feedbackClean);
        }

        return CandidateOfferResponseResult.Fail("Quyết định lựa chọn không hợp lệ. Vui lòng chọn Chấp nhận hoặc Từ chối.");
    }

    public async Task<(bool Success, string Message, string? ResponseUrl)> SendOfferToCandidateAsync(
        Guid offerId,
        Guid recruiterUserId,
        string? customMessage = null,
        string? baseUrl = null,
        CancellationToken cancellationToken = default)
    {
        var offer = await _dbContext.JobOffers
            .Include(o => o.Application)
                .ThenInclude(a => a.Candidate)
            .Include(o => o.Application)
                .ThenInclude(a => a.JobPosting)
            .FirstOrDefaultAsync(o => o.Id == offerId && !o.IsDeleted, cancellationToken);

        if (offer == null || offer.Application?.Candidate == null)
        {
            return (false, "Không tìm thấy thông tin đề xuất Offer hoặc ứng viên.", null);
        }

        if (offer.Status != OfferStatus.APPROVED && offer.Status != OfferStatus.SENT_TO_CANDIDATE)
        {
            return (false, $"Đề xuất Offer đang ở trạng thái '{GetStatusDisplay(offer.Status)}', chỉ có thể gửi khi đã được phê duyệt.", null);
        }

        var cand = offer.Application.Candidate;
        var candName = $"{cand.FirstName} {cand.LastName}".Trim();
        var candEmail = cand.Email;

        if (string.IsNullOrWhiteSpace(candEmail))
        {
            return (false, "Ứng viên chưa có thông tin địa chỉ email để gửi thư mời.", null);
        }

        // Đảm bảo thời hạn phản hồi
        if (!offer.ExpirationDate.HasValue)
        {
            offer.ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));
        }

        // Sinh token bảo mật HMAC
        var token = GenerateOfferResponseToken(offer.Id, candEmail);
        offer.ResponseToken = token;
        offer.Status = OfferStatus.SENT_TO_CANDIDATE;
        offer.UpdatedAt = DateTimeOffset.UtcNow;

        var hostUrl = !string.IsNullOrWhiteSpace(baseUrl) ? baseUrl.TrimEnd('/') : "";
        var responseUrl = $"{hostUrl}/ung-vien/offer/{Uri.EscapeDataString(token)}";

        // Gửi email chính thức
        var jobTitle = !string.IsNullOrWhiteSpace(offer.JobTitle)
            ? offer.JobTitle
            : (offer.Application.JobPosting?.Title ?? "Chuyên viên");

        var startDate = offer.ProposedJoinDate.HasValue
            ? offer.ProposedJoinDate.Value.ToDateTime(TimeOnly.MinValue)
            : (DateTime?)null;

        var emailSent = await _emailService.SendOfferLetterNotificationAsync(
            candidateEmail: candEmail,
            candidateName: candName,
            jobTitle: jobTitle,
            baseSalary: offer.BaseSalary,
            startDate: startDate,
            responseUrl: responseUrl,
            expirationDate: offer.ExpirationDate,
            workLocation: offer.WorkLocation,
            bonusAllowance: offer.BonusAllowance,
            notes: customMessage ?? offer.Notes,
            cancellationToken: cancellationToken);

        // Ghi lại tương tác
        await _dbContext.CandidateInteractions.AddAsync(new CandidateInteraction
        {
            Id = Guid.NewGuid(),
            CandidateId = cand.Id,
            ApplicationId = offer.Application.Id,
            CreatedByUserId = recruiterUserId,
            InteractionType = CandidateInteractionType.EMAIL,
            Title = $"Đã gửi Thư mời nhận việc: {jobTitle}",
            Content = $"Đã gửi thư mời nhận việc với mức lương {offer.BaseSalary:N0} VNĐ cho ứng viên qua email {candEmail}. Hạn phản hồi: {offer.ExpirationDate:dd/MM/yyyy}.",
            InteractionDate = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        }, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Đã gửi thư mời nhận việc Offer {OfferId} cho ứng viên {Email}. Token: {Token}", offer.Id, candEmail, token);

        return (true, $"✅ Đã gửi thư mời nhận việc thành công tới ứng viên {candName} ({candEmail}). Email chứa liên kết phản hồi có hiệu lực đến hết ngày {offer.ExpirationDate:dd/MM/yyyy}.", responseUrl);
    }

    public async Task<int> CheckAndExpireOverdueOffersAsync(CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var overdueOffers = await _dbContext.JobOffers
            .Include(o => o.Application)
            .Where(o => !o.IsDeleted &&
                        (o.Status == OfferStatus.SENT_TO_CANDIDATE || o.Status == OfferStatus.APPROVED) &&
                        o.ExpirationDate.HasValue &&
                        o.ExpirationDate.Value < today)
            .ToListAsync(cancellationToken);

        if (overdueOffers.Count == 0)
        {
            return 0;
        }

        foreach (var offer in overdueOffers)
        {
            offer.Status = OfferStatus.EXPIRED;
            offer.ExpiredAt = DateTimeOffset.UtcNow;
            offer.UpdatedAt = DateTimeOffset.UtcNow;

            if (offer.Application != null)
            {
                await _dbContext.ApplicationStageHistories.AddAsync(new ApplicationStageHistory
                {
                    Id = Guid.NewGuid(),
                    ApplicationId = offer.Application.Id,
                    FromStageId = offer.Application.CurrentStageId,
                    ToStageId = offer.Application.CurrentStageId,
                    ChangedByUserId = offer.CreatedByRecruiterId,
                    Comment = $"Thư mời nhận việc đã tự động chuyển sang HẾT HẠN do quá thời hạn phản hồi ({offer.ExpirationDate:dd/MM/yyyy}).",
                    CreatedAt = DateTimeOffset.UtcNow
                }, cancellationToken);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Đã tự động chuyển {Count} offer quá hạn sang EXPIRED", overdueOffers.Count);

        return overdueOffers.Count;
    }

    private static string GetStatusDisplay(OfferStatus status) => status switch
    {
        OfferStatus.DRAFT => "Lưu nháp",
        OfferStatus.PENDING_APPROVAL => "Chờ phê duyệt",
        OfferStatus.APPROVED => "Đã duyệt",
        OfferStatus.SENT_TO_CANDIDATE => "Đã gửi ứng viên",
        OfferStatus.ACCEPTED => "Ứng viên đã nhận việc",
        OfferStatus.REJECTED => "Bị từ chối",
        OfferStatus.WITHDRAWN => "Đã rút lại",
        OfferStatus.EXPIRED => "Hết hạn",
        _ => status.ToString()
    };
}
