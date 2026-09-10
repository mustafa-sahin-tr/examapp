using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <summary>
    /// Issue #139: StudyPage → StudyItem rename + ContentType (Image/Link/BookPageRange) alanları.
    ///
    /// NOT: EF scaffold'u rename'i DropTable+CreateTable olarak üretmişti (veri kaybı). Up/Down elle
    /// RenameTable/RenameColumn/RENAME CONSTRAINT ile yeniden yazıldı; mevcut StudyPages/StudyPageImages
    /// satırları korunur ve ContentType default 0 (Image) alır. Designer/snapshot dosyaları scaffold çıktısıdır.
    /// </summary>
    public partial class RenameStudyPageToStudyItemAndAddContentTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // --- Tablo ve kolon rename (veri korunur) ---
            migrationBuilder.RenameTable(name: "StudyPages", newName: "StudyItems");
            migrationBuilder.RenameTable(name: "StudyPageImages", newName: "StudyItemImages");

            migrationBuilder.RenameColumn(
                name: "StudyPageId",
                table: "StudyItemImages",
                newName: "StudyItemId");

            migrationBuilder.RenameColumn(
                name: "StudyPageId",
                table: "UserProgramStudyPageSchedules",
                newName: "StudyItemId");

            // --- Index rename ---
            migrationBuilder.RenameIndex(name: "IX_StudyPages_SubjectId", table: "StudyItems", newName: "IX_StudyItems_SubjectId");
            migrationBuilder.RenameIndex(name: "IX_StudyPages_SubTopicId", table: "StudyItems", newName: "IX_StudyItems_SubTopicId");
            migrationBuilder.RenameIndex(name: "IX_StudyPages_TopicId", table: "StudyItems", newName: "IX_StudyItems_TopicId");
            migrationBuilder.RenameIndex(name: "IX_StudyPageImages_StudyPageId", table: "StudyItemImages", newName: "IX_StudyItemImages_StudyItemId");
            migrationBuilder.RenameIndex(
                name: "IX_UserProgramStudyPageSchedules_StudyPageId",
                table: "UserProgramStudyPageSchedules",
                newName: "IX_UserProgramStudyPageSchedules_StudyItemId");

            // --- PK / FK constraint rename (EF RenameTable Postgres'te constraint adlarını değiştirmez) ---
            migrationBuilder.Sql("ALTER TABLE \"StudyItems\" RENAME CONSTRAINT \"PK_StudyPages\" TO \"PK_StudyItems\";");
            migrationBuilder.Sql("ALTER TABLE \"StudyItems\" RENAME CONSTRAINT \"FK_StudyPages_SubTopics_SubTopicId\" TO \"FK_StudyItems_SubTopics_SubTopicId\";");
            migrationBuilder.Sql("ALTER TABLE \"StudyItems\" RENAME CONSTRAINT \"FK_StudyPages_Subjects_SubjectId\" TO \"FK_StudyItems_Subjects_SubjectId\";");
            migrationBuilder.Sql("ALTER TABLE \"StudyItems\" RENAME CONSTRAINT \"FK_StudyPages_Topics_TopicId\" TO \"FK_StudyItems_Topics_TopicId\";");
            migrationBuilder.Sql("ALTER TABLE \"StudyItemImages\" RENAME CONSTRAINT \"PK_StudyPageImages\" TO \"PK_StudyItemImages\";");
            migrationBuilder.Sql("ALTER TABLE \"StudyItemImages\" RENAME CONSTRAINT \"FK_StudyPageImages_StudyPages_StudyPageId\" TO \"FK_StudyItemImages_StudyItems_StudyItemId\";");
            migrationBuilder.Sql("ALTER TABLE \"UserProgramStudyPageSchedules\" RENAME CONSTRAINT \"FK_UserProgramStudyPageSchedules_StudyPages_StudyPageId\" TO \"FK_UserProgramStudyPageSchedules_StudyItems_StudyItemId\";");

            // --- Yeni ContentType alanları; mevcut satırlar ContentType=0 (Image) alır ---
            migrationBuilder.AddColumn<int>(
                name: "ContentType",
                table: "StudyItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Url",
                table: "StudyItems",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Platform",
                table: "StudyItems",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BookId",
                table: "StudyItems",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BookTestId",
                table: "StudyItems",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StartPage",
                table: "StudyItems",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EndPage",
                table: "StudyItems",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudyItems_BookId",
                table: "StudyItems",
                column: "BookId");

            migrationBuilder.CreateIndex(
                name: "IX_StudyItems_BookTestId",
                table: "StudyItems",
                column: "BookTestId");

            migrationBuilder.AddForeignKey(
                name: "FK_StudyItems_Books_BookId",
                table: "StudyItems",
                column: "BookId",
                principalTable: "Books",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_StudyItems_BookTests_BookTestId",
                table: "StudyItems",
                column: "BookTestId",
                principalTable: "BookTests",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "FK_StudyItems_BookTests_BookTestId", table: "StudyItems");
            migrationBuilder.DropForeignKey(name: "FK_StudyItems_Books_BookId", table: "StudyItems");
            migrationBuilder.DropIndex(name: "IX_StudyItems_BookTestId", table: "StudyItems");
            migrationBuilder.DropIndex(name: "IX_StudyItems_BookId", table: "StudyItems");

            migrationBuilder.DropColumn(name: "EndPage", table: "StudyItems");
            migrationBuilder.DropColumn(name: "StartPage", table: "StudyItems");
            migrationBuilder.DropColumn(name: "BookTestId", table: "StudyItems");
            migrationBuilder.DropColumn(name: "BookId", table: "StudyItems");
            migrationBuilder.DropColumn(name: "Platform", table: "StudyItems");
            migrationBuilder.DropColumn(name: "Url", table: "StudyItems");
            migrationBuilder.DropColumn(name: "ContentType", table: "StudyItems");

            migrationBuilder.Sql("ALTER TABLE \"UserProgramStudyPageSchedules\" RENAME CONSTRAINT \"FK_UserProgramStudyPageSchedules_StudyItems_StudyItemId\" TO \"FK_UserProgramStudyPageSchedules_StudyPages_StudyPageId\";");
            migrationBuilder.Sql("ALTER TABLE \"StudyItemImages\" RENAME CONSTRAINT \"FK_StudyItemImages_StudyItems_StudyItemId\" TO \"FK_StudyPageImages_StudyPages_StudyPageId\";");
            migrationBuilder.Sql("ALTER TABLE \"StudyItemImages\" RENAME CONSTRAINT \"PK_StudyItemImages\" TO \"PK_StudyPageImages\";");
            migrationBuilder.Sql("ALTER TABLE \"StudyItems\" RENAME CONSTRAINT \"FK_StudyItems_Topics_TopicId\" TO \"FK_StudyPages_Topics_TopicId\";");
            migrationBuilder.Sql("ALTER TABLE \"StudyItems\" RENAME CONSTRAINT \"FK_StudyItems_Subjects_SubjectId\" TO \"FK_StudyPages_Subjects_SubjectId\";");
            migrationBuilder.Sql("ALTER TABLE \"StudyItems\" RENAME CONSTRAINT \"FK_StudyItems_SubTopics_SubTopicId\" TO \"FK_StudyPages_SubTopics_SubTopicId\";");
            migrationBuilder.Sql("ALTER TABLE \"StudyItems\" RENAME CONSTRAINT \"PK_StudyItems\" TO \"PK_StudyPages\";");

            migrationBuilder.RenameIndex(
                name: "IX_UserProgramStudyPageSchedules_StudyItemId",
                table: "UserProgramStudyPageSchedules",
                newName: "IX_UserProgramStudyPageSchedules_StudyPageId");
            migrationBuilder.RenameIndex(name: "IX_StudyItemImages_StudyItemId", table: "StudyItemImages", newName: "IX_StudyPageImages_StudyPageId");
            migrationBuilder.RenameIndex(name: "IX_StudyItems_TopicId", table: "StudyItems", newName: "IX_StudyPages_TopicId");
            migrationBuilder.RenameIndex(name: "IX_StudyItems_SubTopicId", table: "StudyItems", newName: "IX_StudyPages_SubTopicId");
            migrationBuilder.RenameIndex(name: "IX_StudyItems_SubjectId", table: "StudyItems", newName: "IX_StudyPages_SubjectId");

            migrationBuilder.RenameColumn(
                name: "StudyItemId",
                table: "UserProgramStudyPageSchedules",
                newName: "StudyPageId");

            migrationBuilder.RenameColumn(
                name: "StudyItemId",
                table: "StudyItemImages",
                newName: "StudyPageId");

            migrationBuilder.RenameTable(name: "StudyItemImages", newName: "StudyPageImages");
            migrationBuilder.RenameTable(name: "StudyItems", newName: "StudyPages");
        }
    }
}
