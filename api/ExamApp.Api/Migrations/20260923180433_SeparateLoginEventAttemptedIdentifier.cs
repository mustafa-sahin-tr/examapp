using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class SeparateLoginEventAttemptedIdentifier : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "KeycloakUserId",
                table: "LoginEvents",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AddColumn<string>(
                name: "AttemptedIdentifier",
                table: "LoginEvents",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            // Issue #100 veri taşıması: başarısız login satırlarında (Role='Unknown') KeycloakUserId'de
            // duran doğrulanmamış e-posta AttemptedIdentifier'a taşınır, KeycloakUserId NULL'lanır.
            // Code-exchange hatasının yazdığı "unknown" literal'i bir tanımlayıcı olmadığı için taşınmaz.
            migrationBuilder.Sql(
                """
                UPDATE "LoginEvents"
                SET "AttemptedIdentifier" = NULLIF(NULLIF("KeycloakUserId", 'unknown'), ''),
                    "KeycloakUserId" = NULL
                WHERE "Success" = FALSE AND "Role" = 'Unknown' AND "KeycloakUserId" IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Up()'ın tersi: NOT NULL'a dönmeden önce boş KeycloakUserId'ler eski biçimle doldurulur
            // (e-posta, yoksa code-exchange hatasının "unknown" literal'i). Kolon 64 karakter.
            migrationBuilder.Sql(
                """
                UPDATE "LoginEvents"
                SET "KeycloakUserId" = COALESCE(NULLIF(LEFT("AttemptedIdentifier", 64), ''), 'unknown')
                WHERE "KeycloakUserId" IS NULL;
                """);

            migrationBuilder.DropColumn(
                name: "AttemptedIdentifier",
                table: "LoginEvents");

            migrationBuilder.AlterColumn<string>(
                name: "KeycloakUserId",
                table: "LoginEvents",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);
        }
    }
}
