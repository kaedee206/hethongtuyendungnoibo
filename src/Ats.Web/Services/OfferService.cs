using Ats.Web.Data;
using Ats.Web.Models.DTOs.Offers;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Models.ViewModels.TalentPool;
using Ats.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Ats.Web.Services;

/// <summary>
/// Triển khai dịch vụ quản lý Thư mời nhận việc, Thương lượng lại & Xử lý ứng viên từ chối Offer (User Story SP-7).
/// Áp dụng C# 12 Primary Constructors, tuân thủ Clean Architecture và xử lý Transaction an toàn.
/// </summary>
public class OfferService(
    ApplicationDbContext dbContext,
    ITalentPoolService talentPoolService,
    ILogger<OfferService> logger) : IOfferService
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly ITalentPoolService _talentPoolService = talentPoolService;
    private readonly ILogger<OfferService> _logger = logger;

    public async Task<(bool Success, string Message, JobOffer? Offer)> RejectOfferAsync(
        RejectOfferDto dto,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        if (dto.OfferId == Guid.Empty)
        {
            return (false, "Mã Thư mời nhận việc không hợp lệ.", null);
        }

        var offer = await _dbContext.JobOffers
            .Include(o => o.Application)
                .ThenInclude(a => a.Candidate)
                    .ThenInclude(c => c.Tags)
            .Include(o => o.Application)
                .ThenInclude(a => a.JobPosting)
            .FirstOrDefaultAsync(o => o.Id == dto.OfferId && !o.IsDeleted, cancellationToken);

        if (offer == null)
        {
            return (false, "Không tìm thấy thông tin Thư mời nhận việc.", null);
        }

        // Kiểm tra ràng buộc trạng thái
        if (offer.Status == OfferStatus.ACCEPTED)
        {
            return (false, "Không thể từ chối Thư mời nhận việc đã được ứng viên chấp thuận (ACCEPTED).", null);
        }

        if (offer.Status == OfferStatus.REPLACED)
        {
            return (false, "Thư mời nhận việc này đã được thay thế bởi phiên bản mới hơn (REPLACED).", null);
        }

        if (offer.Status == OfferStatus.REJECTED)
        {
            return (false, "Thư mời nhận việc này đã ở trạng thái Từ chối (REJECTED).", null);
        }

        // Validate danh mục lý do
        if (!Enum.IsDefined(typeof(OfferRejectionReasonCategory), dto.ReasonCategory))
        {
            return (false, "Danh mục lý do từ chối không hợp lệ.", null);
        }

        if (dto.ReasonCategory == OfferRejectionReasonCategory.Salary && dto.ExpectedSalary.HasValue && dto.ExpectedSalary.Value <= 0)
        {
            return (false, "Mức lương kỳ vọng của ứng viên phải lớn hơn 0.", null);
        }

        // Cập nhật trạng thái Thư mời
        offer.Status = OfferStatus.REJECTED;
        offer.RejectionReasonCategory = dto.ReasonCategory;
        offer.RejectionNotes = dto.RejectionNotes?.Trim();
        offer.ExpectedSalary = dto.ExpectedSalary;
        offer.RejectedAt = DateTimeOffset.UtcNow;
        offer.UpdatedAt = DateTimeOffset.UtcNow;
        if (currentUserId != Guid.Empty)
        {
            offer.UpdatedById = currentUserId;
        }

        var categoryName = GetCategoryDisplayName(dto.ReasonCategory);

        // Xử lý đưa vào Kho tiềm năng (Talent Pool) nếu được chỉ định
        if (dto.AddToTalentPool && offer.Application?.Candidate != null)
        {
            var candidate = offer.Application.Candidate;
            candidate.IsInTalentPool = true;
            candidate.TalentPoolAddedAt = DateTimeOffset.UtcNow;
            candidate.TalentPoolNotes = $"Từ chối Offer: {categoryName}. " +
                                        $"{(dto.ExpectedSalary.HasValue ? $"Lương kỳ vọng: {dto.ExpectedSalary.Value:N0} VNĐ. " : "")}" +
                                        $"Ghi chú: {dto.RejectionNotes?.Trim()}";

            var poolReason = !string.IsNullOrWhiteSpace(dto.TalentPoolReason)
                ? dto.TalentPoolReason.Trim()
                : $"Ứng viên từ chối offer v{offer.Version}: {categoryName}";

            var targetTitle = !string.IsNullOrWhiteSpace(dto.TalentPoolTargetJobTitle)
                ? dto.TalentPoolTargetJobTitle.Trim()
                : (offer.Application.JobPosting?.Title ?? "Chức danh chuyên môn");

            var poolNotes = !string.IsNullOrWhiteSpace(dto.TalentPoolNotes)
                ? dto.TalentPoolNotes.Trim()
                : $"Kỳ vọng thu nhập: {(dto.ExpectedSalary.HasValue ? dto.ExpectedSalary.Value.ToString("N0") + " VNĐ" : "Chưa cung cấp")}. Phản hồi: {dto.RejectionNotes?.Trim()}";

            try
            {
                await _talentPoolService.AddCandidateToPoolAsync(new AddTalentPoolCandidateInputModel
                {
                    CandidateId = candidate.Id,
                    SourceApplicationId = offer.ApplicationId,
                    LastStageReached = "Đề nghị tuyển dụng (Offer)",
                    Reason = poolReason,
                    TargetJobTitle = targetTitle,
                    Rating = dto.TalentPoolRating ?? 4,
                    Notes = poolNotes
                }, currentUserId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Lỗi khi gọi TalentPoolService cho ứng viên {CandidateId}", candidate.Id);
            }
        }

        // Ghi nhận nhật ký chuyển bước (Audit Trail)
        if (offer.Application != null)
        {
            var salaryText = dto.ExpectedSalary.HasValue ? $" (Lương kỳ vọng: {dto.ExpectedSalary.Value:N0} VNĐ)" : string.Empty;
            var noteText = !string.IsNullOrWhiteSpace(dto.RejectionNotes) ? $" - Phản hồi: {dto.RejectionNotes.Trim()}" : string.Empty;
            var poolText = dto.AddToTalentPool ? " [Đã đưa vào Kho Tiềm Năng]" : string.Empty;

            await _dbContext.ApplicationStageHistories.AddAsync(new ApplicationStageHistory
            {
                Id = Guid.NewGuid(),
                ApplicationId = offer.ApplicationId,
                FromStageId = offer.Application.CurrentStageId,
                ToStageId = offer.Application.CurrentStageId,
                ChangedByUserId = currentUserId != Guid.Empty ? currentUserId : offer.CreatedByRecruiterId,
                Comment = $"Ứng viên từ chối Thư mời nhận việc v{offer.Version}. Lý do: {categoryName}{salaryText}{noteText}{poolText}",
                CreatedAt = DateTimeOffset.UtcNow
            }, cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Offer {OfferId} marked as REJECTED with category {Category}. AddToTalentPool={AddToPool}",
            offer.Id, dto.ReasonCategory, dto.AddToTalentPool);

        return (true, "Đã ghi nhận ứng viên từ chối Thư mời nhận việc thành công.", offer);
    }

    public async Task<(bool Success, string Message, JobOffer? NewOffer)> ReviseOfferAsync(
        ReviseOfferDto dto,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        if (dto.OriginalOfferId == Guid.Empty)
        {
            return (false, "Mã Thư mời nhận việc gốc không hợp lệ.", null);
        }

        if (dto.BaseSalary <= 0)
        {
            return (false, "Mức lương cơ bản thỏa thuận mới phải lớn hơn 0.", null);
        }

        var originalOffer = await _dbContext.JobOffers
            .Include(o => o.Application)
                .ThenInclude(a => a.Candidate)
            .Include(o => o.Application)
                .ThenInclude(a => a.JobPosting)
            .FirstOrDefaultAsync(o => o.Id == dto.OriginalOfferId && !o.IsDeleted, cancellationToken);

        if (originalOffer == null)
        {
            return (false, "Không tìm thấy thông tin Thư mời nhận việc cần sửa đổi.", null);
        }

        // Kiểm tra ràng buộc
        if (originalOffer.Status == OfferStatus.ACCEPTED)
        {
            return (false, "Không thể sửa đổi Thư mời nhận việc đã được ứng viên chấp thuận (ACCEPTED).", null);
        }

        if (originalOffer.Status == OfferStatus.REPLACED)
        {
            return (false, "Thư mời nhận việc này đã được thay thế trước đó bởi phiên bản mới hơn.", null);
        }

        // 1. Chuyển offer cũ sang trạng thái REPLACED (Đã thay thế)
        originalOffer.Status = OfferStatus.REPLACED;
        originalOffer.UpdatedAt = DateTimeOffset.UtcNow;
        if (currentUserId != Guid.Empty)
        {
            originalOffer.UpdatedById = currentUserId;
        }

        // 2. Tính toán lương mới & Khởi tạo bản ghi JobOffer mới
        var total = dto.TotalPackage ?? (dto.BaseSalary + (dto.BonusAllowance ?? 0));
        var recruiterId = currentUserId != Guid.Empty ? currentUserId : originalOffer.CreatedByRecruiterId;

        var newOffer = new JobOffer
        {
            Id = Guid.NewGuid(),
            ApplicationId = originalOffer.ApplicationId,
            CreatedByRecruiterId = recruiterId,
            ParentOfferId = originalOffer.Id,
            Version = originalOffer.Version + 1,
            BaseSalary = dto.BaseSalary,
            BonusAllowance = dto.BonusAllowance,
            TotalPackage = total,
            ProposedJoinDate = dto.ProposedJoinDate ?? originalOffer.ProposedJoinDate,
            ProbationPeriodMonths = dto.ProbationPeriodMonths ?? originalOffer.ProbationPeriodMonths ?? 2,
            ContractType = !string.IsNullOrWhiteSpace(dto.ContractType) ? dto.ContractType.Trim() : originalOffer.ContractType,
            Status = OfferStatus.PENDING_APPROVAL,
            IsAboveBudget = originalOffer.IsAboveBudget,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            CreatedById = currentUserId != Guid.Empty ? currentUserId : null
        };

        await _dbContext.JobOffers.AddAsync(newOffer, cancellationToken);

        // 3. Đảm bảo Application duy trì tại Giai đoạn Offer
        if (originalOffer.Application != null)
        {
            var offerStage = await _dbContext.PipelineStages
                .FirstOrDefaultAsync(s => s.StageOrder == 5 || s.Name.Contains("Offer"), cancellationToken);

            if (offerStage != null && originalOffer.Application.CurrentStageId != offerStage.Id)
            {
                originalOffer.Application.CurrentStageId = offerStage.Id;
                originalOffer.Application.StageEnteredAt = DateTimeOffset.UtcNow;
            }
            originalOffer.Application.Status = ApplicationStatus.IN_PROCESS;
            originalOffer.Application.UpdatedAt = DateTimeOffset.UtcNow;

            // 4. Ghi nhận lịch sử luồng đàm phán (Audit Trail)
            var noteDetail = !string.IsNullOrWhiteSpace(dto.RevisionNotes) ? $" Ghi chú: {dto.RevisionNotes.Trim()}" : string.Empty;
            await _dbContext.ApplicationStageHistories.AddAsync(new ApplicationStageHistory
            {
                Id = Guid.NewGuid(),
                ApplicationId = originalOffer.ApplicationId,
                FromStageId = originalOffer.Application.CurrentStageId,
                ToStageId = originalOffer.Application.CurrentStageId,
                ChangedByUserId = recruiterId,
                Comment = $"Tạo Thư mời nhận việc sửa đổi (v{newOffer.Version}) thay thế v{originalOffer.Version}. " +
                          $"Lương mới: {newOffer.BaseSalary:N0} VNĐ.{noteDetail}",
                CreatedAt = DateTimeOffset.UtcNow
            }, cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Offer {OldOfferId} (v{OldVersion}) superseded by new Offer {NewOfferId} (v{NewVersion})",
            originalOffer.Id, originalOffer.Version, newOffer.Id, newOffer.Version);

        return (true, $"Đã tạo Thư mời nhận việc sửa đổi (Phiên bản v{newOffer.Version}) thành công!", newOffer);
    }

    public Task<List<OfferRejectionReasonCategoryDto>> GetRejectionReasonsAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<OfferRejectionReasonCategoryDto>
        {
            new()
            {
                Id = (int)OfferRejectionReasonCategory.Salary,
                Code = OfferRejectionReasonCategory.Salary.ToString(),
                DisplayName = "Lương / Đãi ngộ (Compensation)",
                Description = "Mức lương cơ bản, chế độ thưởng hoặc phụ cấp chưa đáp ứng kỳ vọng của ứng viên.",
                RequiresExpectedSalary = true
            },
            new()
            {
                Id = (int)OfferRejectionReasonCategory.Location,
                Code = OfferRejectionReasonCategory.Location.ToString(),
                DisplayName = "Địa điểm / Khoảng cách (Location)",
                Description = "Khoảng cách địa lý xa, giao thông bất tiện hoặc mong muốn hình thức làm việc Remote/Hybrid.",
                RequiresExpectedSalary = false
            },
            new()
            {
                Id = (int)OfferRejectionReasonCategory.CompetitorOffer,
                Code = OfferRejectionReasonCategory.CompetitorOffer.ToString(),
                DisplayName = "Đã nhận việc nơi khác (Accepted Another Offer)",
                Description = "Ứng viên đã chấp thuận đề nghị tuyển dụng từ một công ty/đối thủ cạnh tranh khác.",
                RequiresExpectedSalary = false
            },
            new()
            {
                Id = (int)OfferRejectionReasonCategory.Personal,
                Code = OfferRejectionReasonCategory.Personal.ToString(),
                DisplayName = "Lý do cá nhân (Personal / Family)",
                Description = "Gia đình có việc riêng, kế hoạch học tập, sức khỏe hoặc thay đổi định hướng nghề nghiệp.",
                RequiresExpectedSalary = false
            },
            new()
            {
                Id = (int)OfferRejectionReasonCategory.Other,
                Code = OfferRejectionReasonCategory.Other.ToString(),
                DisplayName = "Khác (Other)",
                Description = "Các lý do đặc thù khác được ghi nhận chi tiết trong phần phản hồi.",
                RequiresExpectedSalary = false
            }
        };

        return Task.FromResult(list);
    }

    public async Task<JobOffer?> GetOfferByIdAsync(Guid offerId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.JobOffers
            .Include(o => o.Application)
                .ThenInclude(a => a.Candidate)
            .Include(o => o.Application)
                .ThenInclude(a => a.JobPosting)
            .Include(o => o.ParentOffer)
            .FirstOrDefaultAsync(o => o.Id == offerId && !o.IsDeleted, cancellationToken);
    }

    public async Task<OfferItemDto?> GetOfferDetailsAsync(Guid offerId, CancellationToken cancellationToken = default)
    {
        var offer = await _dbContext.JobOffers
            .Include(o => o.Application)
                .ThenInclude(a => a.Candidate)
            .Include(o => o.Application)
                .ThenInclude(a => a.JobPosting)
            .FirstOrDefaultAsync(o => o.Id == offerId && !o.IsDeleted, cancellationToken);

        if (offer == null) return null;

        return MapToItemDto(offer);
    }

    public async Task<List<OfferItemDto>> GetOfferHistoryByApplicationAsync(Guid applicationId, CancellationToken cancellationToken = default)
    {
        var offers = await _dbContext.JobOffers
            .Include(o => o.Application)
                .ThenInclude(a => a.Candidate)
            .Include(o => o.Application)
                .ThenInclude(a => a.JobPosting)
            .Where(o => o.ApplicationId == applicationId && !o.IsDeleted)
            .OrderBy(o => o.Version)
            .ThenBy(o => o.CreatedAt)
            .ToListAsync(cancellationToken);

        return offers.Select(MapToItemDto).ToList();
    }

    private static OfferItemDto MapToItemDto(JobOffer offer)
    {
        var cand = offer.Application?.Candidate;
        var candName = cand != null ? $"{cand.FirstName} {cand.LastName}".Trim() : "Ứng viên";

        return new OfferItemDto
        {
            OfferId = offer.Id,
            ApplicationId = offer.ApplicationId,
            CandidateId = cand?.Id ?? Guid.Empty,
            CandidateName = candName,
            CandidateEmail = cand?.Email ?? string.Empty,
            JobTitle = offer.Application?.JobPosting?.Title ?? "Vị trí tuyển dụng",
            Version = offer.Version,
            ParentOfferId = offer.ParentOfferId,
            BaseSalary = offer.BaseSalary,
            BonusAllowance = offer.BonusAllowance,
            TotalPackage = offer.TotalPackage,
            ProposedJoinDate = offer.ProposedJoinDate,
            ProbationPeriodMonths = offer.ProbationPeriodMonths,
            ContractType = offer.ContractType ?? "Chính thức (Toàn thời gian)",
            Status = offer.Status,
            StatusDisplay = GetStatusDisplayName(offer.Status),
            RejectionReasonCategory = offer.RejectionReasonCategory,
            RejectionReasonCategoryDisplay = offer.RejectionReasonCategory.HasValue
                ? GetCategoryDisplayName(offer.RejectionReasonCategory.Value)
                : null,
            RejectionNotes = offer.RejectionNotes,
            ExpectedSalary = offer.ExpectedSalary,
            RejectedAt = offer.RejectedAt,
            CreatedAt = offer.CreatedAt
        };
    }

    private static string GetCategoryDisplayName(OfferRejectionReasonCategory category)
    {
        return category switch
        {
            OfferRejectionReasonCategory.Salary => "Lương / Đãi ngộ (Compensation)",
            OfferRejectionReasonCategory.Location => "Địa điểm / Khoảng cách (Location)",
            OfferRejectionReasonCategory.CompetitorOffer => "Đã nhận việc nơi khác (Accepted Another Offer)",
            OfferRejectionReasonCategory.Personal => "Lý do cá nhân (Personal / Family)",
            OfferRejectionReasonCategory.Other => "Khác (Other)",
            _ => category.ToString()
        };
    }

    private static string GetStatusDisplayName(OfferStatus status)
    {
        return status switch
        {
            OfferStatus.DRAFT => "Bản nháp (Draft)",
            OfferStatus.PENDING_APPROVAL => "Chờ phê duyệt (Pending)",
            OfferStatus.APPROVED => "Đã phê duyệt (Approved)",
            OfferStatus.SENT_TO_CANDIDATE => "Đã gửi ứng viên (Sent)",
            OfferStatus.ACCEPTED => "Ứng viên đồng ý (Accepted)",
            OfferStatus.REJECTED => "Ứng viên từ chối (Rejected)",
            OfferStatus.WITHDRAWN => "Đã rút lại (Withdrawn)",
            OfferStatus.REPLACED => "Đã thay thế (Replaced)",
            _ => status.ToString()
        };
    }
}
