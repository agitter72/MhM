using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MhM.UI.Migrations
{
    /// <inheritdoc />
    public partial class EnforceUniqueReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                WITH [RankedReviews] AS (
                    SELECT [Id], ROW_NUMBER() OVER (
                        PARTITION BY [ListingId], [ReviewerId], [RevieweeId]
                        ORDER BY [CreatedUtc] DESC, [Id] DESC) AS [RowNumber]
                    FROM [Reviews]
                )
                DELETE [Reviews]
                WHERE [Id] IN (SELECT [Id] FROM [RankedReviews] WHERE [RowNumber] > 1);
                """);

            migrationBuilder.DropIndex(
                name: "IX_Reviews_ListingId",
                table: "Reviews");

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_ListingId_ReviewerId_RevieweeId",
                table: "Reviews",
                columns: new[] { "ListingId", "ReviewerId", "RevieweeId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Reviews_ListingId_ReviewerId_RevieweeId",
                table: "Reviews");

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_ListingId",
                table: "Reviews",
                column: "ListingId");
        }
    }
}
