using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class FixBaseEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CreateTime",
                table: "Worksheets",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "CreateUserId",
                table: "Worksheets",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeleteTime",
                table: "Worksheets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeleteUserId",
                table: "Worksheets",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Worksheets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdateTime",
                table: "Worksheets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UpdateUserId",
                table: "Worksheets",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreateTime",
                table: "Topics",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "CreateUserId",
                table: "Topics",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeleteTime",
                table: "Topics",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeleteUserId",
                table: "Topics",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Topics",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdateTime",
                table: "Topics",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UpdateUserId",
                table: "Topics",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreateTime",
                table: "TestPrototypes",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "CreateUserId",
                table: "TestPrototypes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeleteTime",
                table: "TestPrototypes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeleteUserId",
                table: "TestPrototypes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "TestPrototypes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdateTime",
                table: "TestPrototypes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UpdateUserId",
                table: "TestPrototypes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreateTime",
                table: "TestPrototypeDetail",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "CreateUserId",
                table: "TestPrototypeDetail",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeleteTime",
                table: "TestPrototypeDetail",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeleteUserId",
                table: "TestPrototypeDetail",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "TestPrototypeDetail",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdateTime",
                table: "TestPrototypeDetail",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UpdateUserId",
                table: "TestPrototypeDetail",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreateTime",
                table: "TestInstances",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "CreateUserId",
                table: "TestInstances",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeleteTime",
                table: "TestInstances",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeleteUserId",
                table: "TestInstances",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "TestInstances",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdateTime",
                table: "TestInstances",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UpdateUserId",
                table: "TestInstances",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreateTime",
                table: "TestInstanceQuestions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "CreateUserId",
                table: "TestInstanceQuestions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeleteTime",
                table: "TestInstanceQuestions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeleteUserId",
                table: "TestInstanceQuestions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "TestInstanceQuestions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdateTime",
                table: "TestInstanceQuestions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UpdateUserId",
                table: "TestInstanceQuestions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreateTime",
                table: "SubTopics",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "CreateUserId",
                table: "SubTopics",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeleteTime",
                table: "SubTopics",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeleteUserId",
                table: "SubTopics",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "SubTopics",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdateTime",
                table: "SubTopics",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UpdateUserId",
                table: "SubTopics",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreateTime",
                table: "Subjects",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "CreateUserId",
                table: "Subjects",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeleteTime",
                table: "Subjects",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeleteUserId",
                table: "Subjects",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Subjects",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdateTime",
                table: "Subjects",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UpdateUserId",
                table: "Subjects",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreateTime",
                table: "StudentPoints",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "CreateUserId",
                table: "StudentPoints",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeleteTime",
                table: "StudentPoints",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeleteUserId",
                table: "StudentPoints",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "StudentPoints",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdateTime",
                table: "StudentPoints",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UpdateUserId",
                table: "StudentPoints",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreateTime",
                table: "GradeSubjects",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "CreateUserId",
                table: "GradeSubjects",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeleteTime",
                table: "GradeSubjects",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeleteUserId",
                table: "GradeSubjects",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "GradeSubjects",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdateTime",
                table: "GradeSubjects",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UpdateUserId",
                table: "GradeSubjects",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreateTime",
                table: "Grades",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "CreateUserId",
                table: "Grades",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeleteTime",
                table: "Grades",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeleteUserId",
                table: "Grades",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Grades",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdateTime",
                table: "Grades",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UpdateUserId",
                table: "Grades",
                type: "integer",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "GradeSubjects",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "GradeSubjects",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "GradeSubjects",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "GradeSubjects",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "GradeSubjects",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "GradeSubjects",
                keyColumn: "Id",
                keyValue: 6,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "GradeSubjects",
                keyColumn: "Id",
                keyValue: 7,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "GradeSubjects",
                keyColumn: "Id",
                keyValue: 8,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "GradeSubjects",
                keyColumn: "Id",
                keyValue: 9,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "GradeSubjects",
                keyColumn: "Id",
                keyValue: 10,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "GradeSubjects",
                keyColumn: "Id",
                keyValue: 11,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "GradeSubjects",
                keyColumn: "Id",
                keyValue: 12,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "GradeSubjects",
                keyColumn: "Id",
                keyValue: 13,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "GradeSubjects",
                keyColumn: "Id",
                keyValue: 14,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "GradeSubjects",
                keyColumn: "Id",
                keyValue: 15,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "GradeSubjects",
                keyColumn: "Id",
                keyValue: 16,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "GradeSubjects",
                keyColumn: "Id",
                keyValue: 17,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "GradeSubjects",
                keyColumn: "Id",
                keyValue: 18,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "GradeSubjects",
                keyColumn: "Id",
                keyValue: 19,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "GradeSubjects",
                keyColumn: "Id",
                keyValue: 20,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "GradeSubjects",
                keyColumn: "Id",
                keyValue: 21,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "GradeSubjects",
                keyColumn: "Id",
                keyValue: 22,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "GradeSubjects",
                keyColumn: "Id",
                keyValue: 23,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "GradeSubjects",
                keyColumn: "Id",
                keyValue: 24,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "GradeSubjects",
                keyColumn: "Id",
                keyValue: 25,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "GradeSubjects",
                keyColumn: "Id",
                keyValue: 26,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "GradeSubjects",
                keyColumn: "Id",
                keyValue: 27,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "GradeSubjects",
                keyColumn: "Id",
                keyValue: 28,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "GradeSubjects",
                keyColumn: "Id",
                keyValue: 29,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "GradeSubjects",
                keyColumn: "Id",
                keyValue: 30,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "GradeSubjects",
                keyColumn: "Id",
                keyValue: 31,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "GradeSubjects",
                keyColumn: "Id",
                keyValue: 32,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "GradeSubjects",
                keyColumn: "Id",
                keyValue: 33,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "GradeSubjects",
                keyColumn: "Id",
                keyValue: 34,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "GradeSubjects",
                keyColumn: "Id",
                keyValue: 35,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "GradeSubjects",
                keyColumn: "Id",
                keyValue: 36,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "GradeSubjects",
                keyColumn: "Id",
                keyValue: 37,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "GradeSubjects",
                keyColumn: "Id",
                keyValue: 38,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "GradeSubjects",
                keyColumn: "Id",
                keyValue: 39,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "GradeSubjects",
                keyColumn: "Id",
                keyValue: 40,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "GradeSubjects",
                keyColumn: "Id",
                keyValue: 41,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "GradeSubjects",
                keyColumn: "Id",
                keyValue: 42,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "GradeSubjects",
                keyColumn: "Id",
                keyValue: 43,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "GradeSubjects",
                keyColumn: "Id",
                keyValue: 44,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "GradeSubjects",
                keyColumn: "Id",
                keyValue: 45,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: 6,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: 7,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: 8,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: 9,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: 10,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: 11,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: 12,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 6,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 7,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 8,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 9,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 10,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 11,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 12,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 13,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 14,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 15,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 16,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 17,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 18,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 19,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 20,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 21,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 22,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 23,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 24,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 25,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 26,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 27,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 28,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 29,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 30,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 31,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 32,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 33,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 34,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 35,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 36,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 37,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 38,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 39,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 40,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 41,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 42,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 43,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 44,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 45,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 46,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 47,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 48,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 49,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 57,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 58,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 59,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 60,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 61,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 62,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 63,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 64,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 65,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 66,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 67,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 68,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 69,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 70,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 71,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 72,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 73,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 74,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 75,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 76,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 77,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 84,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 85,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 86,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 87,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 88,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 89,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 90,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 91,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 92,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 93,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 94,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 95,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 96,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 97,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 98,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 105,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 106,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 107,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 108,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 109,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 110,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 111,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 112,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 113,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 114,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 115,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 116,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 117,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 118,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 119,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 120,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 121,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 122,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 123,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 124,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 125,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 126,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 127,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 128,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 129,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 130,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 131,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 138,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 139,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 140,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 141,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 142,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 143,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 144,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 145,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 146,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 147,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 148,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 149,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 150,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 151,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 152,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 153,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 154,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 155,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 156,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 157,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 165,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 166,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 167,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 168,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 169,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 170,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 171,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 172,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 173,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 174,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 175,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 176,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 177,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 178,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 179,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 180,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 188,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 189,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 190,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 191,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 192,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 193,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 194,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 195,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 196,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 197,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 198,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 199,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 200,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 201,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 211,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 212,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 213,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 214,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 215,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 216,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 217,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 218,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 219,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 220,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 221,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 222,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 223,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 224,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 225,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 226,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 227,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 228,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 230,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 231,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Subjects",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Subjects",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Subjects",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Subjects",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Subjects",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Subjects",
                keyColumn: "Id",
                keyValue: 6,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Subjects",
                keyColumn: "Id",
                keyValue: 7,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Subjects",
                keyColumn: "Id",
                keyValue: 8,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Subjects",
                keyColumn: "Id",
                keyValue: 9,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Subjects",
                keyColumn: "Id",
                keyValue: 10,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Subjects",
                keyColumn: "Id",
                keyValue: 11,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Subjects",
                keyColumn: "Id",
                keyValue: 12,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Subjects",
                keyColumn: "Id",
                keyValue: 13,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Subjects",
                keyColumn: "Id",
                keyValue: 14,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Subjects",
                keyColumn: "Id",
                keyValue: 15,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 6,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 7,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 8,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 9,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 10,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 11,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 12,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 13,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 50,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 51,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 52,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 53,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 54,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 55,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 56,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 78,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 79,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 80,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 81,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 82,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 83,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 99,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 100,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 101,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 102,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 103,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 104,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 132,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 133,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 134,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 135,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 136,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 137,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 158,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 159,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 160,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 161,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 162,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 163,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 164,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 181,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 182,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 183,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 184,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 185,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 186,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 187,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 202,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 203,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 204,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 205,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 206,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 207,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 208,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 209,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 210,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 211,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 212,
                columns: new[] { "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "UpdateTime", "UpdateUserId" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreateTime",
                table: "Worksheets");

            migrationBuilder.DropColumn(
                name: "CreateUserId",
                table: "Worksheets");

            migrationBuilder.DropColumn(
                name: "DeleteTime",
                table: "Worksheets");

            migrationBuilder.DropColumn(
                name: "DeleteUserId",
                table: "Worksheets");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Worksheets");

            migrationBuilder.DropColumn(
                name: "UpdateTime",
                table: "Worksheets");

            migrationBuilder.DropColumn(
                name: "UpdateUserId",
                table: "Worksheets");

            migrationBuilder.DropColumn(
                name: "CreateTime",
                table: "Topics");

            migrationBuilder.DropColumn(
                name: "CreateUserId",
                table: "Topics");

            migrationBuilder.DropColumn(
                name: "DeleteTime",
                table: "Topics");

            migrationBuilder.DropColumn(
                name: "DeleteUserId",
                table: "Topics");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Topics");

            migrationBuilder.DropColumn(
                name: "UpdateTime",
                table: "Topics");

            migrationBuilder.DropColumn(
                name: "UpdateUserId",
                table: "Topics");

            migrationBuilder.DropColumn(
                name: "CreateTime",
                table: "TestPrototypes");

            migrationBuilder.DropColumn(
                name: "CreateUserId",
                table: "TestPrototypes");

            migrationBuilder.DropColumn(
                name: "DeleteTime",
                table: "TestPrototypes");

            migrationBuilder.DropColumn(
                name: "DeleteUserId",
                table: "TestPrototypes");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "TestPrototypes");

            migrationBuilder.DropColumn(
                name: "UpdateTime",
                table: "TestPrototypes");

            migrationBuilder.DropColumn(
                name: "UpdateUserId",
                table: "TestPrototypes");

            migrationBuilder.DropColumn(
                name: "CreateTime",
                table: "TestPrototypeDetail");

            migrationBuilder.DropColumn(
                name: "CreateUserId",
                table: "TestPrototypeDetail");

            migrationBuilder.DropColumn(
                name: "DeleteTime",
                table: "TestPrototypeDetail");

            migrationBuilder.DropColumn(
                name: "DeleteUserId",
                table: "TestPrototypeDetail");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "TestPrototypeDetail");

            migrationBuilder.DropColumn(
                name: "UpdateTime",
                table: "TestPrototypeDetail");

            migrationBuilder.DropColumn(
                name: "UpdateUserId",
                table: "TestPrototypeDetail");

            migrationBuilder.DropColumn(
                name: "CreateTime",
                table: "TestInstances");

            migrationBuilder.DropColumn(
                name: "CreateUserId",
                table: "TestInstances");

            migrationBuilder.DropColumn(
                name: "DeleteTime",
                table: "TestInstances");

            migrationBuilder.DropColumn(
                name: "DeleteUserId",
                table: "TestInstances");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "TestInstances");

            migrationBuilder.DropColumn(
                name: "UpdateTime",
                table: "TestInstances");

            migrationBuilder.DropColumn(
                name: "UpdateUserId",
                table: "TestInstances");

            migrationBuilder.DropColumn(
                name: "CreateTime",
                table: "TestInstanceQuestions");

            migrationBuilder.DropColumn(
                name: "CreateUserId",
                table: "TestInstanceQuestions");

            migrationBuilder.DropColumn(
                name: "DeleteTime",
                table: "TestInstanceQuestions");

            migrationBuilder.DropColumn(
                name: "DeleteUserId",
                table: "TestInstanceQuestions");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "TestInstanceQuestions");

            migrationBuilder.DropColumn(
                name: "UpdateTime",
                table: "TestInstanceQuestions");

            migrationBuilder.DropColumn(
                name: "UpdateUserId",
                table: "TestInstanceQuestions");

            migrationBuilder.DropColumn(
                name: "CreateTime",
                table: "SubTopics");

            migrationBuilder.DropColumn(
                name: "CreateUserId",
                table: "SubTopics");

            migrationBuilder.DropColumn(
                name: "DeleteTime",
                table: "SubTopics");

            migrationBuilder.DropColumn(
                name: "DeleteUserId",
                table: "SubTopics");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "SubTopics");

            migrationBuilder.DropColumn(
                name: "UpdateTime",
                table: "SubTopics");

            migrationBuilder.DropColumn(
                name: "UpdateUserId",
                table: "SubTopics");

            migrationBuilder.DropColumn(
                name: "CreateTime",
                table: "Subjects");

            migrationBuilder.DropColumn(
                name: "CreateUserId",
                table: "Subjects");

            migrationBuilder.DropColumn(
                name: "DeleteTime",
                table: "Subjects");

            migrationBuilder.DropColumn(
                name: "DeleteUserId",
                table: "Subjects");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Subjects");

            migrationBuilder.DropColumn(
                name: "UpdateTime",
                table: "Subjects");

            migrationBuilder.DropColumn(
                name: "UpdateUserId",
                table: "Subjects");

            migrationBuilder.DropColumn(
                name: "CreateTime",
                table: "StudentPoints");

            migrationBuilder.DropColumn(
                name: "CreateUserId",
                table: "StudentPoints");

            migrationBuilder.DropColumn(
                name: "DeleteTime",
                table: "StudentPoints");

            migrationBuilder.DropColumn(
                name: "DeleteUserId",
                table: "StudentPoints");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "StudentPoints");

            migrationBuilder.DropColumn(
                name: "UpdateTime",
                table: "StudentPoints");

            migrationBuilder.DropColumn(
                name: "UpdateUserId",
                table: "StudentPoints");

            migrationBuilder.DropColumn(
                name: "CreateTime",
                table: "GradeSubjects");

            migrationBuilder.DropColumn(
                name: "CreateUserId",
                table: "GradeSubjects");

            migrationBuilder.DropColumn(
                name: "DeleteTime",
                table: "GradeSubjects");

            migrationBuilder.DropColumn(
                name: "DeleteUserId",
                table: "GradeSubjects");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "GradeSubjects");

            migrationBuilder.DropColumn(
                name: "UpdateTime",
                table: "GradeSubjects");

            migrationBuilder.DropColumn(
                name: "UpdateUserId",
                table: "GradeSubjects");

            migrationBuilder.DropColumn(
                name: "CreateTime",
                table: "Grades");

            migrationBuilder.DropColumn(
                name: "CreateUserId",
                table: "Grades");

            migrationBuilder.DropColumn(
                name: "DeleteTime",
                table: "Grades");

            migrationBuilder.DropColumn(
                name: "DeleteUserId",
                table: "Grades");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Grades");

            migrationBuilder.DropColumn(
                name: "UpdateTime",
                table: "Grades");

            migrationBuilder.DropColumn(
                name: "UpdateUserId",
                table: "Grades");
        }
    }
}
