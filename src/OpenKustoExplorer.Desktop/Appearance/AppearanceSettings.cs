using System.ComponentModel;
using System.Text.Json;
using Avalonia.Platform;
using Avalonia.Styling;
using OpenKustoExplorer.Application.Assistance;

namespace OpenKustoExplorer.Desktop.Appearance;

/// <summary>
/// Coordinates user appearance choices with the current platform accessibility settings.
/// </summary>
internal sealed class AppearanceSettings : INotifyPropertyChanged, IDisposable
{
    /// <summary>
    /// Gets the largest supported application base text size.
    /// </summary>
    internal const double MaximumTextSize = 18;

    /// <summary>
    /// Gets the smallest supported application base text size.
    /// </summary>
    internal const double MinimumTextSize = 11;

    private const double DefaultTextSize = 13;
    private const int SettingsVersion = 1;
    private readonly string filePath;
    private Avalonia.Application? application;
    private bool copilotEnableAzureMcpByDefault;
    private bool copilotEnableMicrosoftLearnMcpByDefault;
    private KustoCopilotModel copilotDefaultModel = new("auto", "Automatic");
    private bool copilotShareResultDataByDefault;
    private bool copilotShareSchemaByDefault = true;
    private bool copilotShareTabContentByDefault = true;
    private WorkbenchDensity density = WorkbenchDensity.Compact;
    private bool isDisposed;
    private bool isHighContrast;
    private IPlatformSettings? platformSettings;
    private double textSize = DefaultTextSize;
    private ThemePreference themePreference = ThemePreference.System;

    /// <summary>
    /// Initializes a new instance of the <see cref="AppearanceSettings"/> class using the default settings path.
    /// </summary>
    public AppearanceSettings()
        : this(GetDefaultFilePath())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AppearanceSettings"/> class.
    /// </summary>
    /// <param name="filePath">The absolute settings file path.</param>
    internal AppearanceSettings(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        this.filePath = Path.GetFullPath(filePath);
        Load();
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Gets or sets the selected color-theme preference.
    /// </summary>
    public ThemePreference ThemePreference
    {
        get => themePreference;
        set
        {
            if (themePreference != value)
            {
                themePreference = value;
                ApplyThemePreference();
                OnPropertyChanged(nameof(ThemePreference));
                Save();
            }
        }
    }

    /// <summary>
    /// Gets or sets the selected workbench density.
    /// </summary>
    public WorkbenchDensity Density
    {
        get => density;
        set
        {
            if (density != value)
            {
                density = value;
                OnPropertyChanged(nameof(Density));
                Save();
            }
        }
    }

    /// <summary>
    /// Gets or sets the model applied to Copilot sessions without a per-tab override.
    /// </summary>
    public KustoCopilotModel CopilotDefaultModel
    {
        get => copilotDefaultModel;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            if (!string.Equals(copilotDefaultModel.Id, value.Id, StringComparison.Ordinal)
                || !string.Equals(copilotDefaultModel.Name, value.Name, StringComparison.Ordinal))
            {
                copilotDefaultModel = value;
                OnPropertyChanged(nameof(CopilotDefaultModel));
                Save();
            }
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether complete query tab content is shared with Copilot by default.
    /// </summary>
    public bool CopilotShareTabContentByDefault
    {
        get => copilotShareTabContentByDefault;
        set
        {
            if (copilotShareTabContentByDefault != value)
            {
                copilotShareTabContentByDefault = value;
                OnPropertyChanged(nameof(CopilotShareTabContentByDefault));
                Save();
            }
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether schema and database context are shared with Copilot by default.
    /// </summary>
    public bool CopilotShareSchemaByDefault
    {
        get => copilotShareSchemaByDefault;
        set
        {
            if (copilotShareSchemaByDefault != value)
            {
                copilotShareSchemaByDefault = value;
                OnPropertyChanged(nameof(CopilotShareSchemaByDefault));
                Save();
            }
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether bounded result data are shared with Copilot by default.
    /// </summary>
    public bool CopilotShareResultDataByDefault
    {
        get => copilotShareResultDataByDefault;
        set
        {
            if (copilotShareResultDataByDefault != value)
            {
                copilotShareResultDataByDefault = value;
                OnPropertyChanged(nameof(CopilotShareResultDataByDefault));
                OnPropertyChanged(nameof(CanEnableAzureMcpByDefault));

                if (!value && copilotEnableAzureMcpByDefault)
                {
                    copilotEnableAzureMcpByDefault = false;
                    OnPropertyChanged(nameof(CopilotEnableAzureMcpByDefault));
                }

                Save();
            }
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether Microsoft Learn MCP is enabled by default.
    /// </summary>
    public bool CopilotEnableMicrosoftLearnMcpByDefault
    {
        get => copilotEnableMicrosoftLearnMcpByDefault;
        set
        {
            if (copilotEnableMicrosoftLearnMcpByDefault != value)
            {
                copilotEnableMicrosoftLearnMcpByDefault = value;
                OnPropertyChanged(nameof(CopilotEnableMicrosoftLearnMcpByDefault));
                Save();
            }
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether read-only Azure MCP is enabled by default.
    /// </summary>
    public bool CopilotEnableAzureMcpByDefault
    {
        get => copilotEnableAzureMcpByDefault;
        set
        {
            bool allowedValue = value && CanEnableAzureMcpByDefault;

            if (copilotEnableAzureMcpByDefault != allowedValue)
            {
                copilotEnableAzureMcpByDefault = allowedValue;
                OnPropertyChanged(nameof(CopilotEnableAzureMcpByDefault));
                Save();
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether result-sharing consent permits Azure MCP as a default.
    /// </summary>
    public bool CanEnableAzureMcpByDefault => CopilotShareResultDataByDefault;

    /// <summary>
    /// Gets or sets the application base text size in device-independent pixels.
    /// </summary>
    public double TextSize
    {
        get => textSize;
        set
        {
            double boundedValue = Math.Round(
                Math.Clamp(value, MinimumTextSize, MaximumTextSize),
                MidpointRounding.AwayFromZero);

            if (Math.Abs(textSize - boundedValue) > double.Epsilon)
            {
                textSize = boundedValue;
                ApplyTextSize();
                OnPropertyChanged(nameof(TextSize));
                Save();
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the operating system requests high contrast.
    /// </summary>
    public bool IsHighContrast => isHighContrast;

    /// <summary>
    /// Connects the settings coordinator to an initialized Avalonia application.
    /// </summary>
    /// <param name="application">The initialized desktop application.</param>
    /// <exception cref="ArgumentNullException"><paramref name="application"/> is <see langword="null"/>.</exception>
    public void Initialize(Avalonia.Application application)
    {
        ArgumentNullException.ThrowIfNull(application);
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (platformSettings is not null)
        {
            platformSettings.ColorValuesChanged -= OnColorValuesChanged;
        }

        this.application = application;
        platformSettings = application.PlatformSettings;
        ApplyThemePreference();
        ApplyTextSize();

        if (platformSettings is not null)
        {
            platformSettings.ColorValuesChanged += OnColorValuesChanged;
            UpdatePlatformPreferences(platformSettings.GetColorValues());
        }
        else
        {
            UpdateHighContrast(false);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!isDisposed)
        {
            isDisposed = true;

            if (platformSettings is not null)
            {
                platformSettings.ColorValuesChanged -= OnColorValuesChanged;
            }
        }
    }

    private static string GetDefaultFilePath()
    {
        string localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localApplicationData, "OpenKustoExplorer", "settings.json");
    }

    private static TEnum ReadEnum<TEnum>(
        JsonElement root,
        string propertyName,
        TEnum defaultValue)
        where TEnum : struct, Enum
    {
        TEnum value = defaultValue;

        if (root.TryGetProperty(propertyName, out JsonElement element)
            && element.ValueKind == JsonValueKind.String)
        {
            string? text = element.GetString();
            if (!Enum.TryParse(text, ignoreCase: true, out value) || !Enum.IsDefined(value))
            {
                value = defaultValue;
            }
        }

        return value;
    }

    private static double ReadTextSize(JsonElement root)
    {
        double value = DefaultTextSize;

        if (root.TryGetProperty("textSize", out JsonElement element)
            && element.TryGetDouble(out double candidate)
            && double.IsFinite(candidate))
        {
            value = Math.Round(
                Math.Clamp(candidate, MinimumTextSize, MaximumTextSize),
                MidpointRounding.AwayFromZero);
        }

        return value;
    }

    private static bool ReadBoolean(JsonElement root, string propertyName, bool defaultValue)
    {
        return root.TryGetProperty(propertyName, out JsonElement element)
            && element.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? element.GetBoolean()
                : defaultValue;
    }

    private static KustoCopilotModel ReadCopilotDefaultModel(JsonElement root)
    {
        const string AutomaticId = "auto";
        const string AutomaticName = "Automatic";
        string? modelId = root.TryGetProperty("copilotDefaultModelId", out JsonElement idElement)
            && idElement.ValueKind == JsonValueKind.String
                ? idElement.GetString()
                : null;
        string? modelName = root.TryGetProperty("copilotDefaultModelName", out JsonElement nameElement)
            && nameElement.ValueKind == JsonValueKind.String
                ? nameElement.GetString()
                : null;
        return string.IsNullOrWhiteSpace(modelId) || string.IsNullOrWhiteSpace(modelName)
            ? new KustoCopilotModel(AutomaticId, AutomaticName)
            : new KustoCopilotModel(modelId, modelName);
    }

    private void ApplyThemePreference()
    {
        if (application is not null)
        {
            application.RequestedThemeVariant = themePreference switch
            {
                ThemePreference.Light => ThemeVariant.Light,
                ThemePreference.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };
        }
    }

    private void ApplyTextSize()
    {
        if (application is not null)
        {
            application.Resources["TypeCaptionSize"] = Math.Max(10, TextSize - 1);
            application.Resources["TypeBodySize"] = TextSize;
            application.Resources["TypeEmphasisSize"] = TextSize + 1;
            application.Resources["TypeTitleSize"] = TextSize + 3;
            application.Resources["TypeDisplaySize"] = TextSize + 7;
            application.Resources["QueryEditorTextSize"] = TextSize + 1;
        }
    }

    private void Load()
    {
        if (File.Exists(filePath))
        {
            try
            {
                using FileStream stream = File.OpenRead(filePath);
                using JsonDocument document = JsonDocument.Parse(stream);
                JsonElement root = document.RootElement;

                if (root.TryGetProperty("version", out JsonElement versionElement)
                    && versionElement.GetInt32() == SettingsVersion)
                {
                    themePreference = ReadEnum(root, "theme", ThemePreference.System);
                    density = ReadEnum(root, "density", WorkbenchDensity.Compact);
                    textSize = ReadTextSize(root);
                    copilotDefaultModel = ReadCopilotDefaultModel(root);
                    copilotShareTabContentByDefault = ReadBoolean(root, "copilotShareTabContent", true);
                    copilotShareSchemaByDefault = ReadBoolean(root, "copilotShareSchema", true);
                    copilotShareResultDataByDefault = ReadBoolean(root, "copilotShareResultData", false);
                    copilotEnableMicrosoftLearnMcpByDefault = ReadBoolean(
                        root,
                        "copilotEnableMicrosoftLearnMcp",
                        false);
                    copilotEnableAzureMcpByDefault = copilotShareResultDataByDefault
                        && ReadBoolean(root, "copilotEnableAzureMcp", false);
                }
            }
            catch (IOException)
            {
                // Unreadable settings fall back to accessible defaults.
            }
            catch (UnauthorizedAccessException)
            {
                // Unreadable settings fall back to accessible defaults.
            }
            catch (JsonException)
            {
                // Malformed settings fall back to accessible defaults.
            }
            catch (InvalidOperationException)
            {
                // Structurally invalid settings fall back to accessible defaults.
            }
            catch (FormatException)
            {
                // Values with unexpected formats fall back to accessible defaults.
            }
        }
    }

    private void Save()
    {
        string directoryPath = Path.GetDirectoryName(filePath)
            ?? throw new InvalidOperationException("The application settings path has no directory.");
        string temporaryPath = $"{filePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            Directory.CreateDirectory(directoryPath);
            using (FileStream stream = File.Create(temporaryPath))
            using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                writer.WriteNumber("version", SettingsVersion);
                writer.WriteString("theme", ThemePreference.ToString());
                writer.WriteString("density", Density.ToString());
                writer.WriteNumber("textSize", TextSize);
                writer.WriteString("copilotDefaultModelId", CopilotDefaultModel.Id);
                writer.WriteString("copilotDefaultModelName", CopilotDefaultModel.Name);
                writer.WriteBoolean("copilotShareTabContent", CopilotShareTabContentByDefault);
                writer.WriteBoolean("copilotShareSchema", CopilotShareSchemaByDefault);
                writer.WriteBoolean("copilotShareResultData", CopilotShareResultDataByDefault);
                writer.WriteBoolean("copilotEnableMicrosoftLearnMcp", CopilotEnableMicrosoftLearnMcpByDefault);
                writer.WriteBoolean("copilotEnableAzureMcp", CopilotEnableAzureMcpByDefault);
                writer.WriteEndObject();
                writer.Flush();
                stream.Flush(true);
            }

            File.Move(temporaryPath, filePath, true);
        }
        catch (IOException)
        {
            // A later settings change retries persistence.
        }
        catch (UnauthorizedAccessException)
        {
            // The active session still retains the selected settings.
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private void OnColorValuesChanged(object? sender, PlatformColorValues eventArguments)
    {
        UpdatePlatformPreferences(eventArguments);
    }

    private void UpdatePlatformPreferences(PlatformColorValues colorValues)
    {
        bool highContrast = colorValues.ContrastPreference == ColorContrastPreference.High;
        UpdateHighContrast(highContrast);
    }

    private void UpdateHighContrast(bool highContrast)
    {
        if (isHighContrast != highContrast)
        {
            isHighContrast = highContrast;
            OnPropertyChanged(nameof(IsHighContrast));
        }
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
