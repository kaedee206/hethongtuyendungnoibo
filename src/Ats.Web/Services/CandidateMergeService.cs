using System.Text.Json;
using System.Text.RegularExpressions;
using Ats.Web.Data;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Models.ViewModels.CandidateMerge;
using Ats.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Ats.Web.Services;

/// <summary>
/// Cài đặt dịch vụ phát hiện và gộp hồ sơ ứng viên trùng lặp (Scrum #40)
/// Đảm bảo tính toàn vẹn dữ liệu, bảo toàn 100% lịch sử ứng tuyển và hỗ trợ hoàn tác trong 24 giờ
/// </summary>
public class CandidateMergeService : ICandidateMergeService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<CandidateMergeService> _logger;

    public CandidateMergeService(
        ApplicationDbContext dbContext,
        ILogger<CandidateMergeService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Chuẩn hóa số điện thoại: bỏ ký tự phi số, chuyển đầu 84 thành 0
    /// </summary>
    public string NormalizePhoneNumber(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return string.Empty;
        var digits = Regex.Replace(phone.Trim(), @"\D", "");
        if (digits.StartsWith("84") && digits.Length >= 11)
        {
            digits = "0" + digits[2..];
        }
        return digits;
    }

    /// <summary>
    /// Quét phát hiện các nhóm ứng viên trùng lặp theo Email hoặc Số điện thoại
    /// </summary>
    public async Task<List<DuplicateCandidateGroupDto>> DetectDuplicatesAsync()
    {
        // Lấy tất cả ứng viên đang hoạt động (chưa bị xóa mềm và chưa bị gộp)
        var candidates = await _dbContext.Candidates
            .AsNoTracking()
            .Include(c => c.Resumes)
            .Include(c => c.Applications)
                .ThenInclude(a => a.JobPosting)
            .Include(c => c.Applications)
                .ThenInclude(a => a.CurrentStage)
            .Where(c => !c.IsDeleted && !c.IsMerged)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();

        var resultGroups = new List<DuplicateCandidateGroupDto>();
        var processedPairs = new HashSet<string>();

        // 1. Phân nhóm theo Email
        var emailGroups = candidates
            .Where(c => !string.IsNullOrWhiteSpace(c.Email))
            .GroupBy(c => c.Email.Trim().ToLowerInvariant())
            .Where(g => g.Count() > 1)
            .ToList();

        // 2. Phân nhóm theo Số điện thoại chuẩn hóa
        var phoneGroups = candidates
            .Select(c => new { Candidate = c, NormPhone = NormalizePhoneNumber(c.Phone) })
            .Where(x => !string.IsNullOrWhiteSpace(x.NormPhone) && x.NormPhone.Length >= 9)
            .GroupBy(x => x.NormPhone)
            .Where(g => g.Count() > 1)
            .ToList();

        // Xử lý các cặp trùng theo cả Email và Phone hoặc từng tiêu chí
        foreach (var eg in emailGroups)
        {
            var list = eg.ToList();
            for (int i = 0; i < list.Count; i++)
            {
                for (int j = i + 1; j < list.Count; j++)
                {
                    var c1 = list[i];
                    var c2 = list[j];
                    var pairKey = GetPairKey(c1.Id, c2.Id);
                    if (processedPairs.Contains(pairKey)) continue;
                    processedPairs.Add(pairKey);

                    var p1 = NormalizePhoneNumber(c1.Phone);
                    var p2 = NormalizePhoneNumber(c2.Phone);
                    bool bothMatch = !string.IsNullOrEmpty(p1) && p1 == p2;

                    var group = new DuplicateCandidateGroupDto
                    {
                        GroupKey = pairKey,
                        DuplicateType = bothMatch ? DuplicateType.BothEmailAndPhone : DuplicateType.EmailOnly,
                        DuplicateReason = bothMatch 
                            ? $"Trùng cả Email ({c1.Email}) và Số điện thoại ({c1.Phone})"
                            : $"Trùng Email ({c1.Email})",
                        MatchedValue = eg.Key,
                        ConfidenceScore = bothMatch ? 100 : 85,
                        Candidates = new List<DuplicateCandidateItemDto>
                        {
                            MapToItemDto(c1),
                            MapToItemDto(c2)
                        }
                    };
                    resultGroups.Add(group);
                }
            }
        }

        foreach (var pg in phoneGroups)
        {
            var list = pg.Select(x => x.Candidate).ToList();
            for (int i = 0; i < list.Count; i++)
            {
                for (int j = i + 1; j < list.Count; j++)
                {
                    var c1 = list[i];
                    var c2 = list[j];
                    var pairKey = GetPairKey(c1.Id, c2.Id);
                    if (processedPairs.Contains(pairKey)) continue; // Đã thêm ở nhánh email
                    processedPairs.Add(pairKey);

                    var group = new DuplicateCandidateGroupDto
                    {
                        GroupKey = pairKey,
                        DuplicateType = DuplicateType.PhoneOnly,
                        DuplicateReason = $"Trùng Số điện thoại ({c1.Phone ?? pg.Key})",
                        MatchedValue = pg.Key,
                        ConfidenceScore = 90,
                        Candidates = new List<DuplicateCandidateItemDto>
                        {
                            MapToItemDto(c1),
                            MapToItemDto(c2)
                        }
                    };
                    resultGroups.Add(group);
                }
            }
        }

        return resultGroups.OrderByDescending(g => g.ConfidenceScore).ToList();
    }

    /// <summary>
    /// Lấy tổng quan các chỉ số trùng lặp và lịch sử cho dashboard quản trị
    /// </summary>
    public async Task<CandidateDuplicateOverviewViewModel> GetDuplicateOverviewAsync()
    {
        var duplicateGroups = await DetectDuplicatesAsync();
        var mergeHistories = await GetMergeHistoryAsync();

        var model = new CandidateDuplicateOverviewViewModel
        {
            TotalDuplicateGroups = duplicateGroups.Count,
            PhoneDuplicateCount = duplicateGroups.Count(g => g.DuplicateType == DuplicateType.PhoneOnly || g.DuplicateType == DuplicateType.BothEmailAndPhone),
            EmailDuplicateCount = duplicateGroups.Count(g => g.DuplicateType == DuplicateType.EmailOnly || g.DuplicateType == DuplicateType.BothEmailAndPhone),
            MergedCount = mergeHistories.Count(h => !h.IsUndone),
            CanUndoCount = mergeHistories.Count(h => h.CanUndo),
            DuplicateGroups = duplicateGroups,
            MergeHistories = mergeHistories
        };

        return model;
    }

    /// <summary>
    /// Tìm danh sách ứng viên có khả năng trùng với một ứng viên cụ thể
    /// </summary>
    public async Task<List<DuplicateCandidateItemDto>> FindDuplicatesForCandidateAsync(Guid candidateId)
    {
        var target = await _dbContext.Candidates
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == candidateId && !c.IsDeleted && !c.IsMerged);

        if (target == null) return new List<DuplicateCandidateItemDto>();

        var targetEmail = (target.Email ?? "").Trim().ToLowerInvariant();
        var targetPhone = NormalizePhoneNumber(target.Phone);

        var query = _dbContext.Candidates
            .AsNoTracking()
            .Include(c => c.Resumes)
            .Include(c => c.Applications)
                .ThenInclude(a => a.JobPosting)
            .Include(c => c.Applications)
                .ThenInclude(a => a.CurrentStage)
            .Where(c => c.Id != candidateId && !c.IsDeleted && !c.IsMerged);

        var matches = new List<Candidate>();
        var candidatesList = await query.ToListAsync();

        foreach (var c in candidatesList)
        {
            bool emailMatch = !string.IsNullOrEmpty(targetEmail) && 
                              (c.Email ?? "").Trim().ToLowerInvariant() == targetEmail;

            bool phoneMatch = !string.IsNullOrEmpty(targetPhone) && 
                              NormalizePhoneNumber(c.Phone) == targetPhone;

            if (emailMatch || phoneMatch)
            {
                matches.Add(c);
            }
        }

        return matches.Select(MapToItemDto).ToList();
    }

    /// <summary>
    /// Lấy dữ liệu so sánh cạnh nhau (Side-by-side) chi tiết giữa 2 hồ sơ
    /// </summary>
    public async Task<CandidateComparisonViewModel?> GetComparisonAsync(Guid primaryId, Guid secondaryId)
    {
        if (primaryId == Guid.Empty || secondaryId == Guid.Empty || primaryId == secondaryId)
        {
            return null;
        }

        var c1 = await LoadCandidateFullDetailsAsync(primaryId);
        var c2 = await LoadCandidateFullDetailsAsync(secondaryId);

        if (c1 == null || c2 == null)
        {
            return null;
        }

        var vm = new CandidateComparisonViewModel
        {
            PrimaryCandidate = c1,
            SecondaryCandidate = c2
        };

        // Xác định lý do trùng
        var p1 = NormalizePhoneNumber(c1.Phone);
        var p2 = NormalizePhoneNumber(c2.Phone);
        bool matchPhone = !string.IsNullOrEmpty(p1) && p1 == p2;
        bool matchEmail = (c1.Email ?? "").Trim().ToLowerInvariant() == (c2.Email ?? "").Trim().ToLowerInvariant();

        if (matchPhone && matchEmail)
            vm.DuplicateReason = $"Trùng cả Email ({c1.Email}) và Số điện thoại ({c1.Phone})";
        else if (matchPhone)
            vm.DuplicateReason = $"Trùng Số điện thoại ({c1.Phone ?? c2.Phone})";
        else if (matchEmail)
            vm.DuplicateReason = $"Trùng Email ({c1.Email})";
        else
            vm.DuplicateReason = "Nghi vấn trùng lặp danh tính ứng viên";

        // Kiểm tra xung đột đợt ứng tuyển cùng vị trí
        var c1Jobs = c1.Applications.Select(a => a.JobTitle).ToHashSet();
        var c2Jobs = c2.Applications.Select(a => a.JobTitle).ToHashSet();
        var commonJobs = c1Jobs.Intersect(c2Jobs).ToList();

        if (commonJobs.Any())
        {
            vm.HasConflictingActiveApplications = true;
            vm.ConflictWarningMessage = $"Hai hồ sơ này đều từng ứng tuyển vào vị trí: {string.Join(", ", commonJobs)}. Khi gộp, toàn bộ lịch sử các vòng phỏng vấn của cả hai đợt ứng tuyển sẽ được liên kết và hiển thị đầy đủ trong hồ sơ chính.";
        }

        return vm;
    }

    /// <summary>
    /// Thực hiện gộp hồ sơ: chuyển toàn bộ Applications và Resumes từ Secondary sang Primary
    /// </summary>
    public async Task<(bool Success, string Message, Guid? MergeHistoryId)> MergeCandidatesAsync(
        MergeCandidatesRequestModel model, Guid currentUserId)
    {
        if (model.PrimaryCandidateId == Guid.Empty || model.SecondaryCandidateId == Guid.Empty)
        {
            return (false, "Vui lòng chọn đầy đủ hồ sơ chính và hồ sơ phụ cần gộp.", null);
        }

        if (model.PrimaryCandidateId == model.SecondaryCandidateId)
        {
            return (false, "Không thể gộp một hồ sơ với chính nó.", null);
        }

        var primary = await _dbContext.Candidates
            .Include(c => c.Applications)
            .Include(c => c.Resumes)
            .FirstOrDefaultAsync(c => c.Id == model.PrimaryCandidateId && !c.IsDeleted);

        var secondary = await _dbContext.Candidates
            .Include(c => c.Applications)
            .Include(c => c.Resumes)
            .FirstOrDefaultAsync(c => c.Id == model.SecondaryCandidateId && !c.IsDeleted);

        if (primary == null)
        {
            return (false, "Hồ sơ chính không tồn tại trong hệ thống.", null);
        }

        if (secondary == null)
        {
            return (false, "Hồ sơ phụ cần gộp không tồn tại trong hệ thống.", null);
        }

        if (secondary.IsMerged)
        {
            return (false, "Hồ sơ phụ này đã được gộp vào một hồ sơ khác trước đó.", null);
        }

        using var transaction = await _dbContext.Database.BeginTransactionAsync();

        try
        {
            // 1. Sao lưu snapshot thông tin Secondary trước khi gộp
            var snapshotSecondary = new
            {
                secondary.Id,
                secondary.FirstName,
                secondary.LastName,
                secondary.Email,
                secondary.Phone,
                secondary.CurrentCompany,
                secondary.CurrentTitle,
                secondary.Address,
                secondary.LinkedinUrl,
                secondary.DateOfBirth,
                secondary.Source
            };

            // 2. Chuyển toàn bộ Applications của Secondary sang Primary
            var movedAppIds = new List<Guid>();
            foreach (var app in secondary.Applications)
            {
                app.CandidateId = primary.Id;
                app.UpdatedAt = DateTimeOffset.UtcNow;
                app.UpdatedById = currentUserId;
                movedAppIds.Add(app.Id);
            }

            // 3. Chuyển toàn bộ Resumes của Secondary sang Primary
            var movedResumeIds = new List<Guid>();
            foreach (var res in secondary.Resumes)
            {
                res.CandidateId = primary.Id;
                res.IsPrimary = false; // Giữ CV chính của hồ sơ Primary
                res.UpdatedAt = DateTimeOffset.UtcNow;
                res.UpdatedById = currentUserId;
                movedResumeIds.Add(res.Id);
            }

            // 4. Cập nhật thông tin ưu tiên giữ lại cho Primary
            if (model.KeepPhone == "secondary" && !string.IsNullOrWhiteSpace(secondary.Phone))
            {
                primary.Phone = secondary.Phone;
            }
            else if (string.IsNullOrWhiteSpace(primary.Phone) && !string.IsNullOrWhiteSpace(secondary.Phone))
            {
                primary.Phone = secondary.Phone;
            }

            if (model.KeepName == "secondary" && !string.IsNullOrWhiteSpace(secondary.FirstName))
            {
                primary.FirstName = secondary.FirstName;
                primary.LastName = secondary.LastName;
            }

            if (string.IsNullOrWhiteSpace(primary.LinkedinUrl) && !string.IsNullOrWhiteSpace(secondary.LinkedinUrl))
            {
                primary.LinkedinUrl = secondary.LinkedinUrl;
            }

            if (string.IsNullOrWhiteSpace(primary.Address) && !string.IsNullOrWhiteSpace(secondary.Address))
            {
                primary.Address = secondary.Address;
            }

            if (string.IsNullOrWhiteSpace(primary.CurrentCompany) && !string.IsNullOrWhiteSpace(secondary.CurrentCompany))
            {
                primary.CurrentCompany = secondary.CurrentCompany;
            }

            primary.UpdatedAt = DateTimeOffset.UtcNow;
            primary.UpdatedById = currentUserId;

            // 5. Đánh dấu Secondary là đã bị gộp
            secondary.IsMerged = true;
            secondary.MergedIntoCandidateId = primary.Id;
            secondary.MergedAt = DateTimeOffset.UtcNow;
            secondary.UpdatedAt = DateTimeOffset.UtcNow;
            secondary.UpdatedById = currentUserId;

            // 6. Tạo bản ghi lịch sử gộp hồ sơ
            var mergeHistory = new CandidateMergeHistory
            {
                Id = Guid.NewGuid(),
                PrimaryCandidateId = primary.Id,
                SecondaryCandidateId = secondary.Id,
                MergedByUserId = currentUserId,
                MergedAt = DateTimeOffset.UtcNow,
                MergeReason = !string.IsNullOrWhiteSpace(model.Reason) 
                    ? model.Reason.Trim() 
                    : $"Gộp hồ sơ trùng theo yêu cầu của nhân sự (Đã chuyển {movedAppIds.Count} đơn ứng tuyển, {movedResumeIds.Count} CV)",
                MovedApplicationIdsJson = JsonSerializer.Serialize(movedAppIds),
                MovedResumeIdsJson = JsonSerializer.Serialize(movedResumeIds),
                SnapshotSecondaryDataJson = JsonSerializer.Serialize(snapshotSecondary),
                IsUndone = false,
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedById = currentUserId,
                UpdatedAt = DateTimeOffset.UtcNow,
                UpdatedById = currentUserId
            };

            await _dbContext.CandidateMergeHistories.AddAsync(mergeHistory);

            // 7. Ghi Audit Log hệ thống
            var auditLog = new AuditLog
            {
                Id = Guid.NewGuid(),
                UserId = currentUserId,
                Action = "MERGE_CANDIDATES",
                EntityName = "Candidate",
                EntityId = primary.Id.ToString(),
                NewValues = JsonSerializer.Serialize(new
                {
                    PrimaryCandidateId = primary.Id,
                    SecondaryCandidateId = secondary.Id,
                    MovedApplicationsCount = movedAppIds.Count,
                    MovedResumesCount = movedResumeIds.Count,
                    MergeHistoryId = mergeHistory.Id
                }),
                CreatedAt = DateTimeOffset.UtcNow
            };
            await _dbContext.AuditLogs.AddAsync(auditLog);

            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation("Người dùng {UserId} đã gộp thành công hồ sơ ứng viên {SecondaryId} vào {PrimaryId}. Chuyển {AppCount} Applications, {ResumeCount} Resumes",
                currentUserId, secondary.Id, primary.Id, movedAppIds.Count, movedResumeIds.Count);

            return (true, $"Gộp hồ sơ thành công! Đã chuyển toàn bộ {movedAppIds.Count} đợt ứng tuyển và {movedResumeIds.Count} CV sang hồ sơ chính. Bạn có thể hoàn tác trong vòng 24 giờ.", mergeHistory.Id);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Lỗi xảy ra trong quá trình gộp hồ sơ ứng viên");
            return (false, "Đã có lỗi xảy ra trong quá trình gộp hồ sơ. Vui lòng thử lại sau.", null);
        }
    }

    /// <summary>
    /// Hoàn tác phiên gộp hồ sơ trong vòng 24 giờ
    /// </summary>
    public async Task<(bool Success, string Message)> UndoMergeAsync(Guid mergeHistoryId, Guid currentUserId)
    {
        if (mergeHistoryId == Guid.Empty)
        {
            return (false, "Mã phiên gộp không hợp lệ.");
        }

        var history = await _dbContext.CandidateMergeHistories
            .Include(h => h.PrimaryCandidate)
            .Include(h => h.SecondaryCandidate)
            .FirstOrDefaultAsync(h => h.Id == mergeHistoryId && !h.IsDeleted);

        if (history == null)
        {
            return (false, "Không tìm thấy thông tin phiên gộp hồ sơ.");
        }

        if (history.IsUndone)
        {
            return (false, "Phiên gộp hồ sơ này đã được hoàn tác trước đó.");
        }

        // Kiểm tra điều kiện giới hạn 24 giờ
        var elapsed = DateTimeOffset.UtcNow - history.MergedAt;
        if (elapsed > TimeSpan.FromHours(24))
        {
            return (false, "Đã quá thời hạn 24 giờ. Theo chính sách an toàn dữ liệu, phiên gộp này không thể hoàn tác.");
        }

        using var transaction = await _dbContext.Database.BeginTransactionAsync();

        try
        {
            // 1. Phục hồi các Application về cho SecondaryCandidate
            var movedAppIds = JsonSerializer.Deserialize<List<Guid>>(history.MovedApplicationIdsJson) ?? new List<Guid>();
            if (movedAppIds.Any())
            {
                var apps = await _dbContext.Applications
                    .Where(a => movedAppIds.Contains(a.Id))
                    .ToListAsync();

                foreach (var app in apps)
                {
                    app.CandidateId = history.SecondaryCandidateId;
                    app.UpdatedAt = DateTimeOffset.UtcNow;
                    app.UpdatedById = currentUserId;
                }
            }

            // 2. Phục hồi các Resume về cho SecondaryCandidate
            var movedResumeIds = JsonSerializer.Deserialize<List<Guid>>(history.MovedResumeIdsJson) ?? new List<Guid>();
            if (movedResumeIds.Any())
            {
                var resumes = await _dbContext.Resumes
                    .Where(r => movedResumeIds.Contains(r.Id))
                    .ToListAsync();

                foreach (var res in resumes)
                {
                    res.CandidateId = history.SecondaryCandidateId;
                    res.UpdatedAt = DateTimeOffset.UtcNow;
                    res.UpdatedById = currentUserId;
                }
            }

            // 3. Mở lại hồ sơ SecondaryCandidate
            var secondary = await _dbContext.Candidates
                .FirstOrDefaultAsync(c => c.Id == history.SecondaryCandidateId);

            if (secondary != null)
            {
                secondary.IsMerged = false;
                secondary.MergedIntoCandidateId = null;
                secondary.MergedAt = null;
                secondary.UpdatedAt = DateTimeOffset.UtcNow;
                secondary.UpdatedById = currentUserId;
            }

            // 4. Cập nhật bản ghi MergeHistory
            history.IsUndone = true;
            history.UndoneAt = DateTimeOffset.UtcNow;
            history.UndoneByUserId = currentUserId;
            history.UpdatedAt = DateTimeOffset.UtcNow;
            history.UpdatedById = currentUserId;
            history.Notes = (history.Notes ?? "") + $" [Đã hoàn tác bởi {currentUserId} lúc {DateTimeOffset.UtcNow:dd/MM/yyyy HH:mm}]";

            // 5. Ghi Audit Log hệ thống
            var auditLog = new AuditLog
            {
                Id = Guid.NewGuid(),
                UserId = currentUserId,
                Action = "UNDO_MERGE_CANDIDATES",
                EntityName = "CandidateMergeHistory",
                EntityId = history.Id.ToString(),
                NewValues = JsonSerializer.Serialize(new
                {
                    history.Id,
                    history.PrimaryCandidateId,
                    history.SecondaryCandidateId,
                    RestoredApplicationsCount = movedAppIds.Count,
                    RestoredResumesCount = movedResumeIds.Count
                }),
                CreatedAt = DateTimeOffset.UtcNow
            };
            await _dbContext.AuditLogs.AddAsync(auditLog);

            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation("Người dùng {UserId} đã hoàn tác phiên gộp {HistoryId} thành công.", currentUserId, history.Id);

            return (true, $"Hoàn tác gộp hồ sơ thành công! Đã khôi phục {movedAppIds.Count} đợt ứng tuyển và {movedResumeIds.Count} CV về hồ sơ ban đầu.");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Lỗi xảy ra trong quá trình hoàn tác gộp hồ sơ");
            return (false, "Đã có lỗi xảy ra trong quá trình hoàn tác. Vui lòng thử lại sau.");
        }
    }

    /// <summary>
    /// Lấy danh sách lịch sử các phiên gộp hồ sơ
    /// </summary>
    public async Task<List<CandidateMergeHistoryDto>> GetMergeHistoryAsync()
    {
        var histories = await _dbContext.CandidateMergeHistories
            .AsNoTracking()
            .Include(h => h.PrimaryCandidate)
            .Include(h => h.SecondaryCandidate)
            .Include(h => h.MergedByUser)
            .Include(h => h.UndoneByUser)
            .OrderByDescending(h => h.MergedAt)
            .ToListAsync();

        var result = new List<CandidateMergeHistoryDto>();

        foreach (var h in histories)
        {
            var movedAppIds = JsonSerializer.Deserialize<List<Guid>>(h.MovedApplicationIdsJson) ?? new List<Guid>();
            var movedResumeIds = JsonSerializer.Deserialize<List<Guid>>(h.MovedResumeIdsJson) ?? new List<Guid>();

            var dto = new CandidateMergeHistoryDto
            {
                HistoryId = h.Id,
                PrimaryCandidateId = h.PrimaryCandidateId,
                PrimaryCandidateName = $"{h.PrimaryCandidate?.FirstName} {h.PrimaryCandidate?.LastName}".Trim(),
                PrimaryCandidateEmail = h.PrimaryCandidate?.Email ?? "",
                PrimaryCandidatePhone = h.PrimaryCandidate?.Phone,

                SecondaryCandidateId = h.SecondaryCandidateId,
                SecondaryCandidateName = $"{h.SecondaryCandidate?.FirstName} {h.SecondaryCandidate?.LastName}".Trim(),
                SecondaryCandidateEmail = h.SecondaryCandidate?.Email ?? "",
                SecondaryCandidatePhone = h.SecondaryCandidate?.Phone,

                MergedByUserId = h.MergedByUserId,
                MergedByUserName = h.MergedByUser?.FullName ?? "Nhân viên tuyển dụng",
                MergedAt = h.MergedAt,
                MergedAtDisplay = h.MergedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
                MergeReason = h.MergeReason,

                MovedApplicationsCount = movedAppIds.Count,
                MovedResumesCount = movedResumeIds.Count,

                IsUndone = h.IsUndone,
                UndoneAt = h.UndoneAt,
                UndoneAtDisplay = h.UndoneAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
                UndoneByUserName = h.UndoneByUser?.FullName
            };

            result.Add(dto);
        }

        return result;
    }

    #region Private Helpers

    private static string GetPairKey(Guid id1, Guid id2)
    {
        return id1.CompareTo(id2) < 0 ? $"{id1:N}_{id2:N}" : $"{id2:N}_{id1:N}";
    }

    private DuplicateCandidateItemDto MapToItemDto(Candidate c)
    {
        var name = $"{c.FirstName} {c.LastName}".Trim();
        if (string.IsNullOrWhiteSpace(name)) name = "Ứng viên";

        var appliedJobs = c.Applications
            .Where(a => !a.IsDeleted && a.JobPosting != null)
            .Select(a => a.JobPosting.Title)
            .Distinct()
            .ToList();

        var latestStage = c.Applications
            .Where(a => !a.IsDeleted && a.CurrentStage != null)
            .OrderByDescending(a => a.AppliedAt ?? a.CreatedAt)
            .Select(a => a.CurrentStage.Name)
            .FirstOrDefault() ?? "Mới ứng tuyển";

        return new DuplicateCandidateItemDto
        {
            CandidateId = c.Id,
            FullName = name,
            Email = c.Email,
            Phone = c.Phone,
            NormalizedPhone = NormalizePhoneNumber(c.Phone),
            CurrentCompany = c.CurrentCompany,
            CurrentTitle = c.CurrentTitle,
            CreatedAt = c.CreatedAt,
            CreatedDateDisplay = c.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy"),
            SourceName = c.Source == CandidateSource.REFERRAL ? "Giới thiệu nội bộ" : "Cổng tuyển dụng",
            ApplicationsCount = c.Applications.Count(a => !a.IsDeleted),
            ResumesCount = c.Resumes.Count(r => !r.IsDeleted),
            AppliedJobTitles = appliedJobs,
            LatestStageName = latestStage
        };
    }

    private async Task<CandidateDetailDto?> LoadCandidateFullDetailsAsync(Guid candidateId)
    {
        var cand = await _dbContext.Candidates
            .AsNoTracking()
            .Include(c => c.Resumes)
            .Include(c => c.Applications)
                .ThenInclude(a => a.JobPosting)
                    .ThenInclude(j => j.Requisition)
                        .ThenInclude(r => r.Department)
            .Include(c => c.Applications)
                .ThenInclude(a => a.CurrentStage)
            .Include(c => c.Applications)
                .ThenInclude(a => a.Interviews)
                    .ThenInclude(i => i.Panelists)
                        .ThenInclude(p => p.Interviewer)
            .FirstOrDefaultAsync(c => c.Id == candidateId && !c.IsDeleted);

        if (cand == null) return null;

        var name = $"{cand.FirstName} {cand.LastName}".Trim();
        if (string.IsNullOrWhiteSpace(name)) name = "Ứng viên";

        var resumes = cand.Resumes
            .Where(r => !r.IsDeleted)
            .OrderByDescending(r => r.IsPrimary)
            .ThenByDescending(r => r.CreatedAt)
            .Select(r => new CandidateResumeItemDto
            {
                ResumeId = r.Id,
                FileName = r.FileName,
                FilePath = r.FilePath,
                FileSize = r.FileSize,
                IsPrimary = r.IsPrimary,
                CreatedAtDisplay = r.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy")
            })
            .ToList();

        var applications = cand.Applications
            .Where(a => !a.IsDeleted)
            .OrderByDescending(a => a.AppliedAt ?? a.CreatedAt)
            .Select(a => new CandidateApplicationItemDto
            {
                ApplicationId = a.Id,
                JobTitle = a.JobPosting?.Title ?? "Vị trí tuyển dụng",
                DepartmentName = a.JobPosting?.Requisition?.Department?.Name ?? "Khối Công nghệ & Sản phẩm",
                TrackingCode = a.TrackingCode ?? "",
                StageName = a.CurrentStage?.Name ?? "Tiếp nhận hồ sơ",
                StageOrder = a.CurrentStage?.StageOrder ?? 1,
                StatusDisplay = a.Status == ApplicationStatus.HIRED ? "Đã tuyển" :
                                a.Status == ApplicationStatus.REJECTED ? "Đã từ chối" : "Đang xử lý",
                StatusBadgeClass = a.Status == ApplicationStatus.HIRED ? "bg-success-subtle text-success" :
                                   a.Status == ApplicationStatus.REJECTED ? "bg-danger-subtle text-danger" : "bg-warning-subtle text-warning-emphasis",
                AppliedAtDisplay = (a.AppliedAt ?? a.CreatedAt).ToLocalTime().ToString("dd/MM/yyyy"),
                Interviews = a.Interviews
                    .Where(i => !i.IsDeleted)
                    .OrderBy(i => i.RoundNumber)
                    .Select(i => new CandidateInterviewItemDto
                    {
                        InterviewId = i.Id,
                        Title = i.Title,
                        RoundTitle = $"Vòng {i.RoundNumber}",
                        StartTimeDisplay = i.StartTime?.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? "Chưa xếp lịch",
                        StatusDisplay = i.Status == InterviewStatus.COMPLETED ? "Đã phỏng vấn" :
                                       i.Status == InterviewStatus.CANCELLED ? "Đã hủy" : "Đã lên lịch",
                        PanelistNames = string.Join(", ", i.Panelists.Where(p => p.Interviewer != null).Select(p => p.Interviewer.FullName))
                    })
                    .ToList()
            })
            .ToList();

        return new CandidateDetailDto
        {
            CandidateId = cand.Id,
            FullName = name,
            FirstName = cand.FirstName,
            LastName = cand.LastName,
            Email = cand.Email,
            Phone = cand.Phone,
            DateOfBirth = cand.DateOfBirth,
            Address = cand.Address,
            CurrentCompany = cand.CurrentCompany,
            CurrentTitle = cand.CurrentTitle,
            LinkedinUrl = cand.LinkedinUrl,
            SourceName = cand.Source == CandidateSource.REFERRAL ? "Giới thiệu nội bộ" : "Cổng tuyển dụng",
            CreatedAt = cand.CreatedAt,
            CreatedAtDisplay = cand.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy"),
            Resumes = resumes,
            Applications = applications
        };
    }

    #endregion
}
