using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class SplitInteractionSolution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InteractionSolution",
                table: "Questions",
                type: "text",
                nullable: true);

            // issue #570: mevcut planlardaki "solution" değeri yeni kolona taşınır ve plandan silinir. Geçersiz JSON / nesne
            // olmayan plan satır bazında atlanır (NOTICE); geçersiz JSON metninde 'solution' geçiyorsa plan NULL yapılır
            // (uygulamadaki InteractionPlanSolution.Split kapalı başarısızlık kuralı). Migration hiçbir satır yüzünden düşmez.
            // Soft-delete'li satırlar dahil.
            migrationBuilder.Sql(@"
DO $$
DECLARE
    r RECORD;
    plan jsonb;
BEGIN
    FOR r IN SELECT ""Id"", ""InteractionPlan"" FROM ""Questions""
             WHERE ""InteractionPlan"" IS NOT NULL AND btrim(""InteractionPlan"") <> ''
    LOOP
        BEGIN
            plan := r.""InteractionPlan""::jsonb;
        EXCEPTION WHEN others THEN
            -- Ayrıştırılamayan plan metninde 'solution' geçiyorsa (çözüm içeriyor olabilir) kapalı başarısızlık: plan NULL.
            IF r.""InteractionPlan"" ILIKE '%solution%' THEN
                UPDATE ""Questions"" SET ""InteractionPlan"" = NULL WHERE ""Id"" = r.""Id"";
                RAISE NOTICE 'issue #570: Question % InteractionPlan gecersiz JSON ve solution iceriyor, plan NULL yapildi', r.""Id"";
            ELSE
                RAISE NOTICE 'issue #570: Question % InteractionPlan gecersiz JSON, atlandi', r.""Id"";
            END IF;
            CONTINUE;
        END;
        IF jsonb_typeof(plan) = 'object' AND plan ? 'solution' THEN
            UPDATE ""Questions""
               SET ""InteractionSolution"" = CASE WHEN jsonb_typeof(plan -> 'solution') = 'null'
                                                THEN NULL ELSE (plan -> 'solution')::text END,
                   ""InteractionPlan"" = (plan - 'solution')::text
             WHERE ""Id"" = r.""Id"";
        END IF;
    END LOOP;
END $$;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // issue #570: çözüm kolon düşmeden önce plana "solution" olarak geri yazılır (geçersiz JSON satırı atlanır).
            // DİKKAT: geri alma #570 sızıntısını yeniden açar — #570 öncesi bir sürümle çalışılırsa plan (içindeki çözümle)
            // öğrenciye aynen gider. Yalnız #570'li sürüm de geri alınıyorsa kullanın; #570'li sürüm çalışmaya devam ederse
            // planı çıktıda zaten çözümsüz döner (savunma), ama veri yine plan içinde saklanır.
            migrationBuilder.Sql(@"
DO $$
DECLARE
    r RECORD;
BEGIN
    RAISE NOTICE 'issue #570: SplitInteractionSolution geri aliniyor - cozum plana geri yaziliyor; #570 oncesi surumle ogrenciye cozum sizar.';
    FOR r IN SELECT ""Id"", ""InteractionPlan"", ""InteractionSolution"" FROM ""Questions""
             WHERE ""InteractionSolution"" IS NOT NULL AND ""InteractionPlan"" IS NOT NULL
    LOOP
        BEGIN
            UPDATE ""Questions""
               SET ""InteractionPlan"" = (r.""InteractionPlan""::jsonb
                                         || jsonb_build_object('solution', r.""InteractionSolution""::jsonb))::text
             WHERE ""Id"" = r.""Id"" AND jsonb_typeof(r.""InteractionPlan""::jsonb) = 'object';
        EXCEPTION WHEN others THEN
            RAISE NOTICE 'issue #570: Question % cozumu plana geri yazilamadi, atlandi', r.""Id"";
        END;
    END LOOP;
END $$;");

            migrationBuilder.DropColumn(
                name: "InteractionSolution",
                table: "Questions");
        }
    }
}
