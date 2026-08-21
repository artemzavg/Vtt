using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace Vtt.Campaign.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddCampaignOutboxRelay : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "LastPublishError",
            table: "integration_outbox",
            type: "character varying(1000)",
            maxLength: 1000,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "PublishAttempts",
            table: "integration_outbox",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "PublishedAt",
            table: "integration_outbox",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_integration_outbox_PublishedAt_OccurredAt",
            table: "integration_outbox",
            columns: new[] { "PublishedAt", "OccurredAt" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_integration_outbox_PublishedAt_OccurredAt",
            table: "integration_outbox");

        migrationBuilder.DropColumn(
            name: "LastPublishError",
            table: "integration_outbox");

        migrationBuilder.DropColumn(
            name: "PublishAttempts",
            table: "integration_outbox");

        migrationBuilder.DropColumn(
            name: "PublishedAt",
            table: "integration_outbox");
    }
}
