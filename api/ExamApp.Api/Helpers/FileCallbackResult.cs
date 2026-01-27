using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Net.Http.Headers;
using System;
using System.IO;
using System.Threading.Tasks;

namespace ExamApp.Api.Helpers;

public sealed class FileCallbackResult : FileResult
{
    private readonly Func<Stream, ActionContext, Task> _callback;

    public FileCallbackResult(string contentType, Func<Stream, ActionContext, Task> callback)
        : base(contentType)
    {
        _callback = callback ?? throw new ArgumentNullException(nameof(callback));
    }

    public override async Task ExecuteResultAsync(ActionContext context)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));

        var response = context.HttpContext.Response;
        response.ContentType = ContentType;

        if (!string.IsNullOrWhiteSpace(FileDownloadName))
        {
            var cd = new ContentDispositionHeaderValue("attachment")
            {
                FileNameStar = FileDownloadName,
            };
            response.Headers[HeaderNames.ContentDisposition] = cd.ToString();
        }

        await _callback(response.Body, context);
    }
}
