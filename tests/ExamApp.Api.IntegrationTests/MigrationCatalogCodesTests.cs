using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Services.Catalog;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// Gerçek Postgres üzerinde: <c>AddCatalogCodes</c> SQL'i <see cref="CatalogCode"/> ile aynı kodu üretir; dışa aktarılan
/// katalog boş veritabanına yüklenince aynı katalog oluşur. Ayrı geçici veritabanı kullanır (#236 migration testiyle aynı desen).
/// </summary>
[Collection(IntegrationCollection.Name)]
public class MigrationCatalogCodesTests : IntegrationTestBase
{
    private const string PreviousMigration = "20261103043147_AddLearningOutcomeVersionAndQuestionLinks";

    public MigrationCatalogCodesTests(IntegrationApiFactory factory) : base(factory)
    {
    }

    private sealed class NoOpJobs : IBackgroundJobClient
    {
        public string Create(Job job, IState state) => "test";

        public bool ChangeState(string jobId, IState state, string expectedState) => true;
    }

    private string CreateTemporaryDatabase()
    {
        var builder = new NpgsqlConnectionStringBuilder(Factory.ConnectionString);
        var name = $"exam_test_migration_{Guid.NewGuid().ToString("N")[..8]}";
        builder.Database = "postgres";
        using (var conn = new NpgsqlConnection(builder.ToString()))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE \"{name}\"";
            cmd.ExecuteNonQuery();
        }

        builder.Database = name;
        return builder.ToString();
    }

    private static void DropTemporaryDatabase(string connStr)
    {
        var builder = new NpgsqlConnectionStringBuilder(connStr);
        var name = builder.Database;
        builder.Database = "postgres";
        using var conn = new NpgsqlConnection(builder.ToString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)";
        cmd.ExecuteNonQuery();
    }

    private static AppDbContext NewContext(string connStr) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connStr).Options);

    private static async Task<int> InsertAsync(AppDbContext ctx, string sql, params object[] args) =>
        (await ctx.Database.SqlQueryRaw<int>(sql, args).ToListAsync()).Single();

    [Theory]
    [InlineData("5. Sınıf")]
    [InlineData("T.C. İnkılap Tarihi ve Atatürkçülük")]
    [InlineData("  Çağdaş / Şiir -- Öğüt  ")]
    [InlineData("Modül 1: Hücre (ÂÎÛ âîû)")]
    [InlineData("I ı İ i Ğ ğ")]
    [InlineData("--- Çok uzun bir ad: Düzlemde iki veya üç doğrunun birbirine göre durumuna bağlı olarak oluşabilecek açılara dair çıkarım yapabilme becerisi ---")]
    public async Task Migration_sql_gives_the_same_code_as_the_service(string name)
    {
        var connStr = CreateTemporaryDatabase();
        try
        {
            await using var ctx = NewContext(connStr);
            await ctx.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            var gradeId = await InsertAsync(ctx, """
                INSERT INTO "Grades" ("Name", "CreateTime", "IsDeleted") VALUES ({0}, now(), FALSE) RETURNING "Id" AS "Value"
                """, "Deneme Sınıfı");
            var subjectId = await InsertAsync(ctx, """
                INSERT INTO "Subjects" ("Name", "CreateTime", "IsDeleted") VALUES ({0}, now(), FALSE) RETURNING "Id" AS "Value"
                """, "Deneme Dersi");
            var topicId = await InsertAsync(ctx, """
                INSERT INTO "Topics" ("Name", "GradeId", "SubjectId", "CreateTime", "IsDeleted") VALUES ({0}, {1}, {2}, now(), FALSE) RETURNING "Id" AS "Value"
                """, name, gradeId, subjectId);
            var subTopicId = await InsertAsync(ctx, """
                INSERT INTO "SubTopics" ("Name", "TopicId", "CreateTime", "IsDeleted") VALUES ({0}, {1}, now(), FALSE) RETURNING "Id" AS "Value"
                """, name, topicId);

            await ctx.GetService<IMigrator>().MigrateAsync();

            var expectedTopic = CatalogCode.ForTopic("deneme-sinifi", "deneme-dersi", name);
            Assert.Equal(expectedTopic, (await ctx.Topics.AsNoTracking().SingleAsync(t => t.Id == topicId)).Code);
            Assert.Equal(CatalogCode.ForSubTopic(expectedTopic, name), (await ctx.SubTopics.AsNoTracking().SingleAsync(s => s.Id == subTopicId)).Code);
        }
        finally
        {
            DropTemporaryDatabase(connStr);
        }
    }

    [Fact]
    public async Task Rows_with_the_same_slug_get_numbered_and_active_rows_come_first()
    {
        var connStr = CreateTemporaryDatabase();
        try
        {
            await using var ctx = NewContext(connStr);
            await ctx.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            var deleted = await InsertAsync(ctx, """
                INSERT INTO "Subjects" ("Name", "CreateTime", "IsDeleted") VALUES ('Deneme Ders!', now(), TRUE) RETURNING "Id" AS "Value"
                """);
            var first = await InsertAsync(ctx, """
                INSERT INTO "Subjects" ("Name", "CreateTime", "IsDeleted") VALUES ('Deneme Ders?', now(), FALSE) RETURNING "Id" AS "Value"
                """);
            var second = await InsertAsync(ctx, """
                INSERT INTO "Subjects" ("Name", "CreateTime", "IsDeleted") VALUES ('Deneme Ders.', now(), FALSE) RETURNING "Id" AS "Value"
                """);

            await ctx.GetService<IMigrator>().MigrateAsync();

            var codes = await ctx.Subjects.IgnoreQueryFilters().AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Code);
            Assert.Equal("deneme-ders", codes[first]);
            Assert.Equal("deneme-ders-2", codes[second]);
            Assert.Equal("deneme-ders-3", codes[deleted]);
        }
        finally
        {
            DropTemporaryDatabase(connStr);
        }
    }

    [Fact]
    public async Task Exported_catalog_loads_into_an_empty_database_and_a_second_load_changes_nothing()
    {
        var sourceConn = CreateTemporaryDatabase();
        var targetConn = CreateTemporaryDatabase();
        try
        {
            await using var source = NewContext(sourceConn);
            await source.GetService<IMigrator>().MigrateAsync();
            // Migration'lar katalog tohumladığı için temiz veritabanı boş değildir: tohumla çakışmayan adlar kullanılır.
            var grade = new Grade { Name = "Deneme Sınıfı", Code = "deneme-sinifi" };
            var subject = new Subject { Name = "Deneme Dersi", Code = "deneme-dersi" };
            source.AddRange(grade, subject);
            await source.SaveChangesAsync();
            source.GradeSubjects.Add(new GradeSubject { GradeId = grade.Id, SubjectId = subject.Id });
            var topic = new Topic { Name = "Okuma", Code = "deneme-sinifi.deneme-dersi.okuma", GradeId = grade.Id, SubjectId = subject.Id };
            source.Topics.Add(topic);
            await source.SaveChangesAsync();
            var subTopic = new SubTopic { Name = "Hece", Code = "deneme-sinifi.deneme-dersi.okuma.hece", TopicId = topic.Id };
            source.SubTopics.Add(subTopic);
            await source.SaveChangesAsync();
            source.LearningOutcomes.Add(new LearningOutcome
            {
                Code = "T.1.1.1",
                Description = "Heceleri okuyabilme",
                GradeId = grade.Id,
                SubjectId = subject.Id,
                SubTopicId = subTopic.Id,
                CurriculumVersion = CurriculumVersion.Maarif,
            });
            await source.SaveChangesAsync();
            var exported = await new CatalogTransferService(source, new NoOpJobs()).ExportAsync();

            await using var target = NewContext(targetConn);
            await target.GetService<IMigrator>().MigrateAsync();
            var service = new CatalogTransferService(target, new NoOpJobs());

            var first = await service.ImportAsync(exported, dryRun: false, userId: 1);
            var second = await service.ImportAsync(exported, dryRun: false, userId: 1);

            Assert.Empty(first.Errors);
            Assert.Equal(1, first.Topics.Created);
            Assert.Equal(1, first.SubTopics.Created);
            Assert.Equal(1, first.LearningOutcomes.Created);
            Assert.Empty(second.Errors);
            Assert.Equal(0, second.Topics.Created + second.Topics.Updated + second.SubTopics.Created + second.SubTopics.Updated);
            Assert.Equal(1, second.LearningOutcomes.Unchanged);
            Assert.Equal(JsonSerializer.Serialize(exported), JsonSerializer.Serialize(await service.ExportAsync()));
        }
        finally
        {
            DropTemporaryDatabase(sourceConn);
            DropTemporaryDatabase(targetConn);
        }
    }
}
