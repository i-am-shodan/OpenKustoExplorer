using OpenKustoExplorer.Application.Assistance;
using OpenKustoExplorer.Desktop.Appearance;

namespace OpenKustoExplorer.Presentation.Tests.Desktop;

/// <summary>
/// Verifies persisted desktop application settings.
/// </summary>
public sealed class AppearanceSettingsTests
{
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
                Assert.Equal("auto", settings.CopilotDefaultModel.Id);
                Assert.Equal("Automatic", settings.CopilotDefaultModel.Name);
                settings.CopilotShareTabContentByDefault = false;
                settings.CopilotShareSchemaByDefault = false;
                settings.CopilotShareResultDataByDefault = true;
                settings.CopilotEnableMicrosoftLearnMcpByDefault = true;
                settings.CopilotEnableAzureMcpByDefault = true;
                settings.CopilotDefaultModel = new KustoCopilotModel("gpt-test", "Test model");
            }

            using (AppearanceSettings reloaded = new(filePath))
            {
                Assert.False(reloaded.CopilotShareTabContentByDefault);
                Assert.False(reloaded.CopilotShareSchemaByDefault);
                Assert.True(reloaded.CopilotShareResultDataByDefault);
                Assert.True(reloaded.CopilotEnableMicrosoftLearnMcpByDefault);
                Assert.True(reloaded.CopilotEnableAzureMcpByDefault);
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
