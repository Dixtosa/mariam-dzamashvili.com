using System.Text.Json;
using System.Threading.RateLimiting;
using Backend;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

var contentRoot = builder.Environment.ContentRootPath;
var paths = new SitePaths(
    WebRoot: builder.Environment.WebRootPath ?? Path.Combine(contentRoot, "wwwroot"),
    Data: Path.GetFullPath(builder.Configuration["Site:DataPath"] ?? "Data", contentRoot));

builder.Services.AddSingleton(paths);
builder.Services.AddSingleton<SiteStore>();
builder.Services.AddSingleton<ProjectImages>();
builder.Services.Configure<AdminOptions>(builder.Configuration.GetSection("Admin"));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "admin";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
        // The admin panel calls the API with fetch: answer with status codes instead of redirecting to a login page.
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();

if (string.IsNullOrEmpty(app.Configuration["Admin:Password"]))
    app.Logger.LogWarning("Admin:Password is not set, nobody can log in to /admin.");
if (!File.Exists(paths.DataFile))
    app.Logger.LogWarning("{DataFile} does not exist, the site starts with no projects.", paths.DataFile);

// One origin serves the public site and the admin panel (the frontend folder) and the project images (wwwroot),
// so uploaded images land where the site reads them from.
// wwwroot is left out of publish, so create it if a fresh deploy doesn't have it yet.
Directory.CreateDirectory(paths.Images);
var files = new CompositeFileProvider(new PhysicalFileProvider(paths.WebRoot));
app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = files,
    OnPrepareResponse = context =>
    {
        // Slides are renumbered when one is deleted, so browsers must revalidate them instead of guessing a cache lifetime.
        if (context.Context.Request.Path.StartsWithSegments("/projects/images"))
            context.Context.Response.Headers.CacheControl = "no-cache";
    },
});

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

var publicJson = new JsonSerializerOptions(SiteStore.FileJson) { WriteIndented = false };

// Replaces the old frontend/projects/data.js and the data half of homepage-projects.js:
// the site loads it with a plain <script> tag, so the existing frontend code keeps working unchanged.
app.MapGet("/api/data.js", async (HttpContext context, SiteStore store, ProjectImages images) =>
{
    var script = await store.ReadAsync(data =>
    {
        var projectData = data.ProjectData.ToDictionary(p => p.Key, p => new
        {
            p.Value.Name,
            p.Value.Description,
            p.Value.Category,
            NumberOfSlides = images.CountSlides(p.Value),
            p.Value.Location,
            p.Value.Publications,
        });

        return $"""
            window.promotionalMessages = {JsonSerializer.Serialize(data.PromotionalMessages, publicJson)};
            window.projectData = {JsonSerializer.Serialize(projectData, publicJson)};
            window.homepageProjects = {JsonSerializer.Serialize(data.HomepageProjects, publicJson)};
            window.projectsGrid = {JsonSerializer.Serialize(data.ProjectsGrid, publicJson)};
            """;
    });

    context.Response.Headers.CacheControl = "no-cache";
    return Results.Text(script, "text/javascript; charset=utf-8");
});

app.MapAdminApi();

app.Run();
