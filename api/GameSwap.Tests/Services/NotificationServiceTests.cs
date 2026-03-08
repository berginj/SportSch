using System.Threading.Tasks;
using GameSwap.Functions.Repositories;
using GameSwap.Functions.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GameSwap.Tests.Services;

public class NotificationServiceTests
{
    [Fact]
    public async Task DeleteOldNotificationsAsync_DelegatesToRepositoryCleanup()
    {
        var repo = new Mock<INotificationRepository>();
        var logger = new Mock<ILogger<NotificationService>>();

        repo.Setup(x => x.DeleteOldNotificationsAsync(45)).ReturnsAsync(12);

        var service = new NotificationService(repo.Object, logger.Object);

        await service.DeleteOldNotificationsAsync(45);

        repo.Verify(x => x.DeleteOldNotificationsAsync(45), Times.Once);
    }
}
