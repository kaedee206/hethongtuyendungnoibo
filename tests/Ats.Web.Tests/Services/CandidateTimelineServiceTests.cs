using Ats.Web.Data;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Models.ViewModels.CandidateTimeline;
using Ats.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ats.Web.Tests.Services;

/// <summary>
/// Unit Tests cho Scrum - Số 46:
/// Nhật ký toàn bộ tương tác với một ứng viên (dòng thời gian gồm: nộp hồ sơ, chuyển giai đoạn,
/// email đã gửi, lịch phỏng vấn, đánh giá, ghi chú cuộc gọi; sắp xếp mới nhất trước).
/// </summary>
public class CandidateTimelineServiceTests
{
    private static ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"AtsTimelineDb_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static async Task<(Candidate candidate, Application app, User recruiter, User interviewer)> SeedFullCandidateDataAsync(ApplicationDbContext context)
    {
        var recruiter = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Nguyễn Tuyển Dụng",
            Email = "recruiter@noveratech.vn",
            PasswordHash = "hash"
        };
        await context.Users.AddAsync(recruiter);

        var interviewer = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Trần Trưởng Nhóm",
            Email = "leader.tran@noveratech.vn",
            PasswordHash = "hash"
        };
        await context.Users.AddAsync(interviewer);

        var candidate = new Candidate
        {
            Id = Guid.NewGuid(),
            FirstName = "Phạm",
            LastName = "Văn Nam",
            Email = "phamvannam@gmail.com",
            Phone = "0987654321",
            CurrentCompany = "Tech Corp",
            CurrentTitle = "Senior Backend Engineer"
        };
        await context.Candidates.AddAsync(candidate);

        var dept = new Department
        {
            Id = Guid.NewGuid(),
            Code = $"DEP_{Guid.NewGuid():N}"[..8],
            Name = "Khối Công Nghệ"
        };
        await context.Departments.AddAsync(dept);

        var job = new JobPosting
        {
            Id = Guid.NewGuid(),
            Title = "Senior .NET Core Developer",
            Slug = $"senior-net-core-{Guid.NewGuid():N}"[..12]
        };
        await context.JobPostings.AddAsync(job);

        var stage1 = new PipelineStage { Id = Guid.NewGuid(), Name = "Ứng tuyển", Order = 1 };
        var stage2 = new PipelineStage { Id = Guid.NewGuid(), Name = "Sàng lọc CV", Order = 2 };
        var stage3 = new PipelineStage { Id = Guid.NewGuid(), Name = "Phỏng vấn kỹ thuật", Order = 3 };
        await context.PipelineStages.AddRangeAsync(stage1, stage2, stage3);

        var resume = new Resume
        {
            Id = Guid.NewGuid(),
            CandidateId = candidate.Id,
            FileName = "PhamVanNam_CV.pdf",
            FilePath = "/uploads/cv/PhamVanNam_CV.pdf"
        };
        await context.Resumes.AddAsync(resume);

        var baseTime = DateTimeOffset.UtcNow.AddDays(-10);

        // 1. Đơn nộp hồ sơ (Applied: 10 ngày trước)
        var app = new Application
        {
            Id = Guid.NewGuid(),
            CandidateId = candidate.Id,
            JobPostingId = job.Id,
            ResumeId = resume.Id,
            CurrentStageId = stage3.Id,
            Status = ApplicationStatus.IN_PROCESS,
            AppliedAt = baseTime,
            TrackingCode = "APP-PVN-001",
            JobPosting = job,
            Resume = resume,
            CurrentStage = stage3
        };
        await context.Applications.AddAsync(app);

        // 2. Chuyển giai đoạn (Stage Change: 8 ngày trước)
        var history = new ApplicationStageHistory
        {
            Id = Guid.NewGuid(),
            ApplicationId = app.Id,
            FromStageId = stage1.Id,
            ToStageId = stage2.Id,
            ChangedByUserId = recruiter.Id,
            Comment = "Hồ sơ ứng viên rất tiềm năng, chuyển vòng phỏng vấn chuyên môn",
            CreatedAt = baseTime.AddDays(2),
            FromStage = stage1,
            ToStage = stage2,
            ChangedByUser = recruiter,
            Application = app
        };
        await context.ApplicationStageHistories.AddAsync(history);

        // 3. Email đã gửi (Email sent: 7 ngày trước)
        var email = new EmailLog
        {
            Id = Guid.NewGuid(),
            RecipientEmail = candidate.Email,
            Subject = "NoveraTech - Thư mời phỏng vấn kỹ thuật vị trí Senior .NET",
            Status = "SENT",
            SentAt = baseTime.AddDays(3),
            CreatedAt = baseTime.AddDays(3)
        };
        await context.EmailLogs.AddAsync(email);

        // 4. Lịch phỏng vấn (Interview: 5 ngày trước)
        var interview = new Interview
        {
            Id = Guid.NewGuid(),
            ApplicationId = app.Id,
            RoundNumber = 1,
            Title = "Phỏng vấn kỹ thuật vòng 1",
            InterviewType = InterviewType.ONLINE_MEET,
            LocationOrLink = "https://meet.google.com/ats-tech-interview",
            StartTime = baseTime.AddDays(5),
            EndTime = baseTime.AddDays(5).AddHours(1),
            CandidateConfirmed = CandidateConfirmStatus.ACCEPTED,
            CandidateNotes = "Ứng viên đã chuẩn bị slide kiến trúc microservices",
            Status = InterviewStatus.COMPLETED,
            Application = app
        };
        interview.Panelists.Add(new InterviewPanelist
        {
            InterviewId = interview.Id,
            UserId = interviewer.Id,
            User = interviewer
        });
        await context.Interviews.AddAsync(interview);

        // 5. Đánh giá phỏng vấn (Evaluation: 4 ngày trước)
        var eval = new InterviewEvaluation
        {
            Id = Guid.NewGuid(),
            InterviewId = interview.Id,
            InterviewerId = interviewer.Id,
            OverallScore = 4.8m,
            Recommendation = RecommendationType.STRONG_HIRE,
            Strengths = "Nắm vững DDD, PostgreSQL, Clean Architecture và tối ưu hiệu năng",
            Weaknesses = "Kinh nghiệm AWS ở mức cơ bản",
            DetailedFeedback = "Đồng ý đề xuất mức lương Senior và chuyển sang vòng Offer",
            IsSubmitted = true,
            SubmittedAt = baseTime.AddDays(6),
            Interview = interview,
            Interviewer = interviewer
        };
        await context.InterviewEvaluations.AddAsync(eval);

        // 6. Ghi nhận cuộc gọi trực tiếp (Call note: 1 ngày trước - mới nhất)
        var call = new CandidateInteraction
        {
            Id = Guid.NewGuid(),
            CandidateId = candidate.Id,
            ApplicationId = app.Id,
            CreatedByUserId = recruiter.Id,
            InteractionType = CandidateInteractionType.CALL,
            Title = "Gọi điện trao đổi kết quả phỏng vấn và thăm dò lương",
            Content = "Đã gọi lúc 14:30. Ứng viên rất vui mừng với đánh giá STRONG_HIRE. Mức lương kỳ vọng 42 triệu Net. Có thể đi làm từ ngày 01 tháng sau.",
            Outcome = "Đồng ý nhận Offer dự kiến",
            InteractionDate = baseTime.AddDays(9),
            CreatedByUser = recruiter,
            Application = app,
            Candidate = candidate
        };
        await context.CandidateInteractions.AddAsync(call);

        await context.SaveChangesAsync();
        return (candidate, app, recruiter, interviewer);
    }

    [Fact]
    public async Task GetCandidateTimelineAsync_ShouldAggregateAllFiveEventTypes()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (candidate, app, _, _) = await SeedFullCandidateDataAsync(context);
        var service = new CandidateTimelineService(context, NullLogger<CandidateTimelineService>.Instance);

        // Act
        var result = await service.GetCandidateTimelineAsync(candidate.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(candidate.Id, result.CandidateId);
        Assert.Equal("Phạm Văn Nam", result.CandidateName);
        Assert.Equal("phamvannam@gmail.com", result.CandidateEmail);
        Assert.Equal(6, result.TotalEventsCount);

        // Kiểm tra đủ cả 6 nhóm sự kiện (5 nhóm đề bài + cuộc gọi trực tiếp)
        Assert.Equal(1, result.ApplicationsCount);
        Assert.Equal(1, result.StageChangesCount);
        Assert.Equal(1, result.EmailsSentCount);
        Assert.Equal(1, result.InterviewsCount);
        Assert.Equal(1, result.EvaluationsCount);
        Assert.Equal(1, result.CallsAndNotesCount);

        Assert.Contains(result.Events, e => e.EventType == "APPLIED");
        Assert.Contains(result.Events, e => e.EventType == "STAGE_CHANGE");
        Assert.Contains(result.Events, e => e.EventType == "EMAIL_SENT");
        Assert.Contains(result.Events, e => e.EventType == "INTERVIEW_SCHEDULED");
        Assert.Contains(result.Events, e => e.EventType == "INTERVIEW_EVALUATION");
        Assert.Contains(result.Events, e => e.EventType == "CALL_NOTE");
    }

    [Fact]
    public async Task GetCandidateTimelineAsync_ShouldOrderByMostRecentFirst()
    {
        // Arrange: Quy tắc quan trọng nhất của User Story: "Sắp xếp mới nhất trước"
        using var context = CreateInMemoryDbContext();
        var (candidate, _, _, _) = await SeedFullCandidateDataAsync(context);
        var service = new CandidateTimelineService(context, NullLogger<CandidateTimelineService>.Instance);

        // Act
        var result = await service.GetCandidateTimelineAsync(candidate.Id);

        // Assert
        Assert.NotNull(result);
        var events = result.Events;
        Assert.NotEmpty(events);

        for (int i = 0; i < events.Count - 1; i++)
        {
            Assert.True(events[i].OccurredAt >= events[i + 1].OccurredAt,
                $"Sự kiện index {i} ({events[i].Title} lúc {events[i].OccurredAt}) phải xảy ra sau hoặc cùng lúc với sự kiện index {i + 1} ({events[i + 1].Title} lúc {events[i + 1].OccurredAt})");
        }

        // Sự kiện đầu tiên (mới nhất) phải là cuộc gọi trao đổi (9 ngày sau ngày nộp)
        Assert.Equal("CALL_NOTE", events[0].EventType);
        // Sự kiện cuối cùng (cũ nhất) phải là nộp hồ sơ (10 ngày trước)
        Assert.Equal("APPLIED", events[^1].EventType);
    }

    [Fact]
    public async Task GetCandidateTimelineAsync_ShouldDisplayActorAndContentCorrectly()
    {
        // Arrange: Mục tiêu câu chuyện "để biết ai đã liên hệ ứng viên này và nói gì trước khi mình gọi"
        using var context = CreateInMemoryDbContext();
        var (candidate, _, recruiter, interviewer) = await SeedFullCandidateDataAsync(context);
        var service = new CandidateTimelineService(context, NullLogger<CandidateTimelineService>.Instance);

        // Act
        var result = await service.GetCandidateTimelineAsync(candidate.Id);

        // Assert
        Assert.NotNull(result);

        // 1. Kiểm tra sự kiện Cuộc gọi: Biết ai gọi và nội dung trao đổi
        var callEvent = result.Events.FirstOrDefault(e => e.EventType == "CALL_NOTE");
        Assert.NotNull(callEvent);
        Assert.Equal(recruiter.FullName, callEvent.ActorName);
        Assert.Contains("Đã gọi lúc 14:30", callEvent.Details);
        Assert.Equal("Đồng ý nhận Offer dự kiến", callEvent.Outcome);

        // 2. Kiểm tra sự kiện Đánh giá: Biết ai chấm và nhận xét điểm mạnh/yếu
        var evalEvent = result.Events.FirstOrDefault(e => e.EventType == "INTERVIEW_EVALUATION");
        Assert.NotNull(evalEvent);
        Assert.Equal(interviewer.FullName, evalEvent.ActorName);
        Assert.Contains("DDD, PostgreSQL, Clean Architecture", evalEvent.Details);
        Assert.Contains("STRONG_HIRE", evalEvent.Details);

        // 3. Kiểm tra sự kiện Lịch phỏng vấn: Biết hội đồng gồm những ai
        var interviewEvent = result.Events.FirstOrDefault(e => e.EventType == "INTERVIEW_SCHEDULED");
        Assert.NotNull(interviewEvent);
        Assert.Contains(interviewer.FullName, interviewEvent.ActorName);
        Assert.Contains("https://meet.google.com/ats-tech-interview", interviewEvent.Details);
    }

    [Fact]
    public async Task GetCandidateTimelineAsync_WithFilter_ShouldFilterCorrectEvents()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (candidate, _, _, _) = await SeedFullCandidateDataAsync(context);
        var service = new CandidateTimelineService(context, NullLogger<CandidateTimelineService>.Instance);

        // Act: Chỉ lọc các cuộc gọi & ghi chú trao đổi
        var filterCallResult = await service.GetCandidateTimelineAsync(candidate.Id, filterType: "CALL_NOTE");
        // Act: Chỉ lọc các email đã gửi
        var filterEmailResult = await service.GetCandidateTimelineAsync(candidate.Id, filterType: "EMAIL_SENT");

        // Assert
        Assert.NotNull(filterCallResult);
        Assert.Single(filterCallResult.Events);
        Assert.All(filterCallResult.Events, e => Assert.Equal("CALL_NOTE", e.EventType));

        Assert.NotNull(filterEmailResult);
        Assert.Single(filterEmailResult.Events);
        Assert.All(filterEmailResult.Events, e => Assert.Equal("EMAIL_SENT", e.EventType));
    }

    [Fact]
    public async Task GetCandidateTimelineAsync_WithSearchQuery_ShouldFilterMatchingKeywords()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (candidate, _, _, _) = await SeedFullCandidateDataAsync(context);
        var service = new CandidateTimelineService(context, NullLogger<CandidateTimelineService>.Instance);

        // Act: Tìm từ khóa "42 triệu" trong nội dung trao đổi
        var searchResult = await service.GetCandidateTimelineAsync(candidate.Id, searchQuery: "42 triệu");

        // Assert
        Assert.NotNull(searchResult);
        Assert.Single(searchResult.Events);
        Assert.Contains("42 triệu", searchResult.Events[0].Details);
    }

    [Fact]
    public async Task AddInteractionAsync_ShouldSaveContactNoteAndAppearInTimeline()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (candidate, app, recruiter, _) = await SeedFullCandidateDataAsync(context);
        var service = new CandidateTimelineService(context, NullLogger<CandidateTimelineService>.Instance);

        var input = new AddCandidateInteractionInputModel
        {
            CandidateId = candidate.Id,
            ApplicationId = app.Id,
            InteractionType = CandidateInteractionType.CALL,
            Title = "Gọi điện chốt lịch Onboard và giấy tờ",
            Content = "Ứng viên xác nhận sẽ nộp bằng gốc và giấy khám sức khỏe vào thứ 2 tuần tới.",
            Outcome = "Đã xác nhận đầy đủ",
            InteractionDate = DateTimeOffset.UtcNow
        };

        // Act
        var (success, message, interactionId) = await service.AddInteractionAsync(input, recruiter.Id);
        var timeline = await service.GetCandidateTimelineAsync(candidate.Id);

        // Assert
        Assert.True(success);
        Assert.NotNull(interactionId);
        Assert.NotNull(timeline);

        // Tổng số cuộc gọi tăng lên 2
        Assert.Equal(2, timeline.CallsAndNotesCount);
        // Sự kiện vừa tạo phải ở vị trí đầu tiên do có thời gian mới nhất (UtcNow)
        var topEvent = timeline.Events[0];
        Assert.Equal("CALL_NOTE", topEvent.EventType);
        Assert.Contains("Gọi điện chốt lịch Onboard", topEvent.Title);
        Assert.Contains("giấy khám sức khỏe", topEvent.Details);
    }

    [Fact]
    public async Task GetCandidateTimelineAsync_WhenCandidateNotFound_ShouldReturnNull()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var service = new CandidateTimelineService(context, NullLogger<CandidateTimelineService>.Instance);

        // Act
        var result = await service.GetCandidateTimelineAsync(Guid.NewGuid());

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetTimelineByApplicationIdAsync_ShouldReturnTimelineForApplicationCandidate()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var (candidate, app, _, _) = await SeedFullCandidateDataAsync(context);
        var service = new CandidateTimelineService(context, NullLogger<CandidateTimelineService>.Instance);

        // Act
        var result = await service.GetTimelineByApplicationIdAsync(app.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(candidate.Id, result.CandidateId);
        Assert.Equal(app.Id, result.SelectedApplicationId);
    }
}
