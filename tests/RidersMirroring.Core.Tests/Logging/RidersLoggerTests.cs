using FluentAssertions;
using Microsoft.Extensions.Logging;
using Riders.Mirroring.Core.Logging;
using Xunit;

namespace Riders.Mirroring.Core.Tests.Logging;

public class RidersLoggerTests
{
    [Fact]
    public void EnsureFactory_ReturnsSingleton()
    {
        var a = RidersLogger.EnsureFactory();
        var b = RidersLogger.EnsureFactory();

        a.Should().BeSameAs(b);
    }

    [Fact]
    public void Create_ReturnsTypedLogger()
    {
        var logger = RidersLogger.Create<RidersLoggerTests>();

        logger.Should().NotBeNull();
        logger.Should().BeAssignableTo<ILogger<RidersLoggerTests>>();
    }

    [Fact]
    public void LogFilePath_IsInsideLocalAppData()
    {
        // Trigger factory init.
        RidersLogger.EnsureFactory();

        RidersLogger.LogFilePath.Should().NotBeNullOrEmpty();
        RidersLogger.LogFilePath.Should().Contain("Riders Mirroring");
        RidersLogger.LogFilePath.Should().EndWith("riders-debug.log");
    }
}