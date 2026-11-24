using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

using Cadence.ToolingCore.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Cadence.SchemaDocExporter;

/// <summary>
/// Validates an EF Core persistence module's model snapshot parity (<c>HasPendingModelChanges()</c>), split per-entity
/// snapshot partials under <c>Migrations/Snapshots/</c>, compacted <c>.Designer.cs</c> companion files and attributes
/// (<see cref="DbContextAttribute"/>, <see cref="MigrationAttribute"/>), and DDL uniqueness across tables, columns,
/// indexes, foreign keys, unique constraints, and check constraints.
/// </summary>
public static partial class SchemaMigrationValidator
{
    [GeneratedRegex(@"^\d{14}_.+", RegexOptions.CultureInvariant)]
    private static partial Regex TimestampedMigrationStemRegex();

    [GeneratedRegex(@":\s*(?:global::)?(?:Microsoft\.EntityFrameworkCore\.Migrations\.)?Migration\b", RegexOptions.CultureInvariant)]
    private static partial Regex InheritsMigrationRegex();

    [GeneratedRegex(@"\bprotected\s+override\s+void\s+BuildTargetModel\s*\(", RegexOptions.CultureInvariant)]
    private static partial Regex BuildTargetModelRegex();

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
            var expectedSnapshotFiles = context.Model.GetEntityTypes()
                .Where(static et => !et.IsOwned())
                .Select(static et => MigrationSnapshotCompactor.GetSnapshotFileName(et.Name))
                .ToHashSet(StringComparer.Ordinal);

            ValidateMigrationsDirectory(
                module,
                contextType.Name,
                migrationsDirectory,
                validMigrationsById,
                expectedSnapshotFiles,
                requireSnapshotWhenMigrated: checkPendingModelChanges,
                errors);
        }

        ValidateOperationUniqueness(module, defaultSchema, validMigrationsById, errors);

        return errors;
    }

    private static IEnumerable<Type> DiscoverMigrationTypes(Assembly assembly) =>
        assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(Migration).IsAssignableFrom(t));

    private static void ValidateMigrationsDirectory(
        string module,
        string contextName,
        string migrationsDirectory,
        IReadOnlyDictionary<string, Type> validMigrationsById,
        IReadOnlySet<string> expectedSnapshotFiles,
        bool requireSnapshotWhenMigrated,
        List<string> errors)
    {
        if (!Directory.Exists(migrationsDirectory))
        {
            errors.Add($"[{module}] Migrations directory '{migrationsDirectory}' does not exist.");
            return;
        }

        var snapshotFiles = new List<string>();

        foreach (string filePath in Directory.GetFiles(migrationsDirectory, "*.cs").OrderBy(f => f, StringComparer.Ordinal))
        {
            string fileName = Path.GetFileName(filePath);
            if (fileName.EndsWith("ModelSnapshot.cs", StringComparison.Ordinal))
            {
                snapshotFiles.Add(filePath);
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

                string designerSource = File.ReadAllText(filePath, Encoding.UTF8);
                if (BuildTargetModelRegex().IsMatch(designerSource))
                {
                    errors.Add(
                        $"[{module}] Designer file '{filePath}' contains an uncompacted BuildTargetModel body. " +
                        "Run 'pnpm cadence fix' to compact Designer files to [DbContext] and [Migration] declarations.");
                }

                if (!StartsWithAutoGeneratedHeader(designerSource))
                {
                    errors.Add($"[{module}] Designer file '{filePath}' must start with '// <auto-generated />'.");
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

        ValidateSnapshotLayout(
            module,
            contextName,
            migrationsDirectory,
            snapshotFiles,
            expectedSnapshotFiles,
            requireSnapshot: requireSnapshotWhenMigrated && validMigrationsById.Count > 0,
            errors);

        foreach (string subDir in Directory.GetDirectories(migrationsDirectory).OrderBy(d => d, StringComparer.Ordinal))
        {
            string dirName = Path.GetFileName(subDir);
            if (string.Equals(dirName, MigrationSnapshotCompactor.SnapshotsSubdirectory, StringComparison.Ordinal))
            {
                continue;
            }

            if (Directory.EnumerateFiles(subDir, "*.cs", SearchOption.AllDirectories).Any())
            {
                errors.Add($"[{module}] Unexpected C# subdirectory '{subDir}' under Migrations/; only '{MigrationSnapshotCompactor.SnapshotsSubdirectory}/' is permitted.");
            }
        }
    }

    private static void ValidateSnapshotLayout(
        string module,
        string contextName,
        string migrationsDirectory,
        IReadOnlyList<string> snapshotFiles,
        IReadOnlySet<string> expectedSnapshotFiles,
        bool requireSnapshot,
        List<string> errors)
    {
        string snapshotsDir = Path.Combine(migrationsDirectory, MigrationSnapshotCompactor.SnapshotsSubdirectory);

        if (snapshotFiles.Count > 1)
        {
            errors.Add(
                $"[{module}] Expected a single *ModelSnapshot.cs in '{migrationsDirectory}', found {snapshotFiles.Count}: " +
                string.Join(", ", snapshotFiles.Select(Path.GetFileName)));
        }
        else if (snapshotFiles.Count == 0 && requireSnapshot)
        {
            errors.Add($"[{module}] Missing *ModelSnapshot.cs in '{migrationsDirectory}'.");
        }

        bool hasSplitRootSnapshot = false;
        if (snapshotFiles.Count == 1)
        {
            string rootSnapshotPath = snapshotFiles[0];
            string rootSource = File.ReadAllText(rootSnapshotPath, Encoding.UTF8);

            if (!StartsWithAutoGeneratedHeader(rootSource))
            {
                errors.Add($"[{module}] Snapshot file '{rootSnapshotPath}' must start with '// <auto-generated />'.");
            }

            if (rootSource.Contains("modelBuilder.Entity(", StringComparison.Ordinal) ||
                !rootSource.Contains("interface IEntitySnapshot", StringComparison.Ordinal))
            {
                errors.Add(
                    $"[{module}] Snapshot file '{rootSnapshotPath}' is unsplit (must delegate entity configuration to '{MigrationSnapshotCompactor.SnapshotsSubdirectory}/*{MigrationSnapshotCompactor.SnapshotFileSuffix}'). " +
                    $"Run 'pnpm cadence fix' or 'pnpm cadence migrations add {module} <MigrationName>'.");
            }
            else
            {
                hasSplitRootSnapshot = true;
            }
        }

        if (hasSplitRootSnapshot && expectedSnapshotFiles.Count > 0)
        {
            if (!Directory.Exists(snapshotsDir))
            {
                errors.Add($"[{module}] Split snapshot for {contextName} is missing partial directory '{snapshotsDir}'.");
            }
            else
            {
                foreach (string expectedFile in expectedSnapshotFiles.OrderBy(f => f, StringComparer.Ordinal))
                {
                    string expectedPath = Path.Combine(snapshotsDir, expectedFile);
                    if (!File.Exists(expectedPath))
                    {
                        errors.Add($"[{module}] Missing entity snapshot partial '{expectedPath}' for {contextName}.");
                    }
                }
            }
        }

        if (Directory.Exists(snapshotsDir))
        {
            foreach (string nestedDir in Directory.GetDirectories(snapshotsDir).OrderBy(d => d, StringComparer.Ordinal))
            {
                errors.Add($"[{module}] Unexpected subdirectory '{nestedDir}' inside '{snapshotsDir}'.");
            }

            foreach (string partialPath in Directory.GetFiles(snapshotsDir, "*", SearchOption.TopDirectoryOnly).OrderBy(f => f, StringComparer.Ordinal))
            {
                string fileName = Path.GetFileName(partialPath);
                if (snapshotFiles.Count == 0 || !expectedSnapshotFiles.Contains(fileName))
                {
                    errors.Add(
                        $"[{module}] Orphan snapshot partial '{partialPath}' does not match any non-owned entity in {contextName}.");
                    continue;
                }

                string partialSource = File.ReadAllText(partialPath, Encoding.UTF8);
                if (!StartsWithAutoGeneratedHeader(partialSource))
                {
                    errors.Add($"[{module}] Snapshot partial '{partialPath}' must start with '// <auto-generated />'.");
                }
            }
        }
    }

    private static bool StartsWithAutoGeneratedHeader(string source) =>
        source.TrimStart('\uFEFF').StartsWith("// <auto-generated />", StringComparison.Ordinal);

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
        var activeTables = new Dictionary<string, TableSchemaState>(StringComparer.Ordinal);
        var tableCreatedBy = new Dictionary<string, string>(StringComparer.Ordinal);
        var columnCreatedBy = new Dictionary<string, string>(StringComparer.Ordinal);
        var activeSchemaIndexes = new Dictionary<string, (string TableKey, string MigrationId)>(StringComparer.Ordinal);

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
                            if (!activeTables.TryGetValue(tableKey, out var tableState))
                            {
                                tableState = new TableSchemaState();
                                activeTables[tableKey] = tableState;
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
                                if (!tableState.Columns.Add(columnName))
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

                            foreach (AddForeignKeyOperation fk in createTable.ForeignKeys)
                            {
                                RegisterForeignKey(module, defaultSchema, migrationId, tableKey, tableState, fk, errors);
                            }

                            foreach (AddUniqueConstraintOperation uc in createTable.UniqueConstraints)
                            {
                                RegisterUniqueConstraint(module, migrationId, tableKey, tableState, uc, errors);
                            }

                            foreach (AddCheckConstraintOperation ck in createTable.CheckConstraints)
                            {
                                RegisterCheckConstraint(module, migrationId, tableKey, tableState, ck, errors);
                            }

                            break;
                        }

                    case AddColumnOperation addColumn:
                        {
                            string tableKey = QualifyTable(addColumn.Schema, defaultSchema, addColumn.Table);
                            TableSchemaState tableState = GetOrCreateImplicitTable(activeTables, tableKey);

                            string colKey = $"{tableKey}.{addColumn.Name}";
                            if (!tableState.Columns.Add(addColumn.Name))
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
                            if (activeTables.Remove(tableKey))
                            {
                                var ownedIndexes = activeSchemaIndexes
                                    .Where(kv => string.Equals(kv.Value.TableKey, tableKey, StringComparison.Ordinal))
                                    .Select(kv => kv.Key)
                                    .ToList();
                                foreach (string idxKey in ownedIndexes)
                                {
                                    activeSchemaIndexes.Remove(idxKey);
                                }
                            }

                            break;
                        }

                    case DropColumnOperation dropColumn:
                        {
                            string tableKey = QualifyTable(dropColumn.Schema, defaultSchema, dropColumn.Table);
                            if (activeTables.TryGetValue(tableKey, out var tableState))
                            {
                                tableState.Columns.Remove(dropColumn.Name);
                            }

                            break;
                        }

                    case RenameTableOperation renameTable:
                        {
                            string oldKey = QualifyTable(renameTable.Schema, defaultSchema, renameTable.Name);
                            string newKey = QualifyTable(
                                renameTable.NewSchema ?? renameTable.Schema,
                                defaultSchema,
                                renameTable.NewName ?? renameTable.Name);
                            if (!string.Equals(oldKey, newKey, StringComparison.Ordinal) &&
                                activeTables.Remove(oldKey, out var tableState))
                            {
                                activeTables[newKey] = tableState;
                                tableCreatedBy[newKey] = migrationId;
                                foreach (string col in tableState.Columns)
                                {
                                    columnCreatedBy[$"{newKey}.{col}"] = migrationId;
                                }

                                var toUpdate = activeSchemaIndexes
                                    .Where(kv => string.Equals(kv.Value.TableKey, oldKey, StringComparison.Ordinal))
                                    .ToList();
                                foreach (var (idxKey, val) in toUpdate)
                                {
                                    activeSchemaIndexes[idxKey] = (newKey, val.MigrationId);
                                }
                            }

                            break;
                        }

                    case RenameColumnOperation renameColumn:
                        {
                            string tableKey = QualifyTable(renameColumn.Schema, defaultSchema, renameColumn.Table);
                            if (activeTables.TryGetValue(tableKey, out var tableState) &&
                                tableState.Columns.Remove(renameColumn.Name))
                            {
                                tableState.Columns.Add(renameColumn.NewName);
                                columnCreatedBy[$"{tableKey}.{renameColumn.NewName}"] = migrationId;
                            }

                            break;
                        }

                    case CreateIndexOperation createIndex:
                        {
                            string tableKey = QualifyTable(createIndex.Schema, defaultSchema, createIndex.Table);
                            TableSchemaState tableState = GetOrCreateImplicitTable(activeTables, tableKey);
                            string schemaIndexKey = QualifyTable(createIndex.Schema, defaultSchema, createIndex.Name);
                            string signature = BuildIndexSignature(createIndex);

                            if (activeSchemaIndexes.TryGetValue(schemaIndexKey, out var existingIndex))
                            {
                                errors.Add(
                                    $"[{module}] Migration '{migrationId}' duplicates index '{createIndex.Name}' on '{tableKey}' " +
                                    $"(already created on '{existingIndex.TableKey}' in '{existingIndex.MigrationId}').");
                            }
                            else if (tableState.IndexBySignature.TryGetValue(signature, out var existingSig))
                            {
                                errors.Add(
                                    $"[{module}] Migration '{migrationId}' duplicates index definition '{createIndex.Name}' on '{tableKey}' " +
                                    $"(equivalent to '{existingSig.Name}' created in '{existingSig.MigrationId}').");
                            }
                            else
                            {
                                activeSchemaIndexes[schemaIndexKey] = (tableKey, migrationId);
                                tableState.IndexSignatureByName[createIndex.Name] = signature;
                                tableState.IndexBySignature[signature] = (createIndex.Name, migrationId);
                            }

                            break;
                        }

                    case DropIndexOperation dropIndex:
                        {
                            string schemaIndexKey = QualifyTable(dropIndex.Schema, defaultSchema, dropIndex.Name);
                            if (activeSchemaIndexes.Remove(schemaIndexKey, out var removedIndex) &&
                                activeTables.TryGetValue(removedIndex.TableKey, out var ownerTable) &&
                                ownerTable.IndexSignatureByName.Remove(dropIndex.Name, out string? removedSig))
                            {
                                ownerTable.IndexBySignature.Remove(removedSig);
                            }

                            if (!string.IsNullOrWhiteSpace(dropIndex.Table))
                            {
                                string tableKey = QualifyTable(dropIndex.Schema, defaultSchema, dropIndex.Table);
                                if (activeTables.TryGetValue(tableKey, out var tableState) &&
                                    tableState.IndexSignatureByName.Remove(dropIndex.Name, out string? sig))
                                {
                                    tableState.IndexBySignature.Remove(sig);
                                }
                            }

                            break;
                        }

                    case RenameIndexOperation renameIndex:
                        {
                            string oldSchemaIndexKey = QualifyTable(renameIndex.Schema, defaultSchema, renameIndex.Name);
                            string newSchemaIndexKey = QualifyTable(renameIndex.Schema, defaultSchema, renameIndex.NewName);
                            string? resolvedTableKey = !string.IsNullOrWhiteSpace(renameIndex.Table)
                                ? QualifyTable(renameIndex.Schema, defaultSchema, renameIndex.Table)
                                : null;

                            if (activeSchemaIndexes.Remove(oldSchemaIndexKey, out var oldOwner))
                            {
                                resolvedTableKey ??= oldOwner.TableKey;
                            }

                            if (activeSchemaIndexes.TryGetValue(newSchemaIndexKey, out var collision))
                            {
                                errors.Add(
                                    $"[{module}] Migration '{migrationId}' renames index '{renameIndex.Name}' to duplicate index '{renameIndex.NewName}' " +
                                    $"(already created on '{collision.TableKey}' in '{collision.MigrationId}').");
                                break;
                            }

                            if (resolvedTableKey is not null)
                            {
                                TableSchemaState tableState = GetOrCreateImplicitTable(activeTables, resolvedTableKey);
                                activeSchemaIndexes[newSchemaIndexKey] = (resolvedTableKey, migrationId);
                                if (tableState.IndexSignatureByName.Remove(renameIndex.Name, out string? sig))
                                {
                                    tableState.IndexSignatureByName[renameIndex.NewName] = sig;
                                    tableState.IndexBySignature[sig] = (renameIndex.NewName, migrationId);
                                }
                            }

                            break;
                        }

                    case AddForeignKeyOperation addForeignKey:
                        {
                            string tableKey = QualifyTable(addForeignKey.Schema, defaultSchema, addForeignKey.Table);
                            TableSchemaState tableState = GetOrCreateImplicitTable(activeTables, tableKey);
                            RegisterForeignKey(module, defaultSchema, migrationId, tableKey, tableState, addForeignKey, errors);
                            break;
                        }

                    case DropForeignKeyOperation dropForeignKey:
                        {
                            string tableKey = QualifyTable(dropForeignKey.Schema, defaultSchema, dropForeignKey.Table);
                            if (activeTables.TryGetValue(tableKey, out var tableState) &&
                                tableState.ForeignKeysByName.Remove(dropForeignKey.Name, out var removedFk))
                            {
                                tableState.ForeignKeysBySignature.Remove(removedFk.Signature);
                            }

                            break;
                        }

                    case AddUniqueConstraintOperation addUnique:
                        {
                            string tableKey = QualifyTable(addUnique.Schema, defaultSchema, addUnique.Table);
                            TableSchemaState tableState = GetOrCreateImplicitTable(activeTables, tableKey);
                            RegisterUniqueConstraint(module, migrationId, tableKey, tableState, addUnique, errors);
                            break;
                        }

                    case DropUniqueConstraintOperation dropUnique:
                        {
                            string tableKey = QualifyTable(dropUnique.Schema, defaultSchema, dropUnique.Table);
                            if (activeTables.TryGetValue(tableKey, out var tableState) &&
                                tableState.UniqueConstraintsByName.Remove(dropUnique.Name, out var removedUc))
                            {
                                tableState.UniqueConstraintsBySignature.Remove(removedUc.Signature);
                            }

                            break;
                        }

                    case AddCheckConstraintOperation addCheck:
                        {
                            string tableKey = QualifyTable(addCheck.Schema, defaultSchema, addCheck.Table);
                            TableSchemaState tableState = GetOrCreateImplicitTable(activeTables, tableKey);
                            RegisterCheckConstraint(module, migrationId, tableKey, tableState, addCheck, errors);
                            break;
                        }

                    case DropCheckConstraintOperation dropCheck:
                        {
                            string tableKey = QualifyTable(dropCheck.Schema, defaultSchema, dropCheck.Table);
                            if (activeTables.TryGetValue(tableKey, out var tableState) &&
                                tableState.CheckConstraintsByName.Remove(dropCheck.Name, out var removedCk))
                            {
                                tableState.CheckConstraintsBySignature.Remove(removedCk.Signature);
                            }

                            break;
                        }
                }
            }
        }
    }

    private static TableSchemaState GetOrCreateImplicitTable(
        Dictionary<string, TableSchemaState> activeTables,
        string tableKey)
    {
        if (!activeTables.TryGetValue(tableKey, out var tableState))
        {
            tableState = new TableSchemaState();
            activeTables[tableKey] = tableState;
        }

        return tableState;
    }

    private static void RegisterForeignKey(
        string module,
        string defaultSchema,
        string migrationId,
        string tableKey,
        TableSchemaState tableState,
        AddForeignKeyOperation fk,
        List<string> errors)
    {
        string principalKey = QualifyTable(fk.PrincipalSchema, defaultSchema, fk.PrincipalTable);
        string signature = $"({string.Join(",", fk.Columns)})->{principalKey}({string.Join(",", fk.PrincipalColumns ?? [])})";

        if (!string.IsNullOrWhiteSpace(fk.Name) &&
            tableState.ForeignKeysByName.TryGetValue(fk.Name, out var existingByName))
        {
            errors.Add(
                $"[{module}] Migration '{migrationId}' duplicates foreign key '{fk.Name}' on '{tableKey}' " +
                $"(already created in '{existingByName.MigrationId}').");
            return;
        }

        if (tableState.ForeignKeysBySignature.TryGetValue(signature, out var existingBySig))
        {
            errors.Add(
                $"[{module}] Migration '{migrationId}' duplicates foreign key '{fk.Name}' on '{tableKey}' referencing '{principalKey}' " +
                $"(equivalent to '{existingBySig.Name}' created in '{existingBySig.MigrationId}').");
            return;
        }

        if (!string.IsNullOrWhiteSpace(fk.Name))
        {
            tableState.ForeignKeysByName[fk.Name] = (signature, migrationId);
        }

        tableState.ForeignKeysBySignature[signature] = (fk.Name, migrationId);
    }

    private static void RegisterUniqueConstraint(
        string module,
        string migrationId,
        string tableKey,
        TableSchemaState tableState,
        AddUniqueConstraintOperation uc,
        List<string> errors)
    {
        string signature = string.Join(",", uc.Columns);

        if (!string.IsNullOrWhiteSpace(uc.Name) &&
            tableState.UniqueConstraintsByName.TryGetValue(uc.Name, out var existingByName))
        {
            errors.Add(
                $"[{module}] Migration '{migrationId}' duplicates unique constraint '{uc.Name}' on '{tableKey}' " +
                $"(already created in '{existingByName.MigrationId}').");
            return;
        }

        if (tableState.UniqueConstraintsBySignature.TryGetValue(signature, out var existingBySig))
        {
            errors.Add(
                $"[{module}] Migration '{migrationId}' duplicates unique constraint '{uc.Name}' on '{tableKey}' for columns ({signature}) " +
                $"(equivalent to '{existingBySig.Name}' created in '{existingBySig.MigrationId}').");
            return;
        }

        if (!string.IsNullOrWhiteSpace(uc.Name))
        {
            tableState.UniqueConstraintsByName[uc.Name] = (signature, migrationId);
        }

        tableState.UniqueConstraintsBySignature[signature] = (uc.Name, migrationId);
    }

    private static void RegisterCheckConstraint(
        string module,
        string migrationId,
        string tableKey,
        TableSchemaState tableState,
        AddCheckConstraintOperation ck,
        List<string> errors)
    {
        string signature = NormalizeSqlExpression(ck.Sql);

        if (!string.IsNullOrWhiteSpace(ck.Name) &&
            tableState.CheckConstraintsByName.TryGetValue(ck.Name, out var existingByName))
        {
            errors.Add(
                $"[{module}] Migration '{migrationId}' duplicates check constraint '{ck.Name}' on '{tableKey}' " +
                $"(already created in '{existingByName.MigrationId}').");
            return;
        }

        if (tableState.CheckConstraintsBySignature.TryGetValue(signature, out var existingBySig))
        {
            errors.Add(
                $"[{module}] Migration '{migrationId}' duplicates check constraint '{ck.Name}' on '{tableKey}' " +
                $"(equivalent SQL to '{existingBySig.Name}' created in '{existingBySig.MigrationId}').");
            return;
        }

        if (!string.IsNullOrWhiteSpace(ck.Name))
        {
            tableState.CheckConstraintsByName[ck.Name] = (signature, migrationId);
        }

        tableState.CheckConstraintsBySignature[signature] = (ck.Name, migrationId);
    }

    private static string BuildIndexSignature(CreateIndexOperation index)
    {
        string cols = string.Join(",", index.Columns);
        string desc = index.IsDescending is null ? string.Empty : string.Join(",", index.IsDescending);
        string filter = NormalizeSqlExpression(index.Filter ?? string.Empty);
        string annotations = string.Join(
            "|",
            index.GetAnnotations()
                .OrderBy(static a => a.Name, StringComparer.Ordinal)
                .Select(static a => $"{a.Name}={FormatAnnotationValue(a.Value)}"));
        return $"cols=[{cols}];unique={index.IsUnique};desc=[{desc}];filter=[{filter}];ann=[{annotations}]";
    }

    private static string FormatAnnotationValue(object? value)
    {
        if (value is null)
        {
            return "null";
        }

        if (value is string str)
        {
            return str;
        }

        if (value is IEnumerable enumerable)
        {
            var items = new List<string>();
            foreach (object? item in enumerable)
            {
                items.Add(item?.ToString() ?? "null");
            }

            return $"[{string.Join(",", items)}]";
        }

        return value.ToString() ?? string.Empty;
    }

    private static string NormalizeSqlExpression(string sql) =>
        string.Join(' ', sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private sealed class TableSchemaState
    {
        public HashSet<string> Columns { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, string> IndexSignatureByName { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, (string Name, string MigrationId)> IndexBySignature { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, (string Signature, string MigrationId)> ForeignKeysByName { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, (string Name, string MigrationId)> ForeignKeysBySignature { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, (string Signature, string MigrationId)> UniqueConstraintsByName { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, (string Name, string MigrationId)> UniqueConstraintsBySignature { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, (string Signature, string MigrationId)> CheckConstraintsByName { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, (string Name, string MigrationId)> CheckConstraintsBySignature { get; } = new(StringComparer.Ordinal);
    }
}
