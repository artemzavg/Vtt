using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861
#pragma warning disable CA1711

namespace Vtt.Campaign.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddCampaignDomainStream : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "domain_events",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                StreamId = table.Column<Guid>(type: "uuid", nullable: false),
                StreamVersion = table.Column<long>(type: "bigint", nullable: false),
                EventType = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                Payload = table.Column<string>(type: "jsonb", nullable: false),
                OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_domain_events", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_domain_events_StreamId_StreamVersion",
            table: "domain_events",
            columns: new[] { "StreamId", "StreamVersion" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "domain_events");
    }
}
