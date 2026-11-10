using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Entitlements;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// Issue #1108 (epic #1118): öğrenci planı sınıf bandı, kapsam (bütün dersler / tek ders) ve süreyle satılır. Fiyat seed tablosundan,
/// sınıfa özel satır bandın önüne geçer; kardeş indirimi ikinci çocuğa; sınıfı olmayan çocuk eskisi gibi aylık, bütün dersler.
/// </summary>
public class StudentPlanPricingEndpointsTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private sealed record Catalog(int Grade7Id, int MathId, int ScienceId, int PhysicsId, int MusicId);

    // 7. sınıf (Middle) Matematik + Fen + Müzik (tek ders olarak satılmaz); Fizik yalnız 9. sınıfta.
    private Task<Catalog> SeedCatalogAsync() => WithDbAsync(async db =>
    {
        var grade7 = new Grade { Name = "7. Sınıf", Code = "7-sinif" };
        var grade9 = new Grade { Name = "9. Sınıf", Code = "9-sinif" };
        var math = new Subject { Name = "Matematik", Code = "matematik" };
        var science = new Subject { Name = "Fen Bilimleri", Code = "fen-bilimleri" };
        var physics = new Subject { Name = "Fizik", Code = "fizik" };
        var music = new Subject { Name = "Müzik", Code = "muzik" };
        db.AddRange(grade7, grade9, math, science, physics, music);
        await db.SaveChangesAsync();
        db.GradeSubjects.AddRange(
            new GradeSubject { GradeId = grade7.Id, SubjectId = math.Id },
            new GradeSubject { GradeId = grade7.Id, SubjectId = science.Id },
            new GradeSubject { GradeId = grade7.Id, SubjectId = music.Id },
            new GradeSubject { GradeId = grade9.Id, SubjectId = physics.Id });
        await db.SaveChangesAsync();
        return new Catalog(grade7.Id, math.Id, science.Id, physics.Id, music.Id);
    });

    private async Task<int> SeedChildAsync(int studentUserId, int parentUserId, int? gradeId)
    {
        var directory = Factory.Services.GetRequiredService<FakeUserDirectory>();
        directory.Add(new() { Id = studentUserId, KeycloakId = $"kc-{studentUserId}", FullName = "Ayşe Kaya" });
        directory.Add(new() { Id = parentUserId, KeycloakId = $"kc-{parentUserId}", FullName = "Veli" });
        var studentId = await WithDbAsync(async db =>
        {
            var student = new Student { UserId = studentUserId, StudentNumber = $"P{studentUserId}", GradeId = gradeId };
            db.Students.Add(student);
            if (!await db.Parents.AnyAsync(p => p.UserId == parentUserId))
                db.Parents.Add(new Parent { UserId = parentUserId });
            await db.SaveChangesAsync();
            return student.Id;
        });
        await SeedParentLinkAsync(parentUserId, studentUserId);
        return studentId;
    }

    private Task<HttpClient> ParentAsync(int userId) => ClientAsAsync(userId, "Parent", $"kc-{userId}", "Parent");

    private static string PlanUrl(int studentId) => $"/api/parent/children/{studentId}/plan";

    private static Task<HttpResponseMessage> BuyAsync(HttpClient client, int studentId, string planKey, int? subjectId = null,
        string? duration = null) =>
        client.PostAsJsonAsync($"{PlanUrl(studentId)}/checkout",
            new SubscriptionCheckoutDto { PlanKey = planKey, SubjectId = subjectId, Duration = duration });

    private Task<UserSubscriptionPlan> AssignmentAsync(int studentUser) =>
        WithDbAsync(db => db.UserSubscriptionPlans.AsNoTracking().SingleAsync(a => a.UserId == studentUser));

    [Fact]
    public async Task Parent_sees_band_prices_and_buys_a_single_subject_for_six_months_issue1108()
    {
        var catalog = await SeedCatalogAsync();
        const int studentUser = 48101, parentUser = 48102;
        var studentId = await SeedChildAsync(studentUser, parentUser, catalog.Grade7Id);
        var parent = await ParentAsync(parentUser);

        var plan = (await (await parent.GetAsync(PlanUrl(studentId))).Content.ReadFromJsonAsync<ChildSubscriptionPlanDto>(Json))!;
        plan.Offer.Band.ShouldBe(nameof(StudentGradeBand.Middle));
        plan.Offer.GradeLevel.ShouldBe(7);
        plan.Offer.SiblingDiscountPercent.ShouldBe(0);
        plan.Offer.Subjects.Select(s => s.Name).ShouldBe(["Fen Bilimleri", "Matematik"]);
        // Kart bandın aylık, bütün dersler fiyatını gösterir.
        plan.Plans.Select(p => p.MonthlyPrice).ShouldBe([199m, 349m, 549m]);
        // Pro tek ders satılmaz.
        plan.Offer.Prices.ShouldNotContain(p => p.PlanKey == SubscriptionPlan.StudentProKey && p.Scope == nameof(PlanScope.SingleSubject));
        plan.Offer.Prices.ShouldContain(p => p.PlanKey == SubscriptionPlan.StudentEssentialKey && p.Scope == nameof(PlanScope.SingleSubject)
            && p.Duration == nameof(PlanDuration.SixMonths) && p.Amount == 1131m);

        var bought = await BuyAsync(parent, studentId, SubscriptionPlan.StudentEssentialKey, catalog.MathId, "SixMonths");
        bought.StatusCode.ShouldBe(HttpStatusCode.OK);
        var after = (await bought.Content.ReadFromJsonAsync<ChildSubscriptionPlanDto>(Json))!;
        after.PlanKey.ShouldBe(SubscriptionPlan.StudentEssentialKey);
        after.ScopeSubjectId.ShouldBe(catalog.MathId);
        after.ScopeSubjectName.ShouldBe("Matematik");
        after.ValidToUtc!.Value.ShouldBeGreaterThan(DateTime.UtcNow.AddMonths(6).AddDays(-1));

        var assignment = await AssignmentAsync(studentUser);
        assignment.ScopeSubjectId.ShouldBe(catalog.MathId);
        assignment.Duration.ShouldBe(PlanDuration.SixMonths);
        assignment.PricePaid.ShouldBe(1131m); // 349 × 0.6 × 5.4 = 1130.76 → 1131
        assignment.DiscountPercent.ShouldBe(0);
    }

    [Fact]
    public async Task Options_that_are_not_on_sale_are_rejected_issue1108()
    {
        var catalog = await SeedCatalogAsync();
        const int studentUser = 48111, parentUser = 48112;
        var studentId = await SeedChildAsync(studentUser, parentUser, catalog.Grade7Id);
        var parent = await ParentAsync(parentUser);

        var rejected = new (string Plan, int? Subject, string? Duration)[]
        {
            (SubscriptionPlan.StudentProKey, catalog.MathId, null), // Pro tek ders yok
            (SubscriptionPlan.StudentBasicKey, catalog.PhysicsId, null), // çocuğun sınıfında olmayan ders
            (SubscriptionPlan.StudentBasicKey, catalog.MusicId, null), // sınıfın dersi ama tek ders olarak satılmaz
            (SubscriptionPlan.StudentBasicKey, 999_999, null),
            (SubscriptionPlan.StudentBasicKey, null, "Weekly"),
            (SubscriptionPlan.StudentBasicKey, null, "1"), // sayı ile enum
        };
        foreach (var (key, subject, duration) in rejected)
        {
            var res = await BuyAsync(parent, studentId, key, subject, duration);
            res.StatusCode.ShouldBe(HttpStatusCode.BadRequest, $"{key} {subject} {duration}");
            (await CodeAsync(res)).ShouldBe(SubscriptionPlanErrorCodes.InvalidOption);
        }

        // Yaz planı yalnız Haziran–Ağustos arasında.
        var summer = await BuyAsync(parent, studentId, SubscriptionPlan.StudentBasicKey, duration: "Summer");
        summer.StatusCode.ShouldBe(StudentPlanPricing.SummerOnSale(DateTime.UtcNow) ? HttpStatusCode.OK : HttpStatusCode.BadRequest);
        if (!StudentPlanPricing.SummerOnSale(DateTime.UtcNow))
            (await WithDbAsync(db => db.UserSubscriptionPlans.CountAsync(a => a.UserId == studentUser))).ShouldBe(0);
    }

    [Fact]
    public async Task A_grade_specific_price_wins_over_the_band_price_issue1108()
    {
        var catalog = await SeedCatalogAsync();
        const int studentUser = 48121, parentUser = 48122;
        var studentId = await SeedChildAsync(studentUser, parentUser, catalog.Grade7Id);
        await WithDbAsync(async db =>
        {
            db.SubscriptionPlanPrices.Add(new SubscriptionPlanPrice
            {
                PlanId = SubscriptionPlanSeed.StudentEssentialPlanId, Band = StudentGradeBand.Middle, GradeLevel = 7,
                Scope = PlanScope.AllSubjects, Duration = PlanDuration.Monthly, Price = 300m,
            });
            await db.SaveChangesAsync();
        });
        var parent = await ParentAsync(parentUser);

        var plan = (await parent.GetFromJsonAsync<ChildSubscriptionPlanDto>(PlanUrl(studentId), Json))!;
        plan.Plans.Single(p => p.Key == SubscriptionPlan.StudentEssentialKey).MonthlyPrice.ShouldBe(300m);

        (await BuyAsync(parent, studentId, SubscriptionPlan.StudentEssentialKey)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AssignmentAsync(studentUser)).PricePaid.ShouldBe(300m);
    }

    [Fact]
    public async Task The_second_child_of_a_paying_parent_gets_the_sibling_discount_issue1108()
    {
        var catalog = await SeedCatalogAsync();
        const int firstUser = 48131, secondUser = 48133, parentUser = 48132;
        var firstId = await SeedChildAsync(firstUser, parentUser, catalog.Grade7Id);
        var secondId = await SeedChildAsync(secondUser, parentUser, catalog.Grade7Id);
        var parent = await ParentAsync(parentUser);

        (await parent.GetFromJsonAsync<ChildSubscriptionPlanDto>(PlanUrl(secondId), Json))!.Offer.SiblingDiscountPercent.ShouldBe(0);
        (await BuyAsync(parent, firstId, SubscriptionPlan.StudentBasicKey)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AssignmentAsync(firstUser)).PricePaid.ShouldBe(199m);

        (await parent.GetFromJsonAsync<ChildSubscriptionPlanDto>(PlanUrl(secondId), Json))!.Offer.SiblingDiscountPercent.ShouldBe(20);
        (await BuyAsync(parent, secondId, SubscriptionPlan.StudentBasicKey, duration: "TwelveMonths")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var second = await AssignmentAsync(secondUser);
        second.DiscountPercent.ShouldBe(20);
        second.PricePaid.ShouldBe(1432.80m); // 199 × 9 = 1791 → %20 indirim
    }

    [Fact]
    public async Task A_child_without_a_grade_buys_monthly_all_subjects_at_the_card_price_issue1108()
    {
        var catalog = await SeedCatalogAsync();
        const int studentUser = 48141, parentUser = 48142;
        var studentId = await SeedChildAsync(studentUser, parentUser, gradeId: null);
        var parent = await ParentAsync(parentUser);

        var plan = (await parent.GetFromJsonAsync<ChildSubscriptionPlanDto>(PlanUrl(studentId), Json))!;
        plan.Offer.Band.ShouldBeNull();
        plan.Offer.Prices.ShouldBeEmpty();
        var cardPrice = plan.Plans.Single(p => p.Key == SubscriptionPlan.StudentBasicKey).MonthlyPrice;

        (await BuyAsync(parent, studentId, SubscriptionPlan.StudentBasicKey, catalog.MathId)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await BuyAsync(parent, studentId, SubscriptionPlan.StudentBasicKey, duration: "SixMonths")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await BuyAsync(parent, studentId, SubscriptionPlan.StudentBasicKey)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var assignment = await AssignmentAsync(studentUser);
        assignment.PricePaid.ShouldBe(cardPrice);
        assignment.Duration.ShouldBe(PlanDuration.Monthly);
        assignment.ScopeSubjectId.ShouldBeNull();
    }

    [Fact]
    public async Task Admin_edits_the_price_table_and_bad_rows_are_rejected_issue1108()
    {
        var admin = await ClientAsAsync(2, "Admin", "kc-price-admin", "Admin");
        var plans = (await admin.GetFromJsonAsync<List<SubscriptionPlanDto>>("/api/admin/subscription-plans", Json))!;
        var basic = plans.Single(p => p.Key == SubscriptionPlan.StudentBasicKey);
        basic.Prices!.Count.ShouldBe(40);
        plans.Single(p => p.Key == SubscriptionPlan.TeacherBasicKey).Prices.ShouldBeNull();

        UpdateSubscriptionPlanDto Request(List<SubscriptionPlanPriceRowDto> prices) => new()
        {
            Name = basic.Name, MonthlyPrice = basic.MonthlyPrice, IsActive = true, MonthlyLimits = basic.MonthlyLimits, Prices = prices,
        };
        var url = "/api/admin/subscription-plans/" + SubscriptionPlan.StudentBasicKey;

        // Yalnız iki satır: biri değişen band fiyatı, biri 6. sınıfa özel. Geri kalanı satıştan kalkar.
        var res = await admin.PutAsJsonAsync(url, Request(
        [
            new() { Band = "Middle", Scope = "AllSubjects", Duration = "Monthly", Price = 210m },
            new() { Band = "Middle", GradeLevel = 6, Scope = "AllSubjects", Duration = "Monthly", Price = 189.90m },
        ]));
        res.StatusCode.ShouldBe(HttpStatusCode.OK);
        var saved = (await res.Content.ReadFromJsonAsync<SubscriptionPlanDto>(Json))!;
        saved.Prices!.Select(p => (p.GradeLevel, p.Price)).ShouldBe([(null, 210m), (6, 189.90m)]);
        (await WithDbAsync(db => db.SubscriptionPlanChangeAudits.CountAsync(a => a.PlanKey == SubscriptionPlan.StudentBasicKey))).ShouldBe(1);
        // Audit'in "sonra"sı yalnız kalan iki satırı taşır.
        (await WithDbAsync(db => db.SubscriptionPlanChangeAudits.SingleAsync(a => a.PlanKey == SubscriptionPlan.StudentBasicKey)))
            .AfterJson.ShouldContain("\"GradeLevel\":6");

        // Sınıf satırını kaldırıp yeniden eklemek (silinmiş satır tekil indekste durur) çakışmaz.
        var bandOnly = new List<SubscriptionPlanPriceRowDto> { new() { Band = "Middle", Scope = "AllSubjects", Duration = "Monthly", Price = 210m } };
        (await admin.PutAsJsonAsync(url, Request(bandOnly))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var readded = await admin.PutAsJsonAsync(url, Request(
        [
            new() { Band = "Middle", Scope = "AllSubjects", Duration = "Monthly", Price = 210m },
            new() { Band = "Middle", GradeLevel = 6, Scope = "AllSubjects", Duration = "Monthly", Price = 179m },
        ]));
        readded.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await readded.Content.ReadFromJsonAsync<SubscriptionPlanDto>(Json))!.Prices!.Select(p => (p.GradeLevel, p.Price))
            .ShouldBe([(null, 210m), (6, 179m)]);

        var bad = new List<SubscriptionPlanPriceRowDto>[]
        {
            [new() { Band = "Middle", GradeLevel = 9, Scope = "AllSubjects", Duration = "Monthly", Price = 1m }], // 9 Middle değil
            [new() { Band = "Galaxy", Scope = "AllSubjects", Duration = "Monthly", Price = 1m }],
            [new() { Band = "Middle", Scope = "AllSubjects", Duration = "Monthly", Price = 0m }],
            [
                new() { Band = "Middle", Scope = "AllSubjects", Duration = "Monthly", Price = 1m },
                new() { Band = "Middle", Scope = "AllSubjects", Duration = "Monthly", Price = 2m },
            ],
        };
        foreach (var rows in bad)
        {
            var rejected = await admin.PutAsJsonAsync(url, Request(rows));
            rejected.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await CodeAsync(rejected)).ShouldBe("subscription.invalidPriceRow");
        }

        var teacher = plans.Single(p => p.Key == SubscriptionPlan.TeacherBasicKey);
        (await admin.PutAsJsonAsync("/api/admin/subscription-plans/" + SubscriptionPlan.TeacherBasicKey, new UpdateSubscriptionPlanDto
        {
            Name = teacher.Name, MonthlyPrice = teacher.MonthlyPrice, IsActive = true, MonthlyLimits = teacher.MonthlyLimits,
            Prices = [new() { Band = "Middle", Scope = "AllSubjects", Duration = "Monthly", Price = 1m }],
        })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString();
}
