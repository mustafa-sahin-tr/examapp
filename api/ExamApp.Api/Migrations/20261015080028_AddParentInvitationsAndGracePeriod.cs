using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddParentInvitationsAndGracePeriod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ParentRequirementStartedAt",
                table: "Students",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ParentInvitationAttempts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudentUserId = table.Column<int>(type: "integer", nullable: false),
                    EmailHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AttemptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParentInvitationAttempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ParentInvitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<int>(type: "integer", nullable: false),
                    ParentUserId = table.Column<int>(type: "integer", nullable: true),
                    ParentKeycloakId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ParentId = table.Column<int>(type: "integer", nullable: true),
                    LinkId = table.Column<int>(type: "integer", nullable: true),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    FullName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Phone = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    IsNewAccount = table.Column<bool>(type: "boolean", nullable: false),
                    SendCount = table.Column<int>(type: "integer", nullable: false),
                    LastSentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParentInvitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ParentInvitations_ParentStudentLinks_LinkId",
                        column: x => x.LinkId,
                        principalTable: "ParentStudentLinks",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ParentInvitations_Parents_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Parents",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ParentInvitations_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StudentParentGraceReminders",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudentId = table.Column<int>(type: "integer", nullable: false),
                    GraceEndsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DaysLeft = table.Column<int>(type: "integer", nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentParentGraceReminders", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Students_ParentRequirementStartedAt",
                table: "Students",
                column: "ParentRequirementStartedAt",
                filter: "\"ParentRequirementStartedAt\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ParentInvitationAttempts_StudentUserId_AttemptedAt",
                table: "ParentInvitationAttempts",
                columns: new[] { "StudentUserId", "AttemptedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ParentInvitations_Email_CreatedAt",
                table: "ParentInvitations",
                columns: new[] { "Email", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ParentInvitations_LinkId",
                table: "ParentInvitations",
                column: "LinkId");

            migrationBuilder.CreateIndex(
                name: "IX_ParentInvitations_ParentId_Status",
                table: "ParentInvitations",
                columns: new[] { "ParentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ParentInvitations_Student_Pending",
                table: "ParentInvitations",
                column: "StudentId",
                unique: true,
                filter: "\"Status\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_ParentInvitations_StudentId_Email",
                table: "ParentInvitations",
                columns: new[] { "StudentId", "Email" });

            migrationBuilder.CreateIndex(
                name: "IX_ParentInvitations_TokenHash",
                table: "ParentInvitations",
                column: "TokenHash",
                unique: true,
                filter: "\"TokenHash\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StudentParentGraceReminders_StudentId_GraceEndsAt_DaysLeft",
                table: "StudentParentGraceReminders",
                columns: new[] { "StudentId", "GraceEndsAt", "DaysLeft" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ParentInvitationAttempts");

            migrationBuilder.DropTable(
                name: "ParentInvitations");

            migrationBuilder.DropTable(
                name: "StudentParentGraceReminders");

            migrationBuilder.DropIndex(
                name: "IX_Students_ParentRequirementStartedAt",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "ParentRequirementStartedAt",
                table: "Students");
        }
    }
}
