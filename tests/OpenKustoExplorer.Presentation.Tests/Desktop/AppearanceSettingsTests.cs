using OpenKustoExplorer.Application.Assistance;
using OpenKustoExplorer.Desktop.Appearance;

namespace OpenKustoExplorer.Presentation.Tests.Desktop;

/// <summary>
/// Verifies persisted desktop application settings.
/// </summary>
public sealed class AppearanceSettingsTests
{
    /// <summary>
    /// Verifies delayed KQL hover help is enabled by default and its preference survives reload.
    /// </summary>
    [Fact]
    public void KqlHoverHelpPreferencePersistsAndDefaultsOn()
    {
        string directoryPath = Path.Combine(Path.GetTempPath(), $"OpenKustoExplorer-{Guid.NewGuid():N}");
        string filePath = Path.Combine(directoryPath, "settings.json");

        try
        {
            using (AppearanceSettings settings = new(filePath))
            {
                Assert.True(settings.ShowKqlHoverHelp);
                settings.ShowKqlHoverHelp = false;
            }

            using AppearanceSettings reloaded = new(filePath);
            Assert.False(reloaded.ShowKqlHoverHelp);
        }
        finally
        {
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies every semantic text tier grows with the application text-zoom preference.
    /// </summary>
    [Fact]
    public void TextZoomUpdatesCompactAndDisplayResources()
    {
        string directoryPath = Path.Combine(Path.GetTempPath(), $"OpenKustoExplorer-{Guid.NewGuid():N}");
        string filePath = Path.Combine(directoryPath, "settings.json");
        Avalonia.Application application = new();

        try
        {
            using AppearanceSettings settings = new(filePath);
            settings.Initialize(application);
            settings.TextZoomPercentage = AppearanceSettings.MaximumTextZoomPercentage;

            Assert.Equal(12.2d, application.Resources["TypeBadgeSize"]);
            Assert.Equal(13.2d, application.Resources["TypeMicroSize"]);
            Assert.Equal(14.2d, application.Resources["TypeMetadataSize"]);
            Assert.Equal(15.2d, application.Resources["TypeSmallSize"]);
            Assert.Equal(16.2d, application.Resources["TypeCompactSize"]);
            Assert.Equal(20.2d, application.Resources["TypeSubheadingSize"]);
            Assert.Equal(23.2d, application.Resources["TypeMetricSize"]);
            Assert.Equal(10d, application.Resources["ResultCellTextSize"]);
            Assert.Equal(11d, application.Resources["ResultHeaderTextSize"]);
            Assert.Equal(9d, application.Resources["ResultMetadataTextSize"]);
        }
        finally
        {
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies result text zoom changes only the result-table typography resources.
    /// </summary>
    [Fact]
    public void ResultTextZoomUpdatesOnlyResultResources()
    {
        string directoryPath = Path.Combine(Path.GetTempPath(), $"OpenKustoExplorer-{Guid.NewGuid():N}");
        string filePath = Path.Combine(directoryPath, "settings.json");
        Avalonia.Application application = new();

        try
        {
            using AppearanceSettings settings = new(filePath);
            settings.Initialize(application);
            settings.ResultTextZoomPercentage = AppearanceSettings.MaximumResultTextZoomPercentage;

            Assert.Equal(25d, application.Resources["ResultCellTextSize"]);
            Assert.Equal(27.5d, application.Resources["ResultHeaderTextSize"]);
            Assert.Equal(22.5d, application.Resources["ResultMetadataTextSize"]);
            Assert.Equal(13d, application.Resources["TypeBodySize"]);
            Assert.Equal(14d, application.Resources["QueryEditorTextSize"]);
        }
        finally
        {
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies text zoom is persisted and legacy text-size settings are migrated to percentages.
    /// </summary>
    [Fact]
    public void TextZoomPersistsAndMigratesLegacyTextSize()
    {
        const string LegacySettings = "{\"version\":1,\"textSize\":15}";
        string directoryPath = Path.Combine(Path.GetTempPath(), $"OpenKustoExplorer-{Guid.NewGuid():N}");
        string filePath = Path.Combine(directoryPath, "settings.json");

        try
        {
            Directory.CreateDirectory(directoryPath);
            File.WriteAllText(filePath, LegacySettings);

            using (AppearanceSettings migrated = new(filePath))
            {
                Assert.Equal(115, migrated.TextZoomPercentage);
                migrated.TextZoomPercentage = 125;
                migrated.ResultTextZoomPercentage = 130;
            }

            using AppearanceSettings reloaded = new(filePath);
            Assert.Equal(125, reloaded.TextZoomPercentage);
            Assert.Equal(130, reloaded.ResultTextZoomPercentage);
        }
        finally
        {
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies Copilot defaults survive reload and Azure MCP remains consent-gated.
    /// </summary>
    [Fact]
    public void CopilotDefaultsPersistWithAzureConsentGate()
    {
        string directoryPath = Path.Combine(Path.GetTempPath(), $"OpenKustoExplorer-{Guid.NewGuid():N}");
        string filePath = Path.Combine(directoryPath, "settings.json");

        try
        {
            using (AppearanceSettings settings = new(filePath))
            {
                Assert.True(settings.CopilotShareTabContentByDefault);
                Assert.True(settings.CopilotShareSchemaByDefault);
                Assert.False(settings.CopilotShareResultDataByDefault);
                Assert.False(settings.CopilotEnableMicrosoftLearnMcpByDefault);
                Assert.False(settings.CopilotEnableAzureMcpByDefault);
                Assert.Equal(KustoAIProviderKind.GitHubCopilot, settings.ProviderKind);
                Assert.Equal(string.Empty, settings.AzureOpenAIEndpoint);
                Assert.Equal(string.Empty, settings.AzureOpenAIDeployment);
                Assert.Equal(
                    KustoAzureOpenAIAuthenticationKind.MicrosoftEntraId,
                    settings.AzureOpenAIAuthenticationKind);
                Assert.Equal("AZURE_OPENAI_API_KEY", settings.AzureOpenAIApiKeyEnvironmentVariable);
                Assert.Equal(string.Empty, settings.OpenAIEndpoint);
                Assert.Equal("gpt-4.1-mini", settings.OpenAIModel);
                Assert.Equal("OPENAI_API_KEY", settings.OpenAIApiKeyEnvironmentVariable);
                Assert.Equal("auto", settings.CopilotDefaultModel.Id);
                Assert.Equal("Automatic", settings.CopilotDefaultModel.Name);
                settings.CopilotShareTabContentByDefault = false;
                settings.CopilotShareSchemaByDefault = false;
                settings.CopilotShareResultDataByDefault = true;
                settings.CopilotEnableMicrosoftLearnMcpByDefault = true;
                settings.CopilotEnableAzureMcpByDefault = true;
                settings.CopilotDefaultModel = new KustoCopilotModel("gpt-test", "Test model");
                settings.ProviderKind = KustoAIProviderKind.AzureOpenAI;
                settings.AzureOpenAIEndpoint = "https://synthetic.openai.azure.example";
                settings.AzureOpenAIDeployment = "synthetic-deployment";
                settings.AzureOpenAIAuthenticationKind = KustoAzureOpenAIAuthenticationKind.ApiKey;
                settings.AzureOpenAIApiKeyEnvironmentVariable = "SYNTHETIC_AZURE_OPENAI_KEY";
                settings.OpenAIEndpoint = "https://api.openai.example/v1";
                settings.OpenAIModel = "synthetic-model";
                settings.OpenAIApiKeyEnvironmentVariable = "SYNTHETIC_OPENAI_KEY";
            }

            using (AppearanceSettings reloaded = new(filePath))
            {
                Assert.False(reloaded.CopilotShareTabContentByDefault);
                Assert.False(reloaded.CopilotShareSchemaByDefault);
                Assert.True(reloaded.CopilotShareResultDataByDefault);
                Assert.True(reloaded.CopilotEnableMicrosoftLearnMcpByDefault);
                Assert.True(reloaded.CopilotEnableAzureMcpByDefault);
                Assert.Equal(KustoAIProviderKind.AzureOpenAI, reloaded.ProviderKind);
                Assert.Equal("https://synthetic.openai.azure.example", reloaded.AzureOpenAIEndpoint);
                Assert.Equal("synthetic-deployment", reloaded.AzureOpenAIDeployment);
                Assert.Equal(
                    KustoAzureOpenAIAuthenticationKind.ApiKey,
                    reloaded.AzureOpenAIAuthenticationKind);
                Assert.Equal("SYNTHETIC_AZURE_OPENAI_KEY", reloaded.AzureOpenAIApiKeyEnvironmentVariable);
                Assert.Equal("https://api.openai.example/v1", reloaded.OpenAIEndpoint);
                Assert.Equal("synthetic-model", reloaded.OpenAIModel);
                Assert.Equal("SYNTHETIC_OPENAI_KEY", reloaded.OpenAIApiKeyEnvironmentVariable);
                Assert.Equal("gpt-test", reloaded.CopilotDefaultModel.Id);
                Assert.Equal("Test model", reloaded.CopilotDefaultModel.Name);
                reloaded.CopilotShareResultDataByDefault = false;
                Assert.False(reloaded.CopilotEnableAzureMcpByDefault);
            }

            using AppearanceSettings consentGated = new(filePath);
            Assert.False(consentGated.CopilotShareResultDataByDefault);
            Assert.False(consentGated.CopilotEnableAzureMcpByDefault);
        }
        finally
        {
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, recursive: true);
            }
        }
    }
}
