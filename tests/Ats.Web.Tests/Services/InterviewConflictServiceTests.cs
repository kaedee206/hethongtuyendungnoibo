using Ats.Web.Data;
using Ats.Web.Exceptions;
using Ats.Web.Models.DTOs;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Models.ViewModels.Interviews;
using Ats.Web.Services;
using Ats.Web.Services.Interfaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ats.Web.Tests.Services;

/// <summary>
/// Unit Tests kiểm thử chuyên sâu cho tính năng SCRUM-53: Cảnh báo và Chặn trùng lịch phỏng vấn / Phòng họp.
/// Bao gồm:
/// 1. Công thức phát hiện xung đột thời gian (StartTime &lt; EndTime).
/// 2. Xung đột Người phỏng vấn (Interviewer Conflict).
/// 3. Xung đột Phòng họp (Room/Location Conflict).
/// 4. Bỏ qua các buổi đã hủy (CANCELLED).
/// 5. Bỏ qua chính ID buổi phỏng vấn khi sửa/đổi lịch (ExcludeInterviewId).
/// 6. Cơ chế Hard Block mặc định (ném BusinessRuleValidationException).
/// 7. Cơ chế Override hợp lệ (lý do &gt;= 10 ký tự) và ghi vết AuditLog.
/// </summary>
public class InterviewConflictServiceTests
{
    private static ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"AtsConflictDb_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static async Task<(Application app, User interviewer, User recruiter, RecruitmentCatalog room)> SeedTestDataAsync(ApplicationDbContext context)
    {
        var recruiter = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Nguyễn Chuyên Viên Tuyển Dụng",
            Email = $"recruiter_{Guid.NewGuid():N}@noveratech.vn",
            Role = "RECRUITER",
            Status = "ACTIVE"
        };

        var interviewer = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Trần Kỹ Sư Trưởng",
            Email = $"interviewer_{Guid.NewGuid():N}@noveratech.vn",
            Role = "INTERVIEWER",
            Status = "ACTIVE"
        };

        var candidate = new Candidate
        {
            Id = Guid.NewGuid(),
            FirstName = "Phạm",
            LastName = "Văn Anh",
            Email = $"candidate_{Guid.NewGuid():N}@gmail.com",
            Phone = "0912345678"
        };

        var requisition = new JobRequisition
        {
            Id = Guid.NewGuid(),
            Code = $"REQ_{Guid.NewGuid():N}"[..8],
            HiringManagerId = recruiter.Id
        };

        var jobPosting = new JobPosting
        {
            Id = Guid.NewGuid(),
            Title = "Chuyên Gia Kiến Trúc Backend .NET",
            Slug = $"be-architect-{Guid.NewGuid():N}"[..18],
            RequisitionId = requisition.Id,
            Status = JobPostingStatus.PUBLISHED
        };

        var stage = new PipelineStage
        {
            Id = Guid.NewGuid(),
            Name = "Phỏng vấn chuyên môn",
            StageOrder = 3
        };

        var application = new Application
        {
            Id = Guid.NewGuid(),
            CandidateId = candidate.Id,
            JobPostingId = jobPosting.Id,
            CurrentStageId = stage.Id,
            Status = ApplicationStatus.IN_PROCESS,
            AppliedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };

        var room = new RecruitmentCatalog
        {
            Id = Guid.NewGuid(),
            CatalogType = "LOCATION",
            Code = "ROOM_RUBY",
            Name = "Phòng Họp Ruby - Tầng 8",
            IsActive = true
        };

        context.Users.AddRange(recruiter, interviewer);
        context.Candidates.Add(candidate);
        context.JobRequisitions.Add(requisition);
        context.JobPostings.Add(jobPosting);
        context.PipelineStages.Add(stage);
        context.Applications.Add(application);
        context.RecruitmentCatalogs.Add(room);

        await context.SaveChangesAsync();

        return (application, interviewer, recruiter, room);
    }

    [Fact]
    public async Task CheckScheduleConflict_WhenInterviewerHasOverlappingInterview_ShouldReturnConflict()
    {
        // Arrange: Đã có 1 buổi phỏng vấn từ 14:00 đến 15:00 UTC
        using var context = CreateInMemoryDbContext();
        var (app, interviewer, _, _) = await SeedTestDataAsync(context);
        var mockEmail = new Mock<IEmailService>();
        var service = new InterviewService(context, mockEmail.Object, null, NullLogger<InterviewService>.Instance);

        var baseTime = DateTimeOffset.UtcNow.Date.AddDays(2).AddHours(14); // 14:00 UTC
        var existingInterview = new Interview
        {
            Id = Guid.NewGuid(),
            ApplicationId = app.Id,
            RoundNumber = 1,
            Title = "Phỏng vấn Vòng 1",
            StartTime = baseTime,
            EndTime = baseTime.AddHours(1), // 15:00 UTC
            Status = InterviewStatus.SCHEDULED,
            LocationOrLink = "https://meet.google.com/test-room"
        };
        var panelist = new InterviewPanelist
        {
            Id = Guid.NewGuid(),
            InterviewId = existingInterview.Id,
            InterviewerId = interviewer.Id
        };
        context.Interviews.Add(existingInterview);
        context.InterviewPanelists.Add(panelist);
        await context.SaveChangesAsync();

        // Act: Đề xuất lịch mới từ 14:30 đến 15:30 (Trùng 30 phút từ 14:30 đến 15:00)
        var input = new InterviewConflictCheckInputModel
        {
            StartTime = baseTime.AddMinutes(30),
            EndTime = baseTime.AddMinutes(90),
            InterviewerIds = [interviewer.Id]
        };

        var result = await service.CheckScheduleConflictAsync(input);

        // Assert
        result.Should().NotBeNull();
        result.HasConflict.Should().BeTrue();
        result.Conflicts.Should().HaveCount(1);
        result.Conflicts[0].ConflictType.Should().Be("INTERVIEWER");
        result.Conflicts[0].ConflictingEntityName.Should().Be(interviewer.FullName);
        result.Conflicts[0].ConflictingCandidateName.Should().Contain("Phạm Văn Anh");
        result.Conflicts[0].JobTitle.Should().Be("Chuyên Gia Kiến Trúc Backend .NET");
        result.Message.Should().Contain("người phỏng vấn");
    }

    [Fact]
    public async Task CheckScheduleConflict_WhenInterviewTimesDoNotOverlap_ShouldReturnNoConflict()
    {
        // Arrange: Đã có 1 buổi từ 14:00 đến 15:00 UTC
        using var context = CreateInMemoryDbContext();
        var (app, interviewer, _, _) = await SeedTestDataAsync(context);
        var mockEmail = new Mock<IEmailService>();
        var service = new InterviewService(context, mockEmail.Object, null, NullLogger<InterviewService>.Instance);

        var baseTime = DateTimeOffset.UtcNow.Date.AddDays(2).AddHours(14);
        var existingInterview = new Interview
        {
            Id = Guid.NewGuid(),
            ApplicationId = app.Id,
            RoundNumber = 1,
            Title = "Vòng 1",
            StartTime = baseTime,
            EndTime = baseTime.AddHours(1), // Đến 15:00
            Status = InterviewStatus.SCHEDULED
        };
        context.Interviews.Add(existingInterview);
        context.InterviewPanelists.Add(new InterviewPanelist
        {
            Id = Guid.NewGuid(),
            InterviewId = existingInterview.Id,
            InterviewerId = interviewer.Id
        });
        await context.SaveChangesAsync();

        // Act: Lịch mới bắt đầu từ 15:00 đến 16:00 (Liền kề, không giao nhau)
        var input = new InterviewConflictCheckInputModel
        {
            StartTime = baseTime.AddHours(1),
            EndTime = baseTime.AddHours(2),
            InterviewerIds = [interviewer.Id]
        };

        var result = await service.CheckScheduleConflictAsync(input);

        // Assert
        result.Should().NotBeNull();
        result.HasConflict.Should().BeFalse();
        result.Conflicts.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckScheduleConflict_WhenExistingInterviewIsCancelled_ShouldIgnoreAndReturnNoConflict()
    {
        // Arrange: Buổi phỏng vấn trùng giờ nhưng đã bị CANCELLED
        using var context = CreateInMemoryDbContext();
        var (app, interviewer, _, _) = await SeedTestDataAsync(context);
        var mockEmail = new Mock<IEmailService>();
        var service = new InterviewService(context, mockEmail.Object, null, NullLogger<InterviewService>.Instance);

        var baseTime = DateTimeOffset.UtcNow.Date.AddDays(2).AddHours(10);
        var cancelledInterview = new Interview
        {
            Id = Guid.NewGuid(),
            ApplicationId = app.Id,
            RoundNumber = 1,
            Title = "Vòng 1 đã hủy",
            StartTime = baseTime,
            EndTime = baseTime.AddHours(1),
            Status = InterviewStatus.CANCELLED // ĐÃ HỦY
        };
        context.Interviews.Add(cancelledInterview);
        context.InterviewPanelists.Add(new InterviewPanelist
        {
            Id = Guid.NewGuid(),
            InterviewId = cancelledInterview.Id,
            InterviewerId = interviewer.Id
        });
        await context.SaveChangesAsync();

        // Act: Đặt lịch mới đúng khung giờ đó
        var input = new InterviewConflictCheckInputModel
        {
            StartTime = baseTime,
            EndTime = baseTime.AddHours(1),
            InterviewerIds = [interviewer.Id]
        };

        var result = await service.CheckScheduleConflictAsync(input);

        // Assert
        result.HasConflict.Should().BeFalse();
        result.Conflicts.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckScheduleConflict_WhenExcludingSelfInterviewId_ShouldIgnoreSelfConflict()
    {
        // Arrange: Buổi phỏng vấn hiện tại cần dời giờ một chút trong cùng khung giờ
        using var context = CreateInMemoryDbContext();
        var (app, interviewer, _, _) = await SeedTestDataAsync(context);
        var mockEmail = new Mock<IEmailService>();
        var service = new InterviewService(context, mockEmail.Object, null, NullLogger<InterviewService>.Instance);

        var baseTime = DateTimeOffset.UtcNow.Date.AddDays(2).AddHours(9);
        var currentInterview = new Interview
        {
            Id = Guid.NewGuid(),
            ApplicationId = app.Id,
            RoundNumber = 1,
            Title = "Vòng 1 hiện tại",
            StartTime = baseTime,
            EndTime = baseTime.AddHours(1),
            Status = InterviewStatus.SCHEDULED
        };
        context.Interviews.Add(currentInterview);
        context.InterviewPanelists.Add(new InterviewPanelist
        {
            Id = Guid.NewGuid(),
            InterviewId = currentInterview.Id,
            InterviewerId = interviewer.Id
        });
        await context.SaveChangesAsync();

        // Act: Kiểm tra trùng nhưng loại trừ chính currentInterview.Id
        var input = new InterviewConflictCheckInputModel
        {
            StartTime = baseTime.AddMinutes(15),
            EndTime = baseTime.AddMinutes(75),
            InterviewerIds = [interviewer.Id],
            ExcludeInterviewId = currentInterview.Id
        };

        var result = await service.CheckScheduleConflictAsync(input);

        // Assert
        result.HasConflict.Should().BeFalse();
    }

    [Fact]
    public async Task CheckScheduleConflict_WhenRoomIsBookedInOverlappingTime_ShouldReturnRoomConflict()
    {
        // Arrange: Phòng họp Ruby đã được đặt từ 13:00 đến 14:00
        using var context = CreateInMemoryDbContext();
        var (app, _, _, room) = await SeedTestDataAsync(context);
        var mockEmail = new Mock<IEmailService>();
        var service = new InterviewService(context, mockEmail.Object, null, NullLogger<InterviewService>.Instance);

        var baseTime = DateTimeOffset.UtcNow.Date.AddDays(3).AddHours(13);
        var roomInterview = new Interview
        {
            Id = Guid.NewGuid(),
            ApplicationId = app.Id,
            RoundNumber = 1,
            Title = "Vòng 1 tại phòng Ruby",
            StartTime = baseTime,
            EndTime = baseTime.AddHours(1),
            InterviewType = InterviewType.OFFLINE_OFFICE,
            LocationOrLink = room.Name, // "Phòng Họp Ruby - Tầng 8"
            Status = InterviewStatus.SCHEDULED
        };
        context.Interviews.Add(roomInterview);
        await context.SaveChangesAsync();

        // Act: Đặt buổi phỏng vấn khác dùng cùng phòng Ruby trong khung giờ 13:30 - 14:30
        var input = new InterviewConflictCheckInputModel
        {
            StartTime = baseTime.AddMinutes(30),
            EndTime = baseTime.AddMinutes(90),
            MeetingRoomId = room.Id,
            InterviewerIds = []
        };

        var result = await service.CheckScheduleConflictAsync(input);

        // Assert
        result.HasConflict.Should().BeTrue();
        result.Conflicts.Should().HaveCount(1);
        result.Conflicts[0].ConflictType.Should().Be("ROOM");
        result.Conflicts[0].ConflictingEntityName.Should().Be(room.Name);
    }

    [Fact]
    public async Task ScheduleInterview_WhenConflictOccursAndNotOverridden_ShouldThrowBusinessRuleValidationException()
    {
        // Arrange: Có xung đột lịch phỏng vấn
        using var context = CreateInMemoryDbContext();
        var (app, interviewer, recruiter, _) = await SeedTestDataAsync(context);
        var mockEmail = new Mock<IEmailService>();
        var service = new InterviewService(context, mockEmail.Object, null, NullLogger<InterviewService>.Instance);

        var conflictTime = DateTime.UtcNow.AddDays(2).Date.AddHours(10);
        var existingInterview = new Interview
        {
            Id = Guid.NewGuid(),
            ApplicationId = app.Id,
            RoundNumber = 1,
            Title = "Lịch đã có sẵn",
            StartTime = new DateTimeOffset(conflictTime, TimeSpan.Zero),
            EndTime = new DateTimeOffset(conflictTime.AddHours(1), TimeSpan.Zero),
            Status = InterviewStatus.SCHEDULED
        };
        context.Interviews.Add(existingInterview);
        context.InterviewPanelists.Add(new InterviewPanelist
        {
            Id = Guid.NewGuid(),
            InterviewId = existingInterview.Id,
            InterviewerId = interviewer.Id
        });
        await context.SaveChangesAsync();

        var model = new ScheduleInterviewInputModel
        {
            ApplicationId = app.Id,
            RoundNumber = 2,
            RoundTitle = "Vòng 2",
            StartTime = conflictTime.AddMinutes(15), // Trùng giờ!
            DurationMinutes = 60,
            LocationOrLink = "https://meet.google.com/test",
            InterviewerIds = [interviewer.Id],
            IsOverridden = false // Mặc định không ghi đè
        };

        // Act & Assert
        var act = async () => await service.ScheduleInterviewAsync(model, recruiter.Id, isRecruiterOnly: false);

        await act.Should().ThrowAsync<BusinessRuleValidationException>()
            .WithMessage("*trùng lịch*");

        // Đảm bảo không tạo thêm interview
        var count = await context.Interviews.CountAsync();
        count.Should().Be(1);
    }

    [Fact]
    public async Task ScheduleInterview_WhenConflictOccursWithOverrideAndShortReason_ShouldThrowException()
    {
        // Arrange: Có xung đột lịch, IsOverridden = true nhưng lý do quá ngắn (< 10 ký tự)
        using var context = CreateInMemoryDbContext();
        var (app, interviewer, recruiter, _) = await SeedTestDataAsync(context);
        var mockEmail = new Mock<IEmailService>();
        var service = new InterviewService(context, mockEmail.Object, null, NullLogger<InterviewService>.Instance);

        var conflictTime = DateTime.UtcNow.AddDays(2).Date.AddHours(10);
        var existingInterview = new Interview
        {
            Id = Guid.NewGuid(),
            ApplicationId = app.Id,
            RoundNumber = 1,
            Title = "Lịch đã có",
            StartTime = new DateTimeOffset(conflictTime, TimeSpan.Zero),
            EndTime = new DateTimeOffset(conflictTime.AddHours(1), TimeSpan.Zero),
            Status = InterviewStatus.SCHEDULED
        };
        context.Interviews.Add(existingInterview);
        context.InterviewPanelists.Add(new InterviewPanelist
        {
            Id = Guid.NewGuid(),
            InterviewId = existingInterview.Id,
            InterviewerId = interviewer.Id
        });
        await context.SaveChangesAsync();

        var model = new ScheduleInterviewInputModel
        {
            ApplicationId = app.Id,
            RoundNumber = 2,
            RoundTitle = "Vòng 2",
            StartTime = conflictTime.AddMinutes(15),
            DurationMinutes = 60,
            LocationOrLink = "https://meet.google.com/test",
            InterviewerIds = [interviewer.Id],
            IsOverridden = true,
            OverrideReason = "Gấp" // Chỉ có 3 ký tự (< 10)
        };

        // Act & Assert
        var act = async () => await service.ScheduleInterviewAsync(model, recruiter.Id, isRecruiterOnly: false);

        await act.Should().ThrowAsync<BusinessRuleValidationException>()
            .WithMessage("*tối thiểu 10 ký tự*");
    }

    [Fact]
    public async Task ScheduleInterview_WhenConflictOccursWithValidOverride_ShouldSucceedAndRecordAuditLog()
    {
        // Arrange: Có xung đột lịch, IsOverridden = true và lý do hợp lệ (>= 10 ký tự)
        using var context = CreateInMemoryDbContext();
        var (app, interviewer, recruiter, _) = await SeedTestDataAsync(context);
        var mockEmail = new Mock<IEmailService>();
        mockEmail.Setup(e => e.SendInterviewInvitationAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var service = new InterviewService(context, mockEmail.Object, null, NullLogger<InterviewService>.Instance);

        var conflictTime = DateTime.UtcNow.AddDays(2).Date.AddHours(10);
        var existingInterview = new Interview
        {
            Id = Guid.NewGuid(),
            ApplicationId = app.Id,
            RoundNumber = 1,
            Title = "Lịch vòng 1 đã có",
            StartTime = new DateTimeOffset(conflictTime, TimeSpan.Zero),
            EndTime = new DateTimeOffset(conflictTime.AddHours(1), TimeSpan.Zero),
            Status = InterviewStatus.SCHEDULED
        };
        context.Interviews.Add(existingInterview);
        context.InterviewPanelists.Add(new InterviewPanelist
        {
            Id = Guid.NewGuid(),
            InterviewId = existingInterview.Id,
            InterviewerId = interviewer.Id
        });
        await context.SaveChangesAsync();

        var overrideReasonText = "Ứng viên chỉ rảnh khung giờ này, giám khảo đã đồng ý dời phỏng vấn khác.";
        var model = new ScheduleInterviewInputModel
        {
            ApplicationId = app.Id,
            RoundNumber = 2,
            RoundTitle = "Vòng 2 Chuyên môn",
            StartTime = conflictTime.AddMinutes(15),
            DurationMinutes = 60,
            LocationOrLink = "https://meet.google.com/test",
            InterviewerIds = [interviewer.Id],
            IsOverridden = true,
            OverrideReason = overrideReasonText
        };

        // Act
        var result = await service.ScheduleInterviewAsync(model, recruiter.Id, isRecruiterOnly: false);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.InterviewId.Should().NotBeNull();

        // Kiểm tra AuditLog đã được ghi nhận chi tiết
        var auditLog = await context.AuditLogs.FirstOrDefaultAsync(a => a.Action == "OVERRIDE_INTERVIEW_CONFLICT");
        auditLog.Should().NotBeNull();
        auditLog!.EntityName.Should().Be(nameof(Interview));
        auditLog.NewValues.Should().Contain(overrideReasonText);
        auditLog.NewValues.Should().Contain("IsOverridden");
    }

    [Fact]
    public async Task GetAvailableMeetingRoomsAsync_ShouldSeedAndReturnDefaultRooms_WhenEmpty()
    {
        // Arrange
        await using var context = CreateInMemoryDbContext();
        var emailMock = new Mock<IEmailService>();
        var service = new InterviewService(context, emailMock.Object, assignmentService: null, NullLogger<InterviewService>.Instance);

        // Act
        var rooms = await service.GetAvailableMeetingRoomsAsync();

        // Assert
        rooms.Should().NotBeNull();
        rooms.Count.Should().Be(5);
        rooms.Should().Contain(r => r.Code == "ROOM_RUBY" && r.Name.Contains("Ruby"));
        rooms.Should().Contain(r => r.Code == "ROOM_DIAMOND" && r.Name.Contains("Diamond"));

        // Verify persisted to DB
        var dbCount = await context.RecruitmentCatalogs.CountAsync(c => c.CatalogType == "MEETING_ROOM");
        dbCount.Should().Be(5);
    }
}
