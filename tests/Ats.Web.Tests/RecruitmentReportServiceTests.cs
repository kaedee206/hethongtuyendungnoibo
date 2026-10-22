using Ats.Web.Models.DTOs;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ats.Web.Tests;

/// <summary>
/// Unit tests cho Báo cáo Phễu tuyển dụng & Tỷ lệ chuyển đổi cho Trưởng phòng Nhân sự (SCRUM-352).
/// </summary>
public class RecruitmentReportServiceTests
{
    [Fact]
    public async Task GetRecruitmentFunnelReportAsync_ShouldReturnFiveFunnelStages()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var service = new RecruitmentReportService(context, NullLogger<RecruitmentReportService>.Instance);

        // Act
        var report = await service.GetRecruitmentFunnelReportAsync(new FunnelFilterCriteriaDto());

        // Assert
        Assert.NotNull(report);
        Assert.Equal(5, report.Stages.Count);
        Assert.Equal("Ứng tuyển mới", report.Stages[0].StageName);
        Assert.Equal("Sàng lọc hồ sơ", report.Stages[1].StageName);
        Assert.Equal("Phỏng vấn", report.Stages[2].StageName);
        Assert.Equal("Đề xuất nhận việc (Offer)", report.Stages[3].StageName);
        Assert.Equal("Tuyển dụng thành công", report.Stages[4].StageName);
    }

    [Fact]
    public async Task GetRecruitmentFunnelReportAsync_ShouldCalculateConversionAndBottleneckCorrectly()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var service = new RecruitmentReportService(context, NullLogger<RecruitmentReportService>.Instance);

        var dept = new Department { Id = Guid.NewGuid(), Name = "Phòng Kỹ thuật", Code = "IT" };
        context.Departments.Add(dept);

        var req = new JobRequisition { Id = Guid.NewGuid(), Code = "REQ-01", DepartmentId = dept.Id };
        context.JobRequisitions.Add(req);

        var job = new JobPosting { Id = Guid.NewGuid(), Title = "Senior .NET Developer", Slug = "senior-dotnet", RequisitionId = req.Id };
        context.JobPostings.Add(job);

        var stage1 = new PipelineStage { Id = Guid.NewGuid(), Name = "Ứng tuyển mới", StageOrder = 1 };
        var stage2 = new PipelineStage { Id = Guid.NewGuid(), Name = "Sàng lọc CV", StageOrder = 2 };
        var stage3 = new PipelineStage { Id = Guid.NewGuid(), Name = "Phỏng vấn", StageOrder = 3 };
        var stage5 = new PipelineStage { Id = Guid.NewGuid(), Name = "Offer", StageOrder = 5 };
        var stage6 = new PipelineStage { Id = Guid.NewGuid(), Name = "Hired", StageOrder = 6 };
        context.PipelineStages.AddRange(stage1, stage2, stage3, stage5, stage6);

        // Giả lập 10 hồ sơ ứng tuyển:
        // - 10 hồ sơ ở vòng 1 (Ứng tuyển mới)
        // - 8 hồ sơ qua vòng 2 (Sàng lọc)
        // - 2 hồ sơ qua vòng 3 (Phỏng vấn) -> Điểm rơi rụng lớn nhất: 8 -> 2 (mất 6 người, drop-off 75%)
        // - 2 hồ sơ qua vòng 4 (Offer)
        // - 1 hồ sơ qua vòng 5 (Tuyển dụng thành công)
        for (int i = 0; i < 10; i++)
        {
            var app = new Application
            {
                Id = Guid.NewGuid(),
                JobPostingId = job.Id,
                CandidateId = Guid.NewGuid(),
                ResumeId = Guid.NewGuid(),
                CurrentStageId = stage1.Id,
                CurrentStage = stage1,
                Status = ApplicationStatus.IN_PROCESS,
                AppliedAt = DateTimeOffset.UtcNow
            };

            if (i < 8)
            {
                app.CurrentStageId = stage2.Id;
                app.CurrentStage = stage2;
            }
            if (i < 2)
            {
                app.CurrentStageId = stage3.Id;
                app.CurrentStage = stage3;
                app.Interviews.Add(new Interview { Id = Guid.NewGuid() });
                app.JobOffers.Add(new JobOffer { Id = Guid.NewGuid() });
            }
            if (i < 1)
            {
                app.CurrentStageId = stage6.Id;
                app.CurrentStage = stage6;
                app.Status = ApplicationStatus.HIRED;
            }

            context.Applications.Add(app);
        }

        await context.SaveChangesAsync();

        // Act
        var report = await service.GetRecruitmentFunnelReportAsync(new FunnelFilterCriteriaDto
        {
            JobPostingId = job.Id
        });

        // Assert
        Assert.Equal(10, report.TotalApplications);
        Assert.Equal(1, report.TotalHired);
        Assert.Equal(10.0, report.OverallConversionRate);

        Assert.Equal(10, report.Stages[0].CandidateCount); // Ứng tuyển: 10
        Assert.Equal(8, report.Stages[1].CandidateCount);  // Sàng lọc: 8
        Assert.Equal(2, report.Stages[2].CandidateCount);  // Phỏng vấn: 2
        Assert.Equal(2, report.Stages[3].CandidateCount);  // Offer: 2
        Assert.Equal(1, report.Stages[4].CandidateCount);  // Hired: 1

        // Chuyển từ Sàng lọc (8) sang Phỏng vấn (2) có drop-off: 6 người, tỷ lệ rơi rụng = 75%
        var interviewStage = report.Stages[2];
        Assert.Equal(6, interviewStage.DropOffCount);
        Assert.Equal(75.0, interviewStage.DropOffRate);
        Assert.True(interviewStage.IsBottleneck, "Giai đoạn Phỏng vấn phải được đánh dấu là điểm nghẽn lớn nhất");

        Assert.Equal("Phỏng vấn", report.HighestDropOffStageName);
        Assert.Equal(75.0, report.HighestDropOffRate);
        Assert.Equal(6, report.HighestDropOffCount);
        Assert.Contains("Phỏng vấn", report.BottleneckInsight);
    }
}
