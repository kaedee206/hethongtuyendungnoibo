using Ats.Web.Data;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Models.ViewModels.Offers;
using Ats.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ats.Web.Services;

/// <summary>
/// Dịch vụ xử lý nghiệp vụ soạn thảo, lưu nháp, kiểm tra dải lương và xem trước Offer (SCRUM-340).
/// </summary>
public class OfferService(
    ApplicationDbContext dbContext,
    IEmailService emailService,
    ILogger<OfferService> logger,
    IRequisitionAssignmentService? assignmentService = null) : IOfferService
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly IEmailService _emailService = emailService;
    private readonly ILogger<OfferService> _logger = logger;
    private readonly IRequisitionAssignmentService? _assignmentService = assignmentService;

    /// <inheritdoc />
    public async Task<JobOfferFormInitViewModel> GetOfferFormInitDataAsync(
        Guid applicationId,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var application = await _dbContext.Applications
            .Include(a => a.Candidate)
            .Include(a => a.JobPosting)
                .ThenInclude(jp => jp.Requisition)
                    .ThenInclude(r => r.JobPosition)
            .Include(a => a.JobPosting)
                .ThenInclude(jp => jp.Requisition)
                    .ThenInclude(r => r.Department)
            .FirstOrDefaultAsync(a => a.Id == applicationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Không tìm thấy hồ sơ ứng tuyển với mã {applicationId}.");

        var requisition = application.JobPosting.Requisition;
        var defaultPosition = requisition.JobPosition;

        var standardMin = defaultPosition?.MinSalary ?? requisition.MinSalary;
        var standardMax = defaultPosition?.MaxSalary ?? requisition.MaxSalary;

        string rangeDisplay = (standardMin.HasValue || standardMax.HasValue)
            ? $"{(standardMin.HasValue ? standardMin.Value.ToString("N0") : "---")} – {(standardMax.HasValue ? standardMax.Value.ToString("N0") : "---")} VNĐ"
            : "Chưa cấu hình dải chuẩn";

        // Lấy danh sách chức danh hiện có để recruiter chọn nếu cần đổi
        var availablePositions = await _dbContext.JobPositions
            .Where(p => p.IsActive)
            .OrderBy(p => p.Title)
            .Select(p => new JobPositionOptionViewModel
            {
                Id = p.Id,
                Code = p.Code,
                Title = p.Title,
                MinSalary = p.MinSalary,
                MaxSalary = p.MaxSalary,
                SalaryRangeDisplay = (p.MinSalary.HasValue || p.MaxSalary.HasValue)
                    ? $"{(p.MinSalary.HasValue ? p.MinSalary.Value.ToString("N0") : "---")} – {(p.MaxSalary.HasValue ? p.MaxSalary.Value.ToString("N0") : "---")} VNĐ"
                    : "Chưa thiết lập"
            })
            .ToListAsync(cancellationToken);

        // Kiểm tra offer hiện tại của hồ sơ này
        var existingOffer = await _dbContext.JobOffers
            .Include(o => o.JobPosition)
            .FirstOrDefaultAsync(o => o.ApplicationId == applicationId, cancellationToken);

        // Kiểm tra xem ứng viên này đã có offer nào khác đang hiệu lực không (SCRUM-340)
        var otherActiveOffer = await _dbContext.JobOffers
            .Include(o => o.Application)
                .ThenInclude(a => a.JobPosting)
            .FirstOrDefaultAsync(o => o.Application.CandidateId == application.CandidateId
                                      && (existingOffer == null || o.Id != existingOffer.Id)
                                      && o.Status != OfferStatus.REJECTED
                                      && o.Status != OfferStatus.WITHDRAWN, cancellationToken);

        var candName = $"{application.Candidate.FirstName} {application.Candidate.LastName}".Trim();

        return new JobOfferFormInitViewModel
        {
            ApplicationId = application.Id,
            CandidateId = application.CandidateId,
            CandidateName = candName,
            CandidateEmail = application.Candidate.Email,
            CandidatePhone = application.Candidate.PhoneNumber,
            JobPostingTitle = application.JobPosting.Title,
            DepartmentName = requisition.Department?.Name ?? "Chưa phân bổ",
            WorkLocation = application.JobPosting.WorkLocation,
            DefaultJobPositionId = defaultPosition?.Id,
            DefaultJobTitle = defaultPosition?.Title ?? application.JobPosting.Title,
            StandardMinSalary = standardMin,
            StandardMaxSalary = standardMax,
            StandardSalaryRangeDisplay = rangeDisplay,
            ExistingOfferId = existingOffer?.Id,
            ExistingOfferStatus = existingOffer?.Status,
            ExistingOfferStatusDisplay = existingOffer != null ? GetStatusDisplay(existingOffer.Status) : null,
            ExistingBaseSalary = existingOffer?.BaseSalary,
            ExistingBonusAllowance = existingOffer?.BonusAllowance,
            ExistingProposedJoinDate = existingOffer?.ProposedJoinDate,
            ExistingExpirationDate = existingOffer?.ExpirationDate,
            ExistingProbationMonths = existingOffer?.ProbationPeriodMonths,
            ExistingContractType = existingOffer?.ContractType,
            ExistingSalaryJustification = existingOffer?.SalaryJustification,
            ExistingNotes = existingOffer?.Notes,
            ExistingIsOutsideBand = existingOffer?.IsOutsideBand ?? false,
            HasOtherActiveOffer = otherActiveOffer != null,
            OtherActiveOfferWarning = otherActiveOffer != null
                ? $"Ứng viên {candName} hiện đã có một đề xuất Offer đang có hiệu lực cho vị trí '{otherActiveOffer.Application?.JobPosting?.Title}' (Mã: {otherActiveOffer.Id}, Trạng thái: {GetStatusDisplay(otherActiveOffer.Status)}). Mỗi ứng viên chỉ được phép có tối đa 1 Offer có hiệu lực tại một thời điểm."
                : null,
            AvailablePositions = availablePositions
        };
    }

    /// <inheritdoc />
    public async Task<JobOfferPreviewViewModel> GenerateOfferPreviewAsync(
        JobOfferInputModel input,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var application = await _dbContext.Applications
            .Include(a => a.Candidate)
            .Include(a => a.JobPosting)
                .ThenInclude(jp => jp.Requisition)
                    .ThenInclude(r => r.Department)
            .FirstOrDefaultAsync(a => a.Id == input.ApplicationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Không tìm thấy hồ sơ ứng tuyển với mã {input.ApplicationId}.");

        var recruiter = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == currentUserId, cancellationToken);
        var recruiterName = recruiter != null ? $"{recruiter.FirstName} {recruiter.LastName}".Trim() : "Phòng Nhân sự NoveraTech";

        var candFullName = $"{application.Candidate.FirstName} {application.Candidate.LastName}".Trim();
        var allowances = input.BonusAllowance ?? 0;
        var total = input.TotalPackage ?? (input.BaseSalary + allowances);
        var joinDate = input.ProposedJoinDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14));
        var expDate = input.ExpirationDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));

        return new JobOfferPreviewViewModel
        {
            ApplicationId = application.Id,
            OfferId = input.OfferId,
            CandidateName = candFullName,
            CandidateEmail = application.Candidate.Email,
            CandidatePhone = application.Candidate.PhoneNumber,
            JobTitle = !string.IsNullOrWhiteSpace(input.JobTitle) ? input.JobTitle : application.JobPosting.Title,
            DepartmentName = application.JobPosting.Requisition?.Department?.Name ?? "Khối Công nghệ & Kỹ thuật",
            WorkLocation = !string.IsNullOrWhiteSpace(input.WorkLocation)
                ? input.WorkLocation
                : (string.IsNullOrWhiteSpace(application.JobPosting.WorkLocation)
                    ? "Tòa nhà Novera Tower, Cầu Giấy, Hà Nội"
                    : application.JobPosting.WorkLocation),
            BaseSalary = input.BaseSalary,
            BonusAllowance = allowances,
            TotalPackage = total,
            ProposedJoinDate = joinDate,
            ExpirationDate = expDate,
            ProbationPeriodMonths = input.ProbationPeriodMonths ?? 2,
            ProbationSalaryPercentage = 85,
            ContractType = !string.IsNullOrWhiteSpace(input.ContractType) ? input.ContractType : "Chính thức (Toàn thời gian)",
            Notes = input.Notes,
            RecruiterName = recruiterName,
            RecruiterTitle = "Chuyên viên Tuyển dụng Cao cấp",
            CompanyName = "Tập đoàn Công nghệ NoveraTech",
            GeneratedDateFormatted = DateTime.UtcNow.ToString("dd/MM/yyyy"),
            SalaryJustification = input.SalaryJustification
        };
    }

    /// <inheritdoc />
    public async Task<OfferOperationResult> SaveOfferAsync(
        JobOfferInputModel input,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        // 1. Kiểm tra tồn tại hồ sơ
        var application = await _dbContext.Applications
            .Include(a => a.Candidate)
            .Include(a => a.JobPosting)
                .ThenInclude(jp => jp.Requisition)
                    .ThenInclude(r => r.JobPosition)
            .Include(a => a.CurrentStage)
            .FirstOrDefaultAsync(a => a.Id == input.ApplicationId, cancellationToken);

        if (application == null)
        {
            return OfferOperationResult.Fail("Không tìm thấy hồ sơ ứng viên.");
        }

        // 2. Kiểm tra phân quyền Recruiter theo vị trí phụ trách (Scrum 26)
        if (_assignmentService != null && currentUserId != Guid.Empty)
        {
            var canAccess = await _assignmentService.CanRecruiterAccessApplicationAsync(currentUserId, input.ApplicationId, cancellationToken);
            if (!canAccess)
            {
                return OfferOperationResult.Fail("Bạn không có quyền thao tác đề xuất Offer cho hồ sơ này do không phụ trách vị trí tuyển dụng tương ứng.");
            }
        }

        var candidateFullName = $"{application.Candidate.FirstName} {application.Candidate.LastName}".Trim();

        // 3. Nghiệp vụ: Một ứng viên tại một thời điểm chỉ có một offer đang hiệu lực (SCRUM-340)
        var existingActiveOffer = await _dbContext.JobOffers
            .Include(o => o.Application)
                .ThenInclude(a => a.JobPosting)
            .FirstOrDefaultAsync(o => o.Application.CandidateId == application.CandidateId
                                      && (!input.OfferId.HasValue || o.Id != input.OfferId.Value)
                                      && o.ApplicationId != application.Id // Cho phép sửa offer của chính application này
                                      && o.Status != OfferStatus.REJECTED
                                      && o.Status != OfferStatus.WITHDRAWN, cancellationToken);

        if (existingActiveOffer != null)
        {
            return OfferOperationResult.Fail(
                $"Ứng viên {candidateFullName} hiện đã có 1 đề xuất Offer đang có hiệu lực tại vị trí '{existingActiveOffer.Application?.JobPosting?.Title}' " +
                $"(Trạng thái: {GetStatusDisplay(existingActiveOffer.Status)}). Mỗi ứng viên tại một thời điểm chỉ được phép có tối đa 1 offer có hiệu lực. " +
                $"Vui lòng rút lại (Withdraw) hoặc hoàn tất offer cũ trước khi tạo đề xuất mới.");
        }

        // 4. Kiểm tra ngày tháng
        if (!input.ProposedJoinDate.HasValue)
        {
            return OfferOperationResult.Fail("Vui lòng nhập Ngày dự kiến nhận việc.");
        }

        if (!input.ExpirationDate.HasValue)
        {
            return OfferOperationResult.Fail("Vui lòng nhập Thời hạn phản hồi Offer.");
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (input.ProposedJoinDate.Value < today)
        {
            return OfferOperationResult.Fail("Ngày dự kiến nhận việc không được ở trong quá khứ.");
        }

        if (input.ExpirationDate.Value < today)
        {
            return OfferOperationResult.Fail("Thời hạn phản hồi Offer không được ở trong quá khứ.");
        }

        if (input.ExpirationDate.Value > input.ProposedJoinDate.Value)
        {
            return OfferOperationResult.Fail("Thời hạn phản hồi Offer phải trước hoặc cùng ngày với Ngày dự kiến nhận việc.");
        }

        // 5. Kiểm tra mức lương ngoài dải chuẩn của chức danh bắt buộc nhập giải trình (SCRUM-340)
        JobPosition? position = null;
        if (input.JobPositionId.HasValue)
        {
            position = await _dbContext.JobPositions.FirstOrDefaultAsync(p => p.Id == input.JobPositionId.Value, cancellationToken);
        }
        position ??= application.JobPosting.Requisition?.JobPosition;

        var standardMin = position?.MinSalary ?? application.JobPosting.Requisition?.MinSalary;
        var standardMax = position?.MaxSalary ?? application.JobPosting.Requisition?.MaxSalary;

        bool isOutsideBand = (standardMin.HasValue && input.BaseSalary < standardMin.Value) ||
                             (standardMax.HasValue && input.BaseSalary > standardMax.Value);

        if (isOutsideBand && string.IsNullOrWhiteSpace(input.SalaryJustification))
        {
            string bandText = (standardMin.HasValue || standardMax.HasValue)
                ? $"{(standardMin.HasValue ? standardMin.Value.ToString("N0") : "---")} – {(standardMax.HasValue ? standardMax.Value.ToString("N0") : "---")} VNĐ"
                : "dải lương quy định";

            return OfferOperationResult.Fail(
                $"Mức lương đề xuất ({input.BaseSalary:N0} VNĐ) nằm ngoài dải chuẩn của chức danh ({bandText}). " +
                $"Bắt buộc phải nhập lý do giải trình chi tiết.");
        }

        var allowances = input.BonusAllowance ?? 0;
        var total = input.TotalPackage ?? (input.BaseSalary + allowances);
        var recruiterId = currentUserId != Guid.Empty ? currentUserId : (await _dbContext.Users.Select(u => u.Id).FirstOrDefaultAsync(cancellationToken));

        // 6. Tìm bản ghi JobOffer hiện có của hồ sơ
        var offer = await _dbContext.JobOffers.FirstOrDefaultAsync(o => o.ApplicationId == application.Id, cancellationToken);

        if (offer == null)
        {
            offer = new JobOffer
            {
                Id = Guid.NewGuid(),
                ApplicationId = application.Id,
                CreatedByRecruiterId = recruiterId,
                JobPositionId = position?.Id,
                JobTitle = !string.IsNullOrWhiteSpace(input.JobTitle) ? input.JobTitle : (position?.Title ?? application.JobPosting.Title),
                WorkLocation = input.WorkLocation ?? application.JobPosting.WorkLocation,
                BaseSalary = input.BaseSalary,
                BonusAllowance = input.BonusAllowance,
                TotalPackage = total,
                ProposedJoinDate = input.ProposedJoinDate,
                ExpirationDate = input.ExpirationDate,
                ProbationPeriodMonths = input.ProbationPeriodMonths ?? 2,
                ContractType = input.ContractType ?? "Chính thức (Toàn thời gian)",
                IsOutsideBand = isOutsideBand,
                IsAboveBudget = standardMax.HasValue && input.BaseSalary > standardMax.Value,
                SalaryJustification = input.SalaryJustification,
                Notes = input.Notes,
                Status = input.IsDraft ? OfferStatus.DRAFT : OfferStatus.PENDING_APPROVAL,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await _dbContext.JobOffers.AddAsync(offer, cancellationToken);
        }
        else
        {
            // Kiểm tra trạng thái offer cũ
            if (offer.Status == OfferStatus.APPROVED || offer.Status == OfferStatus.ACCEPTED)
            {
                return OfferOperationResult.Fail($"Offer của ứng viên đang ở trạng thái '{GetStatusDisplay(offer.Status)}', không thể chỉnh sửa trực tiếp. Vui lòng rút lại (Withdraw) trước nếu cần tạo mới.");
            }

            offer.JobPositionId = position?.Id ?? offer.JobPositionId;
            offer.JobTitle = !string.IsNullOrWhiteSpace(input.JobTitle) ? input.JobTitle : offer.JobTitle;
            offer.WorkLocation = input.WorkLocation ?? offer.WorkLocation;
            offer.BaseSalary = input.BaseSalary;
            offer.BonusAllowance = input.BonusAllowance;
            offer.TotalPackage = total;
            offer.ProposedJoinDate = input.ProposedJoinDate;
            offer.ExpirationDate = input.ExpirationDate;
            offer.ProbationPeriodMonths = input.ProbationPeriodMonths ?? offer.ProbationPeriodMonths;
            offer.ContractType = input.ContractType ?? offer.ContractType;
            offer.IsOutsideBand = isOutsideBand;
            offer.IsAboveBudget = standardMax.HasValue && input.BaseSalary > standardMax.Value;
            offer.SalaryJustification = input.SalaryJustification;
            offer.Notes = input.Notes;
            offer.Status = input.IsDraft ? OfferStatus.DRAFT : OfferStatus.PENDING_APPROVAL;
            offer.UpdatedAt = DateTimeOffset.UtcNow;
        }

        // 7. Xử lý chuyển giai đoạn khi Gửi duyệt (IsDraft = false)
        if (!input.IsDraft)
        {
            var offerStage = await _dbContext.PipelineStages.FirstOrDefaultAsync(s => s.StageOrder == 5 || s.Name.Contains("Offer"), cancellationToken);
            if (offerStage != null && application.CurrentStageId != offerStage.Id)
            {
                var prevStageId = application.CurrentStageId;
                application.CurrentStageId = offerStage.Id;
                application.StageEnteredAt = DateTimeOffset.UtcNow;
                application.IsSlaOverdue = false;
                application.SlaOverdueAlertedAt = null;
                application.UpdatedAt = DateTimeOffset.UtcNow;

                await _dbContext.ApplicationStageHistories.AddAsync(new ApplicationStageHistory
                {
                    Id = Guid.NewGuid(),
                    ApplicationId = application.Id,
                    FromStageId = prevStageId,
                    ToStageId = offerStage.Id,
                    ChangedByUserId = recruiterId,
                    Comment = $"Đề xuất Thư mời nhận việc: Lương {input.BaseSalary:N0} VNĐ. Ngày nhận việc: {input.ProposedJoinDate:dd/MM/yyyy}, Hạn phản hồi: {input.ExpirationDate:dd/MM/yyyy}. Chuyển phê duyệt Offer.",
                    CreatedAt = DateTimeOffset.UtcNow
                }, cancellationToken);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        string actionText = input.IsDraft ? "Đã lưu nháp đề xuất Offer" : "Đã gửi đề xuất Offer để phê duyệt";
        return OfferOperationResult.Ok(
            $"💼 {actionText} cho ứng viên {candidateFullName} thành công! (Mức lương: {input.BaseSalary:N0} VNĐ)",
            offer.Id,
            input.IsDraft);
    }

    /// <inheritdoc />
    public async Task<OfferOperationResult> WithdrawOfferAsync(
        Guid offerId,
        Guid currentUserId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var offer = await _dbContext.JobOffers
            .Include(o => o.Application)
                .ThenInclude(a => a.Candidate)
            .FirstOrDefaultAsync(o => o.Id == offerId, cancellationToken);

        if (offer == null)
        {
            return OfferOperationResult.Fail("Không tìm thấy đề xuất Offer cần rút lại.");
        }

        if (offer.Status == OfferStatus.WITHDRAWN || offer.Status == OfferStatus.REJECTED)
        {
            return OfferOperationResult.Fail("Offer này đã ở trạng thái hết hiệu lực từ trước.");
        }

        offer.Status = OfferStatus.WITHDRAWN;
        offer.UpdatedAt = DateTimeOffset.UtcNow;
        offer.Notes = string.IsNullOrWhiteSpace(offer.Notes)
            ? $"[Rút lại lúc {DateTime.UtcNow:dd/MM/yyyy HH:mm}]: {reason}"
            : $"{offer.Notes}\n[Rút lại lúc {DateTime.UtcNow:dd/MM/yyyy HH:mm}]: {reason}";

        await _dbContext.SaveChangesAsync(cancellationToken);

        var candName = $"{offer.Application.Candidate.FirstName} {offer.Application.Candidate.LastName}".Trim();
        return OfferOperationResult.Ok($"Đã rút lại đề xuất Offer của ứng viên {candName} thành công.", offer.Id);
    }

    /// <inheritdoc />
    public async Task<JobOfferPreviewViewModel?> GetOfferPreviewByIdAsync(
        Guid offerId,
        CancellationToken cancellationToken = default)
    {
        var offer = await _dbContext.JobOffers
            .Include(o => o.JobPosition)
            .Include(o => o.CreatedByRecruiter)
            .Include(o => o.Application)
                .ThenInclude(a => a.Candidate)
            .Include(o => o.Application)
                .ThenInclude(a => a.JobPosting)
                    .ThenInclude(jp => jp.Requisition)
                        .ThenInclude(r => r.Department)
            .FirstOrDefaultAsync(o => o.Id == offerId, cancellationToken);

        if (offer == null) return null;

        var app = offer.Application;
        var candName = $"{app.Candidate.FirstName} {app.Candidate.LastName}".Trim();
        var recruiterName = offer.CreatedByRecruiter != null
            ? $"{offer.CreatedByRecruiter.FirstName} {offer.CreatedByRecruiter.LastName}".Trim()
            : "Phòng Nhân sự NoveraTech";

        return new JobOfferPreviewViewModel
        {
            ApplicationId = app.Id,
            OfferId = offer.Id,
            CandidateName = candName,
            CandidateEmail = app.Candidate.Email,
            CandidatePhone = app.Candidate.PhoneNumber,
            JobTitle = !string.IsNullOrWhiteSpace(offer.JobTitle)
                ? offer.JobTitle
                : (offer.JobPosition?.Title ?? app.JobPosting.Title),
            DepartmentName = app.JobPosting.Requisition?.Department?.Name ?? "Khối Kỹ thuật & Công nghệ",
            WorkLocation = !string.IsNullOrWhiteSpace(offer.WorkLocation)
                ? offer.WorkLocation
                : (string.IsNullOrWhiteSpace(app.JobPosting.WorkLocation) ? "Tòa nhà Novera Tower, Cầu Giấy, Hà Nội" : app.JobPosting.WorkLocation),
            BaseSalary = offer.BaseSalary,
            BonusAllowance = offer.BonusAllowance ?? 0,
            TotalPackage = offer.TotalPackage,
            ProposedJoinDate = offer.ProposedJoinDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)),
            ExpirationDate = offer.ExpirationDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            ProbationPeriodMonths = offer.ProbationPeriodMonths ?? 2,
            ProbationSalaryPercentage = 85,
            ContractType = offer.ContractType ?? "Chính thức (Toàn thời gian)",
            Notes = offer.Notes,
            RecruiterName = recruiterName,
            RecruiterTitle = "Chuyên viên Tuyển dụng Cao cấp",
            CompanyName = "Tập đoàn Công nghệ NoveraTech",
            GeneratedDateFormatted = offer.CreatedAt.ToString("dd/MM/yyyy"),
            IsOutsideBand = offer.IsOutsideBand,
            SalaryJustification = offer.SalaryJustification
        };
    }

    private static string GetStatusDisplay(OfferStatus status) => status switch
    {
        OfferStatus.DRAFT => "Lưu nháp",
        OfferStatus.PENDING_APPROVAL => "Chờ phê duyệt",
        OfferStatus.APPROVED => "Đã duyệt",
        OfferStatus.SENT_TO_CANDIDATE => "Đã gửi ứng viên",
        OfferStatus.ACCEPTED => "Ứng viên đã đồng ý",
        OfferStatus.REJECTED => "Bị từ chối",
        OfferStatus.WITHDRAWN => "Đã rút lại",
        _ => status.ToString()
    };
}
