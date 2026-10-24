using System;
using System.Text.RegularExpressions;
using SkiaSharp;

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
        using var image = SkiaImaging.DecodeOriented(imageBytes, out var format);
        var rect = SKRectI.Create(x, y, width, height);
        if (width <= 0 || height <= 0 || !SKRectI.Create(0, 0, image.Width, image.Height).Contains(rect))
            throw new ArgumentOutOfRangeException(nameof(imageBytes), "Crop rectangle must lie within the image.");

        using var cropped = SkiaImaging.CropResize(image, rect, width, height);
        return SkiaImaging.Encode(cropped, format);
    }

    public async Task<Stream?> ResizeImageAsync(Stream imageStream, double scale)
    {
        using var buffer = new MemoryStream();
        await imageStream.CopyToAsync(buffer);
        using var image = SkiaImaging.DecodeOriented(buffer.ToArray(), out var format);
        int newWidth = (int)(image.Width * scale);
        int newHeight = (int)(image.Height * scale);

        using var resized = SkiaImaging.CropResize(image, SKRectI.Create(0, 0, image.Width, image.Height), newWidth, newHeight);
        return new MemoryStream(SkiaImaging.Encode(resized, format));
    }
}
