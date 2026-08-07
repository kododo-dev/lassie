using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Lassie.Migrations
{
    /// <inheritdoc />
    public partial class RemoveLicenseFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LicenseFieldOptions");

            migrationBuilder.DropTable(
                name: "LicenseFields");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LicenseFields",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DataType = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LicenseFields", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LicenseFieldOptions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LicenseFieldId = table.Column<long>(type: "bigint", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LicenseFieldOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LicenseFieldOptions_LicenseFields_LicenseFieldId",
                        column: x => x.LicenseFieldId,
                        principalTable: "LicenseFields",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LicenseFieldOptions_LicenseFieldId_Value",
                table: "LicenseFieldOptions",
                columns: new[] { "LicenseFieldId", "Value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LicenseFields_Name",
                table: "LicenseFields",
                column: "Name",
                unique: true);
        }
    }
}
