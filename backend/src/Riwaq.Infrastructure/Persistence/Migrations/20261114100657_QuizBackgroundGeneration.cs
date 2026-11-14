using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Riwaq.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class QuizBackgroundGeneration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RequestJson",
                table: "Quizzes",
                type: "nvarchar(max)",
                maxLength: 400,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequestJson",
                table: "Quizzes");
        }
    }
}
