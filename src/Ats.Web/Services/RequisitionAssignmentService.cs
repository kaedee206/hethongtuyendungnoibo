using Ats.Web.Constants;
using Ats.Web.Data;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Models.ViewModels.RequisitionAssignments;
using Ats.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Ats.Web.Services;

public class RequisitionAssignmentService : IRequisitionAssignmentService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<RequisitionAssignmentService> _logger;

    public RequisitionAssignmentService(
        ApplicationDbContext dbContext,
        ILogger<RequisitionAssignmentService> logger)
        : this(dbContext, new MemoryCache(new MemoryCacheOptions()), logger)
    {
    }

    public RequisitionAssignmentService(
        ApplicationDbContext dbContext,
        IMemoryCache memoryCache,
        ILogger<RequisitionAssignmentService> logger)
    {
        _dbContext = dbContext;
        _memoryCache = memoryCache;
        _logger = logger;
    }

    public async Task<RequisitionAssignmentListViewModel> GetAssignmentOverviewAsync(
        string? search = null,
        Guid? departmentId = null,
        Guid? recruiterId = null,
        string? assignmentFilter = null,
        int page = 1,
        int pageSize = 10,
        string? department = null,
        CancellationToken cancellationToken = default)
    {
        var validPageSize = RequisitionAssignmentListViewModel.AllowedPageSizes.Contains(pageSize) ? pageSize : 10;
        var validPage = page < 1 ? 1 : page;

        var model = new RequisitionAssignmentListViewModel
        {
            Search = search,
            SelectedDepartmentId = departmentId,
            SelectedDepartmentName = department,
            SelectedRecruiterId = recruiterId,
            AssignmentFilter = assignmentFilter ?? "ALL",
            CurrentPage = validPage,
            PageSize = validPageSize
        };

        // 1. Tải danh sách Recruiters khả dụng
        model.AvailableRecruiters = await GetAvailableRecruitersAsync(cancellationToken);
        model.TotalActiveRecruitersCount = model.AvailableRecruiters.Count;

        // 2. Base Query JobRequisitions (Bỏ qua bản nháp, đã hủy, và đã từ chối)
        var baseQuery = _dbContext.JobRequisitions
            .AsNoTracking()
            .Where(r => !r.IsDeleted && 
                        r.Status != RequisitionStatus.DRAFT && 
                        r.Status != RequisitionStatus.CANCELLED &&
                        r.Status != RequisitionStatus.REJECTED);

        model.TotalOpenRequisitions = await baseQuery.CountAsync(cancellationToken);
        model.AssignedRequisitionsCount = await baseQuery.CountAsync(r => r.AssignedRecruiterId != null, cancellationToken);
        model.UnassignedRequisitionsCount = model.TotalOpenRequisitions - model.AssignedRequisitionsCount;

        // 3. Tính toán RecruiterWorkloads trên TOÀN BỘ vị trí mở (đảm bảo không bị giới hạn bởi phân trang)
        var allActiveRequisitions = await _dbContext.JobRequisitions
            .AsNoTracking()
            .Include(r => r.JobPosition)
            .Include(r => r.RequisitionRecruiters.Where(rr => !rr.IsDeleted))
            .Where(r => !r.IsDeleted && 
                        r.Status != RequisitionStatus.DRAFT && 
                        r.Status != RequisitionStatus.CANCELLED &&
                        r.Status != RequisitionStatus.REJECTED)
            .Select(r => new
            {
                r.Id,
                r.Code,
                JobTitle = r.JobPosition != null ? r.JobPosition.Title : "Chưa phân bổ chức danh",
                r.AssignedRecruiterId,
                SupportingRecruiterIds = r.RequisitionRecruiters
                    .Where(rr => !rr.IsDeleted && !rr.IsPrimary && rr.RecruiterId != r.AssignedRecruiterId)
                    .Select(rr => rr.RecruiterId)
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        var allReqIds = allActiveRequisitions.Select(r => r.Id).ToList();
        var allPipelineStats = await _dbContext.Applications
            .AsNoTracking()
            .Where(a => !a.IsDeleted && a.Status == ApplicationStatus.IN_PROCESS && allReqIds.Contains(a.JobPosting.RequisitionId))
            .GroupBy(a => a.JobPosting.RequisitionId)
            .Select(g => new { RequisitionId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RequisitionId, x => x.Count, cancellationToken);

        model.RecruiterWorkloads = model.AvailableRecruiters.Select(rec =>
        {
            var leadJobs = allActiveRequisitions
                .Where(i => i.AssignedRecruiterId == rec.Id)
                .Select(i => new RequisitionAssignmentItemViewModel
                {
                    RequisitionId = i.Id,
                    Code = i.Code,
                    JobPositionTitle = i.JobTitle,
                    LeadRecruiterId = i.AssignedRecruiterId,
                    InProcessCandidatesCount = allPipelineStats.TryGetValue(i.Id, out var c) ? c : 0
                })
                .ToList();

            var supportJobs = allActiveRequisitions
                .Where(i => i.SupportingRecruiterIds.Contains(rec.Id))
                .Select(i => new RequisitionAssignmentItemViewModel
                {
                    RequisitionId = i.Id,
                    Code = i.Code,
                    JobPositionTitle = i.JobTitle,
                    LeadRecruiterId = i.AssignedRecruiterId,
                    InProcessCandidatesCount = allPipelineStats.TryGetValue(i.Id, out var c) ? c : 0
                })
                .ToList();

            var allAssigned = leadJobs.Concat(supportJobs).DistinctBy(i => i.RequisitionId).ToList();
            var totalPipeline = allAssigned.Sum(i => i.InProcessCandidatesCount);

            return new RecruiterWorkloadViewModel
            {
                RecruiterId = rec.Id,
                FullName = rec.FullName,
                Email = rec.Email,
                JobTitle = rec.JobTitle,
                DepartmentName = rec.DepartmentName,
                LeadCount = leadJobs.Count,
                SupportCount = supportJobs.Count,
                TotalCandidatesInPipeline = totalPipeline,
                AssignedPositions = allAssigned
            };
        }).OrderByDescending(w => w.LeadCount).ThenBy(w => w.FullName).ToList();

        // 4. Query & Filter cho Bảng danh sách phân công
        var query = _dbContext.JobRequisitions
            .AsNoTracking()
            .Include(r => r.JobPosition)
            .Include(r => r.Department)
            .Include(r => r.AssignedRecruiter)
            .Include(r => r.RequisitionRecruiters.Where(rr => !rr.IsDeleted))
                .ThenInclude(rr => rr.Recruiter)
            .Include(r => r.HandoverHistories.Where(h => !h.IsDeleted))
            .Where(r => !r.IsDeleted && 
                        r.Status != RequisitionStatus.DRAFT && 
                        r.Status != RequisitionStatus.CANCELLED &&
                        r.Status != RequisitionStatus.REJECTED);

        // Lọc theo tìm kiếm từ khóa
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(r =>
                r.Code.ToLower().Contains(s) ||
                (r.JobPosition != null && r.JobPosition.Title.ToLower().Contains(s)) ||
                (r.Department != null && r.Department.Name.ToLower().Contains(s)) ||
                (r.AssignedRecruiter != null && r.AssignedRecruiter.FullName.ToLower().Contains(s)));
        }

        // Lọc theo Phòng ban
        if (departmentId.HasValue && departmentId.Value != Guid.Empty)
        {
            query = query.Where(r => r.DepartmentId == departmentId.Value);
        }
        else if (!string.IsNullOrWhiteSpace(department))
        {
            query = query.Where(r => r.Department != null && r.Department.Name == department);
        }

        // Lọc theo Recruiter
        if (recruiterId.HasValue && recruiterId.Value != Guid.Empty)
        {
            query = query.Where(r =>
                r.AssignedRecruiterId == recruiterId.Value ||
                r.RequisitionRecruiters.Any(rr => rr.RecruiterId == recruiterId.Value && !rr.IsDeleted));
        }

        // Lọc theo trạng thái phân công
        if (assignmentFilter == "ASSIGNED")
        {
            query = query.Where(r => r.AssignedRecruiterId != null);
        }
        else if (assignmentFilter == "UNASSIGNED")
        {
            query = query.Where(r => r.AssignedRecruiterId == null);
        }

        // Đếm tổng số bản ghi khớp bộ lọc
        model.TotalRecords = await query.CountAsync(cancellationToken);
        if (model.TotalPages > 0 && model.CurrentPage > model.TotalPages)
        {
            model.CurrentPage = model.TotalPages;
        }

        // Phân trang tầng Database (OFFSET / LIMIT)
        var dbItems = await query
            .OrderByDescending(r => r.UpdatedAt)
            .Skip((model.CurrentPage - 1) * model.PageSize)
            .Take(model.PageSize)
            .ToListAsync(cancellationToken);

        // Lấy thống kê số lượng ứng viên CHỈ cho các Requisition Id của trang hiện tại
        var pageReqIds = dbItems.Select(r => r.Id).ToList();
        var appStats = await _dbContext.Applications
            .AsNoTracking()
            .Where(a => !a.IsDeleted && pageReqIds.Contains(a.JobPosting.RequisitionId))
            .GroupBy(a => a.JobPosting.RequisitionId)
            .Select(g => new
            {
                RequisitionId = g.Key,
                TotalCount = g.Count(),
                InProcessCount = g.Count(a => a.Status == ApplicationStatus.IN_PROCESS)
            })
            .ToDictionaryAsync(x => x.RequisitionId, cancellationToken);

        // Populate danh sách phòng ban cho filter bar (IMemoryCache TTL 10 phút)
        model.Departments = await _memoryCache.GetOrCreateAsync("lookup_departments_names", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            return await _dbContext.Departments
                .AsNoTracking()
                .Where(d => !d.IsDeleted)
                .OrderBy(d => d.Name)
                .Select(d => d.Name)
                .Distinct()
                .ToListAsync(cancellationToken);
        }) ?? new List<string>();

        model.Items = dbItems.Select(r =>
        {
            appStats.TryGetValue(r.Id, out var stats);

            var supporting = r.RequisitionRecruiters
                .Where(rr => !rr.IsDeleted && !rr.IsPrimary && rr.RecruiterId != r.AssignedRecruiterId)
                .Select(rr => new SupportingRecruiterDto
                {
                    RecruiterId = rr.RecruiterId,
                    FullName = rr.Recruiter?.FullName ?? "Chuyên viên tuyển dụng",
                    Email = rr.Recruiter?.Email ?? "",
                    Note = rr.Note,
                    AssignedAt = rr.AssignedAt
                })
                .ToList();

            var lastHandover = r.HandoverHistories
                .OrderByDescending(h => h.TransferredAt)
                .FirstOrDefault();

            return new RequisitionAssignmentItemViewModel
            {
                RequisitionId = r.Id,
                Code = r.Code,
                JobPositionTitle = r.JobPosition?.Title ?? "Chưa phân bổ chức danh",
                JobPositionCode = r.JobPosition?.Code ?? "",
                DepartmentName = r.Department?.Name ?? "NoveraTech",
                DepartmentId = r.DepartmentId ?? Guid.Empty,
                JobLevel = r.JobPosition?.JobLevel ?? "Middle",
                Quantity = r.Quantity,
                Status = r.Status,
                TargetHireDate = r.TargetHireDate,
                LeadRecruiterId = r.AssignedRecruiterId,
                LeadRecruiterName = r.AssignedRecruiter?.FullName,
                LeadRecruiterEmail = r.AssignedRecruiter?.Email,
                LeadAssignedAt = r.RequisitionRecruiters.FirstOrDefault(rr => rr.IsPrimary && !rr.IsDeleted)?.AssignedAt ?? r.UpdatedAt,
                SupportingRecruiters = supporting,
                TotalCandidatesCount = stats?.TotalCount ?? 0,
                InProcessCandidatesCount = stats?.InProcessCount ?? 0,
                HandoverCount = r.HandoverHistories.Count,
                LastHandoverAt = lastHandover?.TransferredAt
            };
        }).ToList();

        return model;
    }

    public async Task<RequisitionAssignmentItemViewModel?> GetAssignmentDetailAsync(
        Guid requisitionId,
        CancellationToken cancellationToken = default)
    {
        var r = await _dbContext.JobRequisitions
            .AsNoTracking()
            .Include(r => r.JobPosition)
            .Include(r => r.Department)
            .Include(r => r.AssignedRecruiter)
            .Include(r => r.RequisitionRecruiters.Where(rr => !rr.IsDeleted))
                .ThenInclude(rr => rr.Recruiter)
            .Include(r => r.HandoverHistories.Where(h => !h.IsDeleted))
            .FirstOrDefaultAsync(r => r.Id == requisitionId && !r.IsDeleted, cancellationToken);

        if (r == null) return null;

        var stats = await _dbContext.Applications
            .AsNoTracking()
            .Where(a => !a.IsDeleted && a.JobPosting.RequisitionId == requisitionId)
            .GroupBy(a => a.JobPosting.RequisitionId)
            .Select(g => new
            {
                TotalCount = g.Count(),
                InProcessCount = g.Count(a => a.Status == ApplicationStatus.IN_PROCESS)
            })
            .FirstOrDefaultAsync(cancellationToken);

        var supporting = r.RequisitionRecruiters
            .Where(rr => !rr.IsDeleted && !rr.IsPrimary && rr.RecruiterId != r.AssignedRecruiterId)
            .Select(rr => new SupportingRecruiterDto
            {
                RecruiterId = rr.RecruiterId,
                FullName = rr.Recruiter?.FullName ?? "",
                Email = rr.Recruiter?.Email ?? "",
                Note = rr.Note,
                AssignedAt = rr.AssignedAt
            })
            .ToList();

        var lastHandover = r.HandoverHistories
            .OrderByDescending(h => h.TransferredAt)
            .FirstOrDefault();

        return new RequisitionAssignmentItemViewModel
        {
            RequisitionId = r.Id,
            Code = r.Code,
            JobPositionTitle = r.JobPosition?.Title ?? "",
            JobPositionCode = r.JobPosition?.Code ?? "",
            DepartmentName = r.Department?.Name ?? "",
            DepartmentId = r.DepartmentId ?? Guid.Empty,
            Quantity = r.Quantity,
            Status = r.Status,
            TargetHireDate = r.TargetHireDate,
            LeadRecruiterId = r.AssignedRecruiterId,
            LeadRecruiterName = r.AssignedRecruiter?.FullName,
            LeadRecruiterEmail = r.AssignedRecruiter?.Email,
            SupportingRecruiters = supporting,
            TotalCandidatesCount = stats?.TotalCount ?? 0,
            InProcessCandidatesCount = stats?.InProcessCount ?? 0,
            HandoverCount = r.HandoverHistories.Count,
            LastHandoverAt = lastHandover?.TransferredAt
        };
    }

    public async Task<(bool Success, string Message)> AssignRecruitersAsync(
        RequisitionAssignRequestModel model,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        if (model.RequisitionId == Guid.Empty)
        {
            return (false, "Yêu cầu tuyển dụng không hợp lệ.");
        }

        if (model.LeadRecruiterId == Guid.Empty)
        {
            return (false, "Vui lòng chỉ định Chuyên viên tuyển dụng phụ trách chính.");
        }

        // Tải requisition kèm các quan hệ
        var requisition = await _dbContext.JobRequisitions
            .Include(r => r.RequisitionRecruiters)
            .Include(r => r.JobPosition)
            .FirstOrDefaultAsync(r => r.Id == model.RequisitionId && !r.IsDeleted, cancellationToken);

        if (requisition == null)
        {
            return (false, "Không tìm thấy yêu cầu tuyển dụng cần phân công.");
        }

        // Kiểm tra Recruiter chính mới
        var newLeadUser = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == model.LeadRecruiterId && u.Status != "DELETED", cancellationToken);

        if (newLeadUser == null)
        {
            return (false, "Không tìm thấy chuyên viên tuyển dụng chính trong hệ thống.");
        }

        var previousLeadId = requisition.AssignedRecruiterId;
        var isLeadChanged = previousLeadId.HasValue && previousLeadId.Value != model.LeadRecruiterId;

        // Nếu thay đổi người phụ trách chính thì BẮT BUỘC phải có lý do chuyển giao
        if (isLeadChanged && string.IsNullOrWhiteSpace(model.Reason))
        {
            return (false, "Bắt buộc phải nhập lý do chuyển giao khi thay đổi Recruiter chính phụ trách.");
        }

        var now = DateTimeOffset.UtcNow;

        // 1. Ghi nhận lịch sử chuyển giao nếu có thay đổi Lead Recruiter
        if (isLeadChanged)
        {
            var oldLeadUser = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == previousLeadId!.Value, cancellationToken);

            var handover = new RequisitionHandoverHistory
            {
                Id = Guid.NewGuid(),
                RequisitionId = requisition.Id,
                FromRecruiterId = previousLeadId,
                ToRecruiterId = model.LeadRecruiterId,
                HandoverType = "LEAD_HANDOVER",
                Reason = model.Reason!.Trim(),
                Notes = string.IsNullOrWhiteSpace(model.Notes) ? null : model.Notes.Trim(),
                TransferredById = currentUserId,
                TransferredAt = now,
                CreatedAt = now,
                UpdatedAt = now
            };

            await _dbContext.RequisitionHandoverHistories.AddAsync(handover, cancellationToken);
            _logger.LogInformation("Đã ghi lịch sử chuyển giao vị trí {Code} từ {OldLead} sang {NewLead} do: {Reason}",
                requisition.Code, oldLeadUser?.FullName ?? "N/A", newLeadUser.FullName, model.Reason);
        }
        else if (!previousLeadId.HasValue)
        {
            // Phân công lần đầu tiên
            var initialHistory = new RequisitionHandoverHistory
            {
                Id = Guid.NewGuid(),
                RequisitionId = requisition.Id,
                FromRecruiterId = null,
                ToRecruiterId = model.LeadRecruiterId,
                HandoverType = "INITIAL_ASSIGNMENT",
                Reason = string.IsNullOrWhiteSpace(model.Reason) ? "Phân công người phụ trách ban đầu" : model.Reason.Trim(),
                Notes = string.IsNullOrWhiteSpace(model.Notes) ? null : model.Notes.Trim(),
                TransferredById = currentUserId,
                TransferredAt = now,
                CreatedAt = now,
                UpdatedAt = now
            };

            await _dbContext.RequisitionHandoverHistories.AddAsync(initialHistory, cancellationToken);
        }

        // 2. Cập nhật AssignedRecruiterId trên JobRequisition
        requisition.AssignedRecruiterId = model.LeadRecruiterId;
        requisition.UpdatedAt = now;

        // 3. Chuẩn hóa danh sách Recruiter hỗ trợ (loại bỏ trùng lặp và loại trừ Recruiter chính)
        var cleanSupportingIds = (model.SupportingRecruiterIds ?? new List<Guid>())
            .Where(id => id != Guid.Empty && id != model.LeadRecruiterId)
            .Distinct()
            .ToList();

        // 4. Đồng bộ bảng RequisitionRecruiter
        var existingAssignments = await _dbContext.RequisitionRecruiters
            .Where(rr => rr.RequisitionId == requisition.Id)
            .ToListAsync(cancellationToken);

        // Đánh dấu xóa mềm tất cả các phân công hiện tại của yêu cầu này
        foreach (var existing in existingAssignments.Where(rr => !rr.IsDeleted))
        {
            existing.IsDeleted = true;
            existing.DeletedAt = now;
            existing.UpdatedAt = now;
        }

        // Tạo/Kích hoạt phân công Recruiter chính
        var primaryAssignment = existingAssignments
            .FirstOrDefault(rr => rr.RecruiterId == model.LeadRecruiterId);

        if (primaryAssignment != null)
        {
            primaryAssignment.IsPrimary = true;
            primaryAssignment.IsDeleted = false;
            primaryAssignment.DeletedAt = null;
            primaryAssignment.AssignedById = currentUserId;
            primaryAssignment.AssignedAt = now;
            primaryAssignment.UpdatedAt = now;
            primaryAssignment.Note = "Chuyên viên tuyển dụng phụ trách chính";
        }
        else
        {
            var newPrimary = new RequisitionRecruiter
            {
                Id = Guid.NewGuid(),
                RequisitionId = requisition.Id,
                RecruiterId = model.LeadRecruiterId,
                IsPrimary = true,
                AssignedById = currentUserId,
                AssignedAt = now,
                Note = "Chuyên viên tuyển dụng phụ trách chính",
                CreatedAt = now,
                UpdatedAt = now
            };
            await _dbContext.RequisitionRecruiters.AddAsync(newPrimary, cancellationToken);
        }

        // Tạo/Kích hoạt các Recruiter hỗ trợ
        foreach (var supId in cleanSupportingIds)
        {
            var supAssignment = existingAssignments
                .FirstOrDefault(rr => rr.RecruiterId == supId);

            if (supAssignment != null)
            {
                supAssignment.IsPrimary = false;
                supAssignment.IsDeleted = false;
                supAssignment.DeletedAt = null;
                supAssignment.AssignedById = currentUserId;
                supAssignment.AssignedAt = now;
                supAssignment.UpdatedAt = now;
                supAssignment.Note = "Recruiter hỗ trợ";
            }
            else
            {
                var newSupport = new RequisitionRecruiter
                {
                    Id = Guid.NewGuid(),
                    RequisitionId = requisition.Id,
                    RecruiterId = supId,
                    IsPrimary = false,
                    AssignedById = currentUserId,
                    AssignedAt = now,
                    Note = "Recruiter hỗ trợ",
                    CreatedAt = now,
                    UpdatedAt = now
                };
                await _dbContext.RequisitionRecruiters.AddAsync(newSupport, cancellationToken);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _memoryCache.Remove("lookup_available_recruiters");

        var msg = isLeadChanged
            ? $"Đã chuyển giao người phụ trách vị trí '{requisition.JobPosition?.Title ?? requisition.Code}' sang {newLeadUser.FullName} thành công."
            : $"Đã cập nhật phân công recruiter cho yêu cầu '{requisition.Code}' thành công.";

        return (true, msg);
    }

    public async Task<List<RequisitionHandoverHistoryViewModel>> GetHandoverHistoriesAsync(
        Guid requisitionId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.RequisitionHandoverHistories
            .AsNoTracking()
            .Include(h => h.FromRecruiter)
            .Include(h => h.ToRecruiter)
            .Include(h => h.TransferredBy)
            .Include(h => h.Requisition)
                .ThenInclude(r => r.JobPosition)
            .Where(h => h.RequisitionId == requisitionId && !h.IsDeleted)
            .OrderByDescending(h => h.TransferredAt)
            .Select(h => new RequisitionHandoverHistoryViewModel
            {
                Id = h.Id,
                RequisitionId = h.RequisitionId,
                RequisitionCode = h.Requisition.Code,
                JobPositionTitle = h.Requisition.JobPosition != null ? h.Requisition.JobPosition.Title : "Vị trí tuyển dụng",
                FromRecruiterId = h.FromRecruiterId,
                FromRecruiterName = h.FromRecruiter != null ? h.FromRecruiter.FullName : null,
                FromRecruiterEmail = h.FromRecruiter != null ? h.FromRecruiter.Email : null,
                ToRecruiterId = h.ToRecruiterId,
                ToRecruiterName = h.ToRecruiter.FullName,
                ToRecruiterEmail = h.ToRecruiter.Email,
                HandoverType = h.HandoverType,
                Reason = h.Reason,
                Notes = h.Notes,
                TransferredByName = h.TransferredBy.FullName,
                TransferredAt = h.TransferredAt
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<List<RecruiterOptionViewModel>> GetAvailableRecruitersAsync(
        CancellationToken cancellationToken = default)
    {
        return await _memoryCache.GetOrCreateAsync("lookup_available_recruiters", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(3);

            // Lấy tất cả user có vai trò RECRUITER hoặc HR
            var recruiters = await _dbContext.Users
                .AsNoTracking()
                .Include(u => u.DepartmentEntity)
                .Where(u => u.Status != "DELETED" &&
                            (u.Role.ToUpper().Contains("RECRUITER") ||
                             u.Role.ToUpper().Contains("HR") ||
                             u.UserRoles.Any(ur => ur.Role.Code == RoleCode.RECRUITER || ur.Role.Code == RoleCode.HR_MANAGER)))
                .OrderBy(u => u.FullName)
                .ToListAsync(cancellationToken);

            // Đếm số job mà mỗi recruiter đang là lead và support
            var activeReqs = await _dbContext.JobRequisitions
                .AsNoTracking()
                .Where(r => !r.IsDeleted && r.Status != RequisitionStatus.CANCELLED && r.Status != RequisitionStatus.DRAFT)
                .Select(r => new { r.Id, r.AssignedRecruiterId })
                .ToListAsync(cancellationToken);

            var activeLeadsFromRr = await _dbContext.RequisitionRecruiters
                .AsNoTracking()
                .Where(rr => !rr.IsDeleted && rr.IsPrimary)
                .Select(rr => new { rr.RequisitionId, rr.RecruiterId })
                .ToListAsync(cancellationToken);

            var activeSupports = await _dbContext.RequisitionRecruiters
                .AsNoTracking()
                .Where(rr => !rr.IsDeleted && !rr.IsPrimary)
                .Select(rr => rr.RecruiterId)
                .ToListAsync(cancellationToken);

            return recruiters.Select(u =>
            {
                var leadReqIds = activeReqs
                    .Where(r => r.AssignedRecruiterId == u.Id)
                    .Select(r => r.Id)
                    .Union(activeLeadsFromRr.Where(rr => rr.RecruiterId == u.Id).Select(rr => rr.RequisitionId))
                    .Distinct()
                    .ToList();

                return new RecruiterOptionViewModel
                {
                    Id = u.Id,
                    FullName = u.FullName,
                    Email = u.Email,
                    DepartmentName = u.DepartmentEntity?.Name ?? u.Department ?? "Phòng Nhân Sự",
                    JobTitle = u.JobTitle ?? "Chuyên viên tuyển dụng",
                    ActiveLeadCount = leadReqIds.Count,
                    ActiveSupportCount = activeSupports.Count(id => id == u.Id)
                };
            }).ToList();
        }) ?? new List<RecruiterOptionViewModel>();
    }

    public async Task<List<Guid>> GetAssignedRequisitionIdsForRecruiterAsync(
        Guid recruiterUserId,
        CancellationToken cancellationToken = default)
    {
        if (recruiterUserId == Guid.Empty) return new List<Guid>();

        // 1. Requisitions mà user là Recruiter chính
        var primaryIds = await _dbContext.JobRequisitions
            .AsNoTracking()
            .Where(r => !r.IsDeleted && r.AssignedRecruiterId == recruiterUserId)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        // 2. Requisitions mà user là Recruiter hỗ trợ
        var supportingIds = await _dbContext.RequisitionRecruiters
            .AsNoTracking()
            .Where(rr => !rr.IsDeleted && rr.RecruiterId == recruiterUserId)
            .Select(rr => rr.RequisitionId)
            .ToListAsync(cancellationToken);

        return primaryIds.Union(supportingIds).Distinct().ToList();
    }

    public async Task<bool> CanRecruiterAccessApplicationAsync(
        Guid recruiterUserId,
        Guid applicationId,
        CancellationToken cancellationToken = default)
    {
        if (recruiterUserId == Guid.Empty || applicationId == Guid.Empty) return false;

        var assignedReqIds = await GetAssignedRequisitionIdsForRecruiterAsync(recruiterUserId, cancellationToken);
        if (!assignedReqIds.Any()) return false;

        return await _dbContext.Applications
            .AsNoTracking()
            .AnyAsync(a => a.Id == applicationId &&
                           !a.IsDeleted &&
                           assignedReqIds.Contains(a.JobPosting.RequisitionId), cancellationToken);
    }
}
