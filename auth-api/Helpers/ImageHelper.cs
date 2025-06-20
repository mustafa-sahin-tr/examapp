using System;
using System.Text.RegularExpressions;

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
}
