using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MhM.UI.Migrations
{
    /// <inheritdoc />
    public partial class AddChatNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Messages_ConversationId",
                table: "Messages");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_ListingId",
                table: "Conversations");

            migrationBuilder.AddColumn<DateTime>(
                name: "DeliveredUtc",
                table: "Messages",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReadUtc",
                table: "Messages",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RecipientUserId",
                table: "Messages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE m
                SET RecipientUserId = CASE
                    WHEN m.SenderUserId = c.RequesterId THEN c.HelperId
                    ELSE c.RequesterId
                END
                FROM Messages AS m
                INNER JOIN Conversations AS c ON c.Id = m.ConversationId
                WHERE m.RecipientUserId IS NULL;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "RecipientUserId",
                table: "Messages",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "UserNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SenderUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ListingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Content = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    LinkUrl = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DeliveredUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReadUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserNotifications_Conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "Conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_UserNotifications_Listings_ListingId",
                        column: x => x.ListingId,
                        principalTable: "Listings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserNotifications_Messages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "Messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserNotifications_Users_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserNotifications_Users_SenderUserId",
                        column: x => x.SenderUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ConversationId_SentUtc",
                table: "Messages",
                columns: new[] { "ConversationId", "SentUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_RecipientUserId_ReadUtc_DeliveredUtc",
                table: "Messages",
                columns: new[] { "RecipientUserId", "ReadUtc", "DeliveredUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_ListingId_RequesterId_HelperId",
                table: "Conversations",
                columns: new[] { "ListingId", "RequesterId", "HelperId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserNotifications_ConversationId",
                table: "UserNotifications",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_UserNotifications_ListingId",
                table: "UserNotifications",
                column: "ListingId");

            migrationBuilder.CreateIndex(
                name: "IX_UserNotifications_MessageId",
                table: "UserNotifications",
                column: "MessageId",
                unique: true,
                filter: "[MessageId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_UserNotifications_RecipientUserId_ReadUtc_DeliveredUtc_CreatedUtc",
                table: "UserNotifications",
                columns: new[] { "RecipientUserId", "ReadUtc", "DeliveredUtc", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_UserNotifications_SenderUserId",
                table: "UserNotifications",
                column: "SenderUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Messages_Users_RecipientUserId",
                table: "Messages",
                column: "RecipientUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Messages_Users_RecipientUserId",
                table: "Messages");

            migrationBuilder.DropTable(
                name: "UserNotifications");

            migrationBuilder.DropIndex(
                name: "IX_Messages_ConversationId_SentUtc",
                table: "Messages");

            migrationBuilder.DropIndex(
                name: "IX_Messages_RecipientUserId_ReadUtc_DeliveredUtc",
                table: "Messages");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_ListingId_RequesterId_HelperId",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "DeliveredUtc",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "ReadUtc",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "RecipientUserId",
                table: "Messages");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ConversationId",
                table: "Messages",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_ListingId",
                table: "Conversations",
                column: "ListingId");
        }
    }
}
