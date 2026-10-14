using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ats.Web.Constants;
using Ats.Web.Data;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Models.ViewModels.JobPostings;
using Ats.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ats.Web.Services;

/// <summary>
/// Hiện thực nghiệp vụ tạo và quản lý tin tuyển dụng kế thừa từ yêu cầu tuyển dụng đã duyệt (SCRUM-29).
/// </summary>
public class JobPostingService(
    ApplicationDbContext dbContext,
    ILogger<JobPostingService> logger) : IJobPostingService
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly ILogger<JobPostingService> _logger = logger;

    /// <summary>
    /// Lấy dữ liệu kế thừa từ yêu cầu tuyển dụng đã phê duyệt để chuẩn bị form soạn tin.
    /// Bắt buộc Requisition phải có trạng thái APPROVED.
    /// </summary>
    public async Task<JobPostingPreFillViewModel> GetJobPostingPreFillAsync(Guid requisitionId, CancellationToken cancellationToken = default)
    {
        var requisition = await _dbContext.JobRequisitions
            .Include(r => r.JobPosition)
            .Include(r => r.Department)
            .Include(r => r.HiringManager)
            .Include(r => r.AssignedRecruiter)
            .Include(r => r.Approvals)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == requisitionId && !r.IsDeleted, cancellationToken);

        if (requisition == null)
        {
            throw new KeyNotFoundException($"Không tìm thấy yêu cầu tuyển dụng với mã ID: {requisitionId}");
        }

        if (requisition.Status != RequisitionStatus.APPROVED)
        {
            throw new InvalidOperationException(
                $"Chỉ có thể tạo tin tuyển dụng từ yêu cầu đã được phê duyệt (APPROVED). Trạng thái hiện tại: {requisition.Status}");
        }

        var existingPostingsCount = await _dbContext.JobPostings
            .CountAsync(p => p.RequisitionId == requisitionId && !p.IsDeleted, cancellationToken);

        var approvedApproval = requisition.Approvals
            .Where(a => a.Status == ApprovalStatus.APPROVED)
            .OrderByDescending(a => a.DecidedAt)
            .FirstOrDefault();

        var title = requisition.JobPosition?.Title ?? requisition.Code;
        var salaryDisplay = FormatSalaryDisplay(requisition.MinSalary, requisition.MaxSalary, requisition.Currency, true);

        // Hạn chót nộp hồ sơ gợi ý: lấy theo TargetHireDate nếu hợp lệ, hoặc cộng thêm 30 ngày từ hôm nay
        DateTime? suggestedDeadline = null;
        if (requisition.TargetHireDate.HasValue)
        {
            var targetDateTime = requisition.TargetHireDate.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            suggestedDeadline = targetDateTime > DateTime.UtcNow ? targetDateTime : DateTime.UtcNow.AddDays(30);
        }
        else
        {
            suggestedDeadline = DateTime.UtcNow.AddDays(30);
        }

        const string defaultBenefits = "• Thu nhập cạnh tranh, đánh giá tăng lương định kỳ hằng năm.\n" +
                                       "• Thưởng hiệu quả công việc (KPIs), thưởng dự án và thưởng lễ Tết.\n" +
                                       "• Đóng BHXH, BHYT, BHTN đầy đủ theo quy định Luật Lao động.\n" +
                                       "• Gói bảo hiểm sức khỏe cao cấp NoveraCare VIP cho nhân viên.\n" +
                                       "• Chế độ làm việc linh hoạt (Hybrid), trang bị laptop cao cấp.\n" +
                                       "• Teambuilding, dã ngoại và du lịch nghỉ dưỡng 5 sao hằng năm.";

        return new JobPostingPreFillViewModel
        {
            RequisitionId = requisition.Id,
            RequisitionCode = requisition.Code,
            JobPositionId = requisition.JobPositionId,
            JobPositionTitle = requisition.JobPosition?.Title ?? "Chưa xác định",
            JobPositionCode = requisition.JobPosition?.Code,
            DepartmentId = requisition.DepartmentId,
            DepartmentName = requisition.Department?.Name ?? "Chưa phân bổ",
            HiringManagerId = requisition.HiringManagerId,
            HiringManagerName = requisition.HiringManager?.FullName ?? "Chưa xác định",
            HiringManagerEmail = requisition.HiringManager?.Email ?? string.Empty,
            AssignedRecruiterId = requisition.AssignedRecruiterId,
            AssignedRecruiterName = requisition.AssignedRecruiter?.FullName,
            Quantity = requisition.Quantity,
            HeadcountType = requisition.HeadcountType,
            HeadcountTypeName = requisition.HeadcountType == HeadcountType.NEW_HEADCOUNT ? "Tuyển mới (New Headcount)" : "Thay thế (Replacement)",
            TargetHireDate = requisition.TargetHireDate,
            RequisitionStatus = requisition.Status,
            RequisitionStatusName = "Đã phê duyệt (APPROVED)",
            ApprovedAt = approvedApproval?.DecidedAt,
            ExistingJobPostingsCount = existingPostingsCount,

            // Dữ liệu kế thừa
            Title = title,
            JobDescription = requisition.JobDescription ?? string.Empty,
            Requirements = requisition.Requirements ?? string.Empty,
            Benefits = defaultBenefits,
            MinSalary = requisition.MinSalary,
            MaxSalary = requisition.MaxSalary,
            Currency = string.IsNullOrWhiteSpace(requisition.Currency) ? "VND" : requisition.Currency,
            SalaryDisplay = salaryDisplay,

            // Gợi ý mặc định
            SuggestedWorkLocation = "Hà Nội (Hybrid 2 ngày WFH)",
            SuggestedEmploymentType = EmploymentType.FULL_TIME,
            SuggestedDeadline = suggestedDeadline,
            IsSalaryDisclosed = requisition.MinSalary.HasValue || requisition.MaxSalary.HasValue
        };
    }

    /// <summary>
    /// Tạo tin tuyển dụng mới từ yêu cầu tuyển dụng đã duyệt.
    /// Mở database transaction, lưu entity, ghi log kiểm toán (AuditLog) và commit an toàn.
    /// </summary>
    public async Task<JobPosting> CreateJobPostingFromRequisitionAsync(
        JobPostingCreateInputModel input,
        string createdByUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (string.IsNullOrWhiteSpace(createdByUserId) || !Guid.TryParse(createdByUserId, out var currentUserId))
        {
            throw new UnauthorizedAccessException("Người dùng chưa được xác thực hoặc định danh người dùng không hợp lệ.");
        }

        // 1. Kiểm tra quyền Recruiter / Admin
        var hasPermission = await CanUserCreateJobPostingAsync(createdByUserId, cancellationToken);
        if (!hasPermission)
        {
            throw new UnauthorizedAccessException("Bạn không có quyền tạo tin tuyển dụng. Chỉ Chuyên viên tuyển dụng (Recruiter) hoặc Quản trị viên (Admin) mới có quyền thực hiện.");
        }

        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == currentUserId, cancellationToken);

        // 2. Kiểm tra Requisition tồn tại và có trạng thái APPROVED
        var requisition = await _dbContext.JobRequisitions
            .Include(r => r.JobPosition)
            .Include(r => r.Department)
            .FirstOrDefaultAsync(r => r.Id == input.RequisitionId && !r.IsDeleted, cancellationToken);

        if (requisition == null)
        {
            throw new KeyNotFoundException($"Không tìm thấy yêu cầu tuyển dụng với ID: {input.RequisitionId}");
        }

        if (requisition.Status != RequisitionStatus.APPROVED)
        {
            throw new InvalidOperationException($"Chỉ có thể tạo tin tuyển dụng từ yêu cầu tuyển dụng đã được duyệt (APPROVED). Trạng thái hiện tại: {requisition.Status}");
        }

        // 3. Chuẩn bị dữ liệu JobPosting
        var now = DateTimeOffset.UtcNow;
        var status = input.SaveAsDraft ? JobPostingStatus.DRAFT : JobPostingStatus.PUBLISHED;
        var publishedAt = input.SaveAsDraft ? (DateTimeOffset?)null : now;

        DateTimeOffset? expiredAt = null;
        if (input.ApplicationDeadline.HasValue)
        {
            var deadlineUtc = input.ApplicationDeadline.Value.Kind == DateTimeKind.Utc
                ? input.ApplicationDeadline.Value
                : DateTime.SpecifyKind(input.ApplicationDeadline.Value, DateTimeKind.Utc);
            expiredAt = new DateTimeOffset(deadlineUtc);
        }
        else if (!input.SaveAsDraft)
        {
            expiredAt = now.AddDays(30);
        }

        var salaryDisplay = FormatSalaryDisplay(input.MinSalary, input.MaxSalary, input.Currency, input.IsSalaryDisclosed);
        var slug = GenerateUniqueSlug(input.Title);
        var postingCode = "POST-" + now.ToString("yyyyMMdd") + "-" + Guid.NewGuid().ToString("N")[..4].ToUpper();

        var posting = new JobPosting
        {
            Id = Guid.NewGuid(),
            RequisitionId = input.RequisitionId,
            Code = postingCode,
            Title = input.Title.Trim(),
            Slug = slug,
            WorkLocation = input.WorkLocation.Trim(),
            EmploymentType = input.EmploymentType,
            SalaryDisplay = salaryDisplay,
            JobDescription = input.JobDescription?.Trim(),
            Requirements = input.Requirements?.Trim(),
            Benefits = input.Benefits?.Trim(),
            Status = status,
            PublishedAt = publishedAt,
            ExpiredAt = expiredAt,
            CreatedById = currentUserId,
            CreatedAt = now,
            UpdatedAt = now
        };

        // 4. Mở Database Transaction đảm bảo tính toàn vẹn và bất biến
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await _dbContext.JobPostings.AddAsync(posting, cancellationToken);

            // Ghi nhận sổ kiểm toán hệ thống AuditLog
            var auditLog = new AuditLog
            {
                Id = Guid.NewGuid(),
                UserId = currentUserId,
                Action = input.SaveAsDraft ? "JOB_POSTING_CREATE_DRAFT" : "JOB_POSTING_CREATE_PUBLISHED",
                EntityName = "JobPosting",
                EntityId = posting.Id.ToString(),
                OldValues = null,
                NewValues = JsonSerializer.Serialize(new
                {
                    PostingId = posting.Id,
                    posting.Code,
                    posting.RequisitionId,
                    RequisitionCode = requisition.Code,
                    posting.Title,
                    posting.Slug,
                    posting.WorkLocation,
                    EmploymentType = posting.EmploymentType.ToString(),
                    posting.SalaryDisplay,
                    IsSalaryDisclosed = input.IsSalaryDisclosed,
                    MinSalary = input.MinSalary,
                    MaxSalary = input.MaxSalary,
                    Currency = input.Currency,
                    Status = posting.Status.ToString(),
                    posting.PublishedAt,
                    posting.ExpiredAt,
                    CreatedByUserId = currentUserId,
                    CreatedByName = user?.FullName ?? "Unknown",
                    CreatedByEmail = user?.Email ?? "Unknown",
                    CreatedAt = now
                }, new JsonSerializerOptions
                {
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                }),
                CreatedAt = now
            };

            await _dbContext.AuditLogs.AddAsync(auditLog, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "Tạo tin tuyển dụng thành công. Mã: {PostingCode}, Tiêu đề: {Title}, Requisition: {RequisitionCode}, Trạng thái: {Status}, Người tạo: {UserId}",
                posting.Code, posting.Title, requisition.Code, posting.Status, currentUserId);

            return posting;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Lỗi khi tạo tin tuyển dụng từ yêu cầu {RequisitionId}", input.RequisitionId);
            throw;
        }
    }

    /// <summary>
    /// Kiểm tra người dùng có quyền Recruiter hoặc Admin hay không.
    /// </summary>
    public async Task<bool> CanUserCreateJobPostingAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || !Guid.TryParse(userId, out var userGuid))
        {
            return false;
        }

        var user = await _dbContext.Users
            .Include(u => u.RoleEntity)
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userGuid && u.Status == "ACTIVE", cancellationToken);

        if (user == null)
        {
            return false;
        }

        // 1. Kiểm tra chuỗi vai trò trực tiếp (User.Role)
        var normalizedRole = UserRoles.NormalizeRole(user.Role);
        if (normalizedRole is UserRoles.Admin or UserRoles.Recruiter or UserRoles.HRManager)
        {
            return true;
        }

        // 2. Kiểm tra RoleEntity
        if (user.RoleEntity?.Code is RoleCode.ADMIN or RoleCode.RECRUITER or RoleCode.HR_MANAGER)
        {
            return true;
        }

        // 3. Kiểm tra danh sách UserRoles (Nhiều vai trò đồng thời)
        if (user.UserRoles.Any(ur => ur.Role.Code is RoleCode.ADMIN or RoleCode.RECRUITER or RoleCode.HR_MANAGER
                                  || UserRoles.NormalizeRole(ur.Role.Name) is UserRoles.Admin or UserRoles.Recruiter or UserRoles.HRManager))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Lấy chi tiết tin tuyển dụng theo ID.
    /// </summary>
    public async Task<JobPosting?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.JobPostings
            .Include(p => p.Requisition)
                .ThenInclude(r => r.Department)
            .Include(p => p.Requisition)
                .ThenInclude(r => r.JobPosition)
            .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, cancellationToken);
    }

    private static readonly CultureInfo VietnameseCulture = CultureInfo.GetCultureInfo("vi-VN");

    /// <summary>
    /// Định dạng chuỗi hiển thị mức lương thân thiện người dùng (Culture vi-VN).
    /// </summary>
    private static string FormatSalaryDisplay(decimal? minSalary, decimal? maxSalary, string? currency, bool isSalaryDisclosed)
    {
        if (!isSalaryDisclosed)
        {
            return "Thỏa thuận";
        }

        var curr = string.IsNullOrWhiteSpace(currency) ? "VND" : currency.Trim();

        if (minSalary.HasValue && maxSalary.HasValue)
        {
            return $"{minSalary.Value.ToString("N0", VietnameseCulture)} - {maxSalary.Value.ToString("N0", VietnameseCulture)} {curr}";
        }
        if (minSalary.HasValue)
        {
            return $">= {minSalary.Value.ToString("N0", VietnameseCulture)} {curr}";
        }
        if (maxSalary.HasValue)
        {
            return $"<= {maxSalary.Value.ToString("N0", VietnameseCulture)} {curr}";
        }

        return "Thỏa thuận";
    }

    /// <summary>
    /// Sinh chuỗi URL thân thiện (Slug) duy nhất từ tiêu đề bài đăng.
    /// </summary>
    private static string GenerateUniqueSlug(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return "tin-tuyen-dung-" + Guid.NewGuid().ToString("N")[..6];
        }

        var normalizedString = title.Normalize(NormalizationForm.FormD);
        var stringBuilder = new StringBuilder();

        foreach (var c in normalizedString)
        {
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != UnicodeCategory.NonSpacingMark)
            {
                if (c is 'đ' or 'Đ')
                {
                    stringBuilder.Append('d');
                }
                else
                {
                    stringBuilder.Append(c);
                }
            }
        }

        var cleanString = stringBuilder.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
        cleanString = Regex.Replace(cleanString, @"[^a-z0-9\s-]", "");
        cleanString = Regex.Replace(cleanString, @"[\s-]+", "-").Trim('-');

        if (string.IsNullOrWhiteSpace(cleanString))
        {
            cleanString = "tin-tuyen-dung";
        }

        var suffix = Guid.NewGuid().ToString("N")[..6];
        return $"{cleanString}-{suffix}";
    }
}
