using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Riwaq.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExamAssistantUses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AssistantUses",
                table: "QuizAttempts",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AssistantUses",
                table: "QuizAttempts");
        }
    }
}
