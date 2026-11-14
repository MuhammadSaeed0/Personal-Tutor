using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Riwaq.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MistakesNotebook : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OriginQuestionId",
                table: "QuizQuestions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Mistakes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuestionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubjectId = table.Column<int>(type: "int", nullable: false),
                    BookId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TimesWrong = table.Column<int>(type: "int", nullable: false),
                    CorrectStreak = table.Column<int>(type: "int", nullable: false),
                    Stage = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    LastWrongAt = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    NextReviewAt = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    MasteredAt = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Mistakes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Mistakes_QuizQuestions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "QuizQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Mistakes_QuestionId",
                table: "Mistakes",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_Mistakes_UserId_MasteredAt_NextReviewAt",
                table: "Mistakes",
                columns: new[] { "UserId", "MasteredAt", "NextReviewAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Mistakes_UserId_QuestionId",
                table: "Mistakes",
                columns: new[] { "UserId", "QuestionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Mistakes");

            migrationBuilder.DropColumn(
                name: "OriginQuestionId",
                table: "QuizQuestions");
        }
    }
}
