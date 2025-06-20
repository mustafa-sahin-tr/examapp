using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class UpdateProgramEntitiesWithBaseEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ProgramStepActions",
                keyColumn: "Id",
                keyValue: 1);

            migrationBuilder.DeleteData(
                table: "ProgramStepActions",
                keyColumn: "Id",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "ProgramSteps",
                keyColumn: "Id",
                keyValue: 8);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreateTime",
                table: "ProgramSteps",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "CreateUserId",
                table: "ProgramSteps",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeleteTime",
                table: "ProgramSteps",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeleteUserId",
                table: "ProgramSteps",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "ProgramSteps",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdateTime",
                table: "ProgramSteps",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UpdateUserId",
                table: "ProgramSteps",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreateTime",
                table: "ProgramStepOptions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "CreateUserId",
                table: "ProgramStepOptions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeleteTime",
                table: "ProgramStepOptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeleteUserId",
                table: "ProgramStepOptions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "ProgramStepOptions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdateTime",
                table: "ProgramStepOptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UpdateUserId",
                table: "ProgramStepOptions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreateTime",
                table: "ProgramStepActions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "CreateUserId",
                table: "ProgramStepActions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeleteTime",
                table: "ProgramStepActions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeleteUserId",
                table: "ProgramStepActions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "ProgramStepActions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdateTime",
                table: "ProgramStepActions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UpdateUserId",
                table: "ProgramStepActions",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UserPrograms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    ProgramName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    StudyType = table.Column<string>(type: "text", nullable: false),
                    StudyDuration = table.Column<string>(type: "text", nullable: false),
                    QuestionsPerDay = table.Column<int>(type: "integer", nullable: true),
                    SubjectsPerDay = table.Column<int>(type: "integer", nullable: false),
                    RestDays = table.Column<string>(type: "text", nullable: false),
                    DifficultSubjects = table.Column<string>(type: "text", nullable: false),
                    CreateTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreateUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdateTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdateUserId = table.Column<int>(type: "integer", nullable: true),
                    DeleteTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeleteUserId = table.Column<int>(type: "integer", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPrograms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserProgramSchedules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserProgramId = table.Column<int>(type: "integer", nullable: false),
                    ScheduleDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SubjectId = table.Column<int>(type: "integer", nullable: false),
                    SubjectName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StudyDurationMinutes = table.Column<int>(type: "integer", nullable: true),
                    QuestionCount = table.Column<int>(type: "integer", nullable: true),
                    IsCompleted = table.Column<bool>(type: "boolean", nullable: false),
                    CompletedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: false),
                    CreateTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreateUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdateTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdateUserId = table.Column<int>(type: "integer", nullable: true),
                    DeleteTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeleteUserId = table.Column<int>(type: "integer", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserProgramSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserProgramSchedules_UserPrograms_UserProgramId",
                        column: x => x.UserProgramId,
                        principalTable: "UserPrograms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "ProgramStepOptions",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "ProgramStepOptions",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "ProgramStepOptions",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "ProgramStepOptions",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "ProgramStepOptions",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "ProgramStepOptions",
                keyColumn: "Id",
                keyValue: 6,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "ProgramStepOptions",
                keyColumn: "Id",
                keyValue: 7,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "ProgramStepOptions",
                keyColumn: "Id",
                keyValue: 8,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "ProgramStepOptions",
                keyColumn: "Id",
                keyValue: 9,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "ProgramStepOptions",
                keyColumn: "Id",
                keyValue: 10,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "ProgramStepOptions",
                keyColumn: "Id",
                keyValue: 11,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "ProgramStepOptions",
                keyColumn: "Id",
                keyValue: 12,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "ProgramStepOptions",
                keyColumn: "Id",
                keyValue: 13,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "ProgramStepOptions",
                keyColumn: "Id",
                keyValue: 14,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "ProgramStepOptions",
                keyColumn: "Id",
                keyValue: 15,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "ProgramStepOptions",
                keyColumn: "Id",
                keyValue: 16,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "ProgramStepOptions",
                keyColumn: "Id",
                keyValue: 17,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "ProgramStepOptions",
                keyColumn: "Id",
                keyValue: 18,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "ProgramStepOptions",
                keyColumn: "Id",
                keyValue: 19,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "ProgramStepOptions",
                keyColumn: "Id",
                keyValue: 20,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "ProgramStepOptions",
                keyColumn: "Id",
                keyValue: 21,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "ProgramStepOptions",
                keyColumn: "Id",
                keyValue: 22,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "ProgramStepOptions",
                keyColumn: "Id",
                keyValue: 23,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "ProgramStepOptions",
                keyColumn: "Id",
                keyValue: 24,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "ProgramStepOptions",
                keyColumn: "Id",
                keyValue: 25,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "ProgramStepOptions",
                keyColumn: "Id",
                keyValue: 26,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "ProgramStepOptions",
                keyColumn: "Id",
                keyValue: 27,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "ProgramSteps",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "ProgramSteps",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "ProgramSteps",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "ProgramSteps",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "ProgramSteps",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "ProgramSteps",
                keyColumn: "Id",
                keyValue: 6,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "ProgramSteps",
                keyColumn: "Id",
                keyValue: 7,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.CreateIndex(
                name: "IX_UserProgramSchedules_UserProgramId",
                table: "UserProgramSchedules",
                column: "UserProgramId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserProgramSchedules");

            migrationBuilder.DropTable(
                name: "UserPrograms");

            migrationBuilder.DropColumn(
                name: "CreateTime",
                table: "ProgramSteps");

            migrationBuilder.DropColumn(
                name: "CreateUserId",
                table: "ProgramSteps");

            migrationBuilder.DropColumn(
                name: "DeleteTime",
                table: "ProgramSteps");

            migrationBuilder.DropColumn(
                name: "DeleteUserId",
                table: "ProgramSteps");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "ProgramSteps");

            migrationBuilder.DropColumn(
                name: "UpdateTime",
                table: "ProgramSteps");

            migrationBuilder.DropColumn(
                name: "UpdateUserId",
                table: "ProgramSteps");

            migrationBuilder.DropColumn(
                name: "CreateTime",
                table: "ProgramStepOptions");

            migrationBuilder.DropColumn(
                name: "CreateUserId",
                table: "ProgramStepOptions");

            migrationBuilder.DropColumn(
                name: "DeleteTime",
                table: "ProgramStepOptions");

            migrationBuilder.DropColumn(
                name: "DeleteUserId",
                table: "ProgramStepOptions");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "ProgramStepOptions");

            migrationBuilder.DropColumn(
                name: "UpdateTime",
                table: "ProgramStepOptions");

            migrationBuilder.DropColumn(
                name: "UpdateUserId",
                table: "ProgramStepOptions");

            migrationBuilder.DropColumn(
                name: "CreateTime",
                table: "ProgramStepActions");

            migrationBuilder.DropColumn(
                name: "CreateUserId",
                table: "ProgramStepActions");

            migrationBuilder.DropColumn(
                name: "DeleteTime",
                table: "ProgramStepActions");

            migrationBuilder.DropColumn(
                name: "DeleteUserId",
                table: "ProgramStepActions");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "ProgramStepActions");

            migrationBuilder.DropColumn(
                name: "UpdateTime",
                table: "ProgramStepActions");

            migrationBuilder.DropColumn(
                name: "UpdateUserId",
                table: "ProgramStepActions");

            migrationBuilder.InsertData(
                table: "ProgramSteps",
                columns: new[] { "Id", "Description", "Multiple", "Order", "Title" },
                values: new object[] { 8, "Artık programını oluşturmaya hazırsın", false, 8, "Artık programını oluşturmaya hazırsın" });

            migrationBuilder.InsertData(
                table: "ProgramStepActions",
                columns: new[] { "Id", "Label", "ProgramStepId", "Value" },
                values: new object[,]
                {
                    { 1, "Programı Oluştur", 8, "CreateProgram" },
                    { 2, "Vazgeç", 8, "Cancel" }
                });
        }
    }
}
