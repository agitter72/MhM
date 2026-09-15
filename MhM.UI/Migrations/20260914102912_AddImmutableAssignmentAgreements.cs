using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MhM.UI.Migrations
{
    /// <inheritdoc />
    public partial class AddImmutableAssignmentAgreements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AssignmentAgreements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ListingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequesterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HelperId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(3000)", maxLength: 3000, nullable: false),
                    AgreedPrice = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: true),
                    CompensationType = table.Column<int>(type: "int", nullable: false),
                    PreferredDateUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LocationSummary = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    AgreementHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssignmentAgreements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssignmentAgreements_Listings_ListingId",
                        column: x => x.ListingId,
                        principalTable: "Listings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssignmentAgreements_ListingId",
                table: "AssignmentAgreements",
                column: "ListingId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssignmentAgreements");
        }
    }
}
