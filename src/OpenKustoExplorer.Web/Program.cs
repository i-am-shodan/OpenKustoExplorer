using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.FileProviders;
using Microsoft.Identity.Web;
using OpenKustoExplorer.Application.Language;
using OpenKustoExplorer.Kusto.Execution;
using OpenKustoExplorer.Kusto.Gateway.V1;
using OpenKustoExplorer.Kusto.Language;
using OpenKustoExplorer.Web.Assistance;
using OpenKustoExplorer.Web.Authentication;
using OpenKustoExplorer.Web.Components;
using OpenKustoExplorer.Web.Kusto;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

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
    builder.Services.AddSingleton(serviceProvider =>
    {
        _ = serviceProvider;
        HttpClientHandler handler = new()
        {
            AllowAutoRedirect = false,
        };
        return new LocalDevelopmentKustoAccessTokenProvider(new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(10),
        });
    });
    builder.Services.AddSingleton<IKustoAccessTokenProvider>(serviceProvider =>
        serviceProvider.GetRequiredService<LocalDevelopmentKustoAccessTokenProvider>());
    builder.Services.AddSingleton<IWebSignOutService, LocalDevelopmentWebSignOutService>();
}
else
{
    builder.Services
        .AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"))
        .EnableTokenAcquisitionToCallDownstreamApi()
        .AddInMemoryTokenCaches();
    builder.Services.AddScoped<IKustoAccessTokenProvider, WebKustoAccessTokenProvider>();
    builder.Services.AddSingleton<IWebSignOutService, FederatedWebSignOutService>();
}

builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(options =>
{
    options.FormFieldName = KustoGatewayRoutes.AntiforgeryFormFieldName;
    options.HeaderName = KustoGatewayRoutes.AntiforgeryHeaderName;
    options.Cookie.SameSite = SameSiteMode.Strict;
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IHostAddressResolver, SystemHostAddressResolver>();
builder.Services.AddSingleton<IKustoEndpointPolicy, PublicAdxEndpointPolicy>();
builder.Services.AddSingleton<IKustoLanguageService, KustoLanguageService>();
builder.Services.AddSingleton<KustoOperationRegistry>();
builder.Services.Configure<WebCopilotOptions>(
    builder.Configuration.GetSection(WebCopilotOptions.SectionName));
builder.Services.AddSingleton<IWebCopilotService, AzureOpenAIWebCopilotService>();
builder.Services
    .AddHttpClient<KustoExecutionService>()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AllowAutoRedirect = false,
    });
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

WebApplication app = builder.Build();

app.Use(async (context, next) =>
{
    context.Response.Headers.Append("Cross-Origin-Opener-Policy", "same-origin");
    context.Response.Headers.Append("Cross-Origin-Embedder-Policy", "require-corp");
    await next(context);
});

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
string browserAssetsPath = Path.Combine(AppContext.BaseDirectory, "avalonia-browser-assets");
if (Directory.Exists(browserAssetsPath))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(browserAssetsPath),
        RequestPath = "/app/_framework",
    });
}

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .RequireAuthorization();
app.MapKustoGateway();
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
