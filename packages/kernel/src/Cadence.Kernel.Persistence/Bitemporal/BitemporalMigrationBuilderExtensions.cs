using Microsoft.EntityFrameworkCore.Migrations;

namespace Cadence.Kernel.Persistence.Bitemporal;

/// <summary>
/// Migration helpers for bitemporal tables. A migration that creates a table mapped with
/// <see cref="BitemporalEntityTypeBuilderExtensions.HasBitemporalPeriod"/> must call
/// <see cref="CreateBitemporalGuards"/> right after <c>CreateTable</c> (and <see cref="DropBitemporalGuards"/> in
/// <c>Down</c>). See <see cref="BitemporalSql"/> for what the guards enforce.
/// </summary>
internal static class BitemporalMigrationBuilderExtensions
{
    public static void CreateBitemporalAppendOnlyFunction(this MigrationBuilder migrationBuilder, string schema) =>
        migrationBuilder.Sql(BitemporalSql.CreateAppendOnlyFunction(schema));

    public static void DropBitemporalAppendOnlyFunction(this MigrationBuilder migrationBuilder, string schema) =>
        migrationBuilder.Sql(BitemporalSql.DropAppendOnlyFunction(schema));

    public static void CreateBitemporalGuards(
        this MigrationBuilder migrationBuilder, string schema, string table, params string[] entityKeyColumns) =>
        migrationBuilder.Sql(BitemporalSql.CreateGuards(schema, table, entityKeyColumns));

    public static void DropBitemporalGuards(this MigrationBuilder migrationBuilder, string schema, string table) =>
        migrationBuilder.Sql(BitemporalSql.DropGuards(schema, table));
}
