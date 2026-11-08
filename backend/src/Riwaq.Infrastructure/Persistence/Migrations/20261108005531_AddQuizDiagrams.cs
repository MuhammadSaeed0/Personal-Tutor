using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Riwaq.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQuizDiagrams : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DiagramAlt",
                table: "QuizQuestions",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DiagramSvg",
                table: "QuizQuestions",
                type: "nvarchar(max)",
                maxLength: 400,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DiagramAlt",
                table: "QuizQuestions");

            migrationBuilder.DropColumn(
                name: "DiagramSvg",
                table: "QuizQuestions");
        }
    }
}
