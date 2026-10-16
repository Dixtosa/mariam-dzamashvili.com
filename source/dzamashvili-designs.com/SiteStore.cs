using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Backend;

public sealed record SitePaths(string WebRoot, string Data)
{
    public string Images => Path.Combine(WebRoot, "projects", "images");
    public string DataFile => Path.Combine(Data, "data.json");
    public string HomepageProjectsFile => Path.Combine(Data, "homepage-projects.json");
}

public sealed record Category(string Id, string Title);

public static class Categories
{
    public static readonly Category[] All =
    [
        new("apartment", "ბინა"),
        new("private-house", "სახლი"),
        new("commercial", "კომერციული"),
    ];

    public static bool Exists(string id) => All.Any(c => c.Id == id);
}

public sealed class Project
{
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string? Location { get; set; }
    public string Description { get; set; } = "";
    public string? Publications { get; set; }
}

public sealed class SiteData
{
    public List<string> PromotionalMessages { get; set; } = [];
    public Dictionary<string, Project> ProjectData { get; set; } = [];
    public List<string> HomepageProjects { get; set; } = [];
    public List<string> ProjectsGrid { get; set; } = [];
}

/// <summary>
/// Keeps the site data in two JSON files that mirror the old frontend scripts:
/// data.json (promotionalMessages, projectData) and homepage-projects.json (homepageProjects, projectsGrid).
/// All reads and writes go through one lock, which also serializes changes to project images.
/// </summary>
public sealed class SiteStore(SitePaths paths)
{
    public static readonly JsonSerializerOptions FileJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<T> ReadAsync<T>(Func<SiteData, T> read)
    {
        await _lock.WaitAsync();
        try
        {
            return read(await LoadAsync());
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<T> UpdateAsync<T>(Func<SiteData, T> update)
    {
        await _lock.WaitAsync();
        try
        {
            var data = await LoadAsync();
            var result = update(data);
            await SaveAsync(data);
            return result;
        }
        finally
        {
            _lock.Release();
        }
    }

    async Task<SiteData> LoadAsync()
    {
        var data = await ReadFileAsync<DataFile>(paths.DataFile);
        var homepage = await ReadFileAsync<HomepageProjectsFile>(paths.HomepageProjectsFile);
        return new SiteData
        {
            PromotionalMessages = data?.PromotionalMessages ?? [],
            ProjectData = data?.ProjectData ?? [],
            HomepageProjects = homepage?.HomepageProjects ?? [],
            ProjectsGrid = homepage?.ProjectsGrid ?? [],
        };
    }

    async Task SaveAsync(SiteData data)
    {
        Directory.CreateDirectory(paths.Data);
        await WriteFileAsync(paths.DataFile, new DataFile(data.PromotionalMessages, data.ProjectData));
        await WriteFileAsync(paths.HomepageProjectsFile, new HomepageProjectsFile(data.HomepageProjects, data.ProjectsGrid));
    }

    static async Task<T?> ReadFileAsync<T>(string path)
    {
        if (!File.Exists(path))
            return default;

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, FileJson);
    }

    static async Task WriteFileAsync<T>(string path, T value)
    {
        // Write to a temp file first so a crash mid-write can't leave a half-written data file.
        var temp = path + ".tmp";
        await using (var stream = File.Create(temp))
            await JsonSerializer.SerializeAsync(stream, value, FileJson);
        File.Move(temp, path, overwrite: true);
    }

    sealed record DataFile(List<string>? PromotionalMessages, Dictionary<string, Project>? ProjectData);

    sealed record HomepageProjectsFile(List<string>? HomepageProjects, List<string>? ProjectsGrid);
}
