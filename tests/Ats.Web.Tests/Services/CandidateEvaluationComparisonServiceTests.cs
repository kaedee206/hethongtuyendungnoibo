using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Services;
using Ats.Web.Services.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ats.Web.Tests.Services;

/// <summary>
/// Bộ kiểm thử đơn vị cho CandidateEvaluationComparisonService (SCRUM-336 / Scrum - Số 58).
/// Kiểm thử các quy tắc nghiệp vụ:
/// 1. Chỉ hiện ứng viên có tối thiểu 1 phiếu đánh giá đã nộp (IsSubmitted == true).
/// 2. Sắp xếp mặc định theo điểm tổng giảm dần (Rank #1 là Top candidate).
/// 3. Tính toán chính xác điểm từng tiêu chí và điểm trọng số.
/// </summary>
public class CandidateEvaluationComparisonServiceTests
{
    private readonly Mock<IEvaluationCriteriaService> _criteriaServiceMock;
    private readonly NullLogger<CandidateEvaluationComparisonService> _logger;

    public CandidateEvaluationComparisonServiceTests()
    {
        _criteriaServiceMock = new Mock<IEvaluationCriteriaService>();
        _logger = NullLogger<CandidateEvaluationComparisonService>.Instance;
    }

    [Fact]
    public async Task GetEvaluationComparisonAsync_OnlyShowsCandidatesWithSubmittedEvaluations()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemoryDbContext();

        var job = new JobPosting
        {
            Id = Guid.NewGuid(),
            Title = "Senior .NET Engineer",
            Status = JobPostingStatus.PUBLISHED,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.JobPostings.AddAsync(job);

        var stage = new PipelineStage
        {
            Id = Guid.NewGuid(),
            Name = "Phỏng vấn",
            StageOrder = 3,
            ColorCode = "#059669"
        };
        await context.PipelineStages.AddAsync(stage);

        var crit = new EvaluationCriteria
        {
            Id = Guid.NewGuid(),
            Name = "Clean Architecture",
            CriteriaType = "HARD_SKILL",
            Weight = 1.5m,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.EvaluationCriterias.AddAsync(crit);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "interviewer@noveratech.vn",
            FullName = "Nguyễn Văn Giám Khảo",
            PasswordHash = "hash",
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.Users.AddAsync(user);

        // 1. Ứng viên 1: Có phiếu đã nộp (HỢP LỆ)
        var cand1 = new Candidate { Id = Guid.NewGuid(), FirstName = "Tuấn", LastName = "Lê", Email = "tuan@gmail.com", CreatedAt = DateTimeOffset.UtcNow };
        var resume1 = new Resume { Id = Guid.NewGuid(), CandidateId = cand1.Id, FileName = "cv.pdf", FilePath = "/cv.pdf", CreatedAt = DateTimeOffset.UtcNow };
        var app1 = new Application { Id = Guid.NewGuid(), CandidateId = cand1.Id, Candidate = cand1, JobPostingId = job.Id, JobPosting = job, ResumeId = resume1.Id, Resume = resume1, CurrentStageId = stage.Id, CurrentStage = stage, Status = ApplicationStatus.IN_PROCESS, CreatedAt = DateTimeOffset.UtcNow };
        var interview1 = new Interview { Id = Guid.NewGuid(), ApplicationId = app1.Id, Application = app1, RoundNumber = 2, Title = "Vòng 2", Status = InterviewStatus.COMPLETED, CreatedAt = DateTimeOffset.UtcNow };
        var eval1 = new InterviewEvaluation { Id = Guid.NewGuid(), InterviewId = interview1.Id, Interview = interview1, InterviewerId = user.Id, Interviewer = user, OverallScore = 4.5m, Recommendation = RecommendationType.HIRE, IsSubmitted = true, SubmittedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow };
        interview1.Evaluations.Add(eval1);
        app1.Interviews.Add(interview1);

        // 2. Ứng viên 2: Có phiếu nhưng CHƯA NỘP (Draft - IsSubmitted = false) -> KHÔNG ĐƯỢC HIỆN
        var cand2 = new Candidate { Id = Guid.NewGuid(), FirstName = "Hải", LastName = "Phan", Email = "hai@gmail.com", CreatedAt = DateTimeOffset.UtcNow };
        var resume2 = new Resume { Id = Guid.NewGuid(), CandidateId = cand2.Id, FileName = "cv2.pdf", FilePath = "/cv2.pdf", CreatedAt = DateTimeOffset.UtcNow };
        var app2 = new Application { Id = Guid.NewGuid(), CandidateId = cand2.Id, Candidate = cand2, JobPostingId = job.Id, JobPosting = job, ResumeId = resume2.Id, Resume = resume2, CurrentStageId = stage.Id, CurrentStage = stage, Status = ApplicationStatus.IN_PROCESS, CreatedAt = DateTimeOffset.UtcNow };
        var interview2 = new Interview { Id = Guid.NewGuid(), ApplicationId = app2.Id, Application = app2, RoundNumber = 1, Title = "Vòng 1", Status = InterviewStatus.SCHEDULED, CreatedAt = DateTimeOffset.UtcNow };
        var eval2 = new InterviewEvaluation { Id = Guid.NewGuid(), InterviewId = interview2.Id, Interview = interview2, InterviewerId = user.Id, Interviewer = user, OverallScore = 3.5m, Recommendation = RecommendationType.CONSIDER, IsSubmitted = false, CreatedAt = DateTimeOffset.UtcNow };
        interview2.Evaluations.Add(eval2);
        app2.Interviews.Add(interview2);

        // 3. Ứng viên 3: Chưa có buổi phỏng vấn hay phiếu nào -> KHÔNG ĐƯỢC HIỆN
        var cand3 = new Candidate { Id = Guid.NewGuid(), FirstName = "Nga", LastName = "Đinh", Email = "nga@gmail.com", CreatedAt = DateTimeOffset.UtcNow };
        var resume3 = new Resume { Id = Guid.NewGuid(), CandidateId = cand3.Id, FileName = "cv3.pdf", FilePath = "/cv3.pdf", CreatedAt = DateTimeOffset.UtcNow };
        var app3 = new Application { Id = Guid.NewGuid(), CandidateId = cand3.Id, Candidate = cand3, JobPostingId = job.Id, JobPosting = job, ResumeId = resume3.Id, Resume = resume3, CurrentStageId = stage.Id, CurrentStage = stage, Status = ApplicationStatus.IN_PROCESS, CreatedAt = DateTimeOffset.UtcNow };

        // 4. Ứng viên 4: Có phiếu nộp nhưng ĐÃ RÚT HỒ SƠ (WITHDRAWN) -> KHÔNG ĐƯỢC HIỆN
        var cand4 = new Candidate { Id = Guid.NewGuid(), FirstName = "Thắng", LastName = "Vũ", Email = "thang@gmail.com", CreatedAt = DateTimeOffset.UtcNow };
        var resume4 = new Resume { Id = Guid.NewGuid(), CandidateId = cand4.Id, FileName = "cv4.pdf", FilePath = "/cv4.pdf", CreatedAt = DateTimeOffset.UtcNow };
        var app4 = new Application { Id = Guid.NewGuid(), CandidateId = cand4.Id, Candidate = cand4, JobPostingId = job.Id, JobPosting = job, ResumeId = resume4.Id, Resume = resume4, CurrentStageId = stage.Id, CurrentStage = stage, Status = ApplicationStatus.WITHDRAWN, CreatedAt = DateTimeOffset.UtcNow };
        var interview4 = new Interview { Id = Guid.NewGuid(), ApplicationId = app4.Id, Application = app4, RoundNumber = 1, Title = "Vòng 1", Status = InterviewStatus.COMPLETED, CreatedAt = DateTimeOffset.UtcNow };
        var eval4 = new InterviewEvaluation { Id = Guid.NewGuid(), InterviewId = interview4.Id, Interview = interview4, InterviewerId = user.Id, Interviewer = user, OverallScore = 4.0m, Recommendation = RecommendationType.HIRE, IsSubmitted = true, SubmittedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow };
        interview4.Evaluations.Add(eval4);
        app4.Interviews.Add(interview4);

        await context.Candidates.AddRangeAsync(cand1, cand2, cand3, cand4);
        await context.Resumes.AddRangeAsync(resume1, resume2, resume3, resume4);
        await context.Applications.AddRangeAsync(app1, app2, app3, app4);
        await context.Interviews.AddRangeAsync(interview1, interview2, interview4);
        await context.InterviewEvaluations.AddRangeAsync(eval1, eval2, eval4);
        await context.SaveChangesAsync();

        var service = new CandidateEvaluationComparisonService(context, _criteriaServiceMock.Object, _logger);

        // Act
        var result = await service.GetEvaluationComparisonAsync(job.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Candidates);
        Assert.Equal(cand1.Id, result.Candidates[0].CandidateId);
        Assert.Equal("Tuấn Lê", result.Candidates[0].CandidateName);
        Assert.Equal(4.5m, result.Candidates[0].OverallScore);
        Assert.True(result.Candidates[0].IsTopCandidate);
    }

    [Fact]
    public async Task GetEvaluationComparisonAsync_SortsByTotalScoreDescendingByDefault()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemoryDbContext();

        var job = new JobPosting { Id = Guid.NewGuid(), Title = "Product Designer", Status = JobPostingStatus.PUBLISHED, CreatedAt = DateTimeOffset.UtcNow };
        var stage = new PipelineStage { Id = Guid.NewGuid(), Name = "Vòng cuối", StageOrder = 4, CreatedAt = DateTimeOffset.UtcNow };
        var user = new User { Id = Guid.NewGuid(), Email = "panelist@noveratech.vn", FullName = "Giám Khảo", PasswordHash = "hash", CreatedAt = DateTimeOffset.UtcNow };

        await context.JobPostings.AddAsync(job);
        await context.PipelineStages.AddAsync(stage);
        await context.Users.AddAsync(user);

        // Tạo 3 ứng viên với điểm 3.8, 4.9, 4.2
        var data = new[]
        {
            (Name: "Ứng Viên B (Thấp)", Score: 3.8m, Email: "b@gmail.com"),
            (Name: "Ứng Viên A (Cao Nhất)", Score: 4.9m, Email: "a@gmail.com"),
            (Name: "Ứng Viên C (Trung Bình)", Score: 4.2m, Email: "c@gmail.com")
        };

        foreach (var item in data)
        {
            var cand = new Candidate { Id = Guid.NewGuid(), FirstName = item.Name, LastName = "", Email = item.Email, CreatedAt = DateTimeOffset.UtcNow };
            var res = new Resume { Id = Guid.NewGuid(), CandidateId = cand.Id, FileName = "cv.pdf", FilePath = "/cv.pdf", CreatedAt = DateTimeOffset.UtcNow };
            var app = new Application { Id = Guid.NewGuid(), CandidateId = cand.Id, Candidate = cand, JobPostingId = job.Id, JobPosting = job, ResumeId = res.Id, Resume = res, CurrentStageId = stage.Id, CurrentStage = stage, Status = ApplicationStatus.IN_PROCESS, CreatedAt = DateTimeOffset.UtcNow };
            var itv = new Interview { Id = Guid.NewGuid(), ApplicationId = app.Id, Application = app, RoundNumber = 2, Title = "Phỏng vấn cuối", Status = InterviewStatus.COMPLETED, CreatedAt = DateTimeOffset.UtcNow };
            var ev = new InterviewEvaluation { Id = Guid.NewGuid(), InterviewId = itv.Id, Interview = itv, InterviewerId = user.Id, Interviewer = user, OverallScore = item.Score, Recommendation = RecommendationType.HIRE, IsSubmitted = true, SubmittedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow };
            itv.Evaluations.Add(ev);
            app.Interviews.Add(itv);

            await context.Candidates.AddAsync(cand);
            await context.Resumes.AddAsync(res);
            await context.Applications.AddAsync(app);
            await context.Interviews.AddAsync(itv);
            await context.InterviewEvaluations.AddAsync(ev);
        }

        await context.SaveChangesAsync();

        var service = new CandidateEvaluationComparisonService(context, _criteriaServiceMock.Object, _logger);

        // Act
        var result = await service.GetEvaluationComparisonAsync(job.Id, sortBy: "total_desc");

        // Assert
        Assert.Equal(3, result.Candidates.Count);

        // Xếp hạng 1: Điểm 4.9
        Assert.Equal(1, result.Candidates[0].Rank);
        Assert.Equal(4.9m, result.Candidates[0].OverallScore);
        Assert.Equal("Ứng Viên A (Cao Nhất)", result.Candidates[0].CandidateName);
        Assert.True(result.Candidates[0].IsTopCandidate);

        // Xếp hạng 2: Điểm 4.2
        Assert.Equal(2, result.Candidates[1].Rank);
        Assert.Equal(4.2m, result.Candidates[1].OverallScore);
        Assert.Equal("Ứng Viên C (Trung Bình)", result.Candidates[1].CandidateName);
        Assert.False(result.Candidates[1].IsTopCandidate);

        // Xếp hạng 3: Điểm 3.8
        Assert.Equal(3, result.Candidates[2].Rank);
        Assert.Equal(3.8m, result.Candidates[2].OverallScore);
        Assert.Equal("Ứng Viên B (Thấp)", result.Candidates[2].CandidateName);
        Assert.False(result.Candidates[2].IsTopCandidate);

        // Kiểm tra thống kê Header
        Assert.Equal(4.9m, result.HighestScore);
        Assert.Equal(4.3m, result.AverageScore); // (4.9 + 4.2 + 3.8) / 3 = 4.3
        Assert.Equal("Ứng Viên A (Cao Nhất)", result.TopCandidateName);
    }

    [Fact]
    public async Task GetEvaluationComparisonAsync_AggregatesCriteriaScoresAndWeightedScore()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemoryDbContext();

        var job = new JobPosting { Id = Guid.NewGuid(), Title = "Backend Lead", Status = JobPostingStatus.PUBLISHED, CreatedAt = DateTimeOffset.UtcNow };
        var stage = new PipelineStage { Id = Guid.NewGuid(), Name = "Offer", StageOrder = 5, CreatedAt = DateTimeOffset.UtcNow };
        var user = new User { Id = Guid.NewGuid(), Email = "lead@noveratech.vn", FullName = "Tech Lead", PasswordHash = "hash", CreatedAt = DateTimeOffset.UtcNow };

        var crit1 = new EvaluationCriteria { Id = Guid.NewGuid(), Name = "System Design", CriteriaType = "HARD_SKILL", Weight = 2.0m, CreatedAt = DateTimeOffset.UtcNow };
        var crit2 = new EvaluationCriteria { Id = Guid.NewGuid(), Name = "Teamwork", CriteriaType = "SOFT_SKILL", Weight = 1.0m, CreatedAt = DateTimeOffset.UtcNow };

        await context.JobPostings.AddAsync(job);
        await context.PipelineStages.AddAsync(stage);
        await context.Users.AddAsync(user);
        await context.EvaluationCriterias.AddRangeAsync(crit1, crit2);

        var cand = new Candidate { Id = Guid.NewGuid(), FirstName = "Hoàng", LastName = "Minh", Email = "hoang@gmail.com", CreatedAt = DateTimeOffset.UtcNow };
        var res = new Resume { Id = Guid.NewGuid(), CandidateId = cand.Id, FileName = "cv.pdf", FilePath = "/cv.pdf", CreatedAt = DateTimeOffset.UtcNow };
        var app = new Application { Id = Guid.NewGuid(), CandidateId = cand.Id, Candidate = cand, JobPostingId = job.Id, JobPosting = job, ResumeId = res.Id, Resume = res, CurrentStageId = stage.Id, CurrentStage = stage, Status = ApplicationStatus.IN_PROCESS, CreatedAt = DateTimeOffset.UtcNow };
        var itv = new Interview { Id = Guid.NewGuid(), ApplicationId = app.Id, Application = app, RoundNumber = 2, Title = "Final Round", Status = InterviewStatus.COMPLETED, CreatedAt = DateTimeOffset.UtcNow };
        var eval = new InterviewEvaluation { Id = Guid.NewGuid(), InterviewId = itv.Id, Interview = itv, InterviewerId = user.Id, Interviewer = user, OverallScore = 4.5m, Recommendation = RecommendationType.STRONG_HIRE, IsSubmitted = true, SubmittedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow };

        // Điểm tiêu chí: System Design = 5 (w=2.0), Teamwork = 4 (w=1.0)
        // Weighted score = (5 * 2.0 + 4 * 1.0) / (2.0 + 1.0) = 14 / 3 = 4.67
        var score1 = new EvaluationScore { Id = Guid.NewGuid(), EvaluationId = eval.Id, Evaluation = eval, CriteriaId = crit1.Id, Criteria = crit1, Score = 5 };
        var score2 = new EvaluationScore { Id = Guid.NewGuid(), EvaluationId = eval.Id, Evaluation = eval, CriteriaId = crit2.Id, Criteria = crit2, Score = 4 };
        eval.Scores.Add(score1);
        eval.Scores.Add(score2);
        itv.Evaluations.Add(eval);
        app.Interviews.Add(itv);

        await context.Candidates.AddAsync(cand);
        await context.Resumes.AddAsync(res);
        await context.Applications.AddAsync(app);
        await context.Interviews.AddAsync(itv);
        await context.InterviewEvaluations.AddAsync(eval);
        await context.EvaluationScores.AddRangeAsync(score1, score2);
        await context.SaveChangesAsync();

        var service = new CandidateEvaluationComparisonService(context, _criteriaServiceMock.Object, _logger);

        // Act
        var result = await service.GetEvaluationComparisonAsync(job.Id);

        // Assert
        Assert.Single(result.Candidates);
        var candRow = result.Candidates[0];
        Assert.Equal(5.0m, candRow.CriteriaScores[crit1.Id]);
        Assert.Equal(4.0m, candRow.CriteriaScores[crit2.Id]);
        Assert.Equal(4.67m, candRow.WeightedScore);
        Assert.Equal(4.5m, candRow.OverallScore);
        Assert.Equal("STRONG_HIRE", candRow.RecommendationKey);
    }

    [Fact]
    public async Task GetEvaluationComparisonAsync_EnforcesSevenComparisonRules()
    {
        // Arrange: Kiểm thử đầy đủ 7 quy tắc thiết kế mới của SCRUM-336
        using var context = TestDbContextFactory.CreateInMemoryDbContext();

        var job = new JobPosting { Id = Guid.NewGuid(), Title = "Senior .NET Backend Engineer", Status = JobPostingStatus.PUBLISHED, CreatedAt = DateTimeOffset.UtcNow };
        var stage = new PipelineStage { Id = Guid.NewGuid(), Name = "Phỏng vấn vòng cuối", StageOrder = 3, CreatedAt = DateTimeOffset.UtcNow };
        var user = new User { Id = Guid.NewGuid(), Email = "interviewer@noveratech.vn", FullName = "Hội đồng IT", PasswordHash = "hash", CreatedAt = DateTimeOffset.UtcNow };

        // Quy tắc 6: Tiêu chí dài ban đầu -> Rút gọn còn <= 4 từ (Architecture, .NET, SQL, Communication...)
        var critArch = new EvaluationCriteria { Id = Guid.NewGuid(), Name = "Kiến trúc hệ thống & Clean Architecture", CriteriaType = "HARD_SKILL", Weight = 1.5m, CreatedAt = DateTimeOffset.UtcNow };
        var critDotnet = new EvaluationCriteria { Id = Guid.NewGuid(), Name = "Lập trình C# .NET & Async Concurrency", CriteriaType = "HARD_SKILL", Weight = 1.5m, CreatedAt = DateTimeOffset.UtcNow };
        var critSql = new EvaluationCriteria { Id = Guid.NewGuid(), Name = "Thiết kế CSDL & SQL Optimization", CriteriaType = "HARD_SKILL", Weight = 1.2m, CreatedAt = DateTimeOffset.UtcNow };
        var critComm = new EvaluationCriteria { Id = Guid.NewGuid(), Name = "Giao tiếp chuyên môn & Phối hợp đội ngũ", CriteriaType = "SOFT_SKILL", Weight = 1.0m, CreatedAt = DateTimeOffset.UtcNow };

        await context.JobPostings.AddAsync(job);
        await context.PipelineStages.AddAsync(stage);
        await context.Users.AddAsync(user);
        await context.EvaluationCriterias.AddRangeAsync(critArch, critDotnet, critSql, critComm);

        // Ứng viên 1: Phan Văn Hải (Điểm 4.2 - SQL 5/5)
        var candHai = new Candidate { Id = Guid.NewGuid(), FirstName = "Phan Văn", LastName = "Hải", Email = "hai.phan@gmail.com", CreatedAt = DateTimeOffset.UtcNow };
        var resHai = new Resume { Id = Guid.NewGuid(), CandidateId = candHai.Id, FileName = "cv_hai.pdf", FilePath = "/cv_hai.pdf", CreatedAt = DateTimeOffset.UtcNow };
        var appHai = new Application { Id = Guid.NewGuid(), CandidateId = candHai.Id, Candidate = candHai, JobPostingId = job.Id, JobPosting = job, ResumeId = resHai.Id, Resume = resHai, CurrentStageId = stage.Id, CurrentStage = stage, Status = ApplicationStatus.IN_PROCESS, CreatedAt = DateTimeOffset.UtcNow };
        var itvHai = new Interview { Id = Guid.NewGuid(), ApplicationId = appHai.Id, Application = appHai, RoundNumber = 2, Title = "Vòng 2", Status = InterviewStatus.COMPLETED, CreatedAt = DateTimeOffset.UtcNow };
        var evalHai = new InterviewEvaluation
        {
            Id = Guid.NewGuid(),
            InterviewId = itvHai.Id,
            Interview = itvHai,
            InterviewerId = user.Id,
            Interviewer = user,
            OverallScore = 4.2m,
            Recommendation = RecommendationType.HIRE,
            Strengths = "Thiết kế cơ sở dữ liệu và tối ưu truy vấn SQL xuất sắc, xử lý indexing rất chuyên sâu.",
            Weaknesses = "Kinh nghiệm microservices event-driven cần hoàn thiện thêm.",
            IsSubmitted = true,
            SubmittedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
        itvHai.Evaluations.Add(evalHai);
        appHai.Interviews.Add(itvHai);

        var scoreHaiSql = new EvaluationScore { Id = Guid.NewGuid(), EvaluationId = evalHai.Id, Evaluation = evalHai, CriteriaId = critSql.Id, Criteria = critSql, Score = 5 };
        var scoreHaiArch = new EvaluationScore { Id = Guid.NewGuid(), EvaluationId = evalHai.Id, Evaluation = evalHai, CriteriaId = critArch.Id, Criteria = critArch, Score = 4 };
        evalHai.Scores.Add(scoreHaiSql);
        evalHai.Scores.Add(scoreHaiArch);

        // Ứng viên 2: Đinh Thúy Nga (Điểm 3.8 - Chênh lệch 4.2 - 3.8 = +0.4)
        var candNga = new Candidate { Id = Guid.NewGuid(), FirstName = "Đinh Thúy", LastName = "Nga", Email = "nga.dinh@gmail.com", CreatedAt = DateTimeOffset.UtcNow };
        var resNga = new Resume { Id = Guid.NewGuid(), CandidateId = candNga.Id, FileName = "cv_nga.pdf", FilePath = "/cv_nga.pdf", CreatedAt = DateTimeOffset.UtcNow };
        var appNga = new Application { Id = Guid.NewGuid(), CandidateId = candNga.Id, Candidate = candNga, JobPostingId = job.Id, JobPosting = job, ResumeId = resNga.Id, Resume = resNga, CurrentStageId = stage.Id, CurrentStage = stage, Status = ApplicationStatus.IN_PROCESS, CreatedAt = DateTimeOffset.UtcNow };
        var itvNga = new Interview { Id = Guid.NewGuid(), ApplicationId = appNga.Id, Application = appNga, RoundNumber = 2, Title = "Vòng 2", Status = InterviewStatus.COMPLETED, CreatedAt = DateTimeOffset.UtcNow };
        var evalNga = new InterviewEvaluation
        {
            Id = Guid.NewGuid(),
            InterviewId = itvNga.Id,
            Interview = itvNga,
            InterviewerId = user.Id,
            Interviewer = user,
            OverallScore = 3.8m,
            Recommendation = RecommendationType.CONSIDER,
            Strengths = "Tư duy thuật toán sáng sủa, giao tiếp cởi mở.",
            Weaknesses = "Kinh nghiệm thực chiến bài toán chịu tải cao còn hạn chế.",
            IsSubmitted = true,
            SubmittedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
        itvNga.Evaluations.Add(evalNga);
        appNga.Interviews.Add(itvNga);

        await context.Candidates.AddRangeAsync(candHai, candNga);
        await context.Resumes.AddRangeAsync(resHai, resNga);
        await context.Applications.AddRangeAsync(appHai, appNga);
        await context.Interviews.AddRangeAsync(itvHai, itvNga);
        await context.InterviewEvaluations.AddRangeAsync(evalHai, evalNga);
        await context.EvaluationScores.AddRangeAsync(scoreHaiSql, scoreHaiArch);
        await context.SaveChangesAsync();

        var service = new CandidateEvaluationComparisonService(context, _criteriaServiceMock.Object, _logger);

        // Act
        var result = await service.GetEvaluationComparisonAsync(job.Id);

        // Assert
        // Quy tắc 6: Tiêu chí rút gọn tối đa 4 từ
        var archHeader = result.CriteriaHeaders.First(c => c.Id == critArch.Id);
        Assert.Equal("Architecture", archHeader.DisplayTitle);
        var dotnetHeader = result.CriteriaHeaders.First(c => c.Id == critDotnet.Id);
        Assert.Equal(".NET", dotnetHeader.DisplayTitle);
        var sqlHeader = result.CriteriaHeaders.First(c => c.Id == critSql.Id);
        Assert.Equal("SQL", sqlHeader.DisplayTitle);
        var commHeader = result.CriteriaHeaders.First(c => c.Id == critComm.Id);
        Assert.Equal("Communication", commHeader.DisplayTitle);

        // Quy tắc 7: Luôn trả lời câu hỏi "Tôi nên làm gì?" ở đầu trang
        Assert.NotNull(result.TopRecommendation);
        Assert.Equal("Khuyến nghị: Tuyển Phan Văn Hải", result.TopRecommendation.ActionHeadline);
        Assert.Equal("+0.4", result.TopRecommendation.ScoreDifferenceText);
        Assert.Contains("SQL", result.TopRecommendation.KeyAdvantages);

        // Quy tắc 1: Ưu tiên quyết định thay vì dữ liệu
        Assert.Equal("🏆 Ứng viên phù hợp nhất cho vị trí hiện tại", result.TopRecommendation.DecisionBadge);
        Assert.Equal("🏆 Ứng viên phù hợp nhất cho vị trí hiện tại", result.Candidates[0].DecisionTitle);

        // Quy tắc 3: Đúng 3 insight quan trọng nhất
        Assert.Equal(3, result.TopRecommendation.ThreeKeyInsights.Count);
        Assert.StartsWith("✅", result.TopRecommendation.ThreeKeyInsights[0]);
        Assert.StartsWith("✅", result.TopRecommendation.ThreeKeyInsights[1]);
        Assert.StartsWith("⚠", result.TopRecommendation.ThreeKeyInsights[2]);
        Assert.Contains("Event-Driven", result.TopRecommendation.ThreeKeyInsights[2]);

        // Quy tắc 5: Tất cả kết luận phải giải thích được ("Vì: • ...")
        Assert.NotEmpty(result.Candidates[0].DecisionReasons);
        Assert.Contains(result.Candidates[0].DecisionReasons, r => r.Contains("SQL"));
        Assert.Contains(result.Candidates[0].DecisionReasons, r => r.Contains("Top 1 điểm tổng"));

        // Quy tắc 4: Nhận xét tự nhiên và Highlight khác nhau theo ứng viên
        Assert.NotEmpty(result.Candidates[0].HighlightBadges);
        Assert.NotEmpty(result.Candidates[1].HighlightBadges);
        Assert.NotEqual(result.Candidates[0].HighlightBadges, result.Candidates[1].HighlightBadges);
        Assert.False(string.IsNullOrWhiteSpace(result.Candidates[0].NaturalCommentary));
        Assert.DoesNotContain("Điểm mạnh:", result.Candidates[0].NaturalCommentary);
        Assert.DoesNotContain("Cần cải thiện:", result.Candidates[0].NaturalCommentary);
    }

    [Fact]
    public async Task GetEvaluationComparisonAsync_GeneratesTransposedMatrixAndDeltaAnalysisAndEvaluatorBreakdown()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemoryDbContext();

        var job = new JobPosting { Id = Guid.NewGuid(), Title = "Backend Developer Senior", Status = JobPostingStatus.PUBLISHED, CreatedAt = DateTimeOffset.UtcNow };
        var stage = new PipelineStage { Id = Guid.NewGuid(), Name = "Final Interview", StageOrder = 4, CreatedAt = DateTimeOffset.UtcNow };
        var judgeA = new User { Id = Guid.NewGuid(), FullName = "Trần Văn Bình", Email = "binh@noveratech.vn", PasswordHash = "hash", CreatedAt = DateTimeOffset.UtcNow };
        var judgeB = new User { Id = Guid.NewGuid(), FullName = "Lê Quốc Hưng", Email = "hung@noveratech.vn", PasswordHash = "hash", CreatedAt = DateTimeOffset.UtcNow };

        var critDotnet = new EvaluationCriteria { Id = Guid.NewGuid(), Name = ".NET", CriteriaType = "HARD_SKILL", Weight = 1.0m, CreatedAt = DateTimeOffset.UtcNow };
        var critSql = new EvaluationCriteria { Id = Guid.NewGuid(), Name = "SQL", CriteriaType = "HARD_SKILL", Weight = 1.0m, CreatedAt = DateTimeOffset.UtcNow };
        var critArch = new EvaluationCriteria { Id = Guid.NewGuid(), Name = "Architecture", CriteriaType = "HARD_SKILL", Weight = 1.0m, CreatedAt = DateTimeOffset.UtcNow };

        await context.JobPostings.AddAsync(job);
        await context.PipelineStages.AddAsync(stage);
        await context.Users.AddRangeAsync(judgeA, judgeB);
        await context.EvaluationCriterias.AddRangeAsync(critDotnet, critSql, critArch);

        // Ứng viên 1: Hải (Điểm cao: 4.8)
        var candHai = new Candidate { Id = Guid.NewGuid(), FirstName = "Phan Văn", LastName = "Hải", Email = "hai@gmail.com", CreatedAt = DateTimeOffset.UtcNow };
        var resHai = new Resume { Id = Guid.NewGuid(), CandidateId = candHai.Id, FileName = "cv_hai.pdf", FilePath = "/cv_hai.pdf", CreatedAt = DateTimeOffset.UtcNow };
        var appHai = new Application { Id = Guid.NewGuid(), CandidateId = candHai.Id, Candidate = candHai, JobPostingId = job.Id, JobPosting = job, ResumeId = resHai.Id, Resume = resHai, CurrentStageId = stage.Id, CurrentStage = stage, Status = ApplicationStatus.IN_PROCESS, CreatedAt = DateTimeOffset.UtcNow };
        var itvHai = new Interview { Id = Guid.NewGuid(), ApplicationId = appHai.Id, Application = appHai, RoundNumber = 2, Title = "Final Interview", Status = InterviewStatus.COMPLETED, CreatedAt = DateTimeOffset.UtcNow };
        
        var evalHaiA = new InterviewEvaluation { Id = Guid.NewGuid(), InterviewId = itvHai.Id, Interview = itvHai, InterviewerId = judgeA.Id, Interviewer = judgeA, OverallScore = 4.8m, Recommendation = RecommendationType.STRONG_HIRE, IsSubmitted = true, SubmittedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow };
        var evalHaiB = new InterviewEvaluation { Id = Guid.NewGuid(), InterviewId = itvHai.Id, Interview = itvHai, InterviewerId = judgeB.Id, Interviewer = judgeB, OverallScore = 4.8m, Recommendation = RecommendationType.HIRE, IsSubmitted = true, SubmittedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow };
        itvHai.Evaluations.Add(evalHaiA);
        itvHai.Evaluations.Add(evalHaiB);
        appHai.Interviews.Add(itvHai);

        var scoreHaiDotnet = new EvaluationScore { Id = Guid.NewGuid(), EvaluationId = evalHaiA.Id, Evaluation = evalHaiA, CriteriaId = critDotnet.Id, Criteria = critDotnet, Score = 5 };
        var scoreHaiSql = new EvaluationScore { Id = Guid.NewGuid(), EvaluationId = evalHaiA.Id, Evaluation = evalHaiA, CriteriaId = critSql.Id, Criteria = critSql, Score = 5 };
        var scoreHaiArch = new EvaluationScore { Id = Guid.NewGuid(), EvaluationId = evalHaiA.Id, Evaluation = evalHaiA, CriteriaId = critArch.Id, Criteria = critArch, Score = 4 };
        evalHaiA.Scores.Add(scoreHaiDotnet);
        evalHaiA.Scores.Add(scoreHaiSql);
        evalHaiA.Scores.Add(scoreHaiArch);

        // Ứng viên 2: Nam (Điểm nhì: 4.2)
        var candNam = new Candidate { Id = Guid.NewGuid(), FirstName = "Lê Tuấn", LastName = "Nam", Email = "nam@gmail.com", CreatedAt = DateTimeOffset.UtcNow };
        var resNam = new Resume { Id = Guid.NewGuid(), CandidateId = candNam.Id, FileName = "cv_nam.pdf", FilePath = "/cv_nam.pdf", CreatedAt = DateTimeOffset.UtcNow };
        var appNam = new Application { Id = Guid.NewGuid(), CandidateId = candNam.Id, Candidate = candNam, JobPostingId = job.Id, JobPosting = job, ResumeId = resNam.Id, Resume = resNam, CurrentStageId = stage.Id, CurrentStage = stage, Status = ApplicationStatus.IN_PROCESS, CreatedAt = DateTimeOffset.UtcNow };
        var itvNam = new Interview { Id = Guid.NewGuid(), ApplicationId = appNam.Id, Application = appNam, RoundNumber = 2, Title = "Final Interview", Status = InterviewStatus.COMPLETED, CreatedAt = DateTimeOffset.UtcNow };
        
        var evalNamA = new InterviewEvaluation { Id = Guid.NewGuid(), InterviewId = itvNam.Id, Interview = itvNam, InterviewerId = judgeA.Id, Interviewer = judgeA, OverallScore = 4.2m, Recommendation = RecommendationType.HIRE, IsSubmitted = true, SubmittedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow };
        itvNam.Evaluations.Add(evalNamA);
        appNam.Interviews.Add(itvNam);

        var scoreNamDotnet = new EvaluationScore { Id = Guid.NewGuid(), EvaluationId = evalNamA.Id, Evaluation = evalNamA, CriteriaId = critDotnet.Id, Criteria = critDotnet, Score = 4 };
        var scoreNamSql = new EvaluationScore { Id = Guid.NewGuid(), EvaluationId = evalNamA.Id, Evaluation = evalNamA, CriteriaId = critSql.Id, Criteria = critSql, Score = 4 };
        var scoreNamArch = new EvaluationScore { Id = Guid.NewGuid(), EvaluationId = evalNamA.Id, Evaluation = evalNamA, CriteriaId = critArch.Id, Criteria = critArch, Score = 4 };
        evalNamA.Scores.Add(scoreNamDotnet);
        evalNamA.Scores.Add(scoreNamSql);
        evalNamA.Scores.Add(scoreNamArch);

        await context.Candidates.AddRangeAsync(candHai, candNam);
        await context.Resumes.AddRangeAsync(resHai, resNam);
        await context.Applications.AddRangeAsync(appHai, appNam);
        await context.Interviews.AddRangeAsync(itvHai, itvNam);
        await context.InterviewEvaluations.AddRangeAsync(evalHaiA, evalHaiB, evalNamA);
        await context.EvaluationScores.AddRangeAsync(scoreHaiDotnet, scoreHaiSql, scoreHaiArch, scoreNamDotnet, scoreNamSql, scoreNamArch);
        await context.SaveChangesAsync();

        var service = new CandidateEvaluationComparisonService(context, _criteriaServiceMock.Object, _logger);

        // Act
        var result = await service.GetEvaluationComparisonAsync(job.Id);

        // Assert
        // 1. Kiểm tra Ma trận chuyển vị (Transposed Matrix)
        Assert.NotEmpty(result.TransposedMatrix);
        Assert.Equal(3, result.TransposedMatrix.Count);
        
        var dotnetRow = result.TransposedMatrix.First(r => r.DisplayTitle == ".NET");
        Assert.True(dotnetRow.ScoresByCandidateId.ContainsKey(candHai.Id));
        Assert.True(dotnetRow.ScoresByCandidateId.ContainsKey(candNam.Id));
        Assert.Equal(5.0m, dotnetRow.ScoresByCandidateId[candHai.Id].Score);
        Assert.Equal(4.0m, dotnetRow.ScoresByCandidateId[candNam.Id].Score);
        Assert.True(dotnetRow.ScoresByCandidateId[candHai.Id].IsHighestInRow);
        Assert.True(dotnetRow.ScoresByCandidateId[candNam.Id].IsLowestInRow);

        // 2. Kiểm tra Xếp hạng (Rankings)
        Assert.Equal(2, result.Rankings.Count);
        Assert.Equal(1, result.Rankings[0].Rank);
        Assert.Equal("Phan Văn Hải", result.Rankings[0].CandidateName);
        Assert.True(result.Rankings[0].IsLeader);
        Assert.Equal(2, result.Rankings[1].Rank);
        Assert.Equal("Lê Tuấn Nam", result.Rankings[1].CandidateName);
        Assert.False(result.Rankings[1].IsLeader);

        // 3. Kiểm tra Chênh lệch ứng viên (Delta Analysis: Tại sao dẫn đầu?)
        Assert.NotNull(result.DeltaAnalysis);
        Assert.Equal("Phan Văn Hải", result.DeltaAnalysis.LeaderName);
        Assert.Equal("Lê Tuấn Nam", result.DeltaAnalysis.RunnerUpName);
        Assert.Equal(0.6m, result.DeltaAnalysis.TotalDifference);
        Assert.Equal("+0.60", result.DeltaAnalysis.TotalDifferenceText);
        Assert.NotEmpty(result.DeltaAnalysis.CriteriaDeltas);
        var sqlDelta = result.DeltaAnalysis.CriteriaDeltas.First(d => d.CriteriaName == "SQL");
        Assert.Equal("+1.0", sqlDelta.DeltaText);
        Assert.True(sqlDelta.IsAdvantage);

        // 4. Kiểm tra Chi tiết nguồn điểm đánh giá (Evaluator Breakdown - Chống thiên vị)
        Assert.Equal(2, result.EvaluatorBreakdowns.Count);
        var haiBreakdown = result.EvaluatorBreakdowns.First(b => b.CandidateId == candHai.Id);
        Assert.Equal(2, haiBreakdown.EvaluatorCount);
        Assert.Equal(2, haiBreakdown.Evaluators.Count);
        Assert.Equal(4.8m, haiBreakdown.AverageScore);
    }

    [Fact]
    public async Task GetEvaluationComparisonAsync_WithThresholdOnly_FiltersOutCandidatesBelowThreshold()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemoryDbContext();

        var job = new JobPosting { Id = Guid.NewGuid(), Title = "Backend Dev", Status = JobPostingStatus.PUBLISHED, CreatedAt = DateTimeOffset.UtcNow };
        var stage = new PipelineStage { Id = Guid.NewGuid(), Name = "Interview", StageOrder = 3, CreatedAt = DateTimeOffset.UtcNow };
        var judge = new User { Id = Guid.NewGuid(), FullName = "Giám Khảo", Email = "judge@noveratech.vn", PasswordHash = "hash", CreatedAt = DateTimeOffset.UtcNow };

        await context.JobPostings.AddAsync(job);
        await context.PipelineStages.AddAsync(stage);
        await context.Users.AddAsync(judge);

        // Ứng viên 1: Đạt ngưỡng (4.5)
        var candPass = new Candidate { Id = Guid.NewGuid(), FirstName = "Đạt", LastName = "Ngưỡng", Email = "pass@gmail.com", CreatedAt = DateTimeOffset.UtcNow };
        var resPass = new Resume { Id = Guid.NewGuid(), CandidateId = candPass.Id, FileName = "cv1.pdf", FilePath = "/cv1.pdf", CreatedAt = DateTimeOffset.UtcNow };
        var appPass = new Application { Id = Guid.NewGuid(), CandidateId = candPass.Id, Candidate = candPass, JobPostingId = job.Id, JobPosting = job, ResumeId = resPass.Id, Resume = resPass, CurrentStageId = stage.Id, CurrentStage = stage, Status = ApplicationStatus.IN_PROCESS, CreatedAt = DateTimeOffset.UtcNow };
        var itvPass = new Interview { Id = Guid.NewGuid(), ApplicationId = appPass.Id, Application = appPass, RoundNumber = 1, Title = "Vòng 1", Status = InterviewStatus.COMPLETED, CreatedAt = DateTimeOffset.UtcNow };
        var evalPass = new InterviewEvaluation { Id = Guid.NewGuid(), InterviewId = itvPass.Id, Interview = itvPass, InterviewerId = judge.Id, Interviewer = judge, OverallScore = 4.5m, Recommendation = RecommendationType.HIRE, IsSubmitted = true, SubmittedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow };
        itvPass.Evaluations.Add(evalPass);
        appPass.Interviews.Add(itvPass);

        // Ứng viên 2: Dưới ngưỡng (3.5)
        var candFail = new Candidate { Id = Guid.NewGuid(), FirstName = "Dưới", LastName = "Ngưỡng", Email = "fail@gmail.com", CreatedAt = DateTimeOffset.UtcNow };
        var resFail = new Resume { Id = Guid.NewGuid(), CandidateId = candFail.Id, FileName = "cv2.pdf", FilePath = "/cv2.pdf", CreatedAt = DateTimeOffset.UtcNow };
        var appFail = new Application { Id = Guid.NewGuid(), CandidateId = candFail.Id, Candidate = candFail, JobPostingId = job.Id, JobPosting = job, ResumeId = resFail.Id, Resume = resFail, CurrentStageId = stage.Id, CurrentStage = stage, Status = ApplicationStatus.IN_PROCESS, CreatedAt = DateTimeOffset.UtcNow };
        var itvFail = new Interview { Id = Guid.NewGuid(), ApplicationId = appFail.Id, Application = appFail, RoundNumber = 1, Title = "Vòng 1", Status = InterviewStatus.COMPLETED, CreatedAt = DateTimeOffset.UtcNow };
        var evalFail = new InterviewEvaluation { Id = Guid.NewGuid(), InterviewId = itvFail.Id, Interview = itvFail, InterviewerId = judge.Id, Interviewer = judge, OverallScore = 3.5m, Recommendation = RecommendationType.CONSIDER, IsSubmitted = true, SubmittedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow };
        itvFail.Evaluations.Add(evalFail);
        appFail.Interviews.Add(itvFail);

        await context.Candidates.AddRangeAsync(candPass, candFail);
        await context.Resumes.AddRangeAsync(resPass, resFail);
        await context.Applications.AddRangeAsync(appPass, appFail);
        await context.Interviews.AddRangeAsync(itvPass, itvFail);
        await context.InterviewEvaluations.AddRangeAsync(evalPass, evalFail);
        await context.SaveChangesAsync();

        var service = new CandidateEvaluationComparisonService(context, _criteriaServiceMock.Object, _logger);

        // Act: Kích hoạt bộ lọc thresholdOnly = true với ngưỡng 4.0
        var result = await service.GetEvaluationComparisonAsync(job.Id, thresholdOnly: true, minScore: 4.0m);

        // Assert
        Assert.Single(result.Candidates);
        Assert.Equal("Đạt Ngưỡng", result.Candidates[0].CandidateName);
        Assert.Equal(4.5m, result.Candidates[0].OverallScore);
    }
}
