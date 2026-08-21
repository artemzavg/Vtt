using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace Vtt.Campaign.Infrastructure.Migrations;

/// <inheritdoc />
public partial class InitialCampaignAccess : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "campaign_audit",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Target = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                PolicyRevision = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_campaign_audit", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "campaigns",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                Locale = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                TimeZone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                RulesetVersionId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                Status = table.Column<int>(type: "integer", nullable: false),
                AutomationLevel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                DicePolicy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false),
                PolicyRevision = table.Column<long>(type: "bigint", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_campaigns", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "integration_outbox",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                EventType = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                PolicyRevision = table.Column<long>(type: "bigint", nullable: false),
                Payload = table.Column<string>(type: "jsonb", nullable: false),
                OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_integration_outbox", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "invitations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                Role = table.Column<int>(type: "integer", nullable: false),
                TokenHash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                MaxUses = table.Column<int>(type: "integer", nullable: false),
                UseCount = table.Column<int>(type: "integer", nullable: false),
                IsRevoked = table.Column<bool>(type: "boolean", nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_invitations", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "memberships",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                Role = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false),
                JoinedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_memberships", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "ownership_transfers",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                FromUserId = table.Column<Guid>(type: "uuid", nullable: false),
                ToUserId = table.Column<Guid>(type: "uuid", nullable: false),
                ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                AcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ownership_transfers", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_campaign_audit_CampaignId_OccurredAt",
            table: "campaign_audit",
            columns: new[] { "CampaignId", "OccurredAt" });

        migrationBuilder.CreateIndex(
            name: "IX_integration_outbox_CampaignId_PolicyRevision",
            table: "integration_outbox",
            columns: new[] { "CampaignId", "PolicyRevision" });

        migrationBuilder.CreateIndex(
            name: "IX_invitations_TokenHash",
            table: "invitations",
            column: "TokenHash",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_memberships_CampaignId_UserId",
            table: "memberships",
            columns: new[] { "CampaignId", "UserId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_memberships_UserId",
            table: "memberships",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_ownership_transfers_CampaignId",
            table: "ownership_transfers",
            column: "CampaignId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "campaign_audit");

        migrationBuilder.DropTable(
            name: "campaigns");

        migrationBuilder.DropTable(
            name: "integration_outbox");

        migrationBuilder.DropTable(
            name: "invitations");

        migrationBuilder.DropTable(
            name: "memberships");

        migrationBuilder.DropTable(
            name: "ownership_transfers");
    }
}
