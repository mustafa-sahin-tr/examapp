using System.Reflection;
using ExamApp.Api.Controllers;
using ExamApp.Api.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace ExamApp.Api.Tests.Helpers;

/// <summary>Issue #424 (security review): <c>POST api/student/update-avatar</c> — yalnızca öğrenci, PNG/JPEG/WebP, ≤ 2 MB.</summary>
public class AvatarUploadTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 16, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1];
    private static readonly byte[] Webp = "RIFF\x24\0\0\0WEBPVP8 "u8.ToArray();

    private static IFormFile File(byte[] header, string contentType, long? length = null, string name = "a.exe")
    {
        var content = new byte[length ?? header.Length];
        Array.Copy(header, content, Math.Min(header.Length, content.Length));
        return new FormFile(new MemoryStream(content), 0, content.Length, "avatar", name)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    [Theory]
    [InlineData("png", "image/png", ".png")]
    [InlineData("jpeg", "image/jpeg", ".jpg")]
    [InlineData("webp", "image/webp", ".webp")]
    public async Task Accepts_supported_images_and_takes_extension_from_content(string kind, string contentType, string extension)
    {
        var header = kind switch { "png" => Png, "jpeg" => Jpeg, _ => Webp };
        var (error, ext) = await AvatarUpload.ValidateAsync(File(header, contentType));
        error.ShouldBe(AvatarUploadError.None);
        ext.ShouldBe(extension); // istemcinin "a.exe" adı kullanılmaz
    }

    [Fact]
    public async Task Rejects_missing_empty_oversized_and_spoofed_files()
    {
        (await AvatarUpload.ValidateAsync(null)).Error.ShouldBe(AvatarUploadError.Missing);
        (await AvatarUpload.ValidateAsync(File([], "image/png"))).Error.ShouldBe(AvatarUploadError.Missing);
        (await AvatarUpload.ValidateAsync(File(Png, "image/png", AvatarUpload.MaxBytes))).Error.ShouldBe(AvatarUploadError.None);
        (await AvatarUpload.ValidateAsync(File(Png, "image/png", AvatarUpload.MaxBytes + 1))).Error.ShouldBe(AvatarUploadError.TooLarge);
        // Desteklenmeyen tür, sahte Content-Type (imza uymuyor), tür uyuşmazlığı.
        (await AvatarUpload.ValidateAsync(File(Png, "image/gif"))).Error.ShouldBe(AvatarUploadError.UnsupportedType);
        (await AvatarUpload.ValidateAsync(File("<svg onload=x>"u8.ToArray(), "image/png"))).Error.ShouldBe(AvatarUploadError.UnsupportedType);
        (await AvatarUpload.ValidateAsync(File(Jpeg, "image/png"))).Error.ShouldBe(AvatarUploadError.UnsupportedType);
        (await AvatarUpload.ValidateAsync(File(Png, ""))).Error.ShouldBe(AvatarUploadError.UnsupportedType);
    }

    [Fact]
    public void Endpoint_is_student_only_and_size_limited()
    {
        var method = typeof(StudentController).GetMethod(nameof(StudentController.UpdateAvatar))!;
        method.GetCustomAttributes<AuthorizeAttribute>().ShouldHaveSingleItem().Roles.ShouldBe("Student");
        method.GetCustomAttribute<Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute>().ShouldNotBeNull();
    }
}
