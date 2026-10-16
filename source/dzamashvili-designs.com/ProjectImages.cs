using ImageMagick;

namespace Backend;

/// <summary>
/// Project images live in wwwroot/projects/images/{category}/{name}/, served at /projects/images/...:
/// main.webp is the cover, and the slides are 1.webp, 2.webp, ... numberOfSlides.webp.
/// Callers hold the <see cref="SiteStore"/> lock while changing files.
/// </summary>
public sealed class ProjectImages(SitePaths paths)
{
    const string Cover = "main.webp";

    public string DirectoryOf(Project project) => Path.Combine(paths.Images, project.Category, project.Name);

    /// <summary>numberOfSlides: how many of 1.webp, 2.webp, ... exist without a gap.</summary>
    public int CountSlides(Project project)
    {
        var directory = DirectoryOf(project);
        if (!Directory.Exists(directory))
            return 0;

        var files = Directory.EnumerateFiles(directory, "*.webp").Select(Path.GetFileName).ToHashSet();
        var count = 0;
        while (files.Contains(SlideFile(count + 1)))
            count++;
        return count;
    }

    public bool HasCover(Project project) => File.Exists(Path.Combine(DirectoryOf(project), Cover));

    public void SetCover(Project project, byte[] webp)
    {
        Directory.CreateDirectory(DirectoryOf(project));
        File.WriteAllBytes(Path.Combine(DirectoryOf(project), Cover), webp);
    }

    public void AddSlide(Project project, byte[] webp)
    {
        Directory.CreateDirectory(DirectoryOf(project));
        File.WriteAllBytes(SlidePath(project, CountSlides(project) + 1), webp);
    }

    /// <summary>Deletes slide <paramref name="index"/> and moves the following slides up to close the gap.</summary>
    public bool DeleteSlide(Project project, int index)
    {
        var count = CountSlides(project);
        if (index < 1 || index > count)
            return false;

        File.Delete(SlidePath(project, index));
        for (var i = index + 1; i <= count; i++)
            File.Move(SlidePath(project, i), SlidePath(project, i - 1));
        return true;
    }

    /// <summary>Moves the image folder after a rename or category change. Returns false if the target folder is taken.</summary>
    public bool Move(Project from, Project to)
    {
        var source = DirectoryOf(from);
        var target = DirectoryOf(to);
        if (source == target || !Directory.Exists(source))
            return true;
        if (Directory.Exists(target))
            return false;

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        Directory.Move(source, target);
        return true;
    }

    public void Delete(Project project)
    {
        if (Directory.Exists(DirectoryOf(project)))
            Directory.Delete(DirectoryOf(project), recursive: true);
    }

    /// <summary>Converts an uploaded image to WebP, the same way the image optimizer did (quality 85).</summary>
    public static byte[] ToWebp(Stream image)
    {
        using var magick = new MagickImage(image);
        magick.AutoOrient();
        magick.Quality = 85;
        return magick.ToByteArray(MagickFormat.WebP);
    }

    string SlidePath(Project project, int index) => Path.Combine(DirectoryOf(project), SlideFile(index));

    static string SlideFile(int index) => $"{index}.webp";
}
