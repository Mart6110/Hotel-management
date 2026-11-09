using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HotelManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PublicBooking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Slug",
                table: "Companies",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AccessCodeHash",
                table: "Bookings",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reference",
                table: "Bookings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "Bookings",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Staff"); // every booking before this migration was entered by staff

            // Backfill existing companies with a slug derived from their name ("Demo Hotels Group" -> "demo-hotels-group"),
            // suffixing duplicates with -2, -3... in creation order. Must run before the unique index is created.
            migrationBuilder.Sql("""
                WITH base AS (
                    SELECT "Id", "CreatedAt",
                           COALESCE(NULLIF(trim(both '-' from left(
                               lower(regexp_replace("Name", '[^a-zA-Z0-9]+', '-', 'g')), 50)), ''), 'company') AS slug
                    FROM "Companies"
                ),
                numbered AS (
                    SELECT "Id", slug, row_number() OVER (PARTITION BY slug ORDER BY "CreatedAt", "Id") AS n
                    FROM base
                )
                UPDATE "Companies" c
                SET "Slug" = CASE WHEN n.n = 1 THEN n.slug ELSE n.slug || '-' || n.n END
                FROM numbered n
                WHERE c."Id" = n."Id";
                """);

            // Backfill existing bookings with a reference (hex digits are a subset of the reference alphabet).
            migrationBuilder.Sql("""
                UPDATE "Bookings"
                SET "Reference" = 'BK-' || upper(substr(md5("Id"::text || random()::text), 1, 8))
                WHERE "Reference" = '';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Companies_Slug",
                table: "Companies",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_Reference",
                table: "Bookings",
                column: "Reference",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Companies_Slug",
                table: "Companies");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_Reference",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "Slug",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "AccessCodeHash",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "Reference",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "Bookings");
        }
    }
}
