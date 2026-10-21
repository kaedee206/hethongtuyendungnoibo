using Ats.Web.Constants;
using Ats.Web.Data;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Models.ViewModels.Offers;
using Ats.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ats.Web.Services;

/// <summary>
/// Triển khai dịch vụ phê duyệt đề xuất Offer theo hạn mức lương (SCRUM-341).
/// Tuân thủ quy tắc kiểm soát quỹ lương, tự động phân cấp duyệt, 3 hành động và lịch sử bất biến.
/// </summary>
public class OfferApprovalService(
    ApplicationDbContext dbContext,
    ILogger<OfferApprovalService> logger) : IOfferApprovalService
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly ILogger<OfferApprovalService> _logger = logger;

    /// <inheritdoc />
    public async Task<OfferApprovalQueueViewModel> GetPendingOfferQueueAsync(
        Guid currentUserId,
        string role,
        CancellationToken cancellationToken = default)
    {
        var canonicalRole = UserRoles.NormalizeRole(role);
        var queue = new OfferApprovalQueueViewModel();

        // Lấy tất cả offer đang PENDING_APPROVAL kèm đầy đủ dữ liệu liên quan
        var pendingOffers = await _dbContext.JobOffers
            .Include(o => o.JobPosition)
            .Include(o => o.CreatedByRecruiter)
            .Include(o => o.Application)
                .ThenInclude(a => a.Candidate)
            .Include(o => o.Application)
                .ThenInclude(a => a.JobPosting)
                    .ThenInclude(jp => jp.Requisition)
                        .ThenInclude(r => r.Department)
            .Include(o => o.Application)
                .ThenInclude(a => a.JobPosting)
                    .ThenInclude(jp => jp.Requisition)
                        .ThenInclude(r => r.JobPosition)
            .Include(o => o.Approvals.Where(a => !a.IsDeleted))
                .ThenInclude(a => a.Approver)
            .Where(o => !o.IsDeleted && o.Status == OfferStatus.PENDING_APPROVAL)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(cancellationToken);

        foreach (var offer in pendingOffers)
        {
            // Xác định dải chuẩn
            var (minSalary, maxSalary) = GetStandardSalaryBand(offer);
            bool isAboveBudget = maxSalary.HasValue && offer.BaseSalary > maxSalary.Value;

            if (isAboveBudget)
            {
                queue.AboveBandPending++;
            }
            else
            {
                queue.WithinBandPending++;
            }

            // Xác định bước duyệt hiện tại đang chờ
            var pendingSteps = offer.Approvals
                .Where(a => a.Status == ApprovalStatus.PENDING)
                .OrderBy(a => a.Level)
                .ToList();

            var completedSteps = offer.Approvals
                .Where(a => a.Status == ApprovalStatus.APPROVED)
                .OrderBy(a => a.Level)
                .ToList();

            int currentLevel = 1;
            string requiredRole = UserRoles.HRManager;

            if (isAboveBudget)
            {
                // Nếu vượt dải: Cấp 1 HR Manager, Cấp 2 Ban Giám Đốc
                if (completedSteps.Any(s => s.Level == 1))
                {
                    currentLevel = 2;
                    requiredRole = UserRoles.Approver;
                }
                else
                {
                    currentLevel = 1;
                    requiredRole = UserRoles.HRManager;
                }
            }
            else
            {
                currentLevel = 1;
                requiredRole = UserRoles.HRManager;
            }

            // Kiểm tra người dùng hiện tại có quyền duyệt bước này không
            bool canApprove = CheckUserCanApproveLevel(canonicalRole, currentUserId, currentLevel, offer.Approvals);

            decimal? percentOver = null;
            if (maxSalary.HasValue && maxSalary.Value > 0 && offer.BaseSalary > maxSalary.Value)
            {
                percentOver = Math.Round(((offer.BaseSalary - maxSalary.Value) / maxSalary.Value) * 100, 1);
            }

            var cand = offer.Application?.Candidate;
            var candName = cand != null ? $"{cand.FirstName} {cand.LastName}".Trim() : "Ứng viên";
            var deptName = offer.Application?.JobPosting?.Requisition?.Department?.Name ?? "Khối Kỹ thuật & Công nghệ";
            var jobTitle = !string.IsNullOrWhiteSpace(offer.JobTitle)
                ? offer.JobTitle
                : (offer.JobPosition?.Title ?? offer.Application?.JobPosting?.Title ?? "Vị trí tuyển dụng");

            queue.Items.Add(new OfferApprovalQueueItemViewModel
            {
                OfferId = offer.Id,
                ApplicationId = offer.ApplicationId,
                CandidateName = candName,
                CandidateEmail = cand?.Email ?? string.Empty,
                JobTitle = jobTitle,
                DepartmentName = deptName,
                ProposedSalary = offer.BaseSalary,
                SalaryDisplay = $"{offer.BaseSalary:N0} VNĐ",
                StandardMinSalary = minSalary,
                StandardMaxSalary = maxSalary,
                SalaryBandDisplay = FormatSalaryBand(minSalary, maxSalary),
                IsAboveBudget = isAboveBudget,
                PercentOverBand = percentOver,
                SalaryJustification = offer.SalaryJustification,
                CurrentLevel = currentLevel,
                CurrentLevelDisplay = currentLevel == 1 ? "Cấp 1 - Trưởng phòng Nhân sự" : "Cấp 2 - Ban Giám Đốc",
                RequiredApproverRole = requiredRole,
                RequiredApproverRoleDisplay = UserRoles.GetDisplayName(requiredRole),
                CanCurrentUserApprove = canApprove,
                CreatedAtFormatted = offer.CreatedAt.ToString("dd/MM/yyyy HH:mm"),
                RecruiterName = offer.CreatedByRecruiter?.FullName ?? "Chuyên viên Tuyển dụng"
            });
        }

        queue.TotalPending = queue.Items.Count;
        return queue;
    }

    /// <inheritdoc />
    public async Task<OfferApprovalDetailViewModel?> GetOfferApprovalDetailAsync(
        Guid offerId,
        Guid currentUserId,
        string role,
        CancellationToken cancellationToken = default)
    {
        var canonicalRole = UserRoles.NormalizeRole(role);

        var offer = await _dbContext.JobOffers
            .Include(o => o.JobPosition)
            .Include(o => o.CreatedByRecruiter)
            .Include(o => o.Application)
                .ThenInclude(a => a.Candidate)
            .Include(o => o.Application)
                .ThenInclude(a => a.JobPosting)
                    .ThenInclude(jp => jp.Requisition)
                        .ThenInclude(r => r.Department)
            .Include(o => o.Application)
                .ThenInclude(a => a.JobPosting)
                    .ThenInclude(jp => jp.Requisition)
                        .ThenInclude(r => r.JobPosition)
            .Include(o => o.Approvals.Where(a => !a.IsDeleted))
                .ThenInclude(a => a.Approver)
            .FirstOrDefaultAsync(o => o.Id == offerId, cancellationToken);

        if (offer == null) return null;

        var (minSalary, maxSalary) = GetStandardSalaryBand(offer);
        bool isAboveBudget = maxSalary.HasValue && offer.BaseSalary > maxSalary.Value;

        decimal? percentOver = null;
        if (maxSalary.HasValue && maxSalary.Value > 0 && offer.BaseSalary > maxSalary.Value)
        {
            percentOver = Math.Round(((offer.BaseSalary - maxSalary.Value) / maxSalary.Value) * 100, 1);
        }

        var completedSteps = offer.Approvals.Where(a => a.Status == ApprovalStatus.APPROVED).ToList();
        int currentLevel = 1;
        int totalLevelsRequired = isAboveBudget ? 2 : 1;

        if (isAboveBudget && completedSteps.Any(s => s.Level == 1))
        {
            currentLevel = 2;
        }

        bool canApprove = offer.Status == OfferStatus.PENDING_APPROVAL
            && CheckUserCanApproveLevel(canonicalRole, currentUserId, currentLevel, offer.Approvals);

        string? restrictionReason = null;
        if (offer.Status != OfferStatus.PENDING_APPROVAL)
        {
            restrictionReason = $"Đề xuất đã ở trạng thái '{GetStatusDisplay(offer.Status)}', không thể phê duyệt.";
        }
        else if (!canApprove)
        {
            restrictionReason = currentLevel == 1
                ? "Bước này yêu cầu Trưởng phòng Nhân sự duyệt (Cấp 1)."
                : "Bước này yêu cầu Ban Giám Đốc phê duyệt (Cấp 2 - Vượt dải chuẩn).";
        }

        var cand = offer.Application?.Candidate;
        var candName = cand != null ? $"{cand.FirstName} {cand.LastName}".Trim() : "Ứng viên";
        var deptName = offer.Application?.JobPosting?.Requisition?.Department?.Name ?? "Khối Kỹ thuật & Công nghệ";
        var jobTitle = !string.IsNullOrWhiteSpace(offer.JobTitle)
            ? offer.JobTitle
            : (offer.JobPosition?.Title ?? offer.Application?.JobPosting?.Title ?? "Vị trí tuyển dụng");

        // Lấy lịch sử duyệt bất biến
        var history = await GetApprovalHistoryAsync(offer.Id, cancellationToken);

        return new OfferApprovalDetailViewModel
        {
            OfferId = offer.Id,
            ApplicationId = offer.ApplicationId,
            CandidateName = candName,
            CandidateEmail = cand?.Email ?? string.Empty,
            CandidatePhone = cand?.Phone,
            JobTitle = jobTitle,
            DepartmentName = deptName,
            WorkLocation = offer.WorkLocation ?? "Tòa nhà Novera Tower, Cầu Giấy, Hà Nội",
            ProposedSalary = offer.BaseSalary,
            BonusAllowance = offer.BonusAllowance ?? 0,
            TotalPackage = offer.TotalPackage,
            SalaryDisplay = $"{offer.BaseSalary:N0} VNĐ",
            TotalPackageDisplay = $"{offer.TotalPackage:N0} VNĐ",
            ProposedJoinDateFormatted = offer.ProposedJoinDate.HasValue ? offer.ProposedJoinDate.Value.ToString("dd/MM/yyyy") : "Chưa xác định",
            ExpirationDateFormatted = offer.ExpirationDate.HasValue ? offer.ExpirationDate.Value.ToString("dd/MM/yyyy") : "Chưa xác định",
            ProbationPeriodMonths = offer.ProbationPeriodMonths ?? 2,
            ContractType = offer.ContractType ?? "Chính thức (Toàn thời gian)",
            StandardMinSalary = minSalary,
            StandardMaxSalary = maxSalary,
            SalaryBandDisplay = FormatSalaryBand(minSalary, maxSalary),
            IsAboveBudget = isAboveBudget,
            PercentOverBand = percentOver,
            SalaryJustification = offer.SalaryJustification,
            Notes = offer.Notes,
            Status = offer.Status,
            StatusDisplay = GetStatusDisplay(offer.Status),
            CurrentLevel = currentLevel,
            CurrentLevelDisplay = currentLevel == 1 ? "Cấp 1 - Trưởng phòng Nhân sự" : "Cấp 2 - Ban Giám Đốc",
            TotalLevelsRequired = totalLevelsRequired,
            CanApprove = canApprove,
            CanReject = canApprove,
            CanRequestChanges = canApprove,
            UserActionRestrictionReason = restrictionReason,
            RecruiterName = offer.CreatedByRecruiter?.FullName ?? "Chuyên viên Tuyển dụng",
            CreatedAtFormatted = offer.CreatedAt.ToString("dd/MM/yyyy HH:mm"),
            ApprovalHistory = history
        };
    }

    /// <inheritdoc />
    public async Task<OfferApprovalOperationResult> SubmitApprovalDecisionAsync(
        OfferApprovalDecisionInputModel input,
        Guid currentUserId,
        string role,
        CancellationToken cancellationToken = default)
    {
        if (input.OfferId == Guid.Empty)
        {
            return OfferApprovalOperationResult.Fail("Thiếu định danh đề xuất Offer.");
        }

        var decision = input.Decision?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(decision))
        {
            return OfferApprovalOperationResult.Fail("Vui lòng chọn hành động quyết định (Duyệt, Từ chối hoặc Yêu cầu sửa).");
        }

        // Quy tắc cốt tử 2: Từ chối hoặc Yêu cầu sửa BẮT BUỘC nhập ý kiến
        if (decision is OfferApprovalDecisionType.Reject or "REJECT")
        {
            if (string.IsNullOrWhiteSpace(input.Comment))
            {
                return OfferApprovalOperationResult.Fail("Vui lòng nhập lý do từ chối offer.");
            }
        }
        else if (decision is OfferApprovalDecisionType.RequestChanges or "CHANGES_REQUESTED")
        {
            if (string.IsNullOrWhiteSpace(input.Comment))
            {
                return OfferApprovalOperationResult.Fail("Vui lòng nhập nội dung yêu cầu chỉnh sửa offer.");
            }
        }
        else if (decision is not (OfferApprovalDecisionType.Approve or "APPROVE"))
        {
            return OfferApprovalOperationResult.Fail("Hành động phê duyệt không hợp lệ.");
        }

        var offer = await _dbContext.JobOffers
            .Include(o => o.JobPosition)
            .Include(o => o.Application)
                .ThenInclude(a => a.Candidate)
            .Include(o => o.Application)
                .ThenInclude(a => a.JobPosting)
                    .ThenInclude(jp => jp.Requisition)
                        .ThenInclude(r => r.JobPosition)
            .Include(o => o.Approvals.Where(a => !a.IsDeleted))
            .FirstOrDefaultAsync(o => o.Id == input.OfferId, cancellationToken);

        if (offer == null)
        {
            return OfferApprovalOperationResult.Fail("Không tìm thấy đề xuất Offer cần xử lý.");
        }

        if (offer.Status != OfferStatus.PENDING_APPROVAL)
        {
            return OfferApprovalOperationResult.Fail($"Đề xuất Offer đang ở trạng thái '{GetStatusDisplay(offer.Status)}', không thể thực hiện phê duyệt.");
        }

        // Đảm bảo các bước duyệt đã được khởi tạo
        if (!offer.Approvals.Any(a => a.Status == ApprovalStatus.PENDING))
        {
            await InitializeApprovalWorkflowAsync(offer.Id, cancellationToken);
            await _dbContext.Entry(offer).Collection(o => o.Approvals).LoadAsync(cancellationToken);
        }

        var (minSalary, maxSalary) = GetStandardSalaryBand(offer);
        bool isAboveBudget = maxSalary.HasValue && offer.BaseSalary > maxSalary.Value;

        var completedSteps = offer.Approvals.Where(a => a.Status == ApprovalStatus.APPROVED).ToList();
        var pendingSteps = offer.Approvals.Where(a => a.Status == ApprovalStatus.PENDING).OrderBy(a => a.Level).ToList();

        var activeStep = pendingSteps.FirstOrDefault();
        if (activeStep == null)
        {
            return OfferApprovalOperationResult.Fail("Không tìm thấy bước duyệt nào đang chờ xử lý.");
        }

        var canonicalRole = UserRoles.NormalizeRole(role);
        bool canApprove = CheckUserCanApproveLevel(canonicalRole, currentUserId, activeStep.Level, offer.Approvals);
        if (!canApprove)
        {
            string requiredRoleName = activeStep.Level == 1 ? "Trưởng phòng Nhân sự" : "Ban Giám Đốc";
            return OfferApprovalOperationResult.Fail($"Bạn không có quyền thực hiện quyết định ở Cấp {activeStep.Level} ({requiredRoleName}).");
        }

        var candName = offer.Application?.Candidate != null
            ? $"{offer.Application.Candidate.FirstName} {offer.Application.Candidate.LastName}".Trim()
            : "Ứng viên";

        // Thực hiện theo 1 trong 3 hành động
        if (decision is OfferApprovalDecisionType.Approve or "APPROVE")
        {
            activeStep.Status = ApprovalStatus.APPROVED;
            activeStep.Comment = !string.IsNullOrWhiteSpace(input.Comment) ? input.Comment.Trim() : "Đồng ý phê duyệt.";
            activeStep.DecidedAt = DateTimeOffset.UtcNow;
            activeStep.ApproverId = currentUserId;
            activeStep.UpdatedAt = DateTimeOffset.UtcNow;

            // Kiểm tra xem còn cấp duyệt cao hơn không
            bool hasHigherLevel = isAboveBudget && activeStep.Level == 1;

            if (hasHigherLevel)
            {
                // Vẫn giữ offer ở PENDING_APPROVAL, chuyển sang cấp 2
                offer.Status = OfferStatus.PENDING_APPROVAL;
                offer.UpdatedAt = DateTimeOffset.UtcNow;
                await _dbContext.SaveChangesAsync(cancellationToken);

                return OfferApprovalOperationResult.Ok(
                    $"✅ Đã phê duyệt đề xuất Offer (Cấp 1 - Trưởng phòng NS) cho ứng viên {candName}. Đề xuất vượt dải chuẩn đã được chuyển tiếp lên Ban Giám Đốc (Cấp 2) phê duyệt tiếp theo.",
                    offer.Id,
                    offer.Status,
                    ApprovalStatus.APPROVED,
                    isFinalApproval: false,
                    nextLevel: 2);
            }
            else
            {
                // Là cấp duyệt cuối cùng -> Offer chính thức APPROVED
                offer.Status = OfferStatus.APPROVED;
                offer.UpdatedAt = DateTimeOffset.UtcNow;

                // Tự động chuyển ứng viên sang Stage Offer / Hired nếu có
                if (offer.Application != null)
                {
                    var offerStage = await _dbContext.PipelineStages.FirstOrDefaultAsync(s => s.StageOrder == 5 || s.Name.Contains("Offer"), cancellationToken);
                    if (offerStage != null && offer.Application.CurrentStageId != offerStage.Id)
                    {
                        var prevStageId = offer.Application.CurrentStageId;
                        offer.Application.CurrentStageId = offerStage.Id;
                        offer.Application.StageEnteredAt = DateTimeOffset.UtcNow;
                        offer.Application.UpdatedAt = DateTimeOffset.UtcNow;

                        await _dbContext.ApplicationStageHistories.AddAsync(new ApplicationStageHistory
                        {
                            Id = Guid.NewGuid(),
                            ApplicationId = offer.Application.Id,
                            FromStageId = prevStageId,
                            ToStageId = offerStage.Id,
                            ChangedByUserId = currentUserId,
                            Comment = $"Thư mời nhận việc đã được phê duyệt hoàn tất (Mức lương: {offer.BaseSalary:N0} VNĐ).",
                            CreatedAt = DateTimeOffset.UtcNow
                        }, cancellationToken);
                    }
                }

                await _dbContext.SaveChangesAsync(cancellationToken);

                return OfferApprovalOperationResult.Ok(
                    $"🎉 Đã phê duyệt hoàn tất đề xuất Offer cho ứng viên {candName}! Thư mời nhận việc đã sẵn sàng để gửi chính thức.",
                    offer.Id,
                    offer.Status,
                    ApprovalStatus.APPROVED,
                    isFinalApproval: true);
            }
        }
        else if (decision is OfferApprovalDecisionType.Reject or "REJECT")
        {
            activeStep.Status = ApprovalStatus.REJECTED;
            activeStep.Comment = input.Comment!.Trim();
            activeStep.DecidedAt = DateTimeOffset.UtcNow;
            activeStep.ApproverId = currentUserId;
            activeStep.UpdatedAt = DateTimeOffset.UtcNow;

            offer.Status = OfferStatus.REJECTED;
            offer.UpdatedAt = DateTimeOffset.UtcNow;
            offer.Notes = string.IsNullOrWhiteSpace(offer.Notes)
                ? $"[Từ chối lúc {DateTime.UtcNow:dd/MM/yyyy HH:mm} bởi {UserRoles.GetDisplayName(canonicalRole)}]: {input.Comment!.Trim()}"
                : $"{offer.Notes}\n[Từ chối lúc {DateTime.UtcNow:dd/MM/yyyy HH:mm} bởi {UserRoles.GetDisplayName(canonicalRole)}]: {input.Comment!.Trim()}";

            // Hủy các bước sau nếu có
            foreach (var remainingStep in pendingSteps.Where(s => s.Id != activeStep.Id))
            {
                remainingStep.Status = ApprovalStatus.REJECTED;
                remainingStep.Comment = "Hủy bỏ do cấp trước đã từ chối.";
                remainingStep.DecidedAt = DateTimeOffset.UtcNow;
                remainingStep.UpdatedAt = DateTimeOffset.UtcNow;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            return OfferApprovalOperationResult.Ok(
                $"❌ Đã từ chối đề xuất Offer của ứng viên {candName}. Lý do đã được ghi nhận vào lịch sử duyệt.",
                offer.Id,
                offer.Status,
                ApprovalStatus.REJECTED);
        }
        else // CHANGES_REQUESTED
        {
            activeStep.Status = ApprovalStatus.CHANGES_REQUESTED;
            activeStep.Comment = input.Comment!.Trim();
            activeStep.DecidedAt = DateTimeOffset.UtcNow;
            activeStep.ApproverId = currentUserId;
            activeStep.UpdatedAt = DateTimeOffset.UtcNow;

            // Chuyển Offer về nháp (DRAFT) để Recruiter sửa đổi
            offer.Status = OfferStatus.DRAFT;
            offer.UpdatedAt = DateTimeOffset.UtcNow;
            offer.Notes = string.IsNullOrWhiteSpace(offer.Notes)
                ? $"[Yêu cầu sửa lúc {DateTime.UtcNow:dd/MM/yyyy HH:mm} bởi {UserRoles.GetDisplayName(canonicalRole)}]: {input.Comment!.Trim()}"
                : $"{offer.Notes}\n[Yêu cầu sửa lúc {DateTime.UtcNow:dd/MM/yyyy HH:mm} bởi {UserRoles.GetDisplayName(canonicalRole)}]: {input.Comment!.Trim()}";

            // Hủy các bước chờ phía sau nếu có
            foreach (var remainingStep in pendingSteps.Where(s => s.Id != activeStep.Id))
            {
                remainingStep.Status = ApprovalStatus.CHANGES_REQUESTED;
                remainingStep.Comment = "Tạm dừng do cấp duyệt yêu cầu chỉnh sửa offer.";
                remainingStep.DecidedAt = DateTimeOffset.UtcNow;
                remainingStep.UpdatedAt = DateTimeOffset.UtcNow;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            return OfferApprovalOperationResult.Ok(
                $"📝 Đã gửi yêu cầu chỉnh sửa Offer của ứng viên {candName} tới Chuyên viên tuyển dụng kèm ý kiến góp ý.",
                offer.Id,
                offer.Status,
                ApprovalStatus.CHANGES_REQUESTED);
        }
    }

    /// <inheritdoc />
    public async Task<List<OfferApprovalHistoryItemViewModel>> GetApprovalHistoryAsync(
        Guid offerId,
        CancellationToken cancellationToken = default)
    {
        // Quy tắc bất biến: Chỉ đọc các bản ghi đã quyết định hoặc đang chờ, không bao giờ sửa hoặc xóa
        var approvals = await _dbContext.OfferApprovals
            .Include(a => a.Approver)
            .Where(a => a.OfferId == offerId && !a.IsDeleted)
            .OrderBy(a => a.CreatedAt)
            .ThenBy(a => a.Level)
            .ToListAsync(cancellationToken);

        return approvals.Select(a =>
        {
            var role = a.Approver?.Role ?? (a.Level == 1 ? UserRoles.HRManager : UserRoles.Approver);
            return new OfferApprovalHistoryItemViewModel
            {
                Id = a.Id,
                Level = a.Level,
                LevelDisplay = a.Level == 1 ? "Cấp 1 (Trưởng phòng Nhân sự)" : "Cấp 2 (Ban Giám Đốc)",
                ApproverId = a.ApproverId,
                ApproverName = a.Approver?.FullName ?? "Người phê duyệt",
                ApproverRole = role,
                ApproverRoleDisplay = UserRoles.GetDisplayName(role),
                Status = a.Status,
                StatusDisplay = GetApprovalStatusDisplay(a.Status),
                BadgeCssClass = GetApprovalStatusBadgeCss(a.Status),
                Comment = a.Comment,
                DecidedAt = a.DecidedAt,
                DecidedAtFormatted = a.DecidedAt.HasValue ? a.DecidedAt.Value.ToString("dd/MM/yyyy HH:mm") : "Đang chờ duyệt",
                CreatedAt = a.CreatedAt
            };
        }).ToList();
    }

    /// <inheritdoc />
    public async Task InitializeApprovalWorkflowAsync(
        Guid offerId,
        CancellationToken cancellationToken = default)
    {
        var offer = await _dbContext.JobOffers
            .Include(o => o.JobPosition)
            .Include(o => o.Application)
                .ThenInclude(a => a.JobPosting)
                    .ThenInclude(jp => jp.Requisition)
                        .ThenInclude(r => r.JobPosition)
            .Include(o => o.Approvals)
            .FirstOrDefaultAsync(o => o.Id == offerId, cancellationToken);

        if (offer == null) return;

        // Nếu đã có bước PENDING thì không tạo trùng
        if (offer.Approvals.Any(a => !a.IsDeleted && a.Status == ApprovalStatus.PENDING))
        {
            return;
        }

        var (minSalary, maxSalary) = GetStandardSalaryBand(offer);
        bool isAboveBudget = maxSalary.HasValue && offer.BaseSalary > maxSalary.Value;

        offer.IsAboveBudget = isAboveBudget;
        offer.IsOutsideBand = isAboveBudget;

        // Tìm người duyệt Cấp 1 (HR Manager)
        var hrManagerUser = await _dbContext.Users
            .FirstOrDefaultAsync(u => !u.IsDeleted && (u.Role == UserRoles.HRManager || u.Role == "HR_MANAGER" || u.Role == "HR Manager" || u.Role == "Trưởng phòng Nhân sự"), cancellationToken)
            ?? await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == "hr@noveratech.vn" || u.Email == "phuong.nguyen@noveratech.digital", cancellationToken)
            ?? await _dbContext.Users.FirstOrDefaultAsync(u => u.Role == UserRoles.Admin, cancellationToken);

        // Tìm người duyệt Cấp 2 (Ban Giám Đốc / Approver)
        var approverUser = await _dbContext.Users
            .FirstOrDefaultAsync(u => !u.IsDeleted && (u.Role == UserRoles.Approver || u.Role == "APPROVER" || u.Role == "Người duyệt" || u.Role == "Ban Giám Đốc"), cancellationToken)
            ?? await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == "bod@noveratech.vn" || u.Email == "minh.tran@noveratech.digital", cancellationToken)
            ?? await _dbContext.Users.FirstOrDefaultAsync(u => u.Role == UserRoles.Admin, cancellationToken);

        var hrUserId = hrManagerUser?.Id ?? Guid.NewGuid();
        var bodUserId = approverUser?.Id ?? Guid.NewGuid();

        // Cấp 1: Bắt buộc cho mọi offer (Trưởng phòng Nhân sự duyệt)
        var step1 = new OfferApproval
        {
            Id = Guid.NewGuid(),
            OfferId = offer.Id,
            ApproverId = hrUserId,
            Level = 1,
            Status = ApprovalStatus.PENDING,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await _dbContext.OfferApprovals.AddAsync(step1, cancellationToken);

        // Cấp 2: Tự động thêm cấp duyệt Ban Giám Đốc nếu vượt dải chuẩn
        if (isAboveBudget)
        {
            var step2 = new OfferApproval
            {
                Id = Guid.NewGuid(),
                OfferId = offer.Id,
                ApproverId = bodUserId,
                Level = 2,
                Status = ApprovalStatus.PENDING,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await _dbContext.OfferApprovals.AddAsync(step2, cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static (decimal? min, decimal? max) GetStandardSalaryBand(JobOffer offer)
    {
        var pos = offer.JobPosition ?? offer.Application?.JobPosting?.Requisition?.JobPosition;
        var req = offer.Application?.JobPosting?.Requisition;

        decimal? min = pos?.MinSalary ?? req?.MinSalary;
        decimal? max = pos?.MaxSalary ?? req?.MaxSalary;

        return (min, max);
    }

    private static string FormatSalaryBand(decimal? min, decimal? max)
    {
        if (min.HasValue && max.HasValue) return $"{min.Value:N0} - {max.Value:N0} VNĐ";
        if (max.HasValue) return $"Tối đa {max.Value:N0} VNĐ";
        if (min.HasValue) return $"Từ {min.Value:N0} VNĐ";
        return "Thỏa thuận theo năng lực";
    }

    private static bool CheckUserCanApproveLevel(string canonicalRole, Guid currentUserId, int level, IEnumerable<OfferApproval> approvals)
    {
        if (canonicalRole == UserRoles.Admin) return true;

        if (level == 1)
        {
            if (canonicalRole == UserRoles.HRManager) return true;
            return approvals.Any(a => a.Level == 1 && a.ApproverId == currentUserId);
        }

        if (level == 2)
        {
            if (canonicalRole == UserRoles.Approver) return true;
            return approvals.Any(a => a.Level == 2 && a.ApproverId == currentUserId);
        }

        return false;
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
        _ => status.ToString()
    };

    private static string GetApprovalStatusDisplay(ApprovalStatus status) => status switch
    {
        ApprovalStatus.PENDING => "Đang chờ duyệt",
        ApprovalStatus.APPROVED => "Đã phê duyệt",
        ApprovalStatus.REJECTED => "Đã từ chối",
        ApprovalStatus.CHANGES_REQUESTED => "Yêu cầu sửa",
        _ => status.ToString()
    };

    private static string GetApprovalStatusBadgeCss(ApprovalStatus status) => status switch
    {
        ApprovalStatus.PENDING => "bg-warning-subtle text-warning-emphasis border border-warning-subtle",
        ApprovalStatus.APPROVED => "bg-success-subtle text-success border border-success-subtle",
        ApprovalStatus.REJECTED => "bg-danger-subtle text-danger border border-danger-subtle",
        ApprovalStatus.CHANGES_REQUESTED => "bg-info-subtle text-info-emphasis border border-info-subtle",
        _ => "bg-secondary-subtle text-secondary"
    };
}
