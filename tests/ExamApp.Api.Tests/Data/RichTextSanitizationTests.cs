using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Data;

/// <summary>
/// #552 security review L2: soru/pasaj HTML'i kayıt anında izin listesiyle temizlenir (Quill biçimi korunur);
/// <see cref="AppDbContext"/> hook'u her yazma yolunu (oluştur, güncelle, toplu kayıt, aktarım) kapsar.
/// </summary>
public class RichTextSanitizationTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();

    public void Dispose() => _db.Dispose();

    [Theory]
    [InlineData("<p>a</p><script>alert(1)</script>", "<p>a</p>")]
    [InlineData("<img src=\"x\" onerror=\"alert(1)\">", "<img src=\"x\">")]
    [InlineData("<a href=\"javascript:alert(1)\">x</a>", "<a>x</a>")]
    [InlineData("<a href=\"data:text/html;base64,PHNjcmlwdD4=\">x</a>", "<a>x</a>")]
    [InlineData("<img src=\"data:image/svg+xml;base64,PHN2Zz4=\">", "<img>")]
    [InlineData("<iframe src=\"https://evil.example\"></iframe><p>b</p>", "<p>b</p>")]
    [InlineData("<div class=\"cdk-overlay-backdrop ql-indent-1\">x</div>", "<div class=\"ql-indent-1\">x</div>")]
    public void Sanitize_RemovesExecutableContent(string input, string expected)
        => RichTextSanitizer.Sanitize(input).ShouldBe(expected);

    [Theory]
    [InlineData("<p class=\"ql-align-center\"><strong>K</strong> <em>i</em> <u>u</u> <s>s</s></p>")]
    [InlineData("<ol><li>bir</li></ol><ul><li>iki</li></ul>")]
    [InlineData("<h2>Başlık</h2><p><a href=\"https://hedefokul.com/x\">link</a></p>")]
    [InlineData("<p><img src=\"data:image/png;base64,iVBORw0KGgo=\"></p>")]
    [InlineData("<pre>kod &lt;b&gt;</pre>")]
    public void Sanitize_KeepsQuillFormatting(string html)
        => RichTextSanitizer.Sanitize(html).ShouldBe(html);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Sanitize_NullOrEmpty_Unchanged(string? html)
        => RichTextSanitizer.Sanitize(html).ShouldBe(html);

    [Fact]
    public async Task SaveChanges_SanitizesQuestionAndPassageHtml_OnAddAndUpdate()
    {
        int questionId;
        await using (var ctx = _db.NewContext())
        {
            var passage = new Passage { Title = "<b>T</b><script>x()</script>", Text = "<p onclick=\"x()\">P</p>" };
            var question = new Question
            {
                Text = "<p>S</p><img src=x onerror=alert(1)>",
                SubText = "<script>y()</script><u>alt</u>",
                PracticeCorrectAnswer = "<a href=\"javascript:z()\">c</a>",
                Passage = passage,
            };
            ctx.Questions.Add(question);
            await ctx.SaveChangesAsync();
            questionId = question.Id;
        }

        await using (var check = _db.NewContext())
        {
            var q = await check.Questions.Include(x => x.Passage).AsNoTracking().SingleAsync(x => x.Id == questionId);
            q.Text.ShouldBe("<p>S</p><img src=\"x\">");
            q.SubText.ShouldBe("<u>alt</u>");
            q.PracticeCorrectAnswer.ShouldBe("<a>c</a>");
            q.Passage!.Title.ShouldBe("<b>T</b>");
            q.Passage.Text.ShouldBe("<p>P</p>");
        }

        await using (var ctx = _db.NewContext())
        {
            var q = await ctx.Questions.SingleAsync(x => x.Id == questionId);
            q.Text = "<p>Yeni</p><svg onload=alert(1)></svg>";
            await ctx.SaveChangesAsync();
        }

        await using var final = _db.NewContext();
        (await final.Questions.AsNoTracking().SingleAsync(x => x.Id == questionId)).Text.ShouldBe("<p>Yeni</p>");
    }

    [Fact]
    public async Task SaveChanges_UnmodifiedHtmlColumns_AreNotRewritten()
    {
        int questionId;
        await using (var ctx = _db.NewContext())
        {
            var question = new Question { Text = "<p>S</p>" };
            ctx.Questions.Add(question);
            await ctx.SaveChangesAsync();
            questionId = question.Id;
        }

        // Eski (temizlenmemiş) satır: başka bir alan güncellenince HTML sütunu değişmiş sayılmaz, dokunulmaz.
        await using (var raw = _db.NewContext())
        {
            await raw.Questions.Where(x => x.Id == questionId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.SubText, "<script>legacy()</script>"));
        }

        await using (var ctx = _db.NewContext())
        {
            var q = await ctx.Questions.SingleAsync(x => x.Id == questionId);
            q.Point = 5;
            await ctx.SaveChangesAsync();
            ctx.Entry(q).Property(x => x.SubText).IsModified.ShouldBeFalse();
        }
    }
}
