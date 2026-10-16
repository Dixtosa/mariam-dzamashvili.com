using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ImageMagick;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Backend;

public sealed class AdminOptions
{
    public string Password { get; set; } = "";
}

public sealed record LoginRequest(string? Password);

public sealed record ProjectInput(
    string? Name, string? Category, string? Location, string? Description, string? Publications,
    bool ShowInGrid, bool ShowOnHomepage);

public sealed record ProjectSummary(
    string Name, string Category, string? Location, string Description, string? Publications,
    int NumberOfSlides, bool HasCover, bool ShowInGrid, bool ShowOnHomepage);

/// <summary>JSON API behind the /admin panel.</summary>
public static class AdminApi
{
    const long MaxUploadBytes = 100 * 1024 * 1024;

    // A project name is both a folder name and a segment of the #/projects/{category}/{name}/ url.
    const string ForbiddenNameChars = "/\\:*?\"<>|#%";

    public static void MapAdminApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/admin").RequireAuthorization();

        api.MapPost("/login", LoginAsync).AllowAnonymous().RequireRateLimiting("login");

        api.MapPost("/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync();
            return Results.NoContent();
        });

        api.MapGet("/state", (SiteStore store, ProjectImages images) => store.ReadAsync(data => new
        {
            Categories = Categories.All,
            data.PromotionalMessages,
            data.HomepageProjects,
            data.ProjectsGrid,
            Projects = data.ProjectData.Values.Select(p => Summarize(p, data, images)).ToList(),
        }));

        api.MapPut("/promotional-messages", ([FromBody] string?[] messages, SiteStore store) => store.UpdateAsync(data =>
        {
            data.PromotionalMessages = [.. messages.Select(m => m?.Trim()).OfType<string>().Where(m => m.Length > 0)];
            return data.PromotionalMessages;
        }));

        api.MapPut("/homepage-projects", ([FromBody] string?[] names, SiteStore store) =>
            store.UpdateAsync(data => SetProjectList(data, names, list => data.HomepageProjects = list)));

        api.MapPut("/projects-grid", ([FromBody] string?[] names, SiteStore store) =>
            store.UpdateAsync(data => SetProjectList(data, names, list => data.ProjectsGrid = list)));

        api.MapPost("/projects", async (ProjectInput input, SiteStore store, ProjectImages images) =>
        {
            var project = ToProject(input);
            if (Validate(project) is { } error)
                return error;

            return await store.UpdateAsync(data =>
            {
                if (data.ProjectData.ContainsKey(project.Name))
                    return Problem(StatusCodes.Status409Conflict, "A project with this name already exists.");

                data.ProjectData[project.Name] = project;
                SetMembership(data, project.Name, input);
                return Results.Ok(Summarize(project, data, images));
            });
        });

        api.MapPut("/projects/{name}", async (string name, ProjectInput input, SiteStore store, ProjectImages images) =>
        {
            var project = ToProject(input);
            if (Validate(project) is { } error)
                return error;

            return await store.UpdateAsync(data =>
            {
                if (!data.ProjectData.TryGetValue(name, out var existing))
                    return Results.NotFound();
                if (project.Name != name && data.ProjectData.ContainsKey(project.Name))
                    return Problem(StatusCodes.Status409Conflict, "A project with this name already exists.");
                if (!images.Move(existing, project))
                    return Problem(StatusCodes.Status409Conflict, $"The image folder {project.Category}/{project.Name} already exists.");

                // Rebuild the dictionary so a rename keeps the project's position.
                data.ProjectData = data.ProjectData.ToDictionary(
                    p => p.Key == name ? project.Name : p.Key,
                    p => p.Key == name ? project : p.Value);
                Rename(data.HomepageProjects, name, project.Name);
                Rename(data.ProjectsGrid, name, project.Name);
                SetMembership(data, project.Name, input);
                return Results.Ok(Summarize(project, data, images));
            });
        });

        api.MapDelete("/projects/{name}", (string name, SiteStore store, ProjectImages images) => store.UpdateAsync(data =>
        {
            if (!data.ProjectData.Remove(name, out var project))
                return Results.NotFound();

            data.HomepageProjects.RemoveAll(n => n == name);
            data.ProjectsGrid.RemoveAll(n => n == name);
            images.Delete(project);
            return Results.NoContent();
        }));

        api.MapPut("/projects/{name}/cover", (string name, HttpRequest request, SiteStore store, ProjectImages images) =>
            UploadAsync(name, request, store, images, images.SetCover));

        api.MapPost("/projects/{name}/slides", (string name, HttpRequest request, SiteStore store, ProjectImages images) =>
            UploadAsync(name, request, store, images, images.AddSlide));

        api.MapDelete("/projects/{name}/slides/{index:int}", (string name, int index, SiteStore store, ProjectImages images) => store.UpdateAsync(data =>
        {
            if (!data.ProjectData.TryGetValue(name, out var project) || !images.DeleteSlide(project, index))
                return Results.NotFound();

            return Results.Ok(Summarize(project, data, images));
        }));
    }

    static async Task<IResult> LoginAsync(LoginRequest request, HttpContext context, IOptions<AdminOptions> options)
    {
        var expected = options.Value.Password;
        if (string.IsNullOrEmpty(expected) || !CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(request.Password ?? "")),
                SHA256.HashData(Encoding.UTF8.GetBytes(expected))))
            return Results.Unauthorized();

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "admin")], CookieAuthenticationDefaults.AuthenticationScheme);
        await context.SignInAsync(new ClaimsPrincipal(identity));
        return Results.NoContent();
    }

    static async Task<IResult> UploadAsync(string name, HttpRequest request, SiteStore store, ProjectImages images, Action<Project, byte[]> save)
    {
        // Photos can be bigger than Kestrel's default 30 MB request limit.
        if (request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
            limit.MaxRequestBodySize = MaxUploadBytes;

        if (!request.HasFormContentType || (await request.ReadFormAsync()).Files["file"] is not { } file)
            return Problem(StatusCodes.Status400BadRequest, "No file was uploaded.");

        // Convert before taking the store lock: it is the slow part.
        byte[] webp;
        try
        {
            await using var stream = file.OpenReadStream();
            webp = ProjectImages.ToWebp(stream);
        }
        catch (MagickException)
        {
            return Problem(StatusCodes.Status400BadRequest, $"{file.FileName} is not an image that can be converted to WebP.");
        }

        return await store.UpdateAsync(data =>
        {
            if (!data.ProjectData.TryGetValue(name, out var project))
                return Results.NotFound();

            save(project, webp);
            return Results.Ok(Summarize(project, data, images));
        });
    }

    static IResult SetProjectList(SiteData data, string?[] names, Action<List<string>> set)
    {
        if (names.FirstOrDefault(n => n is null || !data.ProjectData.ContainsKey(n)) is { } unknown)
            return Problem(StatusCodes.Status400BadRequest, $"Unknown project: {unknown}");

        var list = names.OfType<string>().Distinct().ToList();
        set(list);
        return Results.Ok(list);
    }

    // The editor's checkboxes add a project to the end of a list or take it out; the lists themselves set the order.
    static void SetMembership(SiteData data, string name, ProjectInput input)
    {
        SetMembership(data.ProjectsGrid, name, input.ShowInGrid);
        SetMembership(data.HomepageProjects, name, input.ShowOnHomepage);
    }

    static void SetMembership(List<string> list, string name, bool member)
    {
        if (!member)
            list.RemoveAll(n => n == name);
        else if (!list.Contains(name))
            list.Add(name);
    }

    static void Rename(List<string> list, string from, string to)
    {
        for (var i = 0; i < list.Count; i++)
            if (list[i] == from)
                list[i] = to;
    }

    static Project ToProject(ProjectInput input) => new()
    {
        Name = input.Name?.Trim() ?? "",
        Category = input.Category ?? "",
        Location = NullIfBlank(input.Location),
        Description = input.Description?.Trim() ?? "",
        Publications = NullIfBlank(input.Publications),
    };

    static IResult? Validate(Project project)
    {
        if (project.Name.Length == 0)
            return Problem(StatusCodes.Status400BadRequest, "Name is required.");
        if (project.Name.Any(c => char.IsControl(c) || ForbiddenNameChars.Contains(c)) || project.Name.EndsWith('.'))
            return Problem(StatusCodes.Status400BadRequest, $"Name can't contain any of {ForbiddenNameChars} or end with a dot.");
        if (!Categories.Exists(project.Category))
            return Problem(StatusCodes.Status400BadRequest, "Unknown category.");
        return null;
    }

    static ProjectSummary Summarize(Project project, SiteData data, ProjectImages images) => new(
        project.Name, project.Category, project.Location, project.Description, project.Publications,
        images.CountSlides(project), images.HasCover(project),
        data.ProjectsGrid.Contains(project.Name), data.HomepageProjects.Contains(project.Name));

    static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    static IResult Problem(int statusCode, string detail) => Results.Problem(detail, statusCode: statusCode);
}
