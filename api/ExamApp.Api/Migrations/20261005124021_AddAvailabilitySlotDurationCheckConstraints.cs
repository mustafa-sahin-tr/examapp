using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAvailabilitySlotDurationCheckConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // issue #323: ihlal eden mevcut satır varsa migration açık bir mesajla durur — veri sessizce silinmez veya
            // değiştirilmez (soft-delete edilmiş satırlar dahil; CHECK tüm satırlara uygulanır). Düzeltme elle yapılmalı.
            migrationBuilder.Sql(@"
DO $$
DECLARE
    bad_slots integer;
    bad_rules integer;
BEGIN
    SELECT count(*) INTO bad_slots FROM ""TeacherAvailabilitySlots""
     WHERE NOT (""EndTime"" <> ""StartTime"" AND (CASE WHEN ""EndTime"" > ""StartTime"" THEN ""EndTime"" - ""StartTime"" ELSE ""EndTime"" - ""StartTime"" + interval '24 hours' END) <= interval '4 hours');
    SELECT count(*) INTO bad_rules FROM ""RecurringAvailabilityRules""
     WHERE NOT (""EndTime"" <> ""StartTime"" AND (CASE WHEN ""EndTime"" > ""StartTime"" THEN ""EndTime"" - ""StartTime"" ELSE ""EndTime"" - ""StartTime"" + interval '24 hours' END) <= interval '4 hours');
    IF bad_slots > 0 OR bad_rules > 0 THEN
        RAISE EXCEPTION 'issue #323: % TeacherAvailabilitySlots and % RecurringAvailabilityRules rows violate the availability duration rule (EndTime = StartTime or duration > 4 hours, next-day end when EndTime < StartTime). Fix or remove these rows manually, then re-run the migration.', bad_slots, bad_rules;
    END IF;
END $$;");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TeacherAvailabilitySlots_Duration",
                table: "TeacherAvailabilitySlots",
                sql: "\"EndTime\" <> \"StartTime\" AND (CASE WHEN \"EndTime\" > \"StartTime\" THEN \"EndTime\" - \"StartTime\" ELSE \"EndTime\" - \"StartTime\" + interval '24 hours' END) <= interval '4 hours'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RecurringAvailabilityRules_Duration",
                table: "RecurringAvailabilityRules",
                sql: "\"EndTime\" <> \"StartTime\" AND (CASE WHEN \"EndTime\" > \"StartTime\" THEN \"EndTime\" - \"StartTime\" ELSE \"EndTime\" - \"StartTime\" + interval '24 hours' END) <= interval '4 hours'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_TeacherAvailabilitySlots_Duration",
                table: "TeacherAvailabilitySlots");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RecurringAvailabilityRules_Duration",
                table: "RecurringAvailabilityRules");
        }
    }
}
