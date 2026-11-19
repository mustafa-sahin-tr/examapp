using System.Collections.Concurrent;
using ExamApp.Api.Data;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.SolutionRecordings;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #1226: çözüm kaydı — çizgi belgesi doğrulanır, ses türü dosyadan belirlenir, soru başına tek kayıt (yenisi eskinin
/// sesini siler), öğrenci yalnız bitmiş oturumda ya da örnek soruda görür. SQLite.
/// </summary>
public class QuestionSolutionRecordingServiceTests : IDisposable
{
    private const int TeacherUser = 9301;
    private const int StudentUser = 9302;
    private const int OtherStudentUser = 9303;
    private static readonly byte[] WebM = [0x1A, 0x45, 0xDF, 0xA3, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
    private static readonly byte[] Mp4 = [0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'M', (byte)'4', (byte)'A', (byte)' ', 0, 0];
    private const string Strokes = """{"v":1,"width":1000,"height":560,"strokes":[{"c":"red","t":1200,"p":[10,20,0,30,40,16]},{"c":"default","t":300,"e":900,"p":[1,2,0]}]}""";

    private readonly TestDb _db = TestDb.Create();
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();
    private readonly ConcurrentDictionary<string, byte[]> _objects = new();
    private int _questionId, _exampleQuestionId, _finishedRow, _startedRow, _startedExampleRow;

    public QuestionSolutionRecordingServiceTests()
    {
        _minio.UploadFileAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(ci =>
            {
                using var copy = new MemoryStream();
                ci.ArgAt<Stream>(0).CopyTo(copy);
                var url = "/img/exam-questions/" + ci.ArgAt<string>(1);
                _objects[url] = copy.ToArray();
                return Task.FromResult(url);
            });
        _minio.GetFileStreamAsync(Arg.Any<string>())
            .Returns(ci => Task.FromResult<Stream?>(_objects.TryGetValue(ci.ArgAt<string>(0), out var b) ? new MemoryStream(b) : null));
        _minio.DeleteFileByUrlAsync(Arg.Any<string>()).Returns(ci => Task.FromResult(_objects.TryRemove(ci.ArgAt<string>(0), out _)));
        SeedAsync().GetAwaiter().GetResult();
    }

    public void Dispose() => _db.Dispose();

    private QuestionSolutionRecordingService NewService(AppDbContext ctx) =>
        new(ctx, _minio, NullLogger<QuestionSolutionRecordingService>.Instance);

    private async Task SeedAsync()
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5. Sınıf" };
        ctx.Add(grade);
        await ctx.SaveChangesAsync();
        ctx.SetCurrentUser(TeacherUser);
        var ws = new Worksheet { Name = "Kesirler", Description = "", GradeId = grade.Id };
        var openWs = new Worksheet { Name = "Kesirler 2", Description = "", GradeId = grade.Id };
        var question = new Question { Text = "Soru", Point = 1 };
        var example = new Question { Text = "Örnek", Point = 0, IsExample = true };
        ctx.AddRange(ws, openWs, question, example);
        await ctx.SaveChangesAsync();
        (_questionId, _exampleQuestionId) = (question.Id, example.Id);
        var wq = new WorksheetQuestion { TestId = ws.Id, QuestionId = question.Id, Order = 1 };
        var wqOpen = new WorksheetQuestion { TestId = openWs.Id, QuestionId = question.Id, Order = 1 };
        var wqExample = new WorksheetQuestion { TestId = openWs.Id, QuestionId = example.Id, Order = 2 };
        var student = new Student { UserId = StudentUser, StudentNumber = "s1", SchoolName = "s", GradeId = grade.Id };
        ctx.AddRange(wq, wqOpen, wqExample, student);
        await ctx.SaveChangesAsync();
        var finished = new WorksheetInstance { StudentId = student.Id, WorksheetId = ws.Id, Status = WorksheetInstanceStatus.Completed, StartTime = DateTime.UtcNow.AddHours(-2), EndTime = DateTime.UtcNow.AddHours(-1) };
        var started = new WorksheetInstance { StudentId = student.Id, WorksheetId = openWs.Id, Status = WorksheetInstanceStatus.Started, StartTime = DateTime.UtcNow.AddMinutes(-5) };
        ctx.AddRange(finished, started);
        await ctx.SaveChangesAsync();
        var finishedRow = new WorksheetInstanceQuestion { WorksheetInstanceId = finished.Id, WorksheetQuestionId = wq.Id };
        var startedRow = new WorksheetInstanceQuestion { WorksheetInstanceId = started.Id, WorksheetQuestionId = wqOpen.Id };
        var startedExampleRow = new WorksheetInstanceQuestion { WorksheetInstanceId = started.Id, WorksheetQuestionId = wqExample.Id };
        ctx.AddRange(finishedRow, startedRow, startedExampleRow);
        await ctx.SaveChangesAsync();
        (_finishedRow, _startedRow, _startedExampleRow) = (finishedRow.Id, startedRow.Id, startedExampleRow.Id);
    }

    private async Task<SolutionRecordingSaveResult> SaveAsync(byte[]? audio = null, string? strokes = Strokes, int durationMs = 60_000,
        string? contentType = "audio/webm;codecs=opus", int? questionId = null)
    {
        await using var ctx = _db.NewContext();
        audio ??= WebM;
        using var stream = new MemoryStream(audio);
        return await NewService(ctx).SaveAsync(questionId ?? _questionId, TeacherUser, strokes, durationMs, stream, audio.Length, contentType, default);
    }

    [Fact]
    public async Task Save_stores_the_normalized_strokes_and_the_audio()
    {
        var result = await SaveAsync();

        result.Error.ShouldBe(SolutionRecordingError.None);
        result.Recording!.DurationMs.ShouldBe(60_000);
        result.Recording.AudioContentType.ShouldBe("audio/webm");
        // Çizgiler başlangıç zamanına göre sıralanır.
        result.Recording.Strokes.GetProperty("strokes")[0].GetProperty("t").GetInt32().ShouldBe(300);
        result.Recording.Strokes.GetProperty("strokes")[0].GetProperty("e").GetInt32().ShouldBe(900);
        result.Recording.Strokes.GetProperty("strokes")[1].TryGetProperty("e", out _).ShouldBeFalse();

        await using var ctx = _db.NewContext();
        var audio = await NewService(ctx).GetAudioAsync(_questionId, default);
        audio!.ContentType.ShouldBe("audio/webm");
        ((MemoryStream)audio.Content).ToArray().ShouldBe(WebM);
        _objects.Keys.ShouldHaveSingleItem().ShouldStartWith($"/img/exam-questions/solution-audio/{_questionId}/");
    }

    [Fact]
    public async Task Saving_again_replaces_the_recording_and_removes_the_old_audio()
    {
        await SaveAsync();
        var first = _objects.Keys.Single();

        var result = await SaveAsync(audio: Mp4, contentType: "audio/mp4", durationMs: 30_000);

        result.Recording!.AudioContentType.ShouldBe("audio/mp4");
        _objects.Keys.ShouldHaveSingleItem().ShouldNotBe(first);
        await using var ctx = _db.NewContext();
        (await ctx.QuestionSolutionRecordings.CountAsync()).ShouldBe(1);
        (await ctx.QuestionSolutionRecordings.SingleAsync()).DurationMs.ShouldBe(30_000);
    }

    [Theory]
    [InlineData("""{"v":2,"width":1000,"height":560,"strokes":[]}""")]
    [InlineData("""{"v":1,"width":800,"height":560,"strokes":[]}""")]
    [InlineData("""{"v":1,"width":1000,"height":560,"strokes":[{"c":"pink","t":0,"p":[1,2,0]}]}""")]
    [InlineData("""{"v":1,"width":1000,"height":560,"strokes":[{"c":"red","t":0,"p":[1,2]}]}""")]
    [InlineData("""{"v":1,"width":1000,"height":560,"strokes":[{"c":"red","t":70000,"p":[1,2,0]}]}""")]
    [InlineData("""{"v":1,"width":1000,"height":560,"strokes":[{"c":"red","t":500,"e":100,"p":[1,2,0]}]}""")]
    [InlineData("""{"v":1,"width":1000,"height":560,"strokes":[{"c":"red","t":0,"p":[1001,2,0]}]}""")]
    [InlineData("not json")]
    [InlineData(null)]
    public async Task Save_rejects_strokes_outside_the_schema(string? strokes)
    {
        (await SaveAsync(strokes: strokes)).Error.ShouldBe(SolutionRecordingError.InvalidStrokes);
        _objects.ShouldBeEmpty();
    }

    [Fact]
    public async Task Save_rejects_audio_by_content_and_duration_by_limit()
    {
        (await SaveAsync(audio: "<html><script>alert(1)</script></html>"u8.ToArray())).Error.ShouldBe(SolutionRecordingError.InvalidAudio);
        (await SaveAsync(contentType: "image/png")).Error.ShouldBe(SolutionRecordingError.InvalidAudio);
        (await SaveAsync(durationMs: 0)).Error.ShouldBe(SolutionRecordingError.InvalidDuration);
        (await SaveAsync(durationMs: QuestionSolutionRecordingService.MaxDurationMs + 60_000)).Error.ShouldBe(SolutionRecordingError.InvalidDuration);
        _objects.ShouldBeEmpty();
    }

    [Fact]
    public async Task Delete_removes_the_row_and_the_audio()
    {
        await SaveAsync();

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).DeleteAsync(_questionId, default)).ShouldBeTrue();

        _objects.ShouldBeEmpty();
        await using var read = _db.NewContext();
        (await NewService(read).GetAsync(_questionId, default)).ShouldBeNull();
        (await NewService(read).DeleteAsync(_questionId, default)).ShouldBeFalse();
        // Silinen kaydın yerine yenisi yapılabilir (tekil indeks yumuşak silinmiş satıra takılmaz).
        (await SaveAsync()).Error.ShouldBe(SolutionRecordingError.None);
    }

    [Fact]
    public async Task A_student_reaches_the_solution_only_after_finishing_or_on_an_example_question()
    {
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);

        (await service.ResolveForStudentAsync(StudentUser, _finishedRow, default)).ShouldBe(_questionId);
        (await service.ResolveForStudentAsync(StudentUser, _startedRow, default)).ShouldBeNull();
        (await service.ResolveForStudentAsync(StudentUser, _startedExampleRow, default)).ShouldBe(_exampleQuestionId);
        (await service.ResolveForStudentAsync(OtherStudentUser, _finishedRow, default)).ShouldBeNull();
        (await service.ResolveForStudentAsync(StudentUser, 999_999, default)).ShouldBeNull();
    }

    [Fact]
    public async Task Lists_which_questions_have_a_recording()
    {
        await SaveAsync();
        await using var ctx = _db.NewContext();

        (await NewService(ctx).QuestionIdsWithRecordingAsync([_questionId, _exampleQuestionId], default)).ShouldBe([_questionId]);
    }
}
