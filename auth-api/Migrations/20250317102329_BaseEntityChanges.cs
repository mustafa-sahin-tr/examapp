using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class BaseEntityChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CreateTime",
                table: "Users",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "CreateUserId",
                table: "Users",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeleteTime",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeleteUserId",
                table: "Users",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdateTime",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UpdateUserId",
                table: "Users",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreateTime",
                table: "TestQuestions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "CreateUserId",
                table: "TestQuestions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeleteTime",
                table: "TestQuestions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeleteUserId",
                table: "TestQuestions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "TestQuestions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdateTime",
                table: "TestQuestions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UpdateUserId",
                table: "TestQuestions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreateTime",
                table: "Teachers",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "CreateUserId",
                table: "Teachers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeleteTime",
                table: "Teachers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeleteUserId",
                table: "Teachers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Teachers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdateTime",
                table: "Teachers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UpdateUserId",
                table: "Teachers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreateTime",
                table: "StudentSpecialEvents",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "CreateUserId",
                table: "StudentSpecialEvents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeleteTime",
                table: "StudentSpecialEvents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeleteUserId",
                table: "StudentSpecialEvents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "StudentSpecialEvents",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdateTime",
                table: "StudentSpecialEvents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UpdateUserId",
                table: "StudentSpecialEvents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreateTime",
                table: "Students",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "CreateUserId",
                table: "Students",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeleteTime",
                table: "Students",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeleteUserId",
                table: "Students",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Students",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdateTime",
                table: "Students",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UpdateUserId",
                table: "Students",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreateTime",
                table: "StudentRewards",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "CreateUserId",
                table: "StudentRewards",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeleteTime",
                table: "StudentRewards",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeleteUserId",
                table: "StudentRewards",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "StudentRewards",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdateTime",
                table: "StudentRewards",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UpdateUserId",
                table: "StudentRewards",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreateTime",
                table: "StudentPointHistories",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "CreateUserId",
                table: "StudentPointHistories",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeleteTime",
                table: "StudentPointHistories",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeleteUserId",
                table: "StudentPointHistories",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "StudentPointHistories",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdateTime",
                table: "StudentPointHistories",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UpdateUserId",
                table: "StudentPointHistories",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreateTime",
                table: "StudentBadges",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "CreateUserId",
                table: "StudentBadges",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeleteTime",
                table: "StudentBadges",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeleteUserId",
                table: "StudentBadges",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "StudentBadges",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdateTime",
                table: "StudentBadges",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UpdateUserId",
                table: "StudentBadges",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreateTime",
                table: "SpecialEvents",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "CreateUserId",
                table: "SpecialEvents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeleteTime",
                table: "SpecialEvents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeleteUserId",
                table: "SpecialEvents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "SpecialEvents",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdateTime",
                table: "SpecialEvents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UpdateUserId",
                table: "SpecialEvents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreateTime",
                table: "Rewards",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "CreateUserId",
                table: "Rewards",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeleteTime",
                table: "Rewards",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeleteUserId",
                table: "Rewards",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Rewards",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdateTime",
                table: "Rewards",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UpdateUserId",
                table: "Rewards",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreateTime",
                table: "Questions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "CreateUserId",
                table: "Questions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeleteTime",
                table: "Questions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeleteUserId",
                table: "Questions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Questions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdateTime",
                table: "Questions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UpdateUserId",
                table: "Questions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreateTime",
                table: "Passage",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "CreateUserId",
                table: "Passage",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeleteTime",
                table: "Passage",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeleteUserId",
                table: "Passage",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Passage",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdateTime",
                table: "Passage",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UpdateUserId",
                table: "Passage",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreateTime",
                table: "Parents",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "CreateUserId",
                table: "Parents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeleteTime",
                table: "Parents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeleteUserId",
                table: "Parents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Parents",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdateTime",
                table: "Parents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UpdateUserId",
                table: "Parents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreateTime",
                table: "Leaderboards",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "CreateUserId",
                table: "Leaderboards",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeleteTime",
                table: "Leaderboards",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeleteUserId",
                table: "Leaderboards",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Leaderboards",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdateTime",
                table: "Leaderboards",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UpdateUserId",
                table: "Leaderboards",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreateTime",
                table: "BookTests",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "CreateUserId",
                table: "BookTests",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeleteTime",
                table: "BookTests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeleteUserId",
                table: "BookTests",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "BookTests",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdateTime",
                table: "BookTests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UpdateUserId",
                table: "BookTests",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreateTime",
                table: "Books",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "CreateUserId",
                table: "Books",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeleteTime",
                table: "Books",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeleteUserId",
                table: "Books",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Books",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdateTime",
                table: "Books",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UpdateUserId",
                table: "Books",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreateTime",
                table: "Badge",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "CreateUserId",
                table: "Badge",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeleteTime",
                table: "Badge",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeleteUserId",
                table: "Badge",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Badge",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdateTime",
                table: "Badge",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UpdateUserId",
                table: "Badge",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreateTime",
                table: "Answers",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "CreateUserId",
                table: "Answers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeleteTime",
                table: "Answers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeleteUserId",
                table: "Answers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Answers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdateTime",
                table: "Answers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UpdateUserId",
                table: "Answers",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreateTime",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CreateUserId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "DeleteTime",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "DeleteUserId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "UpdateTime",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "UpdateUserId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CreateTime",
                table: "TestQuestions");

            migrationBuilder.DropColumn(
                name: "CreateUserId",
                table: "TestQuestions");

            migrationBuilder.DropColumn(
                name: "DeleteTime",
                table: "TestQuestions");

            migrationBuilder.DropColumn(
                name: "DeleteUserId",
                table: "TestQuestions");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "TestQuestions");

            migrationBuilder.DropColumn(
                name: "UpdateTime",
                table: "TestQuestions");

            migrationBuilder.DropColumn(
                name: "UpdateUserId",
                table: "TestQuestions");

            migrationBuilder.DropColumn(
                name: "CreateTime",
                table: "Teachers");

            migrationBuilder.DropColumn(
                name: "CreateUserId",
                table: "Teachers");

            migrationBuilder.DropColumn(
                name: "DeleteTime",
                table: "Teachers");

            migrationBuilder.DropColumn(
                name: "DeleteUserId",
                table: "Teachers");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Teachers");

            migrationBuilder.DropColumn(
                name: "UpdateTime",
                table: "Teachers");

            migrationBuilder.DropColumn(
                name: "UpdateUserId",
                table: "Teachers");

            migrationBuilder.DropColumn(
                name: "CreateTime",
                table: "StudentSpecialEvents");

            migrationBuilder.DropColumn(
                name: "CreateUserId",
                table: "StudentSpecialEvents");

            migrationBuilder.DropColumn(
                name: "DeleteTime",
                table: "StudentSpecialEvents");

            migrationBuilder.DropColumn(
                name: "DeleteUserId",
                table: "StudentSpecialEvents");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "StudentSpecialEvents");

            migrationBuilder.DropColumn(
                name: "UpdateTime",
                table: "StudentSpecialEvents");

            migrationBuilder.DropColumn(
                name: "UpdateUserId",
                table: "StudentSpecialEvents");

            migrationBuilder.DropColumn(
                name: "CreateTime",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "CreateUserId",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "DeleteTime",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "DeleteUserId",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "UpdateTime",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "UpdateUserId",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "CreateTime",
                table: "StudentRewards");

            migrationBuilder.DropColumn(
                name: "CreateUserId",
                table: "StudentRewards");

            migrationBuilder.DropColumn(
                name: "DeleteTime",
                table: "StudentRewards");

            migrationBuilder.DropColumn(
                name: "DeleteUserId",
                table: "StudentRewards");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "StudentRewards");

            migrationBuilder.DropColumn(
                name: "UpdateTime",
                table: "StudentRewards");

            migrationBuilder.DropColumn(
                name: "UpdateUserId",
                table: "StudentRewards");

            migrationBuilder.DropColumn(
                name: "CreateTime",
                table: "StudentPointHistories");

            migrationBuilder.DropColumn(
                name: "CreateUserId",
                table: "StudentPointHistories");

            migrationBuilder.DropColumn(
                name: "DeleteTime",
                table: "StudentPointHistories");

            migrationBuilder.DropColumn(
                name: "DeleteUserId",
                table: "StudentPointHistories");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "StudentPointHistories");

            migrationBuilder.DropColumn(
                name: "UpdateTime",
                table: "StudentPointHistories");

            migrationBuilder.DropColumn(
                name: "UpdateUserId",
                table: "StudentPointHistories");

            migrationBuilder.DropColumn(
                name: "CreateTime",
                table: "StudentBadges");

            migrationBuilder.DropColumn(
                name: "CreateUserId",
                table: "StudentBadges");

            migrationBuilder.DropColumn(
                name: "DeleteTime",
                table: "StudentBadges");

            migrationBuilder.DropColumn(
                name: "DeleteUserId",
                table: "StudentBadges");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "StudentBadges");

            migrationBuilder.DropColumn(
                name: "UpdateTime",
                table: "StudentBadges");

            migrationBuilder.DropColumn(
                name: "UpdateUserId",
                table: "StudentBadges");

            migrationBuilder.DropColumn(
                name: "CreateTime",
                table: "SpecialEvents");

            migrationBuilder.DropColumn(
                name: "CreateUserId",
                table: "SpecialEvents");

            migrationBuilder.DropColumn(
                name: "DeleteTime",
                table: "SpecialEvents");

            migrationBuilder.DropColumn(
                name: "DeleteUserId",
                table: "SpecialEvents");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "SpecialEvents");

            migrationBuilder.DropColumn(
                name: "UpdateTime",
                table: "SpecialEvents");

            migrationBuilder.DropColumn(
                name: "UpdateUserId",
                table: "SpecialEvents");

            migrationBuilder.DropColumn(
                name: "CreateTime",
                table: "Rewards");

            migrationBuilder.DropColumn(
                name: "CreateUserId",
                table: "Rewards");

            migrationBuilder.DropColumn(
                name: "DeleteTime",
                table: "Rewards");

            migrationBuilder.DropColumn(
                name: "DeleteUserId",
                table: "Rewards");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Rewards");

            migrationBuilder.DropColumn(
                name: "UpdateTime",
                table: "Rewards");

            migrationBuilder.DropColumn(
                name: "UpdateUserId",
                table: "Rewards");

            migrationBuilder.DropColumn(
                name: "CreateTime",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "CreateUserId",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "DeleteTime",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "DeleteUserId",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "UpdateTime",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "UpdateUserId",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "CreateTime",
                table: "Passage");

            migrationBuilder.DropColumn(
                name: "CreateUserId",
                table: "Passage");

            migrationBuilder.DropColumn(
                name: "DeleteTime",
                table: "Passage");

            migrationBuilder.DropColumn(
                name: "DeleteUserId",
                table: "Passage");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Passage");

            migrationBuilder.DropColumn(
                name: "UpdateTime",
                table: "Passage");

            migrationBuilder.DropColumn(
                name: "UpdateUserId",
                table: "Passage");

            migrationBuilder.DropColumn(
                name: "CreateTime",
                table: "Parents");

            migrationBuilder.DropColumn(
                name: "CreateUserId",
                table: "Parents");

            migrationBuilder.DropColumn(
                name: "DeleteTime",
                table: "Parents");

            migrationBuilder.DropColumn(
                name: "DeleteUserId",
                table: "Parents");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Parents");

            migrationBuilder.DropColumn(
                name: "UpdateTime",
                table: "Parents");

            migrationBuilder.DropColumn(
                name: "UpdateUserId",
                table: "Parents");

            migrationBuilder.DropColumn(
                name: "CreateTime",
                table: "Leaderboards");

            migrationBuilder.DropColumn(
                name: "CreateUserId",
                table: "Leaderboards");

            migrationBuilder.DropColumn(
                name: "DeleteTime",
                table: "Leaderboards");

            migrationBuilder.DropColumn(
                name: "DeleteUserId",
                table: "Leaderboards");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Leaderboards");

            migrationBuilder.DropColumn(
                name: "UpdateTime",
                table: "Leaderboards");

            migrationBuilder.DropColumn(
                name: "UpdateUserId",
                table: "Leaderboards");

            migrationBuilder.DropColumn(
                name: "CreateTime",
                table: "BookTests");

            migrationBuilder.DropColumn(
                name: "CreateUserId",
                table: "BookTests");

            migrationBuilder.DropColumn(
                name: "DeleteTime",
                table: "BookTests");

            migrationBuilder.DropColumn(
                name: "DeleteUserId",
                table: "BookTests");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "BookTests");

            migrationBuilder.DropColumn(
                name: "UpdateTime",
                table: "BookTests");

            migrationBuilder.DropColumn(
                name: "UpdateUserId",
                table: "BookTests");

            migrationBuilder.DropColumn(
                name: "CreateTime",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "CreateUserId",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "DeleteTime",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "DeleteUserId",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "UpdateTime",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "UpdateUserId",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "CreateTime",
                table: "Badge");

            migrationBuilder.DropColumn(
                name: "CreateUserId",
                table: "Badge");

            migrationBuilder.DropColumn(
                name: "DeleteTime",
                table: "Badge");

            migrationBuilder.DropColumn(
                name: "DeleteUserId",
                table: "Badge");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Badge");

            migrationBuilder.DropColumn(
                name: "UpdateTime",
                table: "Badge");

            migrationBuilder.DropColumn(
                name: "UpdateUserId",
                table: "Badge");

            migrationBuilder.DropColumn(
                name: "CreateTime",
                table: "Answers");

            migrationBuilder.DropColumn(
                name: "CreateUserId",
                table: "Answers");

            migrationBuilder.DropColumn(
                name: "DeleteTime",
                table: "Answers");

            migrationBuilder.DropColumn(
                name: "DeleteUserId",
                table: "Answers");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Answers");

            migrationBuilder.DropColumn(
                name: "UpdateTime",
                table: "Answers");

            migrationBuilder.DropColumn(
                name: "UpdateUserId",
                table: "Answers");
        }
    }
}
