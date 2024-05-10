using ImageMagick;

var location = "";

Console.WriteLine("start");

Parallel.ForEach(Directory.GetFiles(location, "*.jpg", SearchOption.AllDirectories), filePath =>
{
    if (!filePath.EndsWith(".webp"))
        try
        {
            using var image = new MagickImage(filePath) { Quality = 85 };
            image.Write($"{filePath}.webp", MagickFormat.WebP);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    else
        try
        {
            using var image = new MagickImage(filePath);
            image.Write(filePath);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
});

Console.Write("done, click enter to exit: ");


Console.ReadLine();