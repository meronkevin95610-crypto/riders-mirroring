using System.Collections.Concurrent;
using FluentAssertions;
using Riders.Mirroring.Core.AirPlay;
using Riders.Mirroring.Desktop.Theming;

namespace Riders.Mirroring.Desktop.Tests;

public class AppServicesTests
{
    public AppServicesTests()
    {
        // Clear any leftover registrations from other tests in the static locator.
        AppServices.Reset();
    }

    [Fact]
    public void Register_Stores_ServiceByType()
    {
        var theme = new FakeThemeService(ThemePreset.Ocean);
        AppServices.Register<IThemeService>(theme);

        AppServices.Resolve<IThemeService>().Should().BeSameAs(theme);
    }

    [Fact]
    public void Register_Overwrites_PreviousRegistration()
    {
        var a = new FakeThemeService(ThemePreset.Ocean);
        var b = new FakeThemeService(ThemePreset.Forest);

        AppServices.Register<IThemeService>(a);
        AppServices.Register<IThemeService>(b);

        AppServices.Resolve<IThemeService>().Should().BeSameAs(b);
    }

    [Fact]
    public void Resolve_ReturnsNull_WhenUnknown()
    {
        AppServices.Resolve<IThemeService>().Should().BeNull();
    }

    [Fact]
    public void Require_Throws_WhenMissing()
    {
        Action act = () => AppServices.Require<IThemeService>();
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*was not registered*");
    }

    [Fact]
    public void Register_NullInstance_Throws()
    {
        Action act = () => AppServices.Register<IThemeService>(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Register_IsThreadSafe_UnderConcurrentAccess()
    {
        var bag = new ConcurrentBag<int>();

        await Task.WhenAll(Enumerable.Range(0, 100).Select(async i =>
        {
            await Task.Yield();
            var svc = new FakeThemeService(ThemePreset.Ocean);
            AppServices.Register<IThemeService>(svc);
            var resolved = AppServices.Resolve<IThemeService>();
            if (resolved is not null)
            {
                bag.Add(i);
            }
        }));

        bag.Should().NotBeEmpty();
        // Last writer wins — but no exception was thrown.
        AppServices.Resolve<IThemeService>().Should().NotBeNull();
    }
}

internal sealed class FakeThemeService : IThemeService
{
    public FakeThemeService(ThemePreset preset) => Preset = preset;
    public AppTheme Theme { get; set; } = AppTheme.Dark;
    public ThemePreset Preset { get; set; }
    public bool IsDark => Theme == AppTheme.Dark;
    public event EventHandler? Changed { add { } remove { } }
    public void ApplyTo(System.Windows.Application app) { /* no-op */ }
}
