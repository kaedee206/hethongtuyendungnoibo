using Ats.Web.Constants;
using Ats.Web.Data;
using Ats.Web.Models.DTOs;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
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
/// Unit Tests kiểm thử toàn diện nghiệp vụ Đặt lịch phỏng vấn ứng viên (Scrum - Số 51 / EP-06).
/// Tiêu chí nghiệm thu:
/// 1. Chọn vòng phỏng vấn, ngày giờ, thời lượng, hình thức (trực tiếp hoặc trực tuyến), phòng hoặc liên kết họp.
/// 2. Chọn một hoặc nhiều người phỏng vấn (Hội đồng phỏng vấn).
/// 3. Không cho phép đặt lịch ở quá khứ (Validation chặt chẽ cả server-side và client-side).
/// 4. Lịch được tạo hiển thị ngay trên hồ sơ ứng viên và lịch của người phỏng vấn.
/// </summary>
public class InterviewSchedulingServiceTests
{
    private static ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"AtsInterviewSchedulingDb_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static async Task<(Application app, User interviewer1, User interviewer2, User inactiveUser, User recruiter)> SeedTestDataAsync(ApplicationDbContext context)
    {
        var recruiter = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Lê Chuyên Viên Tuyển Dụng",
            Email = $"recruiter_{Guid.NewGuid():N}@noveratech.vn",
            PasswordHash = "hash",
            Status = "ACTIVE",
            Role = UserRoles.Recruiter,
            UserType = UserType.INTERNAL
        };

        var interviewer1 = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Nguyễn Văn Giám Khảo 1",
            Email = $"panelist1_{Guid.NewGuid():N}@noveratech.vn",
            PasswordHash = "hash",
            Status = "ACTIVE",
            Role = UserRoles.Interviewer,
            UserType = UserType.INTERNAL
        };

        var interviewer2 = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Trần Thị Giám Khảo 2",
            Email = $"panelist2_{Guid.NewGuid():N}@noveratech.vn",
            PasswordHash = "hash",
            Status = "ACTIVE",
            Role = UserRoles.Interviewer,
            UserType = UserType.INTERNAL
        };

        var inactiveUser = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Đỗ Nhân Sự Đã Nghỉ",
            Email = $"inactive_{Guid.NewGuid():N}@noveratech.vn",
            PasswordHash = "hash",
            Status = "INACTIVE",
            Role = UserRoles.Interviewer,
            UserType = UserType.INTERNAL
        };

        var candidate = new Candidate
        {
            Id = Guid.NewGuid(),
            FirstName = "Hoàng",
            LastName = "Văn Nam",
            Email = $"candidate_{Guid.NewGuid():N}@gmail.com",
            Phone = "0987654321",
            Source = CandidateSource.LINKEDIN
        };

        var department = new Department
        {
            Id = Guid.NewGuid(),
            Name = "Trung tâm Công nghệ Thông tin",
            Code = $"IT_{Guid.NewGuid():N}"[..8]
        };

        var stage = new PipelineStage
        {
            Id = Guid.NewGuid(),
            Name = "Phỏng vấn chuyên môn",
            StageOrder = 3
        };

        var requisition = new JobRequisition
        {
            Id = Guid.NewGuid(),
            Code = $"REQ_{Guid.NewGuid():N}"[..8],
            DepartmentId = department.Id,
            HiringManagerId = recruiter.Id
        };

        var jobPosting = new JobPosting
        {
            Id = Guid.NewGuid(),
            Title = "Senior .NET Core Backend Engineer",
            Slug = $"senior-net-core-{Guid.NewGuid():N}"[..20],
            RequisitionId = requisition.Id,
            Status = JobPostingStatus.PUBLISHED
        };

        var application = new Application
        {
            Id = Guid.NewGuid(),
            CandidateId = candidate.Id,
            JobPostingId = jobPosting.Id,
            CurrentStageId = stage.Id,
            Status = ApplicationStatus.IN_PROCESS,
            AppliedAt = DateTimeOffset.UtcNow.AddDays(-2)
        };

        context.Users.AddRange(recruiter, interviewer1, interviewer2, inactiveUser);
        context.Candidates.Add(candidate);
        context.Departments.Add(department);
        context.PipelineStages.Add(stage);
        context.JobRequisitions.Add(requisition);
        context.JobPostings.Add(jobPosting);
        context.Applications.Add(application);

        await context.SaveChangesAsync();

        return (application, interviewer1, interviewer2, inactiveUser, recruiter);
    }

    [Fact]
    public async Task ScheduleInterview_WithPastDateTime_ShouldFailAndReturnFriendlyErrorMessage()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (app, iv1, _, _, recruiter) = await SeedTestDataAsync(context);
        var mockEmail = new Mock<IEmailService>();
        var service = new InterviewService(context, mockEmail.Object, null, NullLogger<InterviewService>.Instance);

        var model = new ScheduleInterviewInputModel
        {
            ApplicationId = app.Id,
            RoundNumber = 1,
            RoundTitle = "Phỏng vấn sơ loại",
            StartTime = DateTime.UtcNow.AddHours(-2), // Thời gian trong quá khứ!
            DurationMinutes = 60,
            InterviewType = "ONLINE_MEET",
            LocationOrLink = "https://meet.google.com/test-room",
            InterviewerIds = [iv1.Id]
        };

        // Act
        var result = await service.ScheduleInterviewAsync(model, recruiter.Id, isRecruiterOnly: false);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("quá khứ");
        result.Errors.Should().NotBeEmpty();

        // Xác nhận không tạo bản ghi Interview nào trong CSDL
        var interviewsCount = await context.Interviews.CountAsync();
        interviewsCount.Should().Be(0);
    }

    [Fact]
    public async Task ScheduleInterview_WithValidFutureDateTime_ShouldSucceedAndCreateInterviewWithPanelists()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (app, iv1, iv2, _, recruiter) = await SeedTestDataAsync(context);
        var mockEmail = new Mock<IEmailService>();
        mockEmail.Setup(e => e.SendInterviewInvitationAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var service = new InterviewService(context, mockEmail.Object, null, NullLogger<InterviewService>.Instance);

        var futureTime = DateTime.UtcNow.AddDays(3);
        var model = new ScheduleInterviewInputModel
        {
            ApplicationId = app.Id,
            RoundNumber = 2,
            RoundTitle = "Phỏng vấn Kỹ thuật & Kiến trúc .NET",
            StartTime = futureTime,
            DurationMinutes = 90,
            InterviewType = "ONLINE_MEET",
            LocationOrLink = "https://meet.google.com/nov-interview-net",
            InterviewerIds = [iv1.Id, iv2.Id],
            Notes = "Yêu cầu ứng viên chuẩn bị laptop để live coding."
        };

        // Act
        var result = await service.ScheduleInterviewAsync(model, recruiter.Id, isRecruiterOnly: false);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.InterviewId.Should().NotBeNull();
        result.RoundNumber.Should().Be(2);
        result.Title.Should().Be("Phỏng vấn Kỹ thuật & Kiến trúc .NET");
        result.Panelists.Should().Contain(iv1.FullName);
        result.Panelists.Should().Contain(iv2.FullName);

        // Kiểm tra trong CSDL
        var dbInterview = await context.Interviews
            .Include(i => i.Panelists)
            .FirstOrDefaultAsync(i => i.Id == result.InterviewId);

        dbInterview.Should().NotBeNull();
        dbInterview!.Status.Should().Be(InterviewStatus.SCHEDULED);
        dbInterview.RoundNumber.Should().Be(2);
        dbInterview.LocationOrLink.Should().Be("https://meet.google.com/nov-interview-net");
        dbInterview.Panelists.Should().HaveCount(2);
        dbInterview.Panelists.Select(p => p.InterviewerId).Should().Contain([iv1.Id, iv2.Id]);

        // Đợi background task gửi email (tránh race condition của Task.Run)
        for (int i = 0; i < 10; i++)
        {
            if (mockEmail.Invocations.Any(inv => inv.Method.Name == nameof(IEmailService.SendInterviewInvitationAsync)))
                break;
            await Task.Delay(50);
        }

        // Kiểm tra gửi email mời
        mockEmail.Verify(e => e.SendInterviewInvitationAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ScheduleInterview_WithoutInterviewers_ShouldFail()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (app, _, _, _, recruiter) = await SeedTestDataAsync(context);
        var mockEmail = new Mock<IEmailService>();
        var service = new InterviewService(context, mockEmail.Object, null, NullLogger<InterviewService>.Instance);

        var model = new ScheduleInterviewInputModel
        {
            ApplicationId = app.Id,
            RoundNumber = 1,
            RoundTitle = "Vòng sơ loại",
            StartTime = DateTime.UtcNow.AddDays(1),
            DurationMinutes = 60,
            LocationOrLink = "https://meet.google.com/room",
            InterviewerIds = [] // Không chọn ai!
        };

        // Act
        var result = await service.ScheduleInterviewAsync(model, recruiter.Id, isRecruiterOnly: false);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("ít nhất một người phỏng vấn");
    }

    [Fact]
    public async Task ScheduleInterview_WithZeroOrNegativeDuration_ShouldFail()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (app, iv1, _, _, recruiter) = await SeedTestDataAsync(context);
        var mockEmail = new Mock<IEmailService>();
        var service = new InterviewService(context, mockEmail.Object, null, NullLogger<InterviewService>.Instance);

        var model = new ScheduleInterviewInputModel
        {
            ApplicationId = app.Id,
            RoundNumber = 1,
            RoundTitle = "Vòng sơ loại",
            StartTime = DateTime.UtcNow.AddDays(1),
            DurationMinutes = 0, // Không hợp lệ
            LocationOrLink = "https://meet.google.com/room",
            InterviewerIds = [iv1.Id]
        };

        // Act
        var result = await service.ScheduleInterviewAsync(model, recruiter.Id, isRecruiterOnly: false);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Thời lượng phỏng vấn không hợp lệ");
    }

    [Fact]
    public async Task ScheduleInterview_WithNonExistentApplication_ShouldFail()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (_, iv1, _, _, recruiter) = await SeedTestDataAsync(context);
        var mockEmail = new Mock<IEmailService>();
        var service = new InterviewService(context, mockEmail.Object, null, NullLogger<InterviewService>.Instance);

        var nonExistentAppId = Guid.NewGuid();
        var model = new ScheduleInterviewInputModel
        {
            ApplicationId = nonExistentAppId,
            RoundNumber = 1,
            StartTime = DateTime.UtcNow.AddDays(1),
            DurationMinutes = 60,
            LocationOrLink = "https://meet.google.com/room",
            InterviewerIds = [iv1.Id]
        };

        // Act
        var result = await service.ScheduleInterviewAsync(model, recruiter.Id, isRecruiterOnly: false);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Không tìm thấy hồ sơ ứng tuyển");
    }

    [Fact]
    public async Task GetAvailableInterviewersAsync_ShouldReturnOnlyActiveUsers()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (_, iv1, iv2, inactiveUser, _) = await SeedTestDataAsync(context);
        var mockEmail = new Mock<IEmailService>();
        var service = new InterviewService(context, mockEmail.Object, null, NullLogger<InterviewService>.Instance);

        // Act
        var interviewers = await service.GetAvailableInterviewersAsync();

        // Assert
        interviewers.Should().NotBeNull();
        interviewers.Should().NotBeEmpty();
        interviewers.Select(u => u.Id).Should().Contain([iv1.Id, iv2.Id]);
        interviewers.Select(u => u.Id).Should().NotContain(inactiveUser.Id);
    }

    [Fact]
    public async Task GetApplicationInterviewsAsync_ShouldReturnRoundsOrderedAscending()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (app, _, _, _, _) = await SeedTestDataAsync(context);
        var mockEmail = new Mock<IEmailService>();
        var service = new InterviewService(context, mockEmail.Object, null, NullLogger<InterviewService>.Instance);

        // Tạo 2 buổi phỏng vấn theo thứ tự vòng 2 và vòng 1
        var ivRound2 = new Interview
        {
            Id = Guid.NewGuid(),
            ApplicationId = app.Id,
            RoundNumber = 2,
            Title = "Vòng Chuyên môn",
            StartTime = DateTimeOffset.UtcNow.AddDays(5),
            EndTime = DateTimeOffset.UtcNow.AddDays(5).AddMinutes(60),
            InterviewType = InterviewType.ONLINE_MEET,
            LocationOrLink = "https://meet.google.com/round-2",
            Status = InterviewStatus.SCHEDULED
        };

        var ivRound1 = new Interview
        {
            Id = Guid.NewGuid(),
            ApplicationId = app.Id,
            RoundNumber = 1,
            Title = "Vòng Sơ loại",
            StartTime = DateTimeOffset.UtcNow.AddDays(2),
            EndTime = DateTimeOffset.UtcNow.AddDays(2).AddMinutes(45),
            InterviewType = InterviewType.ONLINE_MEET,
            LocationOrLink = "https://meet.google.com/round-1",
            Status = InterviewStatus.COMPLETED
        };

        context.Interviews.AddRange(ivRound2, ivRound1);
        await context.SaveChangesAsync();

        // Act
        var interviews = await service.GetApplicationInterviewsAsync(app.Id);

        // Assert
        interviews.Should().HaveCount(2);
        interviews[0].RoundNumber.Should().Be(1);
        interviews[1].RoundNumber.Should().Be(2);
        interviews[0].Title.Should().Be("Vòng Sơ loại");
        interviews[1].Title.Should().Be("Vòng Chuyên môn");
    }

    [Fact]
    public async Task GetScheduleWizardDataAsync_ShouldReturnPipelineCandidatesAndPresets()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (app, _, _, _, _) = await SeedTestDataAsync(context);
        var mockEmail = new Mock<IEmailService>();
        var service = new InterviewService(context, mockEmail.Object, null, NullLogger<InterviewService>.Instance);

        // Act
        var result = await service.GetScheduleWizardDataAsync(app.Id);

        // Assert
        result.Should().NotBeNull();
        result.PipelineCandidates.Should().NotBeEmpty();
        result.AvailableInterviewers.Should().NotBeEmpty();
        result.RoomPresets.Should().HaveCount(7);
        result.SelectedCandidate.Should().NotBeNull();
        result.SelectedCandidate!.ApplicationId.Should().Be(app.Id);
        result.SelectedCandidate.NextRoundNumber.Should().Be(1);
    }

    [Fact]
    public async Task GetCandidateWizardDetailAsync_WhenCandidateHasPriorInterviews_ShouldCalculateNextRoundCorrectly()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (app, interviewer1, _, _, _) = await SeedTestDataAsync(context);
        var mockEmail = new Mock<IEmailService>();
        var service = new InterviewService(context, mockEmail.Object, null, NullLogger<InterviewService>.Instance);

        // Tạo 1 buổi phỏng vấn Vòng 1 đã diễn ra
        var round1 = new Interview
        {
            Id = Guid.NewGuid(),
            ApplicationId = app.Id,
            RoundNumber = 1,
            Title = "Vòng 1 - Sơ vấn",
            StartTime = DateTimeOffset.UtcNow.AddDays(-2),
            EndTime = DateTimeOffset.UtcNow.AddDays(-2).AddMinutes(60),
            Status = InterviewStatus.COMPLETED
        };
        context.Interviews.Add(round1);
        context.InterviewPanelists.Add(new InterviewPanelist
        {
            Id = Guid.NewGuid(),
            InterviewId = round1.Id,
            InterviewerId = interviewer1.Id,
            IsLead = true
        });
        await context.SaveChangesAsync();

        // Act
        var dossier = await service.GetCandidateWizardDetailAsync(app.Id);

        // Assert
        dossier.Should().NotBeNull();
        dossier!.NextRoundNumber.Should().Be(2);
        dossier.SuggestedRoundTitle.Should().Contain("Vòng 2");
        dossier.InterviewHistory.Should().HaveCount(1);
        dossier.InterviewHistory[0].StatusDisplay.Should().Be("Đã hoàn thành");
    }

    [Fact]
    public async Task CheckInterviewersAvailabilityAsync_WhenConflictExists_ShouldFlagBusy()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (app, interviewer1, interviewer2, _, _) = await SeedTestDataAsync(context);
        var mockEmail = new Mock<IEmailService>();
        var service = new InterviewService(context, mockEmail.Object, null, NullLogger<InterviewService>.Instance);

        var meetingStart = DateTimeOffset.UtcNow.AddDays(2).AddHours(9);
        var meetingEnd = meetingStart.AddMinutes(60);

        // Giám khảo 1 có lịch phỏng vấn khác trùng vào khoảng thời gian meetingStart -> meetingEnd
        var busyInterview = new Interview
        {
            Id = Guid.NewGuid(),
            ApplicationId = app.Id,
            RoundNumber = 1,
            Title = "Phỏng vấn trùng giờ",
            StartTime = meetingStart.AddMinutes(-15),
            EndTime = meetingStart.AddMinutes(45),
            Status = InterviewStatus.SCHEDULED
        };
        context.Interviews.Add(busyInterview);
        context.InterviewPanelists.Add(new InterviewPanelist
        {
            Id = Guid.NewGuid(),
            InterviewId = busyInterview.Id,
            InterviewerId = interviewer1.Id
        });
        await context.SaveChangesAsync();

        // Act
        var checkResults = await service.CheckInterviewersAvailabilityAsync(
            new List<Guid> { interviewer1.Id, interviewer2.Id }, meetingStart, meetingEnd);

        // Assert
        checkResults.Should().HaveCount(2);

        var inv1Check = checkResults.First(c => c.InterviewerId == interviewer1.Id);
        inv1Check.IsBusy.Should().BeTrue();
        inv1Check.ConflictTitle.Should().Be("Phỏng vấn trùng giờ");

        var inv2Check = checkResults.First(c => c.InterviewerId == interviewer2.Id);
        inv2Check.IsBusy.Should().BeFalse();
        inv2Check.StatusMessage.Should().Contain("Trống lịch");
    }

    [Fact]
    public async Task GetAvailableInterviewersAsync_ShouldNeverReturnCandidates_Rule12Security()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (_, interviewer1, interviewer2, _, _) = await SeedTestDataAsync(context);

        // Tạo thêm 2 Candidate có Status = ACTIVE
        var candidateUser1 = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Ứng Viên Nguyễn Văn A",
            Email = "candidate1@gmail.com",
            PasswordHash = "hash",
            Status = "ACTIVE",
            Role = UserRoles.Candidate,
            UserType = UserType.EXTERNAL_CANDIDATE
        };
        var candidateUser2 = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Ứng Viên Trần Thị B",
            Email = "candidate2@gmail.com",
            PasswordHash = "hash",
            Status = "ACTIVE",
            Role = "Candidate",
            UserType = UserType.INTERNAL // Dù giả mạo INTERNAL nhưng Role = Candidate vẫn phải bị loại
        };
        await context.Users.AddRangeAsync(candidateUser1, candidateUser2);
        await context.SaveChangesAsync();

        var mockEmail = new Mock<IEmailService>();
        var service = new InterviewService(context, mockEmail.Object, null, NullLogger<InterviewService>.Instance);

        // Act
        var availableInterviewers = await service.GetAvailableInterviewersAsync();

        // Assert
        availableInterviewers.Should().NotBeEmpty();
        availableInterviewers.Should().NotContain(u => u.Id == candidateUser1.Id);
        availableInterviewers.Should().NotContain(u => u.Id == candidateUser2.Id);
        availableInterviewers.Should().OnlyContain(u => UserRoles.CanBeInterviewer(u.Role));
    }

    [Fact]
    public async Task ScheduleInterview_WithCandidateAsPanelist_ShouldThrowSecurityException_Rule12()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (app, interviewer1, _, _, recruiter) = await SeedTestDataAsync(context);

        var candidatePanelist = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Ứng Viên Bị Gán Làm Giám Khảo",
            Email = "bad_candidate@gmail.com",
            PasswordHash = "hash",
            Status = "ACTIVE",
            Role = UserRoles.Candidate,
            UserType = UserType.EXTERNAL_CANDIDATE
        };
        context.Users.Add(candidatePanelist);
        await context.SaveChangesAsync();

        var mockEmail = new Mock<IEmailService>();
        var service = new InterviewService(context, mockEmail.Object, null, NullLogger<InterviewService>.Instance);

        var input = new ScheduleInterviewInputModel
        {
            ApplicationId = app.Id,
            RoundNumber = 1,
            RoundTitle = "Phỏng vấn kiểm thử bảo mật Rule 12",
            StartTime = DateTime.UtcNow.AddDays(2),
            DurationMinutes = 60,
            InterviewType = "ONLINE_MEET",
            InterviewerIds = [interviewer1.Id, candidatePanelist.Id]
        };

        // Act & Assert
        var act = async () => await service.ScheduleInterviewAsync(input, recruiter.Id, isRecruiterOnly: false);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Rule 12*Ứng viên tuyệt đối không được phép tham gia hội đồng phỏng vấn*");
    }
}
