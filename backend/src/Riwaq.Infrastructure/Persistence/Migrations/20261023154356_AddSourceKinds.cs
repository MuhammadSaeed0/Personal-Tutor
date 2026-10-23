using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Riwaq.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceKinds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EndSeconds",
                table: "DocumentPages",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StartSeconds",
                table: "DocumentPages",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Author",
                table: "Books",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DurationSeconds",
                table: "Books",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "Books",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "Books",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SourceUrl",
                table: "Books",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EndSeconds",
                table: "DocumentPages");

            migrationBuilder.DropColumn(
                name: "StartSeconds",
                table: "DocumentPages");

            migrationBuilder.DropColumn(
                name: "Author",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "DurationSeconds",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "SourceUrl",
                table: "Books");
        }
    }
}
