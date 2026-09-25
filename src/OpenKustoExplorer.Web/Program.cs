using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web;
using OpenKustoExplorer.Web.Assistance;
using OpenKustoExplorer.Web.Authentication;
using OpenKustoExplorer.Web.Components;
using OpenKustoExplorer.Web.Hosting;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
string webBasePath = builder.Configuration["Web:BasePath"] ?? "/";

bool useLocalDevelopmentAuthentication = WebAuthenticationConfiguration.UseLocalDevelopment(
    builder.Configuration,
    builder.Environment);
if (useLocalDevelopmentAuthentication)
{
    builder.Services
        .AddAuthentication(LocalDevelopmentAuthenticationHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, LocalDevelopmentAuthenticationHandler>(
            LocalDevelopmentAuthenticationHandler.SchemeName,
            _ => { });
    builder.Services.AddOpenKustoExplorerLocalDevelopmentAccess();
}
else
{
    builder.Services
        .AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"))
        .EnableTokenAcquisitionToCallDownstreamApi()
        .AddInMemoryTokenCaches();
    builder.Services.AddOpenKustoExplorerDelegatedAccess();
}

builder.Services.AddAuthorization();
builder.Services.AddOpenKustoExplorerGateway();
builder.Services.Configure<WebCopilotOptions>(
    builder.Configuration.GetSection(WebCopilotOptions.SectionName));
builder.Services.AddSingleton<IWebCopilotService, AzureOpenAIWebCopilotService>();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

WebApplication app = builder.Build();

app.UseOpenKustoExplorerBrowserHost(webBasePath);

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .RequireAuthorization();
app.MapOpenKustoExplorerGateway(webBasePath);
app.MapGet("/healthz", () => Results.Ok(new { Status = "ok" }));

await app.RunAsync();

/// <summary>
/// Exposes the generated Web entry point to integration tests.
/// </summary>
public partial class Program
{
    private Program()
    {
    }
}
