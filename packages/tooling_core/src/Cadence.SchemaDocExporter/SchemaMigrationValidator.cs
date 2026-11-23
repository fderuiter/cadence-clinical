using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Cadence.SchemaDocExporter;

/// <summary>
/// Validates an EF Core persistence module's model snapshot parity (<c>HasPendingModelChanges()</c>), migration
/// attributes (<see cref="DbContextAttribute"/>, <see cref="MigrationAttribute"/>), <c>.Designer.cs</c> companion
/// files, and DDL table/column creation uniqueness (<see cref="CreateTableOperation"/>, <see cref="AddColumnOperation"/>).
/// </summary>
public static partial class SchemaMigrationValidator
{
    [GeneratedRegex(@"^\d{14}_.+", RegexOptions.CultureInvariant)]
    private static partial Regex TimestampedMigrationStemRegex();

    [GeneratedRegex(@":\s*(?:global::)?(?:Microsoft\.EntityFrameworkCore\.Migrations\.)?Migration\b", RegexOptions.CultureInvariant)]
    private static partial Regex InheritsMigrationRegex();

    /// <summary>
    /// Validates <paramref name="context"/>, compiled <see cref="Migration"/> types, and the on-disk
    /// <paramref name="migrationsDirectory"/> (when provided), returning all detected errors.
    /// </summary>
    public static IReadOnlyList<string> Validate(
        DbContext context,
        string module,
        string? migrationsDirectory,
        string defaultSchema,
        IEnumerable<Type>? migrationTypes = null,
        bool checkPendingModelChanges = true)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(module);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultSchema);

        var errors = new List<string>();
        Type contextType = context.GetType();

        if (checkPendingModelChanges && context.Database.IsRelational() && context.Database.HasPendingModelChanges())
        {
            errors.Add(
                $"[{module}] {contextType.Name} has pending model changes without a migration. " +
                $"Run 'pnpm cadence migrations add {module} <MigrationName>'.");
        }

        var discoveredTypes = (migrationTypes ?? DiscoverMigrationTypes(contextType.Assembly))
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToList();

        var validMigrationsById = new SortedDictionary<string, Type>(StringComparer.Ordinal);

        foreach (Type migrationType in discoveredTypes)
        {
            var dbContextAttr = migrationType.GetCustomAttribute<DbContextAttribute>(inherit: false);
            if (dbContextAttr is null)
            {
                errors.Add(
                    $"[{module}] Migration class '{migrationType.FullName}' is missing [DbContext(typeof({contextType.Name}))] (check its .Designer.cs companion).");
            }
            else if (dbContextAttr.ContextType != contextType)
            {
                errors.Add(
                    $"[{module}] Migration class '{migrationType.FullName}' targets '{dbContextAttr.ContextType.Name}' instead of '{contextType.Name}'.");
            }

            var migrationAttr = migrationType.GetCustomAttribute<MigrationAttribute>(inherit: false);
            string? migrationId = migrationAttr?.Id;
            if (string.IsNullOrWhiteSpace(migrationId))
            {
                errors.Add(
                    $"[{module}] Migration class '{migrationType.FullName}' is missing [Migration(\"<id>\")] (check its .Designer.cs companion).");
            }

            if (!string.IsNullOrWhiteSpace(migrationId))
            {
                if (!validMigrationsById.TryAdd(migrationId, migrationType))
                {
                    errors.Add(
                        $"[{module}] Duplicate [Migration(\"{migrationId}\")] attribute on '{migrationType.FullName}' and '{validMigrationsById[migrationId].FullName}'.");
                }

                if (!string.IsNullOrWhiteSpace(migrationsDirectory) && Directory.Exists(migrationsDirectory))
                {
                    string codeFile = Path.Combine(migrationsDirectory, $"{migrationId}.cs");
                    string designerFile = Path.Combine(migrationsDirectory, $"{migrationId}.Designer.cs");
                    if (!File.Exists(codeFile))
                    {
                        errors.Add($"[{module}] Migration '{migrationId}' is missing source file '{codeFile}'.");
                    }

                    if (!File.Exists(designerFile))
                    {
                        errors.Add($"[{module}] Migration '{migrationId}' is missing companion file '{designerFile}'.");
                    }
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(migrationsDirectory))
        {
            ValidateMigrationsDirectory(module, migrationsDirectory, validMigrationsById, errors);
        }

        ValidateOperationUniqueness(module, defaultSchema, validMigrationsById, errors);

        return errors;
    }

    private static IEnumerable<Type> DiscoverMigrationTypes(Assembly assembly) =>
        assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(Migration).IsAssignableFrom(t));

    private static void ValidateMigrationsDirectory(
        string module,
        string migrationsDirectory,
        IReadOnlyDictionary<string, Type> validMigrationsById,
        List<string> errors)
    {
        if (!Directory.Exists(migrationsDirectory))
        {
            errors.Add($"[{module}] Migrations directory '{migrationsDirectory}' does not exist.");
            return;
        }

        foreach (string filePath in Directory.GetFiles(migrationsDirectory, "*.cs").OrderBy(f => f, StringComparer.Ordinal))
        {
            string fileName = Path.GetFileName(filePath);
            if (fileName.EndsWith("ModelSnapshot.cs", StringComparison.Ordinal))
            {
                continue;
            }

            if (fileName.EndsWith(".Designer.cs", StringComparison.Ordinal))
            {
                string stem = fileName[..^".Designer.cs".Length];
                string companionCode = Path.Combine(migrationsDirectory, $"{stem}.cs");
                if (!File.Exists(companionCode))
                {
                    errors.Add($"[{module}] Orphan designer file '{filePath}' has no matching '{stem}.cs' migration file.");
                }
                else if (!validMigrationsById.ContainsKey(stem))
                {
                    errors.Add($"[{module}] Designer file '{filePath}' does not match any compiled migration with [Migration(\"{stem}\")] and [DbContext].");
                }

                continue;
            }

            string migrationStem = fileName[..^".cs".Length];
            if (!IsMigrationSourceFile(migrationStem, filePath))
            {
                continue;
            }

            string designerPath = Path.Combine(migrationsDirectory, $"{migrationStem}.Designer.cs");
            if (!File.Exists(designerPath))
            {
                errors.Add($"[{module}] Migration file '{filePath}' is missing its '{migrationStem}.Designer.cs' companion.");
            }

            if (!validMigrationsById.ContainsKey(migrationStem))
            {
                errors.Add($"[{module}] Migration file '{filePath}' has no compiled Migration class with [DbContext] and [Migration(\"{migrationStem}\")].");
            }
        }
    }

    private static bool IsMigrationSourceFile(string stem, string filePath)
    {
        if (TimestampedMigrationStemRegex().IsMatch(stem))
        {
            return true;
        }

        string source = File.ReadAllText(filePath);
        return InheritsMigrationRegex().IsMatch(source);
    }

    private static string QualifyTable(string? schema, string defaultSchema, string table) =>
        $"{(string.IsNullOrWhiteSpace(schema) ? defaultSchema : schema)}.{table}";

    private static void ValidateOperationUniqueness(
        string module,
        string defaultSchema,
        IReadOnlyDictionary<string, Type> migrationsById,
        List<string> errors)
    {
        var activeTables = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var tableCreatedBy = new Dictionary<string, string>(StringComparer.Ordinal);
        var columnCreatedBy = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (migrationId, migrationType) in migrationsById)
        {
            var migration = (Migration)Activator.CreateInstance(migrationType, nonPublic: true)!;
            foreach (MigrationOperation operation in migration.UpOperations)
            {
                switch (operation)
                {
                    case CreateTableOperation createTable:
                        {
                            string tableKey = QualifyTable(createTable.Schema, defaultSchema, createTable.Name);
                            if (!activeTables.TryGetValue(tableKey, out var columns))
                            {
                                columns = new HashSet<string>(StringComparer.Ordinal);
                                activeTables[tableKey] = columns;
                                tableCreatedBy[tableKey] = migrationId;
                            }
                            else
                            {
                                string original = tableCreatedBy.GetValueOrDefault(tableKey, "an earlier migration");
                                errors.Add(
                                    $"[{module}] Migration '{migrationId}' duplicates CreateTable for '{tableKey}' (already created in '{original}').");
                            }

                            foreach (string columnName in createTable.Columns.Select(column => column.Name))
                            {
                                string colKey = $"{tableKey}.{columnName}";
                                if (!columns.Add(columnName))
                                {
                                    string original = columnCreatedBy.GetValueOrDefault(colKey, migrationId);
                                    errors.Add(
                                        $"[{module}] Migration '{migrationId}' duplicates column creation for '{colKey}' (already created in '{original}').");
                                }
                                else
                                {
                                    columnCreatedBy[colKey] = migrationId;
                                }
                            }

                            break;
                        }

                    case AddColumnOperation addColumn:
                        {
                            string tableKey = QualifyTable(addColumn.Schema, defaultSchema, addColumn.Table);
                            if (!activeTables.TryGetValue(tableKey, out var columns))
                            {
                                columns = new HashSet<string>(StringComparer.Ordinal);
                                activeTables[tableKey] = columns;
                            }

                            string colKey = $"{tableKey}.{addColumn.Name}";
                            if (!columns.Add(addColumn.Name))
                            {
                                string original = columnCreatedBy.GetValueOrDefault(colKey, "an earlier migration");
                                errors.Add(
                                    $"[{module}] Migration '{migrationId}' duplicates AddColumn for '{colKey}' (already created in '{original}').");
                            }
                            else
                            {
                                columnCreatedBy[colKey] = migrationId;
                            }

                            break;
                        }

                    case DropTableOperation dropTable:
                        {
                            string tableKey = QualifyTable(dropTable.Schema, defaultSchema, dropTable.Name);
                            activeTables.Remove(tableKey);
                            break;
                        }

                    case DropColumnOperation dropColumn:
                        {
                            string tableKey = QualifyTable(dropColumn.Schema, defaultSchema, dropColumn.Table);
                            if (activeTables.TryGetValue(tableKey, out var columns))
                            {
                                columns.Remove(dropColumn.Name);
                            }

                            break;
                        }

                    case RenameTableOperation renameTable:
                        {
                            string oldKey = QualifyTable(renameTable.Schema, defaultSchema, renameTable.Name);
                            string newKey = QualifyTable(renameTable.NewSchema ?? renameTable.Schema, defaultSchema, renameTable.NewName ?? renameTable.Name);
                            if (!string.Equals(oldKey, newKey, StringComparison.Ordinal) && activeTables.Remove(oldKey, out var columns))
                            {
                                activeTables[newKey] = columns;
                                tableCreatedBy[newKey] = migrationId;
                                foreach (string col in columns)
                                {
                                    columnCreatedBy[$"{newKey}.{col}"] = migrationId;
                                }
                            }

                            break;
                        }

                    case RenameColumnOperation renameColumn:
                        {
                            string tableKey = QualifyTable(renameColumn.Schema, defaultSchema, renameColumn.Table);
                            if (activeTables.TryGetValue(tableKey, out var columns) && columns.Remove(renameColumn.Name))
                            {
                                columns.Add(renameColumn.NewName);
                                columnCreatedBy[$"{tableKey}.{renameColumn.NewName}"] = migrationId;
                            }

                            break;
                        }
                }
            }
        }
    }
}
