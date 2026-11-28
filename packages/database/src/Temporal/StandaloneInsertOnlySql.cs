using System;
using System.Collections.Generic;

using Cadence.Kernel.Sql;

using Microsoft.EntityFrameworkCore.Migrations;

namespace Cadence.Database.Temporal;

/// <summary>
/// K-XS-2-compliant PostgreSQL insert-only trigger DDL helpers for standalone clinical schemas (KA001, ADR-2621).
/// Rejects every <c>UPDATE</c>, <c>DELETE</c>, and <c>TRUNCATE</c> on historical revision tables, financial ledger
/// entries, and GxP audit records with SQLSTATE <see cref="ViolationSqlState"/> (<c>KA001</c>).
/// </summary>
public static class StandaloneInsertOnlySql
{
    public const string FunctionName = PostgresInsertOnlySql.FunctionName;
    public const string ViolationSqlState = PostgresInsertOnlySql.ViolationSqlState;

    public static string CreateInsertOnlyFunctionSql(string schema) =>
        PostgresInsertOnlySql.CreateFunctionSql(schema, allowMixedCaseIdentifiers: true);

    public static string DropInsertOnlyFunctionSql(string schema) =>
        PostgresInsertOnlySql.DropFunctionSql(schema, allowMixedCaseIdentifiers: true);

    public static string CreateInsertOnlyGuardsSql(string schema, string table) =>
        PostgresInsertOnlySql.CreateGuardsSql(schema, table, allowMixedCaseIdentifiers: true, dropIfExists: true);

    public static string DropInsertOnlyGuardsSql(string schema, string table) =>
        PostgresInsertOnlySql.DropGuardsSql(schema, table, allowMixedCaseIdentifiers: true);

    public static void EnableStandaloneInsertOnlyGuards(
        this MigrationBuilder migrationBuilder,
        string schema,
        IEnumerable<string> tables)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        ArgumentNullException.ThrowIfNull(tables);

        migrationBuilder.Sql(CreateInsertOnlyFunctionSql(schema));
        foreach (string table in tables)
        {
            migrationBuilder.Sql(CreateInsertOnlyGuardsSql(schema, table));
        }
    }

    public static void DisableStandaloneInsertOnlyGuards(
        this MigrationBuilder migrationBuilder,
        string schema,
        IEnumerable<string> tables)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        ArgumentNullException.ThrowIfNull(tables);

        foreach (string table in tables)
        {
            migrationBuilder.Sql(DropInsertOnlyGuardsSql(schema, table));
        }
    }

    public static string RowTriggerName(string table) => PostgresInsertOnlySql.RowTriggerName(table);

    public static string TruncateTriggerName(string table) => PostgresInsertOnlySql.TruncateTriggerName(table);
}
