using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using Microsoft.Extensions.Logging;

namespace Riders.Mirroring.Desktop.Theming;

/// <summary>
/// Owns the current <see cref="AppTheme"/> (dark/light) and active
/// <see cref="ThemePreset"/>. Persists the user's choice to
/// <c>%LOCALAPPDATA%\Riders Mirroring\theme.json</c> so it survives restarts,
/// and re-themes the running WPF application on demand by swapping the merged
/// resource dictionaries.
/// </summary>
/// <remarks>
/// The contract is intentionally tiny — this service is the single place that
/// knows how to load/save the user choice and how to apply it to the WPF
/// resource tree. ViewModels and views bind to the observable properties
/// exposed here and re-render automatically.
/// </remarks>
public sealed class ThemeService : IThemeService
{
    private static readonly Uri DarkDictionaryUri =
        new("Themes/Dark.xaml", UriKind.Relative);

    private static readonly Uri LightDictionaryUri =
        new("Themes/Light.xaml", UriKind.Relative);

    private static readonly Uri DofusTouchDictionaryUri =
        new("Themes/DofusTouch.xaml", UriKind.Relative);

    private readonly string _settingsPath;
    private readonly JsonSerializerOptions _jsonOptions;

    private AppTheme _theme = AppTheme.DofusTouch;
    private ThemePreset _preset = ThemePreset.DofusTouch;

    /// <inheritdoc />
    public AppTheme Theme
    {
        get => _theme;
        set
        {
            if (_theme == value)
            {
                return;
            }

            _theme = value;
            OnThemeChanged();
            OnPropertyChanged(nameof(IsDark));
            Persist();
        }
    }

    /// <inheritdoc />
    public ThemePreset Preset
    {
        get => _preset;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            if (ReferenceEquals(_preset, value))
            {
                return;
            }

            _preset = value;
            OnPresetChanged();
            Persist();
        }
    }

    /// <inheritdoc />
    public bool IsDark => _theme is AppTheme.Dark or AppTheme.DofusTouch;

    /// <inheritdoc />
    public event EventHandler? Changed;

    public ThemeService()
        : this(DefaultSettingsPath())
    {
    }

    /// <summary>Test-friendly constructor allowing an arbitrary file path.</summary>
    public ThemeService(string settingsPath)
    {
        _settingsPath = settingsPath ?? throw new ArgumentNullException(nameof(settingsPath));

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        Load();
    }

    /// <summary>
    /// Apply the current <see cref="Theme"/> and <see cref="Preset"/> to the
    /// supplied <see cref="Application"/> instance. Safe to call from
    /// <c>App.OnStartup</c>.
    /// </summary>
    public void ApplyTo(Application app)
    {
        ArgumentNullException.ThrowIfNull(app);

        SwapThemeDictionary(app);
        OnPresetChanged();
    }

    private void SwapThemeDictionary(Application app)
    {
        // Drop our previously-added theme dictionary, if any, then push the new one.
        ResourceDictionary? existing = null;
        foreach (var dict in app.Resources.MergedDictionaries)
        {
            if (dict.Source == DarkDictionaryUri ||
                dict.Source == LightDictionaryUri ||
                dict.Source == DofusTouchDictionaryUri)
            {
                existing = dict;
                break;
            }
        }

        if (existing is not null)
        {
            app.Resources.MergedDictionaries.Remove(existing);
        }

        var source = _theme switch
        {
            AppTheme.Light      => LightDictionaryUri,
            AppTheme.DofusTouch => DofusTouchDictionaryUri,
            _                   => DarkDictionaryUri,
        };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = source });
    }

    private void OnThemeChanged()
    {
        if (Application.Current is { } app)
        {
            SwapThemeDictionary(app);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnPresetChanged()
    {
        // Push the new accent colours into the live application resources so
        // every DynamicResource bound to one of the accent brushes updates
        // without needing a restart or a view reload.
        if (Application.Current is { } app)
        {
            app.Resources["AccentPrimaryBrush"]   = BrushFactory.FromHex(_preset.PrimaryHex);
            app.Resources["AccentSecondaryBrush"] = BrushFactory.FromHex(_preset.SecondaryHex);
            app.Resources["AccentTertiaryBrush"]  = BrushFactory.FromHex(_preset.AccentHex);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnPropertyChanged(string name) => Changed?.Invoke(this, EventArgs.Empty);

    // --------------------------------------------------------------------
    // Persistence
    // --------------------------------------------------------------------

    private void Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return;
            }

            using var stream = File.OpenRead(_settingsPath);
            var dto = JsonSerializer.Deserialize<ThemeSettingsDto>(stream, _jsonOptions);
            if (dto is null)
            {
                return;
            }

            _theme = Enum.TryParse<AppTheme>(dto.Theme, ignoreCase: true, out var parsedTheme)
                ? parsedTheme
                : AppTheme.Dark;

            _preset = ThemePreset.FromId(dto.PresetId);
        }
        catch
        {
            // Corrupted settings file should never crash the app — fall back
            // to defaults. We could log here in the future.
            _theme = AppTheme.DofusTouch;
            _preset = ThemePreset.DofusTouch;
        }
    }

    private void Persist()
    {
        try
        {
            var dir = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var dto = new ThemeSettingsDto
            {
                Theme = _theme.ToString(),
                PresetId = _preset.Id,
            };

            using var stream = File.Create(_settingsPath);
            JsonSerializer.Serialize(stream, dto, _jsonOptions);
        }
        catch (Exception ex)
        {
            // Settings persistence is best-effort. We log to give the user a
            // fighting chance of noticing that their preferences aren't being
            // remembered (e.g. corporate profile lockdown, OneDrive sync conflicts).
            try
            {
                Riders.Mirroring.Core.Logging.RidersLogger
                    .Create<ThemeService>()
                    .LogWarning(ex, "Could not persist theme settings to {Path}", _settingsPath);
            }
            catch
            {
                // Even the logger is broken — fall through silently.
            }
        }
    }

    private static string DefaultSettingsPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "Riders Mirroring", "theme.json");
    }

    /// <summary>DTO used by <see cref="System.Text.Json"/> for round-tripping.</summary>
    private sealed class ThemeSettingsDto
    {
        public string Theme { get; set; } = nameof(AppTheme.Dark);

        public string PresetId { get; set; } = ThemePreset.VioletPink.Id;
    }
}