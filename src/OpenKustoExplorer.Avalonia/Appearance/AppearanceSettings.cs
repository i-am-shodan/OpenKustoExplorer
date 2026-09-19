using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Avalonia.Platform;
using Avalonia.Styling;
using OpenKustoExplorer.Application.Assistance;

namespace OpenKustoExplorer.Desktop.Appearance;

/// <summary>
/// Coordinates user appearance choices with the current platform accessibility settings.
/// </summary>
public sealed class AppearanceSettings : IKustoAIProviderConfiguration, INotifyPropertyChanged, IDisposable
{
    /// <summary>
    /// Gets the largest supported application text zoom percentage.
    /// </summary>
    internal const int MaximumTextZoomPercentage = 140;

    /// <summary>
    /// Gets the largest supported result-table text zoom percentage.
    /// </summary>
    internal const int MaximumResultTextZoomPercentage = 250;

    /// <summary>
    /// Gets the smallest supported application text zoom percentage.
    /// </summary>
    internal const int MinimumTextZoomPercentage = 85;

    private const double DefaultTextSize = 13;
    private const int DefaultTextZoomPercentage = 100;
    private const int TextZoomStep = 5;
    private const string DefaultAzureOpenAIApiKeyEnvironmentVariable = "AZURE_OPENAI_API_KEY";
    private const string DefaultOpenAIApiKeyEnvironmentVariable = "OPENAI_API_KEY";
    private const string DefaultOpenAIModel = "gpt-4.1-mini";
    private const int SettingsVersion = 1;
    private readonly IAppearanceSettingsStore? store;
    private Avalonia.Application? application;
    private bool copilotEnableAzureMcpByDefault;
    private bool copilotEnableMicrosoftLearnMcpByDefault;
    private KustoCopilotModel copilotDefaultModel = new("auto", "Automatic");
    private KustoAIProviderKind providerKind;
    private bool copilotShareResultDataByDefault;
    private bool copilotShareSchemaByDefault = true;
    private bool copilotShareTabContentByDefault = true;
    private KustoAzureOpenAIAuthenticationKind azureOpenAIAuthenticationKind;
    private string azureOpenAIApiKeyEnvironmentVariable = DefaultAzureOpenAIApiKeyEnvironmentVariable;
    private string azureOpenAIDeployment = string.Empty;
    private string azureOpenAIEndpoint = string.Empty;
    private WorkbenchDensity density = WorkbenchDensity.Compact;
    private bool isDisposed;
    private bool isHighContrast;
    private IPlatformSettings? platformSettings;
    private int resultTextZoomPercentage = DefaultTextZoomPercentage;
    private bool showKqlHoverHelp = true;
    private string openAIApiKeyEnvironmentVariable = DefaultOpenAIApiKeyEnvironmentVariable;
    private string openAIEndpoint = string.Empty;
    private string openAIModel = DefaultOpenAIModel;
    private int textZoomPercentage = DefaultTextZoomPercentage;
    private ThemePreference themePreference = ThemePreference.System;

    /// <summary>
    /// Initializes a new instance of the <see cref="AppearanceSettings"/> class using the default settings path.
    /// </summary>
    public AppearanceSettings()
        : this(new FileAppearanceSettingsStore(GetDefaultFilePath()))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AppearanceSettings"/> class.
    /// </summary>
    /// <param name="filePath">The absolute settings file path.</param>
    internal AppearanceSettings(string filePath)
        : this(new FileAppearanceSettingsStore(filePath))
    {
    }

    private AppearanceSettings(IAppearanceSettingsStore? store)
    {
        this.store = store;
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

    /// <inheritdoc />
    public KustoAIProviderKind ProviderKind
    {
        get => providerKind;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            if (providerKind != value)
            {
                providerKind = value;
                OnPropertyChanged(nameof(ProviderKind));
                Save();
            }
        }
    }

    /// <inheritdoc />
    public string AzureOpenAIEndpoint
    {
        get => azureOpenAIEndpoint;
        set => SetProviderText(ref azureOpenAIEndpoint, value, nameof(AzureOpenAIEndpoint));
    }

    /// <inheritdoc />
    public string AzureOpenAIDeployment
    {
        get => azureOpenAIDeployment;
        set => SetProviderText(ref azureOpenAIDeployment, value, nameof(AzureOpenAIDeployment));
    }

    /// <inheritdoc />
    public KustoAzureOpenAIAuthenticationKind AzureOpenAIAuthenticationKind
    {
        get => azureOpenAIAuthenticationKind;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            if (azureOpenAIAuthenticationKind != value)
            {
                azureOpenAIAuthenticationKind = value;
                OnPropertyChanged(nameof(AzureOpenAIAuthenticationKind));
                Save();
            }
        }
    }

    /// <inheritdoc />
    public string AzureOpenAIApiKeyEnvironmentVariable
    {
        get => azureOpenAIApiKeyEnvironmentVariable;
        set => SetProviderText(
            ref azureOpenAIApiKeyEnvironmentVariable,
            value,
            nameof(AzureOpenAIApiKeyEnvironmentVariable),
            DefaultAzureOpenAIApiKeyEnvironmentVariable);
    }

    /// <inheritdoc />
    public string OpenAIEndpoint
    {
        get => openAIEndpoint;
        set => SetProviderText(ref openAIEndpoint, value, nameof(OpenAIEndpoint));
    }

    /// <inheritdoc />
    public string OpenAIModel
    {
        get => openAIModel;
        set => SetProviderText(ref openAIModel, value, nameof(OpenAIModel), DefaultOpenAIModel);
    }

    /// <inheritdoc />
    public string OpenAIApiKeyEnvironmentVariable
    {
        get => openAIApiKeyEnvironmentVariable;
        set => SetProviderText(
            ref openAIApiKeyEnvironmentVariable,
            value,
            nameof(OpenAIApiKeyEnvironmentVariable),
            DefaultOpenAIApiKeyEnvironmentVariable);
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
    /// Gets or sets the application text zoom percentage.
    /// </summary>
    public int TextZoomPercentage
    {
        get => textZoomPercentage;
        set
        {
            int boundedValue = NormalizeTextZoomPercentage(value, MaximumTextZoomPercentage);

            if (textZoomPercentage != boundedValue)
            {
                textZoomPercentage = boundedValue;
                ApplyTextSize();
                OnPropertyChanged(nameof(TextZoomPercentage));
                Save();
            }
        }
    }

    /// <summary>
    /// Gets or sets the result-table text zoom percentage.
    /// </summary>
    public int ResultTextZoomPercentage
    {
        get => resultTextZoomPercentage;
        set
        {
            int boundedValue = NormalizeTextZoomPercentage(value, MaximumResultTextZoomPercentage);

            if (resultTextZoomPercentage != boundedValue)
            {
                resultTextZoomPercentage = boundedValue;
                ApplyResultTextSize();
                OnPropertyChanged(nameof(ResultTextZoomPercentage));
                Save();
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the operating system requests high contrast.
    /// </summary>
    public bool IsHighContrast => isHighContrast;

    /// <summary>
    /// Gets or sets a value indicating whether delayed KQL syntax help appears while hovering.
    /// </summary>
    public bool ShowKqlHoverHelp
    {
        get => showKqlHoverHelp;
        set
        {
            if (showKqlHoverHelp != value)
            {
                showKqlHoverHelp = value;
                OnPropertyChanged(nameof(ShowKqlHoverHelp));
                Save();
            }
        }
    }

    /// <summary>
    /// Creates session-only appearance settings for hosts without local file persistence.
    /// </summary>
    /// <returns>Appearance settings that do not read or write the local filesystem.</returns>
    public static AppearanceSettings CreateTransient() => new((IAppearanceSettingsStore?)null);

    /// <summary>
    /// Creates appearance settings backed by host-provided persistence.
    /// </summary>
    /// <param name="store">The host-specific settings store.</param>
    /// <returns>Appearance settings loaded from the supplied store.</returns>
    public static AppearanceSettings CreatePersistent(IAppearanceSettingsStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        return new AppearanceSettings(store);
    }

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
        ApplyResultTextSize();

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

    private static int ReadTextZoomPercentage(JsonElement root)
    {
        int value = DefaultTextZoomPercentage;

        if (root.TryGetProperty("textZoomPercentage", out JsonElement zoomElement)
            && zoomElement.TryGetInt32(out int zoomPercentage))
        {
            value = zoomPercentage;
        }
        else if (root.TryGetProperty("textSize", out JsonElement textSizeElement)
            && textSizeElement.TryGetDouble(out double textSize)
            && double.IsFinite(textSize))
        {
            value = (int)Math.Round(
                (textSize / DefaultTextSize) * DefaultTextZoomPercentage,
                MidpointRounding.AwayFromZero);
        }

        return NormalizeTextZoomPercentage(value, MaximumTextZoomPercentage);
    }

    private static int ReadResultTextZoomPercentage(JsonElement root)
    {
        int value = root.TryGetProperty("resultTextZoomPercentage", out JsonElement element)
            && element.TryGetInt32(out int percentage)
                ? percentage
                : DefaultTextZoomPercentage;
        return NormalizeTextZoomPercentage(value, MaximumResultTextZoomPercentage);
    }

    private static int NormalizeTextZoomPercentage(int value, int maximumPercentage)
    {
        return Math.Clamp(
            (int)Math.Round((double)value / TextZoomStep, MidpointRounding.AwayFromZero) * TextZoomStep,
            MinimumTextZoomPercentage,
            maximumPercentage);
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

    private static string ReadString(
        JsonElement root,
        string propertyName,
        string defaultValue)
    {
        string? value = root.TryGetProperty(propertyName, out JsonElement element)
            && element.ValueKind == JsonValueKind.String
                ? element.GetString()
                : null;
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim();
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
            double textSize = DefaultTextSize * TextZoomPercentage / DefaultTextZoomPercentage;
            application.Resources["TypeBadgeSize"] = Math.Max(7, textSize - 6);
            application.Resources["TypeMicroSize"] = Math.Max(8, textSize - 5);
            application.Resources["TypeMetadataSize"] = Math.Max(9, textSize - 4);
            application.Resources["TypeSmallSize"] = Math.Max(10, textSize - 3);
            application.Resources["TypeCompactSize"] = Math.Max(11, textSize - 2);
            application.Resources["TypeCaptionSize"] = Math.Max(10, textSize - 1);
            application.Resources["TypeBodySize"] = textSize;
            application.Resources["TypeEmphasisSize"] = textSize + 1;
            application.Resources["TypeSubheadingSize"] = textSize + 2;
            application.Resources["TypeTitleSize"] = textSize + 3;
            application.Resources["TypeMetricSize"] = textSize + 5;
            application.Resources["TypeDisplaySize"] = textSize + 7;
            application.Resources["QueryEditorTextSize"] = textSize + 1;
        }
    }

    private void ApplyResultTextSize()
    {
        if (application is not null)
        {
            application.Resources["ResultCellTextSize"] = 10 * ResultTextZoomPercentage / 100d;
            application.Resources["ResultHeaderTextSize"] = 11 * ResultTextZoomPercentage / 100d;
            application.Resources["ResultMetadataTextSize"] = 9 * ResultTextZoomPercentage / 100d;
        }
    }

    private void Load()
    {
        if (store is null)
        {
            return;
        }

        try
        {
            string? settingsJson = store.Load();
            if (string.IsNullOrWhiteSpace(settingsJson))
            {
                return;
            }

            using JsonDocument document = JsonDocument.Parse(settingsJson);
            JsonElement root = document.RootElement;

            if (root.TryGetProperty("version", out JsonElement versionElement)
                && versionElement.GetInt32() == SettingsVersion)
            {
                themePreference = ReadEnum(root, "theme", ThemePreference.System);
                density = ReadEnum(root, "density", WorkbenchDensity.Compact);
                textZoomPercentage = ReadTextZoomPercentage(root);
                resultTextZoomPercentage = ReadResultTextZoomPercentage(root);
                copilotDefaultModel = ReadCopilotDefaultModel(root);
                providerKind = ReadEnum(root, "aiProvider", KustoAIProviderKind.GitHubCopilot);
                azureOpenAIEndpoint = ReadString(root, "azureOpenAIEndpoint", string.Empty);
                azureOpenAIDeployment = ReadString(root, "azureOpenAIDeployment", string.Empty);
                azureOpenAIAuthenticationKind = ReadEnum(
                    root,
                    "azureOpenAIAuthentication",
                    KustoAzureOpenAIAuthenticationKind.MicrosoftEntraId);
                azureOpenAIApiKeyEnvironmentVariable = ReadString(
                    root,
                    "azureOpenAIApiKeyEnvironmentVariable",
                    DefaultAzureOpenAIApiKeyEnvironmentVariable);
                openAIEndpoint = ReadString(root, "openAIEndpoint", string.Empty);
                openAIModel = ReadString(root, "openAIModel", DefaultOpenAIModel);
                openAIApiKeyEnvironmentVariable = ReadString(
                    root,
                    "openAIApiKeyEnvironmentVariable",
                    DefaultOpenAIApiKeyEnvironmentVariable);
                copilotShareTabContentByDefault = ReadBoolean(root, "copilotShareTabContent", true);
                copilotShareSchemaByDefault = ReadBoolean(root, "copilotShareSchema", true);
                copilotShareResultDataByDefault = ReadBoolean(root, "copilotShareResultData", false);
                copilotEnableMicrosoftLearnMcpByDefault = ReadBoolean(
                    root,
                    "copilotEnableMicrosoftLearnMcp",
                    false);
                copilotEnableAzureMcpByDefault = copilotShareResultDataByDefault
                    && ReadBoolean(root, "copilotEnableAzureMcp", false);
                showKqlHoverHelp = ReadBoolean(root, "showKqlHoverHelp", true);
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

    private void Save()
    {
        if (store is null)
        {
            return;
        }

        try
        {
            using MemoryStream stream = new();
            using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                writer.WriteNumber("version", SettingsVersion);
                writer.WriteString("theme", ThemePreference.ToString());
                writer.WriteString("density", Density.ToString());
                writer.WriteNumber("textZoomPercentage", TextZoomPercentage);
                writer.WriteNumber("resultTextZoomPercentage", ResultTextZoomPercentage);
                writer.WriteString("aiProvider", ProviderKind.ToString());
                writer.WriteString("azureOpenAIEndpoint", AzureOpenAIEndpoint);
                writer.WriteString("azureOpenAIDeployment", AzureOpenAIDeployment);
                writer.WriteString(
                    "azureOpenAIAuthentication",
                    AzureOpenAIAuthenticationKind.ToString());
                writer.WriteString(
                    "azureOpenAIApiKeyEnvironmentVariable",
                    AzureOpenAIApiKeyEnvironmentVariable);
                writer.WriteString("openAIEndpoint", OpenAIEndpoint);
                writer.WriteString("openAIModel", OpenAIModel);
                writer.WriteString("openAIApiKeyEnvironmentVariable", OpenAIApiKeyEnvironmentVariable);
                writer.WriteString("copilotDefaultModelId", CopilotDefaultModel.Id);
                writer.WriteString("copilotDefaultModelName", CopilotDefaultModel.Name);
                writer.WriteBoolean("copilotShareTabContent", CopilotShareTabContentByDefault);
                writer.WriteBoolean("copilotShareSchema", CopilotShareSchemaByDefault);
                writer.WriteBoolean("copilotShareResultData", CopilotShareResultDataByDefault);
                writer.WriteBoolean("copilotEnableMicrosoftLearnMcp", CopilotEnableMicrosoftLearnMcpByDefault);
                writer.WriteBoolean("copilotEnableAzureMcp", CopilotEnableAzureMcpByDefault);
                writer.WriteBoolean("showKqlHoverHelp", ShowKqlHoverHelp);
                writer.WriteEndObject();
                writer.Flush();
            }

            store.Save(Encoding.UTF8.GetString(stream.GetBuffer(), 0, checked((int)stream.Length)));
        }
        catch (IOException)
        {
            // A later settings change retries persistence.
        }
        catch (UnauthorizedAccessException)
        {
            // The active session still retains the selected settings.
        }
    }

    private void OnColorValuesChanged(object? sender, PlatformColorValues eventArguments)
    {
        UpdatePlatformPreferences(eventArguments);
    }

    private void SetProviderText(
        ref string field,
        string? value,
        string propertyName,
        string defaultValue = "")
    {
        string normalized = string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim();
        if (!string.Equals(field, normalized, StringComparison.Ordinal))
        {
            field = normalized;
            OnPropertyChanged(propertyName);
            Save();
        }
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
