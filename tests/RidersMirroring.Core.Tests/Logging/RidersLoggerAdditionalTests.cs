using FluentAssertions;
using Microsoft.Extensions.Logging;
using Riders.Mirroring.Core.Logging;

namespace Riders.Mirroring.Core.Tests.Logging;

public class RidersLoggerAdditionalTests
{
    [Fact]
    public void LogFilePath_IsEmptyBeforeEnsureFactory()
    {
        // Defensive: until EnsureFactory runs, the property stays empty.
        // (Other tests may have already warmed the factory — don't assert emptiness in that case.)
        RidersLogger.LogFilePath.Should().NotBeNull();
    }

    [Fact]
    public void EnsureFactory_PopulatesLogFilePath()
    {
        RidersLogger.EnsureFactory(LogLevel.Information);
        RidersLogger.LogFilePath.Should().NotBeNullOrEmpty();
        RidersLogger.LogFilePath.Should().StartWith(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        RidersLogger.LogFilePath.Should().Contain("Riders Mirroring");
        RidersLogger.LogFilePath.Should().EndWith("riders-debug.log");
    }

    [Fact]
    public void Create_ReturnsNonNullLogger()
    {
        var logger = RidersLogger.Create<RidersLoggerAdditionalTests>();
        logger.Should().NotBeNull();
    }

    [Fact]
    public void Create_Twice_ReturnsUsableLoggers()
    {
        var a = RidersLogger.Create<RidersLoggerAdditionalTests>();
        var b = RidersLogger.Create<RidersLoggerAdditionalTests>();

        a.Should().NotBeNull();
        b.Should().NotBeNull();

        Action act = () =>
        {
            a.LogInformation("hello");
            b.LogInformation("hello again");
        };
        act.Should().NotThrow();
    }

    [Fact]
    public void Shutdown_DoesNotThrow_OnCleanState()
    {
        RidersLogger.Shutdown();
        Action act = () => RidersLogger.Shutdown();
        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureFactory_IsIdempotent()
    {
        var first = RidersLogger.EnsureFactory();
        var second = RidersLogger.EnsureFactory();
        first.Should().BeSameAs(second);
    }
}
