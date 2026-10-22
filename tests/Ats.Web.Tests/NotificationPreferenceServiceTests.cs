using Ats.Web.Models.DTOs;
using Ats.Web.Models.Entities;
using Ats.Web.Models.Enums;
using Ats.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ats.Web.Tests;

/// <summary>
/// Unit tests cho chức năng Cấu hình bật/tắt thông báo cá nhân nhân sự nội bộ (SCRUM-349).
/// </summary>
public class NotificationPreferenceServiceTests
{
    [Fact]
    public async Task GetUserPreferencesAsync_ShouldReturnAllSixDefinedNotificationTypes()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var userId = Guid.NewGuid();
        var service = new NotificationPreferenceService(context, NullLogger<NotificationPreferenceService>.Instance);

        // Act
        var prefs = await service.GetUserPreferencesAsync(userId);

        // Assert
        Assert.NotNull(prefs);
        Assert.Equal(6, prefs.Count);

        var approvalPref = prefs.FirstOrDefault(p => p.NotificationType == NotificationType.APPROVAL_REQUEST);
        Assert.NotNull(approvalPref);
        Assert.True(approvalPref.IsMandatory, "APPROVAL_REQUEST phải mang tính bắt buộc");
        Assert.True(approvalPref.InAppEnabled, "APPROVAL_REQUEST In-App mặc định phải bật");
        Assert.True(approvalPref.EmailEnabled, "APPROVAL_REQUEST Email mặc định phải bật");
    }

    [Fact]
    public async Task UpdateSinglePreferenceAsync_ShouldBlockDisablingMandatoryApprovalNotification()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var userId = Guid.NewGuid();
        var service = new NotificationPreferenceService(context, NullLogger<NotificationPreferenceService>.Instance);

        var request = new UpdateSinglePreferenceRequestDto
        {
            NotificationType = NotificationType.APPROVAL_REQUEST,
            Channel = "inapp",
            Enabled = false // Cố gắng tắt thông báo bắt buộc
        };

        // Act
        var (success, message) = await service.UpdateSinglePreferenceAsync(userId, request);

        // Assert
        Assert.False(success, "Không được phép tắt thông báo bắt buộc tuân thủ");
        Assert.Contains("bắt buộc", message);
    }

    [Fact]
    public async Task UpdateSinglePreferenceAsync_ShouldToggleInAppAndEmailSuccessfully()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var userId = Guid.NewGuid();
        var service = new NotificationPreferenceService(context, NullLogger<NotificationPreferenceService>.Instance);

        // Act 1: Tắt Email cho NEW_APPLICATION
        var (s1, m1) = await service.UpdateSinglePreferenceAsync(userId, new UpdateSinglePreferenceRequestDto
        {
            NotificationType = NotificationType.NEW_APPLICATION,
            Channel = "email",
            Enabled = false
        });

        // Act 2: Tắt InApp cho SLA_OVERDUE_WARNING
        var (s2, m2) = await service.UpdateSinglePreferenceAsync(userId, new UpdateSinglePreferenceRequestDto
        {
            NotificationType = NotificationType.SLA_OVERDUE_WARNING,
            Channel = "inapp",
            Enabled = false
        });

        // Assert
        Assert.True(s1);
        Assert.True(s2);

        var prefs = await service.GetUserPreferencesAsync(userId);
        var newAppPref = prefs.First(p => p.NotificationType == NotificationType.NEW_APPLICATION);
        var slaPref = prefs.First(p => p.NotificationType == NotificationType.SLA_OVERDUE_WARNING);

        Assert.True(newAppPref.InAppEnabled);
        Assert.False(newAppPref.EmailEnabled);

        Assert.False(slaPref.InAppEnabled);
        Assert.True(slaPref.EmailEnabled);

        // Check should send methods
        var canSendEmail = await service.ShouldSendEmailAsync(userId, NotificationType.NEW_APPLICATION);
        Assert.False(canSendEmail);

        var canSendInApp = await service.ShouldSendInAppAsync(userId, NotificationType.SLA_OVERDUE_WARNING);
        Assert.False(canSendInApp);
    }

    [Fact]
    public async Task ShouldSendMethods_ShouldAlwaysReturnTrueForMandatoryApproval()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var userId = Guid.NewGuid();
        var service = new NotificationPreferenceService(context, NullLogger<NotificationPreferenceService>.Instance);

        // Act & Assert
        var shouldInApp = await service.ShouldSendInAppAsync(userId, NotificationType.APPROVAL_REQUEST);
        var shouldEmail = await service.ShouldSendEmailAsync(userId, NotificationType.APPROVAL_REQUEST);

        Assert.True(shouldInApp);
        Assert.True(shouldEmail);
    }
}
