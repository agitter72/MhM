using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MhM.UI.Migrations
{
    /// <inheritdoc />
    public partial class AddListingApplicationStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ListingApplications_ListingId",
                table: "ListingApplications");

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "ListingApplications",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "IX_ListingApplications_ListingId_ApplicantId",
                table: "ListingApplications",
                columns: new[] { "ListingId", "ApplicantId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ListingApplications_ListingId_ApplicantId",
                table: "ListingApplications");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "ListingApplications");

            migrationBuilder.CreateIndex(
                name: "IX_ListingApplications_ListingId",
                table: "ListingApplications",
                column: "ListingId");
        }
    }
}
