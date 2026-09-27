using System.Reflection;
using FluentAssertions;
using Riders.Mirroring.Desktop.ViewModels;

namespace Riders.Mirroring.Desktop.Tests.ViewModels;

/// <summary>
/// Unit tests for <see cref="AboutViewModel"/>. The VM has no service
/// dependencies, so there are no fakes — we just instantiate it directly.
/// </summary>
public class AboutViewModelTests
{
    private static AboutViewModel CreateSut() => new();

    [Fact]
    public void VersionString_IsNotEmpty()
    {
        var vm = CreateSut();

        vm.VersionString.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void VersionString_FollowsSemanticVersionFormat()
    {
        var vm = CreateSut();

        // Must look like "1.2.3" or "1.2.3.4"
        var parts = vm.VersionString.Split('.');
        parts.Should().HaveCountGreaterOrEqualTo(2)
            .And.HaveCountLessOrEqualTo(4);
        parts.Should().AllSatisfy(p => int.Parse(p).Should().BeGreaterOrEqualTo(0));
    }

    [Fact]
    public void ProductName_IsRidersMirroring()
    {
        var vm = CreateSut();

        vm.ProductName.Should().Be("Riders Mirroring");
    }

    [Fact]
    public void AppLogoSource_LoadsFromBundledBranding()
    {
        // The placeholder PNG ships as a WPF Resource manifest. The AboutView
        // panel gracefully hides itself when the asset is missing, so a null
        // here is acceptable only in a stripped publish output.
        var vm = CreateSut();

        var asm = typeof(AboutViewModel).Assembly;
        var hasResource = asm.GetManifestResourceNames()
            .Any(n => n.EndsWith("riders-mirroring-logo.png", StringComparison.OrdinalIgnoreCase));

        if (hasResource)
        {
            vm.AppLogoSource.Should().NotBeNull(
                "the bundled branding must be resolved into an ImageSource when present in the assembly");
        }
    }

    [Fact]
    public void Copyright_IsNotEmpty()
    {
        var vm = CreateSut();

        vm.Copyright.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Description_IsNotEmpty()
    {
        var vm = CreateSut();

        vm.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Components_ContainsAllRequiredThirdParties()
    {
        var vm = CreateSut();

        var names = vm.Components.Select(c => c.Name).ToList();
        names.Should().Contain("scrcpy");
        names.Should().Contain("UxPlay");
        names.Should().Contain("FFmpeg");
        names.Should().Contain("QRCoder");
        names.Should().Contain("CommunityToolkit.Mvvm");
    }

    [Fact]
    public void Components_EveryRowHasNonEmptyFields()
    {
        var vm = CreateSut();

        foreach (var component in vm.Components)
        {
            component.Name.Should().NotBeNullOrWhiteSpace(because: "every component must have a name");
            component.Licence.Should().NotBeNullOrWhiteSpace(because: "every component must have a licence");
            component.Url.Should().NotBeNullOrWhiteSpace(because: "every component must have a URL");
            component.Usage.Should().NotBeNullOrWhiteSpace(because: "every component must describe its usage");
        }
    }

    [Fact]
    public void Components_EveryUrlIsAbsolute()
    {
        var vm = CreateSut();

        foreach (var component in vm.Components)
        {
            Uri.IsWellFormedUriString(component.Url, UriKind.Absolute)
               .Should().BeTrue(because: $"URL for '{component.Name}' should be absolute");
        }
    }

    [Fact]
    public void OpenProjectUrlCommand_DoesNotThrow()
    {
        var vm = CreateSut();
        // In the test environment there's no shell, so the command may silently
        // fail (TryOpenUrl catches). We just verify no exception propagates.
        var act = () => vm.OpenProjectUrlCommand.Execute(null);
        act.Should().NotThrow();
    }

    [Fact]
    public void OpenComponentUrlCommand_WithNull_DoesNotThrow()
    {
        var vm = CreateSut();
        // Passing null should be a safe no-op.
        var act = () => vm.OpenComponentUrlCommand.Execute(null);
        act.Should().NotThrow();
    }

    [Fact]
    public void OpenComponentUrlCommand_WithValidComponent_DoesNotThrow()
    {
        var vm = CreateSut();
        var component = vm.Components[0];
        var act = () => vm.OpenComponentUrlCommand.Execute(component);
        act.Should().NotThrow();
    }
}
