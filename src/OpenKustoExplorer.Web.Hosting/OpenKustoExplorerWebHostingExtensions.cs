using Microsoft.Extensions.FileProviders;
using OpenKustoExplorer.Application.Language;
using OpenKustoExplorer.Kusto.Execution;
using OpenKustoExplorer.Kusto.Gateway.V1;
using OpenKustoExplorer.Kusto.Language;
using OpenKustoExplorer.Web.Authentication;
using OpenKustoExplorer.Web.Kusto;

namespace OpenKustoExplorer.Web.Hosting;

/// <summary>
/// Composes the Open Kusto Explorer gateway into an ASP.NET Core host.
/// </summary>
public static class OpenKustoExplorerWebHostingExtensions
{
    /// <summary>
    /// Adds path-scoped browser isolation headers and Avalonia framework assets.
    /// </summary>
    /// <param name="app">The host application builder.</param>
    /// <param name="routePrefix">The path prefix that owns the browser application.</param>
    /// <param name="browserAssetsPath">The optional physical Avalonia browser-assets path.</param>
    /// <returns>The supplied application builder.</returns>
    public static IApplicationBuilder UseOpenKustoExplorerBrowserHost(
        this IApplicationBuilder app,
        string routePrefix = "/",
        string? browserAssetsPath = null)
    {
        ArgumentNullException.ThrowIfNull(app);
        PathString pathBase = GetPathBase(routePrefix);

        app.UseWhen(
            context => pathBase == PathString.Empty
                || context.Request.Path.StartsWithSegments(pathBase, StringComparison.Ordinal),
            branch => branch.Use(async (context, next) =>
            {
                context.Response.Headers.Append("Cross-Origin-Opener-Policy", "same-origin");
                context.Response.Headers.Append("Cross-Origin-Embedder-Policy", "require-corp");
                await next(context).ConfigureAwait(false);
            }));

        string assetsPath = browserAssetsPath
            ?? Path.Combine(AppContext.BaseDirectory, "avalonia-browser-assets");
        if (Directory.Exists(assetsPath))
        {
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(assetsPath),
                RequestPath = pathBase.Add(new PathString("/app/_framework")),
            });
        }

        return app;
    }

    /// <summary>
    /// Adds the provider-neutral Open Kusto Explorer gateway services.
    /// </summary>
    /// <param name="services">The host service collection.</param>
    /// <returns>The supplied service collection.</returns>
    public static IServiceCollection AddOpenKustoExplorerGateway(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddAntiforgery(options =>
        {
            options.FormFieldName = KustoGatewayRoutes.AntiforgeryFormFieldName;
            options.HeaderName = KustoGatewayRoutes.AntiforgeryHeaderName;
            options.Cookie.SameSite = SameSiteMode.Strict;
        });
        services.AddHttpContextAccessor();
        services.AddSingleton<IHostAddressResolver, SystemHostAddressResolver>();
        services.AddSingleton<IKustoEndpointPolicy, PublicAdxEndpointPolicy>();
        services.AddSingleton<IKustoLanguageService, KustoLanguageService>();
        services.AddSingleton<KustoOperationRegistry>();
        services
            .AddHttpClient<KustoExecutionService>()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false,
            });
        return services;
    }

    /// <summary>
    /// Adds loopback development token acquisition for Azure Data Explorer.
    /// </summary>
    /// <param name="services">The host service collection.</param>
    /// <returns>The supplied service collection.</returns>
    public static IServiceCollection AddOpenKustoExplorerLocalDevelopmentAccess(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(serviceProvider =>
        {
            HttpClientHandler handler = new()
            {
                AllowAutoRedirect = false,
            };
            return new LocalDevelopmentKustoAccessTokenProvider(
                serviceProvider.GetRequiredService<IHttpContextAccessor>(),
                new HttpClient(handler)
                {
                    Timeout = TimeSpan.FromMinutes(10),
                });
        });
        services.AddSingleton<IKustoAccessTokenProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<LocalDevelopmentKustoAccessTokenProvider>());
        services.AddSingleton<IWebSignOutService, LocalDevelopmentWebSignOutService>();
        return services;
    }

    /// <summary>
    /// Adds current-user delegated token acquisition for Azure Data Explorer.
    /// </summary>
    /// <param name="services">The host service collection.</param>
    /// <returns>The supplied service collection.</returns>
    public static IServiceCollection AddOpenKustoExplorerDelegatedAccess(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IKustoAccessTokenProvider, WebKustoAccessTokenProvider>();
        services.AddSingleton<IWebSignOutService, FederatedWebSignOutService>();
        return services;
    }

    /// <summary>
    /// Maps the authenticated Open Kusto Explorer gateway beneath a host path.
    /// </summary>
    /// <param name="endpoints">The host endpoint route builder.</param>
    /// <param name="routePrefix">The path prefix that owns the gateway.</param>
    /// <param name="authorizationPolicy">The optional host authorization policy.</param>
    /// <returns>The supplied endpoint route builder.</returns>
    public static IEndpointRouteBuilder MapOpenKustoExplorerGateway(
        this IEndpointRouteBuilder endpoints,
        string routePrefix = "/",
        string? authorizationPolicy = null)
    {
        return KustoGatewayEndpoints.MapKustoGateway(
            endpoints,
            routePrefix,
            authorizationPolicy);
    }

    private static PathString GetPathBase(string routePrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routePrefix);
        string normalized = routePrefix.TrimEnd('/');
        if (normalized.Length == 0)
        {
            return PathString.Empty;
        }

        if (normalized[0] != '/')
        {
            throw new ArgumentException("The route prefix must start with '/'.", nameof(routePrefix));
        }

        return new PathString(normalized);
    }
}
