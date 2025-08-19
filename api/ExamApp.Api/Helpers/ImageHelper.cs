using System;
using System.Text.RegularExpressions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.PixelFormats;

namespace ExamApp.Api.Helpers;

public class ImageHelper
{
    public bool IsBase64String(string input)
    {
        if (string.IsNullOrEmpty(input)) return false;

        // Base64 formatını tespit etmek için regex
        string base64Pattern = @"^data:image\/(png|jpeg|jpg|gif|bmp|webp);base64,[A-Za-z0-9+/=]+$";
        return Regex.IsMatch(input, base64Pattern);
    }

    public bool IsValidImageUrl(string input)
    {
        if (string.IsNullOrEmpty(input)) return false;

        // URL olup olmadığını anlamak için regex
        string urlPattern = @"^(http|https):\/\/.*\.(jpeg|jpg|png|gif|bmp|webp)(\?.*)?$";
        return Regex.IsMatch(input, urlPattern, RegexOptions.IgnoreCase);
    }

    // CropImage: Crops the given image bytes using the provided rectangle and returns the cropped image bytes.
    public byte[] CropImage(byte[] imageBytes, int x, int y, int width, int height)
    {
        using var inputStream = new MemoryStream(imageBytes);
        using var image = Image.Load<Rgba32>(inputStream);
        image.Mutate(ctx => ctx.Crop(new SixLabors.ImageSharp.Rectangle(x, y, width, height)));
        using var outputStream = new MemoryStream();
        image.Save(outputStream, GetEncoder(image));
        return outputStream.ToArray();
    }

    public async Task<Stream?> ResizeImageAsync(Stream imageStream, double scale)
    {
        // Cross-platform implementation using ImageSharp
        using var image = await Image.LoadAsync<Rgba32>(imageStream);
        int newWidth = (int)(image.Width * scale);
        int newHeight = (int)(image.Height * scale);

        image.Mutate(x => x.Resize(newWidth, newHeight));
        var outputStream = new MemoryStream();
        await image.SaveAsync(outputStream, GetEncoder(image));
        outputStream.Position = 0;
        return outputStream;
    }
    
    private static SixLabors.ImageSharp.Formats.IImageEncoder GetEncoder(Image<Rgba32> image)
    {
        var format = image.Metadata.DecodedImageFormat?.Name?.ToLowerInvariant();
        return format switch
        {
            "jpeg" or "jpg" => new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder(),
            "png" => new SixLabors.ImageSharp.Formats.Png.PngEncoder(),
            "gif" => new SixLabors.ImageSharp.Formats.Gif.GifEncoder(),
            "bmp" => new SixLabors.ImageSharp.Formats.Bmp.BmpEncoder(),
            "webp" => new SixLabors.ImageSharp.Formats.Webp.WebpEncoder(),
            _ => new SixLabors.ImageSharp.Formats.Png.PngEncoder()
        };
    }
}
