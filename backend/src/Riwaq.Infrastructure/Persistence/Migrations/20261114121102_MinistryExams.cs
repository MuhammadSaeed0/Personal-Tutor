using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Riwaq.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MinistryExams : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MinistryFormat",
                table: "Quizzes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ExamTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubjectId = table.Column<int>(type: "int", nullable: false),
                    MultipleChoiceCount = table.Column<int>(type: "int", nullable: false),
                    TwoMarkChoiceCount = table.Column<int>(type: "int", nullable: false),
                    EssayCount = table.Column<int>(type: "int", nullable: false),
                    EssayPoints = table.Column<double>(type: "float", nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    DiagramCount = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamTemplates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExamTemplates_UserId_SubjectId",
                table: "ExamTemplates",
                columns: new[] { "UserId", "SubjectId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExamTemplates");

            migrationBuilder.DropColumn(
                name: "MinistryFormat",
                table: "Quizzes");
        }
    }
}
