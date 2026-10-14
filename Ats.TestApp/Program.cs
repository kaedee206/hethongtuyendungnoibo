using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Ats.Web.Data;
using Ats.Web.Models.DTOs.ApprovalRule;
using Ats.Web.Services;

class TestProgram
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Bắt đầu tự động test ApprovalRuleService...");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=ats_db;Username=postgres;Password=postgres")
            .Options;

        using var dbContext = new ApplicationDbContext(options);
        var service = new ApprovalRuleService(dbContext);
        
        var hrRoleId = Guid.NewGuid(); // giả lập Role HR
        var adminUserId = Guid.NewGuid();

        // TEST 1: Tạo Rule rỗng bị lỗi
        Console.WriteLine("Test 1: Tạo Rule rỗng bị chặn (không có người duyệt)...");
        var badDto = new CreateApprovalRuleDto
        {
            Name = "Test Fail",
            MinSalary = 0,
            Steps = new System.Collections.Generic.List<CreateApprovalRuleStepDto>()
        };
        var res1 = await service.CreateRuleAsync(badDto, adminUserId);
        Console.WriteLine($"Kỳ vọng IsSuccess=False. Kết quả: {res1.IsSuccess}, Message: {res1.Message}");

        // TEST 2: Tạo Rule hợp lệ (có cấp duyệt)
        Console.WriteLine("\nTest 2: Tạo Rule hợp lệ...");
        var goodDto = new CreateApprovalRuleDto
        {
            Name = "Quy trình HR",
            MinSalary = 10000000, // 10 triệu
            Steps = new System.Collections.Generic.List<CreateApprovalRuleStepDto>
            {
                new CreateApprovalRuleStepDto { StepOrder = 1, ApproverRoleId = hrRoleId }
            }
        };
        var res2 = await service.CreateRuleAsync(goodDto, adminUserId);
        Console.WriteLine($"Kỳ vọng IsSuccess=True. Kết quả: {res2.IsSuccess}");
        
        if (res2.IsSuccess)
        {
            var ruleId = res2.Data.Id;
            Console.WriteLine($"Đã tạo Rule ID: {ruleId}");

            // TEST 3: Cập nhật Max Salary
            Console.WriteLine("\nTest 3: Cập nhật Rule (thêm MaxSalary)...");
            var updateDto = new UpdateApprovalRuleDto
            {
                Name = "Quy trình HR (Update)",
                MinSalary = 10000000,
                MaxSalary = 50000000,
                IsActive = true,
                Steps = new System.Collections.Generic.List<CreateApprovalRuleStepDto>
                {
                    new CreateApprovalRuleStepDto { StepOrder = 1, ApproverRoleId = hrRoleId }
                }
            };
            var res3 = await service.UpdateRuleAsync(ruleId, updateDto, adminUserId);
            Console.WriteLine($"Kỳ vọng cập nhật IsSuccess=True. Kết quả: {res3.IsSuccess}");

            // TEST 4: GetApplicableSteps
            Console.WriteLine("\nTest 4: Kiểm tra tự động chọn đúng Rule dựa theo mức lương...");
            var res4 = await service.GetApplicableStepsAsync(null, 25000000); // 25 triệu nằm trong khoảng 10tr - 50tr
            if (res4.IsSuccess && res4.Data.Count > 0)
            {
                Console.WriteLine($"Lấy thành công {res4.Data.Count} cấp duyệt cho mức lương 25tr.");
            }
            else
            {
                Console.WriteLine("Lỗi: Không tìm thấy cấp duyệt nào cho 25tr.");
            }

            // TEST 5: Xóa Rule
            Console.WriteLine("\nTest 5: Xóa Rule...");
            var res5 = await service.DeleteRuleAsync(ruleId);
            Console.WriteLine($"Kỳ vọng xoá IsSuccess=True. Kết quả: {res5.IsSuccess}");
        }
        
        Console.WriteLine("\nHoàn tất bộ test tự động!");
    }
}
