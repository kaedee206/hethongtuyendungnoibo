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
/// Unit Tests kiểm thử toàn diện nghiệp vụ Đặt lịch hàng loạt cho nhiều ứng viên trong cùng một vòng (Scrum - Số 52 / EP-06).
/// Tiêu chí nghiệm thu:
/// 1. Chọn nhiều ứng viên (ví dụ 10 ứng viên) và một khung giờ bắt đầu, hệ thống tự động chia khe thời gian liên tiếp kèm giải lao.
/// 2. Xem trước (preview) toàn bộ khe giờ trước khi xác nhận.
/// 3. Thư mời email được gửi cho từng ứng viên với đúng khe giờ của họ.
/// 4. Không cho phép đặt lịch ở quá khứ.
/// 5. Tạo các bản ghi Interview và InterviewPanelist tương ứng trong DB.
/// </summary>
public class BatchInterviewSchedulingServiceTests
{
    private static ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"AtsBatchInterviewDb_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static async Task<(List<Application> apps, User interviewer1, User interviewer2, User recruiter)> SeedBatchTestDataAsync(ApplicationDbContext context, int candidateCount = 10)
    {
        var recruiter = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Lê Chuyên Viên Tuyển Dụng",
            Email = $"recruiter_{Guid.NewGuid():N}@noveratech.vn",
            PasswordHash = "hash",
            Status = "ACTIVE",
            Role = "RECRUITER"
        };

        var interviewer1 = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Nguyễn Văn Giám Khảo 1",
            Email = $"panelist1_{Guid.NewGuid():N}@noveratech.vn",
            PasswordHash = "hash",
            Status = "ACTIVE",
            Role = "INTERVIEWER"
        };

        var interviewer2 = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Trần Thị Giám Khảo 2",
            Email = $"panelist2_{Guid.NewGuid():N}@noveratech.vn",
            PasswordHash = "hash",
            Status = "ACTIVE",
            Role = "INTERVIEWER"
        };

        var department = new Department
        {
            Id = Guid.NewGuid(),
            Name = "Khối Công Nghệ",
            Code = $"TECH_{Guid.NewGuid():N}"[..8]
        };

        var screeningStage = new PipelineStage
        {
            Id = Guid.NewGuid(),
            Name = "Sàng lọc hồ sơ",
            StageOrder = 2
        };

        var interviewStage = new PipelineStage
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
            Title = "Senior .NET Developer",
            Slug = $"senior-net-{Guid.NewGuid():N}"[..20],
            RequisitionId = requisition.Id,
            Status = JobPostingStatus.PUBLISHED
        };

        context.Users.AddRange(recruiter, interviewer1, interviewer2);
        context.Departments.Add(department);
        context.PipelineStages.AddRange(screeningStage, interviewStage);
        context.JobRequisitions.Add(requisition);
        context.JobPostings.Add(jobPosting);

        var apps = new List<Application>();
        for (int i = 0; i < candidateCount; i++)
        {
            var candidate = new Candidate
            {
                Id = Guid.NewGuid(),
                FirstName = "Ứng viên",
                LastName = $"Số {i + 1}",
                Email = $"cand_{i + 1}_{Guid.NewGuid():N}@gmail.com",
                Phone = $"090000000{i % 10}",
                Source = CandidateSource.LINKEDIN
            };

            var app = new Application
            {
                Id = Guid.NewGuid(),
                CandidateId = candidate.Id,
                Candidate = candidate,
                JobPostingId = jobPosting.Id,
                JobPosting = jobPosting,
                CurrentStageId = screeningStage.Id,
                CurrentStage = screeningStage,
                Status = ApplicationStatus.IN_PROCESS,
                AppliedAt = DateTimeOffset.UtcNow.AddDays(-2)
            };

            context.Candidates.Add(candidate);
            context.Applications.Add(app);
            apps.Add(app);
        }

        await context.SaveChangesAsync();
        return (apps, interviewer1, interviewer2, recruiter);
    }

    [Fact]
    public async Task PreviewBatchScheduleAsync_With10Candidates_GeneratesConsecutiveSlotsWithBreaks()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (apps, interviewer1, interviewer2, recruiter) = await SeedBatchTestDataAsync(context, 10);
        var mockEmail = new Mock<IEmailService>();
        var service = new InterviewService(context, mockEmail.Object, null, NullLogger<InterviewService>.Instance);

        var startTimeUtc = DateTime.UtcNow.AddDays(1).Date.AddHours(13).AddMinutes(30); // Ngày mai 13:30 UTC
        var input = new BatchSchedulePreviewInputModel
        {
            ApplicationIds = apps.Select(a => a.Id).ToList(),
            StartTime = startTimeUtc,
            SlotDurationMinutes = 30,
            BreakDurationMinutes = 10,
            RoundNumber = 1,
            RoundTitle = "Phỏng vấn chuyên môn tập trung chiều",
            InterviewType = "ONLINE_MEET",
            LocationOrLink = "https://teams.microsoft.com/meet/scrum52",
            InterviewerIds = new List<Guid> { interviewer1.Id, interviewer2.Id }
        };

        // Act
        var result = await service.PreviewBatchScheduleAsync(input, recruiter.Id, isRecruiterOnly: false);

        // Assert
        result.Success.Should().BeTrue();
        result.Slots.Should().HaveCount(10);
        result.TotalCandidates.Should().Be(10);
        result.TotalDurationMinutes.Should().Be(10 * 30 + 9 * 10); // 10 ca 30p + 9 khoảng nghỉ 10p = 390p

        // Kiểm tra ca 1
        result.Slots[0].SlotOrder.Should().Be(1);
        result.Slots[0].StartTime.Should().Be(startTimeUtc);
        result.Slots[0].EndTime.Should().Be(startTimeUtc.AddMinutes(30));

        // Kiểm tra ca 2: Bắt đầu = EndTime của ca 1 + 10 phút nghỉ
        result.Slots[1].SlotOrder.Should().Be(2);
        result.Slots[1].StartTime.Should().Be(startTimeUtc.AddMinutes(40));
        result.Slots[1].EndTime.Should().Be(startTimeUtc.AddMinutes(70));
    }

    [Fact]
    public async Task PreviewBatchScheduleAsync_WhenStartTimeInPast_ReturnsFailure()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (apps, interviewer1, _, recruiter) = await SeedBatchTestDataAsync(context, 3);
        var mockEmail = new Mock<IEmailService>();
        var service = new InterviewService(context, mockEmail.Object, null, NullLogger<InterviewService>.Instance);

        var input = new BatchSchedulePreviewInputModel
        {
            ApplicationIds = apps.Select(a => a.Id).ToList(),
            StartTime = DateTime.UtcNow.AddMinutes(-30), // Thời gian quá khứ
            SlotDurationMinutes = 30,
            BreakDurationMinutes = 5,
            InterviewerIds = new List<Guid> { interviewer1.Id }
        };

        // Act
        var result = await service.PreviewBatchScheduleAsync(input, recruiter.Id, isRecruiterOnly: false);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("quá khứ");
    }

    [Fact]
    public async Task PreviewBatchScheduleAsync_WhenEmptyCandidates_ReturnsFailure()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var mockEmail = new Mock<IEmailService>();
        var service = new InterviewService(context, mockEmail.Object, null, NullLogger<InterviewService>.Instance);

        var input = new BatchSchedulePreviewInputModel
        {
            ApplicationIds = new List<Guid>(),
            StartTime = DateTime.UtcNow.AddDays(1),
            SlotDurationMinutes = 30
        };

        // Act
        var result = await service.PreviewBatchScheduleAsync(input, Guid.NewGuid(), isRecruiterOnly: false);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("ít nhất một ứng viên");
    }

    [Fact]
    public async Task ConfirmBatchScheduleAsync_CreatesInterviewsAndAssignsPanelists_AndSendsEmails()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (apps, interviewer1, interviewer2, recruiter) = await SeedBatchTestDataAsync(context, 3);
        var mockEmail = new Mock<IEmailService>();
        mockEmail.Setup(e => e.SendInterviewInvitationAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = new InterviewService(context, mockEmail.Object, null, NullLogger<InterviewService>.Instance);

        var startBase = DateTime.UtcNow.AddDays(2).Date.AddHours(14); // 14:00 2 ngày tới
        var confirmInput = new BatchScheduleConfirmInputModel
        {
            RoundNumber = 1,
            RoundTitle = "Vòng Phỏng Vấn Tập Trung",
            InterviewType = "ONLINE_MEET",
            LocationOrLink = "https://meet.google.com/abc-xyz",
            InterviewerIds = new List<Guid> { interviewer1.Id, interviewer2.Id },
            Slots = new List<BatchScheduleSlotConfirmItemModel>
            {
                new()
                {
                    ApplicationId = apps[0].Id,
                    StartTime = startBase,
                    EndTime = startBase.AddMinutes(30),
                    SlotOrder = 1
                },
                new()
                {
                    ApplicationId = apps[1].Id,
                    StartTime = startBase.AddMinutes(35),
                    EndTime = startBase.AddMinutes(65),
                    SlotOrder = 2
                },
                new()
                {
                    ApplicationId = apps[2].Id,
                    StartTime = startBase.AddMinutes(70),
                    EndTime = startBase.AddMinutes(100),
                    SlotOrder = 3
                }
            }
        };

        // Act
        var result = await service.ConfirmBatchScheduleAsync(confirmInput, recruiter.Id, isRecruiterOnly: false);

        // Assert
        result.Success.Should().BeTrue();
        result.SuccessCount.Should().Be(3);
        result.ScheduledInterviews.Should().HaveCount(3);

        // Kiểm tra database: 3 bản ghi Interview được tạo
        var interviewsInDb = await context.Interviews
            .Include(i => i.Panelists)
            .OrderBy(i => i.StartTime)
            .ToListAsync();

        interviewsInDb.Should().HaveCount(3);
        foreach (var interview in interviewsInDb)
        {
            interview.RoundNumber.Should().Be(1);
            interview.Status.Should().Be(InterviewStatus.SCHEDULED);
            interview.InterviewType.Should().Be(InterviewType.ONLINE_MEET);
            interview.LocationOrLink.Should().Be("https://meet.google.com/abc-xyz");
            interview.Panelists.Should().HaveCount(2);
            interview.Panelists.Select(p => p.InterviewerId).Should().Contain(new[] { interviewer1.Id, interviewer2.Id });
        }

        // Kiểm tra giai đoạn ứng viên được cập nhật sang Phỏng vấn (StageOrder = 3)
        var updatedApps = await context.Applications.Include(a => a.CurrentStage).ToListAsync();
        foreach (var app in updatedApps)
        {
            app.CurrentStage?.StageOrder.Should().Be(3);
        }

        // Chờ ngắn để background email Task.Run kích hoạt
        await Task.Delay(200);
        mockEmail.Verify(e => e.SendInterviewInvitationAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.AtLeast(1));
    }

    [Fact]
    public async Task ConfirmBatchScheduleAsync_WhenSlotInPast_ReturnsFailure()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (apps, interviewer1, _, recruiter) = await SeedBatchTestDataAsync(context, 2);
        var mockEmail = new Mock<IEmailService>();
        var service = new InterviewService(context, mockEmail.Object, null, NullLogger<InterviewService>.Instance);

        var confirmInput = new BatchScheduleConfirmInputModel
        {
            RoundNumber = 1,
            RoundTitle = "PV Test",
            InterviewType = "OFFLINE_OFFICE",
            LocationOrLink = "Phòng họp 302",
            InterviewerIds = new List<Guid> { interviewer1.Id },
            Slots = new List<BatchScheduleSlotConfirmItemModel>
            {
                new()
                {
                    ApplicationId = apps[0].Id,
                    StartTime = DateTime.UtcNow.AddMinutes(-60), // Quá khứ
                    EndTime = DateTime.UtcNow.AddMinutes(-30),
                    SlotOrder = 1
                }
            }
        };

        // Act
        var result = await service.ConfirmBatchScheduleAsync(confirmInput, recruiter.Id, isRecruiterOnly: false);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("quá khứ");
    }
}
