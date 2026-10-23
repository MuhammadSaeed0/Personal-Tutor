using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Riwaq.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameGeographyToGeology : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Subjects",
                keyColumn: "Id",
                keyValue: 7,
                columns: new[] { "Code", "Icon", "NameAr", "NameEn" },
                values: new object[] { "geology", "mountain", "الجيولوجيا", "Geology" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Subjects",
                keyColumn: "Id",
                keyValue: 7,
                columns: new[] { "Code", "Icon", "NameAr", "NameEn" },
                values: new object[] { "geography", "earth", "الجغرافيا", "Geography" });
        }
    }
}
