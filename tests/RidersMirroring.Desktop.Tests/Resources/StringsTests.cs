using System.Globalization;
using FluentAssertions;
using Riders.Mirroring.Desktop.Resources;

namespace RidersMirroring.Desktop.Tests.Resources;

/// <summary>
/// Smoke tests for the i18n resource layer. We force
/// <see cref="CultureInfo.CurrentUICulture"/> because WPF's automatic
/// culture selection happens at runtime via ResourceManager; in tests we
/// have to opt in explicitly so we exercise the French fallback too.
/// </summary>
public sealed class StringsTests
{
    public StringsTests()
    {
        // Belt and braces — make sure no earlier test leaked a non-default
        // culture into the static ResourceManager cache.
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
    }

    [Fact]
    public void Invariant_culture_returns_English_values()
    {
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

        Strings.AppTitle.Should().Be("Riders Mirroring");
        Strings.SidebarDevicesHeader.Should().Be("DEVICES");
        Strings.AirPlayTitle.Should().Be("AirPlay Receiver");
        Strings.WirelessHostTitle.Should().Be("Wireless Host");
        Strings.SettingsTitle.Should().Be("Settings");
        Strings.AboutOpenRepo.Should().Be("🔗 Open GitHub repository");
    }

    [Fact]
    public void French_culture_returns_french_values()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("fr-FR");

        Strings.AppTitle.Should().Be("Riders Mirroring");
        Strings.SidebarDevicesHeader.Should().Be("APPAREILS");
        Strings.AirPlayTitle.Should().Be("Récepteur AirPlay");
        Strings.WirelessHostTitle.Should().Be("Hôte sans fil");
        Strings.SettingsTitle.Should().Be("Paramètres");
        Strings.MirrorStartButton.Should().Be("▶ DÉMARRER LE MIRRORING");
        Strings.MirrorEmptyTitle.Should().Be("📱 Aucun appareil sélectionné");
    }

    [Fact]
    public void English_culture_returns_english_values()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en-US");

        Strings.SidebarDevicesHeader.Should().Be("DEVICES");
        Strings.AirPlayTitle.Should().Be("AirPlay Receiver");
        Strings.WirelessHostTitle.Should().Be("Wireless Host");
        Strings.SettingsTitle.Should().Be("Settings");
    }

    [Fact]
    public void Unknown_culture_falls_back_to_invariant_english()
    {
        // Klingon locale — clearly no Strings.kl.resx.
        CultureInfo.CurrentUICulture = new CultureInfo("kl-GL");

        Strings.AppTitle.Should().Be("Riders Mirroring");
        Strings.SidebarDevicesHeader.Should().Be("DEVICES");
    }

    [Fact]
    public void ResourceManager_exposes_the_same_strings()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("fr-FR");

        var rm = Strings.ResourceManager;
        rm.GetString("AirPlayTitle", CultureInfo.GetCultureInfo("fr-FR"))
            .Should().Be("Récepteur AirPlay");
        rm.GetString("SettingsTitle", CultureInfo.GetCultureInfo("en-US"))
            .Should().Be("Settings");
    }
}