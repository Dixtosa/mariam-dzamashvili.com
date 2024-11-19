using ImageOptimizer;
using System.Text.Json;

var location = "D:\\mariam-dzamashvili.com\\mariam-dzamashvili.com\\images";

Console.WriteLine("start");

//Parallel.ForEach(Directory.GetFiles(location, "*.jpg", SearchOption.AllDirectories), filePath =>
//{
//    if (!filePath.EndsWith(".webp"))
//        try
//        {
//            using var image = new MagickImage(filePath) { Quality = 85 };
//            image.Write($"{filePath}.webp", MagickFormat.WebP);
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine(ex.Message);
//        }
//    else
//        try
//        {
//            using var image = new MagickImage(filePath);
//            image.Write(filePath);
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine(ex.Message);
//        }
//});


//Directory.GetFiles(location, "*.webp", SearchOption.AllDirectories).ToList().ForEach(filePath =>
//{
//    if (filePath.EndsWith(".webp"))
//    {
//        if (filePath.Contains("main."))
//        {
//            System.IO.File.Move(filePath, filePath.Replace(".jpg", ""));
//        }
//        else
//        {
//            System.IO.File.Move(filePath, filePath.Replace(".jpg.", "."));
//            
//
//            var filename = Path.GetFileName(filePath);
//            var ext = filename.Split('.')[1];
//            filename = filename.Split('.')[0];
//
//            var dir = Path.GetDirectoryName(filePath);
//
//            filename = Regex.Replace(filename, "[a-z ]", "");
//            filename = filename.Replace("(", "");
//            filename = filename.Replace(")", "");
//
//            System.IO.File.Move(filePath, dir + "/" + filename + "." + ext);
//        }
//    }
//});


var dict = new Dictionary<string, Data>();

Directory.GetDirectories(location).ToList().ForEach(categoryDirectory =>
{
    Directory.GetDirectories(categoryDirectory).ToList().ForEach(projectCategory =>
    {
        var files = Directory.GetFiles(projectCategory).Where(f => !f.Contains("main")).ToList();

        for (int i = 0; i < files.Count; i++)
        {
            var filename = Path.GetFileName(files[i]);
            var ext = filename.Split('.')[1];
            filename = filename.Split('.')[0];

            System.IO.File.Move(files[i], projectCategory + "/temp" + (i + 1) + "." + ext);
        }

        files = Directory.GetFiles(projectCategory).Where(f => !f.Contains("main")).ToList();
        for (int i = 0; i < files.Count; i++)
        {
            var filename = Path.GetFileName(files[i]);
            var ext = filename.Split('.')[1];
            filename = filename.Split('.')[0];

            System.IO.File.Move(files[i], projectCategory + "/" + (i + 1) + "." + ext);
        }

        var name = new DirectoryInfo(projectCategory).Name;
        var category = new DirectoryInfo(categoryDirectory).Name;
        dict.Add(name, new Data
        {
            name = name,
            category = category,
            description = "",
            numberOfSlides = files.Count
        });
    });
});

await File.WriteAllTextAsync(location + "/data.json", JsonSerializer.Serialize(dict, new JsonSerializerOptions
{
    WriteIndented = true,
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
}));



Console.Write("done, click enter to exit: ");
