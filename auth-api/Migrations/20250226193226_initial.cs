using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Badge",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    ImageUrl = table.Column<string>(type: "text", nullable: false),
                    RequiredXP = table.Column<int>(type: "integer", nullable: false),
                    RequiredQuestionsSolved = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Badge", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Books",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Books", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Grades",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Grades", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Passage",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "text", nullable: true),
                    Text = table.Column<string>(type: "text", nullable: true),
                    ImageUrl = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Passage", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Rewards",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    PointsRequired = table.Column<int>(type: "integer", nullable: false),
                    Stock = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rewards", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SpecialEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    RewardPoints = table.Column<int>(type: "integer", nullable: false),
                    StartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpecialEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Subjects",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subjects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FullName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    AvatarUrl = table.Column<string>(type: "text", nullable: true),
                    Role = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BookTests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    BookId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookTests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookTests_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TestPrototypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    GradeId = table.Column<int>(type: "integer", nullable: false),
                    DifficultyLevel = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestPrototypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TestPrototypes_Grades_GradeId",
                        column: x => x.GradeId,
                        principalTable: "Grades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GradeSubjects",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GradeId = table.Column<int>(type: "integer", nullable: false),
                    SubjectId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GradeSubjects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GradeSubjects_Grades_GradeId",
                        column: x => x.GradeId,
                        principalTable: "Grades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GradeSubjects_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Topics",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    SubjectId = table.Column<int>(type: "integer", nullable: false),
                    GradeId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Topics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Topics_Grades_GradeId",
                        column: x => x.GradeId,
                        principalTable: "Grades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Topics_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Parents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Parents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Parents_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Teachers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Subject = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Teachers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Teachers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Worksheets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    GradeId = table.Column<int>(type: "integer", nullable: false),
                    SubjectId = table.Column<int>(type: "integer", nullable: true),
                    MaxDurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    IsPracticeTest = table.Column<bool>(type: "boolean", nullable: false),
                    Subtitle = table.Column<string>(type: "text", nullable: true),
                    ImageUrl = table.Column<string>(type: "text", nullable: true),
                    BadgeText = table.Column<string>(type: "text", nullable: true),
                    BookTestId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Worksheets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Worksheets_BookTests_BookTestId",
                        column: x => x.BookTestId,
                        principalTable: "BookTests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Worksheets_Grades_GradeId",
                        column: x => x.GradeId,
                        principalTable: "Grades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Worksheets_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TestPrototypeDetail",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WorksheetPrototypeId = table.Column<int>(type: "integer", nullable: false),
                    SubjectId = table.Column<int>(type: "integer", nullable: false),
                    QuestionCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestPrototypeDetail", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TestPrototypeDetail_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TestPrototypeDetail_TestPrototypes_WorksheetPrototypeId",
                        column: x => x.WorksheetPrototypeId,
                        principalTable: "TestPrototypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SubTopics",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    TopicId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubTopics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubTopics_Topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "Topics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Students",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    StudentNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SchoolName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    GradeId = table.Column<int>(type: "integer", nullable: true),
                    ParentId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Students", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Students_Grades_GradeId",
                        column: x => x.GradeId,
                        principalTable: "Grades",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Students_Parents_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Parents",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Students_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Leaderboards",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudentId = table.Column<int>(type: "integer", nullable: false),
                    TotalPoints = table.Column<int>(type: "integer", nullable: false),
                    Rank = table.Column<int>(type: "integer", nullable: false),
                    TimePeriod = table.Column<string>(type: "text", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Leaderboards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Leaderboards_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StudentBadges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudentId = table.Column<int>(type: "integer", nullable: false),
                    BadgeId = table.Column<int>(type: "integer", nullable: false),
                    EarnedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentBadges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentBadges_Badge_BadgeId",
                        column: x => x.BadgeId,
                        principalTable: "Badge",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StudentBadges_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StudentPointHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudentId = table.Column<int>(type: "integer", nullable: false),
                    Points = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    EarnedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentPointHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentPointHistories_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StudentPoints",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudentId = table.Column<int>(type: "integer", nullable: false),
                    XP = table.Column<int>(type: "integer", nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentPoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentPoints_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StudentRewards",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudentId = table.Column<int>(type: "integer", nullable: false),
                    RewardId = table.Column<int>(type: "integer", nullable: false),
                    PointsSpent = table.Column<int>(type: "integer", nullable: false),
                    RedeemedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentRewards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentRewards_Rewards_RewardId",
                        column: x => x.RewardId,
                        principalTable: "Rewards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StudentRewards_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StudentSpecialEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudentId = table.Column<int>(type: "integer", nullable: false),
                    SpecialEventId = table.Column<int>(type: "integer", nullable: false),
                    Completed = table.Column<bool>(type: "boolean", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentSpecialEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentSpecialEvents_SpecialEvents_SpecialEventId",
                        column: x => x.SpecialEventId,
                        principalTable: "SpecialEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StudentSpecialEvents_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TestInstances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudentId = table.Column<int>(type: "integer", nullable: false),
                    WorksheetId = table.Column<int>(type: "integer", nullable: false),
                    StartTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestInstances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TestInstances_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TestInstances_Worksheets_WorksheetId",
                        column: x => x.WorksheetId,
                        principalTable: "Worksheets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Answers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Text = table.Column<string>(type: "text", nullable: false),
                    ImageUrl = table.Column<string>(type: "text", nullable: true),
                    QuestionId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Answers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Questions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Text = table.Column<string>(type: "text", nullable: true),
                    SubText = table.Column<string>(type: "text", nullable: true),
                    ImageUrl = table.Column<string>(type: "text", nullable: true),
                    SubjectId = table.Column<int>(type: "integer", nullable: false),
                    Point = table.Column<int>(type: "integer", nullable: false),
                    TopicId = table.Column<int>(type: "integer", nullable: true),
                    SubTopicId = table.Column<int>(type: "integer", nullable: true),
                    DifficultyLevel = table.Column<int>(type: "integer", nullable: false),
                    CorrectAnswerId = table.Column<int>(type: "integer", nullable: true),
                    PassageId = table.Column<int>(type: "integer", nullable: true),
                    PracticeCorrectAnswer = table.Column<string>(type: "text", nullable: true),
                    IsExample = table.Column<bool>(type: "boolean", nullable: false),
                    AnswerColCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Questions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Questions_Answers_CorrectAnswerId",
                        column: x => x.CorrectAnswerId,
                        principalTable: "Answers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Questions_Passage_PassageId",
                        column: x => x.PassageId,
                        principalTable: "Passage",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Questions_SubTopics_SubTopicId",
                        column: x => x.SubTopicId,
                        principalTable: "SubTopics",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Questions_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Questions_Topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "Topics",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TestQuestions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TestId = table.Column<int>(type: "integer", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    QuestionId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TestQuestions_Questions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "Questions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TestQuestions_Worksheets_TestId",
                        column: x => x.TestId,
                        principalTable: "Worksheets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TestInstanceQuestions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WorksheetInstanceId = table.Column<int>(type: "integer", nullable: false),
                    WorksheetQuestionId = table.Column<int>(type: "integer", nullable: false),
                    SelectedAnswerId = table.Column<int>(type: "integer", nullable: true),
                    IsCorrect = table.Column<bool>(type: "boolean", nullable: false),
                    TimeTaken = table.Column<int>(type: "integer", nullable: false),
                    ShowCorrectAnswer = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestInstanceQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TestInstanceQuestions_Answers_SelectedAnswerId",
                        column: x => x.SelectedAnswerId,
                        principalTable: "Answers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TestInstanceQuestions_TestInstances_WorksheetInstanceId",
                        column: x => x.WorksheetInstanceId,
                        principalTable: "TestInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TestInstanceQuestions_TestQuestions_WorksheetQuestionId",
                        column: x => x.WorksheetQuestionId,
                        principalTable: "TestQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Grades",
                columns: new[] { "Id", "Name" },
                values: new object[,]
                {
                    { 1, "1. Sınıf" },
                    { 2, "2. Sınıf" },
                    { 3, "3. Sınıf" },
                    { 4, "4. Sınıf" },
                    { 5, "5. Sınıf" },
                    { 6, "6. Sınıf" },
                    { 7, "7. Sınıf" },
                    { 8, "8. Sınıf" },
                    { 9, "9. Sınıf" },
                    { 10, "10. Sınıf" },
                    { 11, "11. Sınıf" },
                    { 12, "12. Sınıf" }
                });

            migrationBuilder.InsertData(
                table: "Subjects",
                columns: new[] { "Id", "Name" },
                values: new object[,]
                {
                    { 1, "Türkçe" },
                    { 2, "Matematik" },
                    { 3, "Hayat Bilgisi" },
                    { 4, "Fen Bilimleri" },
                    { 5, "Sosyal Bilgiler" },
                    { 6, "T.C. İnkılâp Tarihi ve Atatürkçülük" },
                    { 7, "Yabancı Dil" },
                    { 8, "Din Kültürü ve Ahlak Bilgisi" },
                    { 9, "Türk Dili ve Edebiyatı" },
                    { 10, "Tarih" },
                    { 11, "Coğrafya" },
                    { 12, "Fizik" },
                    { 13, "Kimya" },
                    { 14, "Biyoloji" },
                    { 15, "Felsefe" }
                });

            migrationBuilder.InsertData(
                table: "GradeSubjects",
                columns: new[] { "Id", "GradeId", "SubjectId" },
                values: new object[,]
                {
                    { 1, 1, 1 },
                    { 2, 1, 2 },
                    { 3, 1, 3 },
                    { 4, 2, 1 },
                    { 5, 2, 2 },
                    { 6, 2, 3 },
                    { 7, 3, 1 },
                    { 8, 3, 2 },
                    { 9, 3, 3 },
                    { 10, 3, 4 },
                    { 11, 4, 1 },
                    { 12, 4, 2 },
                    { 13, 4, 4 },
                    { 14, 4, 5 },
                    { 15, 5, 1 },
                    { 16, 5, 2 },
                    { 17, 5, 4 },
                    { 18, 5, 5 },
                    { 19, 5, 7 },
                    { 20, 6, 1 },
                    { 21, 6, 2 },
                    { 22, 6, 4 },
                    { 23, 6, 5 },
                    { 24, 9, 9 },
                    { 25, 9, 10 },
                    { 26, 9, 11 },
                    { 27, 9, 2 },
                    { 28, 9, 12 },
                    { 29, 9, 13 },
                    { 30, 9, 14 },
                    { 31, 9, 7 },
                    { 32, 10, 9 },
                    { 33, 10, 10 },
                    { 34, 10, 11 },
                    { 35, 10, 2 },
                    { 36, 10, 12 },
                    { 37, 10, 13 },
                    { 38, 10, 14 },
                    { 39, 10, 7 },
                    { 40, 11, 9 },
                    { 41, 11, 10 },
                    { 42, 11, 12 },
                    { 43, 11, 13 },
                    { 44, 11, 14 },
                    { 45, 11, 15 }
                });

            migrationBuilder.InsertData(
                table: "Topics",
                columns: new[] { "Id", "GradeId", "Name", "SubjectId" },
                values: new object[,]
                {
                    { 1, 3, "Harf, Hece, Sözcük ve Cümle Bilgisi", 1 },
                    { 2, 3, "Söz Varlığını Geliştirme", 1 },
                    { 3, 3, "Okuduğunu Anlama", 1 },
                    { 4, 3, "Sözcük Türleri", 1 },
                    { 5, 3, "Noktalama İşaretleri", 1 },
                    { 6, 3, "Yazım Kuralları", 1 },
                    { 7, 3, "Doğal Sayılar ve Ritmik Saymalar", 2 },
                    { 8, 3, "Toplama ve Çıkarma İşlemi", 2 },
                    { 9, 3, "Çarpma ve Bölme İşlemi", 2 },
                    { 10, 3, "Kesirler", 2 },
                    { 11, 3, "Zaman Ölçme", 2 },
                    { 12, 3, "Geometrik Cisimler ve Örüntüler", 2 },
                    { 13, 3, "Uzunluk, Alan ve Çevre Ölçme", 2 },
                    { 50, 3, "Gezegenimizi Tanıyalım", 4 },
                    { 51, 3, "Beş Duyumuz", 4 },
                    { 52, 3, "Kuvveti Tanıyalım", 4 },
                    { 53, 3, "Maddeyi Tanıyalım", 4 },
                    { 54, 3, "Çevremizdeki Işık ve Sesler", 4 },
                    { 55, 3, "Canlılar Dünyasına Yolculuk", 4 },
                    { 56, 3, "Elektrikli Araçlar", 4 },
                    { 78, 3, "Okulumuzda Hayat", 3 },
                    { 79, 3, "Evimizde Hayat", 3 },
                    { 80, 3, "Sağlıklı Hayat", 3 },
                    { 81, 3, "Güvenli Hayat", 3 },
                    { 82, 3, "Ülkemizde Hayat", 3 },
                    { 83, 3, "Doğada Hayat", 3 },
                    { 99, 4, "Harf, Hece ve Sözcük Bilgisi", 1 },
                    { 100, 4, "Cümle Bilgisi", 1 },
                    { 101, 4, "Okuma Anlama", 1 },
                    { 102, 4, "Sözcük Türleri", 1 },
                    { 103, 4, "Noktalama İşaretleri", 1 },
                    { 104, 4, "Yazım Kuralları", 1 },
                    { 132, 4, "Doğal Sayılar ve İşlemler", 2 },
                    { 133, 4, "Kesirler ve Kesirlerle İşlemler", 2 },
                    { 134, 4, "Ondalık Gösterim ve Yüzdeler", 2 },
                    { 135, 4, "Temel Geometrik Kavramlar ve Çizimler", 2 },
                    { 136, 4, "Veri Toplama ve Değerlendirme / Uzunluk ve Zaman Ölçme", 2 },
                    { 137, 4, "Alan Ölçme ve Geometrik Cisimler", 2 },
                    { 158, 4, "Yer Kabuğu ve Dünyamızın Hareketleri", 4 },
                    { 159, 4, "Besinlerimiz", 4 },
                    { 160, 4, "Kuvvetin Etkileri", 4 },
                    { 161, 4, "Maddenin Özellikleri", 4 },
                    { 162, 4, "Aydınlatma ve Ses Teknolojileri", 4 },
                    { 163, 4, "İnsan ve Çevre / Canlılar ve Yaşam", 4 },
                    { 164, 4, "Basit Elektrik Devreleri", 4 },
                    { 181, 4, "Birey ve Toplum", 4 },
                    { 182, 4, "Kültür ve Miras", 4 },
                    { 183, 4, "İnsanlar, Yerler ve Çevreler", 4 },
                    { 184, 4, "Bilim, Teknoloji ve Toplum", 4 },
                    { 185, 4, "Üretim, Dağıtım ve Tüketim", 4 },
                    { 186, 4, "Etkin Vatandaşlık", 4 },
                    { 187, 4, "Küresel Bağlantılar", 4 },
                    { 202, 4, "Greetings", 5 },
                    { 203, 4, "Classroom Rules", 5 },
                    { 204, 4, "Numbers and Counting", 5 },
                    { 205, 4, "Colors and Shapes", 5 },
                    { 206, 4, "My Family", 5 },
                    { 207, 4, "Daily Routines", 5 },
                    { 208, 4, "My House", 5 },
                    { 209, 4, "Food and Drinks", 5 },
                    { 210, 4, "Animals", 5 }
                });

            migrationBuilder.InsertData(
                table: "SubTopics",
                columns: new[] { "Id", "Name", "TopicId" },
                values: new object[,]
                {
                    { 1, "Harf, Hece, Sözcük 1", 1 },
                    { 2, "Harf, Hece, Sözcük 2", 1 },
                    { 3, "Eş Anlamlı (Anlamdaş) Sözcükler", 1 },
                    { 4, "Zıt (Karşıt) Anlamlı Sözcükler", 1 },
                    { 5, "Eş Sesli (Sesteş) Sözcükler", 1 },
                    { 6, "Sözcük Türetme - Ekler", 1 },
                    { 7, "Sözcükte Anlam", 1 },
                    { 8, "Cümle Türleri", 1 },
                    { 9, "Cümlede Anlam", 1 },
                    { 10, "Sebep-Sonuç İlişkileri", 2 },
                    { 11, "Karşılaştırmalar", 2 },
                    { 12, "Betimlemeler", 2 },
                    { 13, "5N 1K", 3 },
                    { 14, "Olayların Oluş Sırası", 3 },
                    { 15, "Öykü Unsurları", 3 },
                    { 16, "Başlık Konu İlişkisi", 3 },
                    { 17, "Ana Duygu", 3 },
                    { 18, "Ana Düşünce", 3 },
                    { 19, "Okuma Anlama 1", 3 },
                    { 20, "Okuma Anlama 2", 3 },
                    { 21, "Okuma Anlama 3", 3 },
                    { 22, "Görsel Yorumlama 1", 3 },
                    { 23, "Görsel Yorumlama 2", 3 },
                    { 24, "Üç Basamaklı Doğal Sayılar", 7 },
                    { 25, "Birer, Onar ve Yüzer Ritmik Sayma", 7 },
                    { 26, "Basamak Adları ve Basamak Değerleri", 7 },
                    { 27, "En Yakın Onluk ve Yüzlük", 7 },
                    { 28, "Doğal Sayıları Karşılaştırma ve Sıralama", 7 },
                    { 29, "Ritmik Saymalar", 7 },
                    { 30, "Sayı Örüntüleri", 7 },
                    { 31, "Eldeli ve Eldesiz Toplama İşlemi", 8 },
                    { 32, "Toplananların Yer Değiştirmesi", 8 },
                    { 33, "Onluk Bozmadan ve Bozarak Çıkarma İşlemi", 8 },
                    { 34, "10’un ve 100’ün Katlarıyla Zihinden Çıkarma İşlemi", 8 },
                    { 35, "Çarpım Tablosu", 9 },
                    { 36, "Çarpma İşlemi", 9 },
                    { 37, "10 ve 100 ile Kısa Yoldan Çarpma İşlemi", 9 },
                    { 38, "Çarpma İşlemi ile İlgili Problemler", 9 },
                    { 39, "İki Basamaklı Doğal Sayılarla Bölme İşlemi", 9 },
                    { 40, "Bölme İşlemi ile İlgili Problemler", 9 },
                    { 41, "Kesirler", 10 },
                    { 42, "Bir Çokluğun Belirtilen Kesir Kadarı", 10 },
                    { 43, "Zaman Ölçme", 11 },
                    { 44, "Zaman Ölçme ile İlgili Problemler", 11 },
                    { 45, "Geometrik Cisimler", 12 },
                    { 46, "Örüntüler", 12 },
                    { 47, "Uzunluk Ölçme", 13 },
                    { 48, "Alan Ölçme", 13 },
                    { 49, "Çevre Ölçme", 13 },
                    { 57, "Dünya’nın Şekli", 50 },
                    { 58, "Dünya’nın Yapısı", 50 },
                    { 59, "Duyu Organları ve Önemi", 51 },
                    { 60, "Duyu Organlarının Temel Görevleri", 51 },
                    { 61, "Duyu Organlarının Sağlığı", 51 },
                    { 62, "Varlıkların Hareket Özellikleri", 52 },
                    { 63, "Cisimleri Hareket Ettirme ve Durdurma", 52 },
                    { 64, "Hareketli Cisimlerin Sebep Olabileceği Tehlikeler", 52 },
                    { 65, "Maddeyi Niteleyen Özellikler 1", 53 },
                    { 66, "Maddeyi Niteleyen Özellikler 2", 53 },
                    { 67, "Maddenin Halleri", 53 },
                    { 68, "Işığın Görmedeki Rolü", 54 },
                    { 69, "Işık Kaynakları", 54 },
                    { 70, "Sesin İşitmedeki Rolü", 54 },
                    { 71, "Çevremizdeki Sesler", 54 },
                    { 72, "Çevremizdeki Varlıkları Tanıyalım", 55 },
                    { 73, "Ben ve Çevrem", 55 },
                    { 74, "Doğal ve Yapay Çevre", 55 },
                    { 75, "Elektrikli Araç-Gereçler", 56 },
                    { 76, "Elektrik Kaynakları", 56 },
                    { 77, "Elektriğin Güvenli Kullanımı", 56 },
                    { 84, "Okulumuzda Hayat 1", 78 },
                    { 85, "Okulumuzda Hayat 2", 78 },
                    { 86, "Okulumuzda Hayat 3", 78 },
                    { 87, "Evimizde Hayat 1", 79 },
                    { 88, "Evimizde Hayat 2", 79 },
                    { 89, "Evimizde Hayat 3", 79 },
                    { 90, "Sağlıklı Hayat 1", 80 },
                    { 91, "Sağlıklı Hayat 2", 80 },
                    { 92, "Güvenli Hayat 1", 81 },
                    { 93, "Güvenli Hayat 2", 81 },
                    { 94, "Ülkemizde Hayat 1", 82 },
                    { 95, "Ülkemizde Hayat 2", 82 },
                    { 96, "Ülkemizde Hayat 3", 82 },
                    { 97, "Doğada Hayat 1", 83 },
                    { 98, "Doğada Hayat 2", 83 },
                    { 105, "Harf, Hece, Sözcük", 99 },
                    { 106, "Eş Anlamlı (Anlamdaş) Sözcükler", 99 },
                    { 107, "Zıt (Karşıt) Anlamlı Sözcükler", 99 },
                    { 108, "Eş Sesli (Sesteş) Sözcükler", 99 },
                    { 109, "Gerçek, Mecaz ve Terim Anlam", 99 },
                    { 110, "Sözcük Türetme - Ekler", 99 },
                    { 111, "Sözcükte Anlam", 99 },
                    { 112, "Cümle", 100 },
                    { 113, "Sebep - Sonuç İlişkileri", 100 },
                    { 114, "Karşılaştırmalar", 100 },
                    { 115, "Öznel - Nesnel Yargılar", 100 },
                    { 116, "Duygusal ve Abartılı İfadeler", 100 },
                    { 117, "Atasözü, Deyim, Özdeyiş", 100 },
                    { 118, "Dil, İfade ve Bilgi Yanlışları", 100 },
                    { 119, "5N 1K", 101 },
                    { 120, "Öykü Unsurları", 101 },
                    { 121, "Oluş Sırası", 101 },
                    { 122, "Başlık - Konu İlişkisi", 101 },
                    { 123, "Ana Düşünce", 101 },
                    { 124, "Okuma Anlama", 101 },
                    { 125, "Paragrafta Anlam", 101 },
                    { 126, "Adlar", 102 },
                    { 127, "Varlıkların Özelliklerini Belirten Sözcükler", 102 },
                    { 128, "Adın Yerine Kullanılan Sözcükler", 102 },
                    { 129, "Eylem Bildiren Sözcükler", 102 },
                    { 130, "Noktalama İşaretleri", 103 },
                    { 131, "Yazım Kuralları", 104 },
                    { 138, "Milyonlar", 132 },
                    { 139, "Sayı ve Şekil Örüntüleri", 132 },
                    { 140, "Doğal Sayılarla Toplama ve Çıkarma İşlemleri", 132 },
                    { 141, "Doğal Sayılarla Çarpma ve Bölme İşlemleri", 132 },
                    { 142, "Bir Doğal Sayının Karesi ve Küpü", 132 },
                    { 143, "Birim Kesirler", 133 },
                    { 144, "Denk Kesirler", 133 },
                    { 145, "Kesirlerde Sıralama", 133 },
                    { 146, "Basit Kesirlerde İşlemler", 133 },
                    { 147, "Kesirlerde Problemler", 133 },
                    { 148, "Ondalık Gösterim", 134 },
                    { 149, "Yüzdeler", 134 },
                    { 150, "Temel Geometrik Kavramlar ve Çizimler", 135 },
                    { 151, "Üçgenler ve Dörtgenler", 135 },
                    { 152, "Veri Toplama ve Değerlendirme", 136 },
                    { 153, "Uzunluk Ölçüleri", 136 },
                    { 154, "Çevre Uzunluğu", 136 },
                    { 155, "Zaman Ölçüleri", 136 },
                    { 156, "Alan Ölçme", 137 },
                    { 157, "Geometrik Cisimler", 137 },
                    { 165, "Yer Kabuğunun Yapısı", 158 },
                    { 166, "Dünyamızın Hareketleri", 158 },
                    { 167, "Besinler ve Özellikleri", 159 },
                    { 168, "İnsan Sağlığı ve Zararlı Maddeler", 159 },
                    { 169, "Kuvvetin Cisimler Üzerindeki Etkileri", 160 },
                    { 170, "Mıknatısın Çekme Kuvveti", 160 },
                    { 171, "Maddeyi Niteleyen Özellikler", 161 },
                    { 172, "Maddenin Ölçülebilir Özellikleri", 161 },
                    { 173, "Maddenin Halleri", 161 },
                    { 174, "Maddenin Isı Etkisiyle Değişimi", 161 },
                    { 175, "Aydınlatma Teknolojileri", 162 },
                    { 176, "Uygun Aydınlatma - Işık Kirliliği", 162 },
                    { 177, "Geçmişten Günümüze Ses Teknolojileri", 162 },
                    { 178, "Bilinçli Tüketici - Tasarruf", 163 },
                    { 179, "Geri Dönüşümün Önemi", 163 },
                    { 180, "Basit Elektrik Devreleri", 164 },
                    { 188, "Birey ve Toplum 1", 181 },
                    { 189, "Birey ve Toplum 2", 181 },
                    { 190, "Kültür ve Miras 1", 182 },
                    { 191, "Kültür ve Miras 2", 182 },
                    { 192, "İnsanlar, Yerler ve Çevre 1", 183 },
                    { 193, "İnsanlar, Yerler ve Çevre 2", 183 },
                    { 194, "Bilim, Teknoloji ve Toplum 1", 184 },
                    { 195, "Bilim, Teknoloji ve Toplum 2", 184 },
                    { 196, "Üretim, Dağıtım ve Tüketim 1", 185 },
                    { 197, "Üretim, Dağıtım ve Tüketim 2", 185 },
                    { 198, "Etkin Vatandaşlık 1", 186 },
                    { 199, "Etkin Vatandaşlık 2", 186 },
                    { 200, "Küresel Bağlantılar 1", 187 },
                    { 201, "Küresel Bağlantılar 2", 187 },
                    { 211, "Introducing Yourself", 202 },
                    { 212, "Common Greetings and Responses", 202 },
                    { 213, "Classroom Instructions", 203 },
                    { 214, "Polite Expressions", 203 },
                    { 215, "Counting to 100", 204 },
                    { 216, "Ordinal Numbers", 204 },
                    { 217, "Basic Colors", 205 },
                    { 218, "Shapes and Their Properties", 205 },
                    { 219, "Family Members", 206 },
                    { 220, "Describing My Family", 206 },
                    { 221, "Daily Activities", 207 },
                    { 222, "Telling the Time", 207 },
                    { 223, "Rooms in the House", 208 },
                    { 224, "Furniture and Objects", 208 },
                    { 225, "Common Foods", 209 },
                    { 226, "Healthy Eating", 209 },
                    { 227, "Domestic Animals", 210 },
                    { 228, "Wild Animals", 210 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Answers_QuestionId",
                table: "Answers",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_BookTests_BookId",
                table: "BookTests",
                column: "BookId");

            migrationBuilder.CreateIndex(
                name: "IX_GradeSubjects_GradeId",
                table: "GradeSubjects",
                column: "GradeId");

            migrationBuilder.CreateIndex(
                name: "IX_GradeSubjects_SubjectId",
                table: "GradeSubjects",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Leaderboards_StudentId",
                table: "Leaderboards",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_Parents_UserId",
                table: "Parents",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Questions_CorrectAnswerId",
                table: "Questions",
                column: "CorrectAnswerId");

            migrationBuilder.CreateIndex(
                name: "IX_Questions_PassageId",
                table: "Questions",
                column: "PassageId");

            migrationBuilder.CreateIndex(
                name: "IX_Questions_SubjectId",
                table: "Questions",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Questions_SubTopicId",
                table: "Questions",
                column: "SubTopicId");

            migrationBuilder.CreateIndex(
                name: "IX_Questions_TopicId",
                table: "Questions",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentBadges_BadgeId",
                table: "StudentBadges",
                column: "BadgeId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentBadges_StudentId",
                table: "StudentBadges",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentPointHistories_StudentId",
                table: "StudentPointHistories",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentPoints_StudentId",
                table: "StudentPoints",
                column: "StudentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudentRewards_RewardId",
                table: "StudentRewards",
                column: "RewardId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentRewards_StudentId",
                table: "StudentRewards",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_Students_GradeId",
                table: "Students",
                column: "GradeId");

            migrationBuilder.CreateIndex(
                name: "IX_Students_ParentId",
                table: "Students",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_Students_UserId",
                table: "Students",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudentSpecialEvents_SpecialEventId",
                table: "StudentSpecialEvents",
                column: "SpecialEventId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentSpecialEvents_StudentId",
                table: "StudentSpecialEvents",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_SubTopics_TopicId",
                table: "SubTopics",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_Teachers_UserId",
                table: "Teachers",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TestInstanceQuestions_SelectedAnswerId",
                table: "TestInstanceQuestions",
                column: "SelectedAnswerId");

            migrationBuilder.CreateIndex(
                name: "IX_TestInstanceQuestions_WorksheetInstanceId",
                table: "TestInstanceQuestions",
                column: "WorksheetInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_TestInstanceQuestions_WorksheetQuestionId",
                table: "TestInstanceQuestions",
                column: "WorksheetQuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_TestInstances_StudentId",
                table: "TestInstances",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_TestInstances_WorksheetId",
                table: "TestInstances",
                column: "WorksheetId");

            migrationBuilder.CreateIndex(
                name: "IX_TestPrototypeDetail_SubjectId",
                table: "TestPrototypeDetail",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_TestPrototypeDetail_WorksheetPrototypeId",
                table: "TestPrototypeDetail",
                column: "WorksheetPrototypeId");

            migrationBuilder.CreateIndex(
                name: "IX_TestPrototypes_GradeId",
                table: "TestPrototypes",
                column: "GradeId");

            migrationBuilder.CreateIndex(
                name: "IX_TestQuestions_QuestionId",
                table: "TestQuestions",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_TestQuestions_TestId",
                table: "TestQuestions",
                column: "TestId");

            migrationBuilder.CreateIndex(
                name: "IX_Topics_GradeId",
                table: "Topics",
                column: "GradeId");

            migrationBuilder.CreateIndex(
                name: "IX_Topics_SubjectId",
                table: "Topics",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Worksheets_BookTestId",
                table: "Worksheets",
                column: "BookTestId");

            migrationBuilder.CreateIndex(
                name: "IX_Worksheets_GradeId",
                table: "Worksheets",
                column: "GradeId");

            migrationBuilder.CreateIndex(
                name: "IX_Worksheets_SubjectId",
                table: "Worksheets",
                column: "SubjectId");

            migrationBuilder.AddForeignKey(
                name: "FK_Answers_Questions_QuestionId",
                table: "Answers",
                column: "QuestionId",
                principalTable: "Questions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Answers_Questions_QuestionId",
                table: "Answers");

            migrationBuilder.DropTable(
                name: "GradeSubjects");

            migrationBuilder.DropTable(
                name: "Leaderboards");

            migrationBuilder.DropTable(
                name: "StudentBadges");

            migrationBuilder.DropTable(
                name: "StudentPointHistories");

            migrationBuilder.DropTable(
                name: "StudentPoints");

            migrationBuilder.DropTable(
                name: "StudentRewards");

            migrationBuilder.DropTable(
                name: "StudentSpecialEvents");

            migrationBuilder.DropTable(
                name: "Teachers");

            migrationBuilder.DropTable(
                name: "TestInstanceQuestions");

            migrationBuilder.DropTable(
                name: "TestPrototypeDetail");

            migrationBuilder.DropTable(
                name: "Badge");

            migrationBuilder.DropTable(
                name: "Rewards");

            migrationBuilder.DropTable(
                name: "SpecialEvents");

            migrationBuilder.DropTable(
                name: "TestInstances");

            migrationBuilder.DropTable(
                name: "TestQuestions");

            migrationBuilder.DropTable(
                name: "TestPrototypes");

            migrationBuilder.DropTable(
                name: "Students");

            migrationBuilder.DropTable(
                name: "Worksheets");

            migrationBuilder.DropTable(
                name: "Parents");

            migrationBuilder.DropTable(
                name: "BookTests");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Books");

            migrationBuilder.DropTable(
                name: "Questions");

            migrationBuilder.DropTable(
                name: "Answers");

            migrationBuilder.DropTable(
                name: "Passage");

            migrationBuilder.DropTable(
                name: "SubTopics");

            migrationBuilder.DropTable(
                name: "Topics");

            migrationBuilder.DropTable(
                name: "Grades");

            migrationBuilder.DropTable(
                name: "Subjects");
        }
    }
}
