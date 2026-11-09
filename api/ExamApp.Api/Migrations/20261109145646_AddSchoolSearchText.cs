using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSchoolSearchText : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SearchText",
                table: "Schools",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            // Mevcut okullar: SchoolSearchText.Build ile aynı katlama. Yeni satırları uygulama yazar.
            migrationBuilder.Sql("""
                UPDATE "Schools" s
                SET "SearchText" = regexp_replace(trim(lower(translate(
                        concat_ws(' ', s."Name",
                            (SELECT d."Name" FROM "Districts" d WHERE d."Id" = s."DistrictId"),
                            (SELECT p."Name" FROM "Provinces" p WHERE p."Id" = s."ProvinceId")),
                        'İIıĞğÜüŞşÖöÇçÂâÎîÛûÊêÔô',
                        'iiigguussooccaaiiuueeoo'))), '\s+', ' ', 'g');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SearchText",
                table: "Schools");
        }
    }
}
