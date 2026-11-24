using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// Splits monolithic EF Core <c>*ModelSnapshot.cs</c> files into a static root snapshot plus per-entity partial files
/// under <c>Migrations/Snapshots/</c>, and compacts <c>*.Designer.cs</c> files to <c>[DbContext]</c> and
/// <c>[Migration]</c> partial declarations without <c>BuildTargetModel</c> bodies.
/// </summary>
public static partial class MigrationSnapshotCompactor
{
    public const string SnapshotsSubdirectory = "Snapshots";
    public const string SnapshotFileSuffix = ".Snapshot.cs";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly HashSet<string> BoilerplateDesignerUsings = new(StringComparer.Ordinal)
    {
        "System",
        "System.Collections.Generic",
        "Microsoft.EntityFrameworkCore",
        "Microsoft.EntityFrameworkCore.Infrastructure",
        "Microsoft.EntityFrameworkCore.Migrations",
        "Microsoft.EntityFrameworkCore.Storage.ValueConversion",
        "Npgsql.EntityFrameworkCore.PostgreSQL.Metadata",
        "NpgsqlTypes",
    };

    private static readonly HashSet<string> RootExcludedUsings = new(StringComparer.Ordinal)
    {
        "System.Collections.Generic",
        "Microsoft.EntityFrameworkCore.Storage.ValueConversion",
        "NpgsqlTypes",
    };

    [GeneratedRegex(@"^using\s+([A-Za-z_][A-Za-z0-9_.]*)\s*;\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex UsingDirectiveRegex();

    [GeneratedRegex(@"^\s*namespace\s+([A-Za-z_][A-Za-z0-9_.]*)\s*(?:;|\{)", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex NamespaceRegex();

    [GeneratedRegex(@"\[DbContext\(\s*typeof\(\s*([^)]+?)\s*\)\s*\)\]", RegexOptions.CultureInvariant)]
    private static partial Regex DbContextAttributeRegex();

    [GeneratedRegex(@"\[Migration\(\s*""([^""]+)""\s*\)\]", RegexOptions.CultureInvariant)]
    private static partial Regex MigrationAttributeRegex();

    [GeneratedRegex(@"^\s*((?:(?:public|internal)\s+)?partial\s+class\s+([A-Za-z_][A-Za-z0-9_]*)(?:\s*:\s*ModelSnapshot)?)\b", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex PartialClassHeaderRegex();

    /// <summary>Returns the deterministic partial snapshot filename under <c>Migrations/Snapshots/</c> for <paramref name="entityName"/>.</summary>
    public static string GetSnapshotFileName(string entityName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityName);

        var sb = new StringBuilder(entityName.Length + SnapshotFileSuffix.Length + 10);
        bool sanitizedAny = false;
        foreach (char ch in entityName)
        {
            if (char.IsAsciiLetterOrDigit(ch) || ch is '.' or '_' or '-')
            {
                sb.Append(ch);
            }
            else
            {
                sb.Append('_');
                sanitizedAny = true;
            }
        }

        if (sanitizedAny)
        {
            sb.Append('_').Append(ShortSha256Hex(entityName));
        }

        sb.Append(SnapshotFileSuffix);
        return sb.ToString();
    }

    /// <summary>Returns a PascalCase C# nested class identifier for <paramref name="entityName"/>.</summary>
    public static string GetSnapshotClassIdentifier(string entityName, bool includeHashSuffix = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityName);

        var sb = new StringBuilder(entityName.Length + 16);
        bool capitalizeNext = true;
        foreach (char ch in entityName)
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                sb.Append(capitalizeNext ? char.ToUpperInvariant(ch) : ch);
                capitalizeNext = false;
            }
            else
            {
                capitalizeNext = true;
            }
        }

        if (sb.Length == 0 || char.IsAsciiDigit(sb[0]))
        {
            sb.Insert(0, "Entity");
        }

        if (includeHashSuffix)
        {
            sb.Append(ShortSha256Hex(entityName));
        }

        sb.Append("Snapshot");
        return sb.ToString();
    }

    /// <summary>
    /// Splits monolithic <c>*ModelSnapshot.cs</c> files and compacts <c>*.Designer.cs</c> files across all 18 modules
    /// in <see cref="PersistenceModuleCatalog.All"/>.
    /// </summary>
    public static int NormalizeAllModules(string? repoRoot = null)
    {
        repoRoot ??= PreCommitOpenApiService.FindRepoRoot();
        try
        {
            int splitSnapshots = 0;
            int compactedDesigners = 0;
            foreach (PersistenceModule module in PersistenceModuleCatalog.All)
            {
                string migrationsDir = module.GetMigrationsDirectory(repoRoot);
                if (!Directory.Exists(migrationsDir))
                {
                    continue;
                }

                var (split, compacted) = NormalizeModuleMigrations(migrationsDir);
                if (split)
                {
                    splitSnapshots++;
                }

                compactedDesigners += compacted;
            }

            if (splitSnapshots > 0 || compactedDesigners > 0)
            {
                Console.WriteLine($"✔ Normalized EF Core migrations ({splitSnapshots} snapshot(s) split, {compactedDesigners} designer file(s) compacted).");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] Failed to normalize EF Core snapshots/designers: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Splits any monolithic <c>*ModelSnapshot.cs</c> into <c>Migrations/Snapshots/</c> partials and compacts all
    /// <c>*.Designer.cs</c> files in <paramref name="migrationsDirectory"/>.
    /// </summary>
    public static (bool SnapshotUpdated, int DesignersUpdated) NormalizeModuleMigrations(string migrationsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(migrationsDirectory);
        if (!Directory.Exists(migrationsDirectory))
        {
            return (false, 0);
        }

        bool snapshotUpdated = false;
        string[] snapshotFiles = Directory.GetFiles(migrationsDirectory, "*ModelSnapshot.cs", SearchOption.TopDirectoryOnly);
        Array.Sort(snapshotFiles, StringComparer.Ordinal);
        foreach (string snapshotFile in snapshotFiles)
        {
            if (SplitModelSnapshot(snapshotFile))
            {
                snapshotUpdated = true;
            }
        }

        int designersUpdated = 0;
        string[] designerFiles = Directory.GetFiles(migrationsDirectory, "*.Designer.cs", SearchOption.TopDirectoryOnly);
        Array.Sort(designerFiles, StringComparer.Ordinal);
        foreach (string designerFile in designerFiles)
        {
            if (CompactDesignerFile(designerFile))
            {
                designersUpdated++;
            }
        }

        return (snapshotUpdated, designersUpdated);
    }

    /// <summary>
    /// Splits a monolithic <c>*ModelSnapshot.cs</c> file into a static root snapshot plus per-entity partial files in
    /// <c>Migrations/Snapshots/</c>. Returns <see langword="true"/> if any file on disk changed.
    /// </summary>
    public static bool SplitModelSnapshot(string snapshotPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotPath);
        string raw = File.ReadAllText(snapshotPath, Encoding.UTF8);
        string source = NormalizeSource(raw);
        string migrationsDir = Path.GetDirectoryName(Path.GetFullPath(snapshotPath))!;
        string snapshotsDir = Path.Combine(migrationsDir, SnapshotsSubdirectory);

        if (!source.Contains("modelBuilder.Entity(", StringComparison.Ordinal) &&
            source.Contains("interface IEntitySnapshot", StringComparison.Ordinal))
        {
            bool anyNormalized = WriteUtf8IfChanged(snapshotPath, source);
            if (Directory.Exists(snapshotsDir))
            {
                foreach (string partialPath in Directory.GetFiles(snapshotsDir, "*" + SnapshotFileSuffix, SearchOption.TopDirectoryOnly))
                {
                    string partialRaw = File.ReadAllText(partialPath, Encoding.UTF8);
                    if (WriteUtf8IfChanged(partialPath, NormalizeSource(partialRaw)))
                    {
                        anyNormalized = true;
                    }
                }
            }

            return anyNormalized;
        }

        var parsed = ParseMonolithicSnapshot(source, snapshotPath);
        bool changed = false;

        string rootContent = RenderRootSnapshot(parsed);
        if (WriteUtf8IfChanged(snapshotPath, rootContent))
        {
            changed = true;
        }

        Directory.CreateDirectory(snapshotsDir);
        var expectedFileNames = new HashSet<string>(StringComparer.Ordinal);

        // Detect any PascalCase class identifier collisions across entities in the same module.
        var identifierCounts = parsed.Entities.Keys
            .GroupBy(e => GetSnapshotClassIdentifier(e, includeHashSuffix: false), StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var (entityName, blocks) in parsed.Entities)
        {
            string fileName = GetSnapshotFileName(entityName);
            expectedFileNames.Add(fileName);

            string baseId = GetSnapshotClassIdentifier(entityName, includeHashSuffix: false);
            string classId = GetSnapshotClassIdentifier(entityName, includeHashSuffix: identifierCounts.Contains(baseId));

            string partialContent = RenderEntityPartial(parsed, entityName, classId, blocks);
            string partialPath = Path.Combine(snapshotsDir, fileName);
            if (WriteUtf8IfChanged(partialPath, partialContent))
            {
                changed = true;
            }
        }

        foreach (string existingFile in Directory.GetFiles(snapshotsDir, "*", SearchOption.TopDirectoryOnly))
        {
            if (!expectedFileNames.Contains(Path.GetFileName(existingFile)))
            {
                File.Delete(existingFile);
                changed = true;
            }
        }

        if (parsed.Entities.Count == 0 &&
            Directory.Exists(snapshotsDir) &&
            Directory.GetFileSystemEntries(snapshotsDir).Length == 0)
        {
            Directory.Delete(snapshotsDir);
        }

        return changed;
    }

    /// <summary>
    /// Rewrites a <c>*.Designer.cs</c> migration metadata file without its <c>BuildTargetModel</c> body, preserving
    /// <c>[DbContext]</c> and <c>[Migration]</c> attributes. Returns <see langword="true"/> if the file changed.
    /// </summary>
    public static bool CompactDesignerFile(string designerPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(designerPath);
        string raw = File.ReadAllText(designerPath, Encoding.UTF8);
        string source = NormalizeSource(raw);

        var usings = ExtractUsings(source);
        string ns = ExtractNamespace(source, designerPath);
        string dbContextType = ExtractRequiredGroup(DbContextAttributeRegex(), source, $"[DbContext(typeof(...))] in '{designerPath}'");
        string migrationId = ExtractRequiredGroup(MigrationAttributeRegex(), source, $"[Migration(\"...\")] in '{designerPath}'");

        var classMatch = PartialClassHeaderRegex().Match(source);
        if (!classMatch.Success)
        {
            throw new InvalidOperationException($"Could not locate partial class declaration in '{designerPath}'.");
        }

        string classDecl = NormalizeWhitespace(classMatch.Groups[1].Value);

        var keptUsings = new SortedSet<string>(StringComparer.Ordinal)
        {
            "Microsoft.EntityFrameworkCore.Infrastructure",
            "Microsoft.EntityFrameworkCore.Migrations",
        };

        foreach (string u in usings)
        {
            if (!BoilerplateDesignerUsings.Contains(u))
            {
                keptUsings.Add(u);
            }
        }

        var sb = new StringBuilder(512);
        sb.Append("// <auto-generated />\n");
        AppendGroupedUsings(sb, keptUsings);
        sb.Append("\n#nullable disable\n\n");
        sb.Append("namespace ").Append(ns).Append(";\n\n");
        sb.Append("[DbContext(typeof(").Append(dbContextType).Append("))]\n");
        sb.Append("[Migration(\"").Append(migrationId).Append("\")]\n");
        sb.Append(classDecl).Append('\n');
        sb.Append("{\n}\n");

        return WriteUtf8IfChanged(designerPath, sb.ToString());
    }

    private static ParsedSnapshot ParseMonolithicSnapshot(string source, string snapshotPath)
    {
        var usings = ExtractUsings(source);
        string ns = ExtractNamespace(source, snapshotPath);
        string dbContextType = ExtractRequiredGroup(DbContextAttributeRegex(), source, $"[DbContext(typeof(...))] in '{snapshotPath}'");

        var classMatch = PartialClassHeaderRegex().Match(source);
        if (!classMatch.Success)
        {
            throw new InvalidOperationException($"Could not locate snapshot partial class declaration in '{snapshotPath}'.");
        }

        string snapshotClassName = classMatch.Groups[2].Value;
        string rawHeader = NormalizeWhitespace(classMatch.Groups[1].Value);
        string rootClassHeader = rawHeader.Contains(": ModelSnapshot", StringComparison.Ordinal)
            ? rawHeader
            : $"{rawHeader} : ModelSnapshot";
        string partialClassHeader = rawHeader.Replace(" : ModelSnapshot", string.Empty, StringComparison.Ordinal).Trim();

        int buildModelIndex = source.IndexOf("protected override void BuildModel(ModelBuilder modelBuilder)", StringComparison.Ordinal);
        if (buildModelIndex < 0)
        {
            throw new InvalidOperationException($"Could not locate BuildModel(ModelBuilder modelBuilder) in '{snapshotPath}'.");
        }

        int openBrace = source.IndexOf('{', buildModelIndex);
        if (openBrace < 0)
        {
            throw new InvalidOperationException($"Could not locate opening brace of BuildModel in '{snapshotPath}'.");
        }

        int closeBrace = FindMatchingDelimiter(source, openBrace, '{', '}');
        string body = source[(openBrace + 1)..closeBrace];

        body = RemovePragmaLines(body);

        int firstEntityCall = FindNextEntityStatementStart(body, 0);
        string preambleRaw = firstEntityCall >= 0 ? body[..firstEntityCall] : body;
        string preamble = ReindentPreamble(preambleRaw);

        var entities = new SortedDictionary<string, EntitySnapshotBlocks>(StringComparer.Ordinal);
        if (firstEntityCall >= 0)
        {
            ParseEntityStatements(body, firstEntityCall, snapshotPath, entities);
        }

        return new ParsedSnapshot(
            usings,
            ns,
            dbContextType,
            snapshotClassName,
            rootClassHeader,
            partialClassHeader,
            preamble,
            entities);
    }

    private static void ParseEntityStatements(
        string body,
        int startIndex,
        string snapshotPath,
        SortedDictionary<string, EntitySnapshotBlocks> entities)
    {
        const string marker = "modelBuilder.Entity(";
        int cursor = startIndex;

        while (cursor < body.Length)
        {
            int lineStart = cursor;
            while (cursor < body.Length && char.IsWhiteSpace(body[cursor]))
            {
                if (body[cursor] == '\n')
                {
                    lineStart = cursor + 1;
                }

                cursor++;
            }

            if (cursor >= body.Length)
            {
                break;
            }

            if (!body.AsSpan(cursor).StartsWith(marker, StringComparison.Ordinal))
            {
                string snippet = body[cursor..Math.Min(body.Length, cursor + 80)].Replace('\n', ' ');
                throw new InvalidOperationException(
                    $"Unexpected statement inside BuildModel in '{snapshotPath}' after entity blocks began: '{snippet}'.");
            }

            int openParen = cursor + "modelBuilder.Entity".Length;
            int closeParen = FindMatchingDelimiter(body, openParen, '(', ')');
            int semicolon = closeParen + 1;
            while (semicolon < body.Length && char.IsWhiteSpace(body[semicolon]))
            {
                semicolon++;
            }

            if (semicolon >= body.Length || body[semicolon] != ';')
            {
                throw new InvalidOperationException($"Expected ';' after modelBuilder.Entity(...) in '{snapshotPath}'.");
            }

            string statement = body[lineStart..(semicolon + 1)].TrimEnd();
            if (!statement.StartsWith("            modelBuilder.Entity(", StringComparison.Ordinal))
            {
                statement = "            " + statement.TrimStart();
            }

            string entityName = ExtractFirstStringLiteral(body, openParen + 1, closeParen, snapshotPath);
            EntityPassKind pass = ClassifyEntityStatement(statement, snapshotPath);

            if (!entities.TryGetValue(entityName, out var bucket))
            {
                bucket = new EntitySnapshotBlocks();
                entities[entityName] = bucket;
            }

            switch (pass)
            {
                case EntityPassKind.Table:
                    bucket.TableBlocks.Add(statement);
                    break;
                case EntityPassKind.ForeignKeys:
                    bucket.ForeignKeyBlocks.Add(statement);
                    break;
                case EntityPassKind.Navigations:
                    bucket.NavigationBlocks.Add(statement);
                    break;
            }

            cursor = semicolon + 1;
        }
    }

    private static EntityPassKind ClassifyEntityStatement(string statement, string snapshotPath)
    {
        int arrowIndex = statement.IndexOf("=>", StringComparison.Ordinal);
        if (arrowIndex < 0)
        {
            throw new InvalidOperationException($"Expected lambda '=>' in modelBuilder.Entity statement in '{snapshotPath}'.");
        }

        int lambdaOpenBrace = statement.IndexOf('{', arrowIndex);
        if (lambdaOpenBrace < 0)
        {
            throw new InvalidOperationException($"Expected lambda '{{' in modelBuilder.Entity statement in '{snapshotPath}'.");
        }

        int lambdaCloseBrace = FindMatchingDelimiter(statement, lambdaOpenBrace, '{', '}');

        // Collect only outer lambda text (brace depth 0 inside lambdaOpenBrace..lambdaCloseBrace) so nested b1 => { ... } calls are ignored.
        var outerText = new StringBuilder();
        int i = lambdaOpenBrace + 1;
        int braceDepth = 0;
        while (i < lambdaCloseBrace)
        {
            int skipped = TrySkipLiteralOrComment(statement, i);
            if (skipped > i)
            {
                i = skipped;
                continue;
            }

            char ch = statement[i];
            if (ch == '{')
            {
                braceDepth++;
            }
            else if (ch == '}')
            {
                braceDepth--;
            }
            else if (braceDepth == 0)
            {
                outerText.Append(ch);
            }

            i++;
        }

        string outer = outerText.ToString();
        if (outer.Contains("b.HasOne(", StringComparison.Ordinal) ||
            outer.Contains("b.OwnsOne(", StringComparison.Ordinal) ||
            outer.Contains("b.OwnsMany(", StringComparison.Ordinal))
        {
            return EntityPassKind.ForeignKeys;
        }

        if (outer.Contains("b.Navigation(", StringComparison.Ordinal) &&
            !outer.Contains("b.Property", StringComparison.Ordinal) &&
            !outer.Contains("b.ToTable(", StringComparison.Ordinal) &&
            !outer.Contains("b.HasKey(", StringComparison.Ordinal) &&
            !outer.Contains("b.ComplexProperty", StringComparison.Ordinal) &&
            !outer.Contains("b.PrimitiveCollection", StringComparison.Ordinal))
        {
            return EntityPassKind.Navigations;
        }

        return EntityPassKind.Table;
    }

    private static string RenderRootSnapshot(ParsedSnapshot parsed)
    {
        var rootUsings = new SortedSet<string>(StringComparer.Ordinal)
        {
            "System",
            "System.Linq",
            "System.Reflection",
            "Microsoft.EntityFrameworkCore",
            "Microsoft.EntityFrameworkCore.Infrastructure",
        };

        if (parsed.Preamble.Contains("Npgsql", StringComparison.Ordinal))
        {
            rootUsings.Add("Npgsql.EntityFrameworkCore.PostgreSQL.Metadata");
        }

        foreach (string u in parsed.Usings)
        {
            if (!RootExcludedUsings.Contains(u) && !BoilerplateDesignerUsings.Contains(u))
            {
                rootUsings.Add(u);
            }
        }

        var sb = new StringBuilder(2048);
        sb.Append("// <auto-generated />\n");
        AppendGroupedUsings(sb, rootUsings);
        sb.Append("\n#nullable disable\n\n");
        sb.Append("namespace ").Append(parsed.Namespace).Append(";\n\n");
        sb.Append("[DbContext(typeof(").Append(parsed.DbContextType).Append("))]\n");
        sb.Append(parsed.RootClassHeader).Append('\n');
        sb.Append("{\n");
        sb.Append("    private interface IEntitySnapshot\n");
        sb.Append("    {\n");
        sb.Append("        string EntityName { get; }\n\n");
        sb.Append("        void ConfigureTable(ModelBuilder modelBuilder);\n\n");
        sb.Append("        void ConfigureForeignKeys(ModelBuilder modelBuilder);\n\n");
        sb.Append("        void ConfigureNavigations(ModelBuilder modelBuilder);\n");
        sb.Append("    }\n\n");
        sb.Append("    protected override void BuildModel(ModelBuilder modelBuilder)\n");
        sb.Append("    {\n");
        sb.Append("#pragma warning disable 612, 618\n");
        if (!string.IsNullOrWhiteSpace(parsed.Preamble))
        {
            sb.Append(parsed.Preamble).Append("\n\n");
        }

        sb.Append("        var snapshots = typeof(").Append(parsed.SnapshotClassName).Append(")\n");
        sb.Append("            .GetNestedTypes(BindingFlags.NonPublic)\n");
        sb.Append("            .Where(static type => !type.IsInterface && !type.IsAbstract && typeof(IEntitySnapshot).IsAssignableFrom(type))\n");
        sb.Append("            .Select(static type => (IEntitySnapshot)Activator.CreateInstance(type, nonPublic: true)!)\n");
        sb.Append("            .OrderBy(static snapshot => snapshot.EntityName, StringComparer.Ordinal)\n");
        sb.Append("            .ToArray();\n\n");
        sb.Append("        foreach (var snapshot in snapshots)\n");
        sb.Append("        {\n");
        sb.Append("            snapshot.ConfigureTable(modelBuilder);\n");
        sb.Append("        }\n\n");
        sb.Append("        foreach (var snapshot in snapshots)\n");
        sb.Append("        {\n");
        sb.Append("            snapshot.ConfigureForeignKeys(modelBuilder);\n");
        sb.Append("        }\n\n");
        sb.Append("        foreach (var snapshot in snapshots)\n");
        sb.Append("        {\n");
        sb.Append("            snapshot.ConfigureNavigations(modelBuilder);\n");
        sb.Append("        }\n");
        sb.Append("#pragma warning restore 612, 618\n");
        sb.Append("    }\n");
        sb.Append("}\n");
        return sb.ToString();
    }

    private static string RenderEntityPartial(
        ParsedSnapshot parsed,
        string entityName,
        string classIdentifier,
        EntitySnapshotBlocks blocks)
    {
        string combinedBlocks = string.Join(
            "\n",
            blocks.TableBlocks.Concat(blocks.ForeignKeyBlocks).Concat(blocks.NavigationBlocks));

        var entityUsings = new SortedSet<string>(StringComparer.Ordinal)
        {
            "System",
            "Microsoft.EntityFrameworkCore",
        };

        if (combinedBlocks.Contains("Dictionary<", StringComparison.Ordinal) ||
            combinedBlocks.Contains("List<", StringComparison.Ordinal) ||
            combinedBlocks.Contains("ICollection<", StringComparison.Ordinal) ||
            combinedBlocks.Contains("IEnumerable<", StringComparison.Ordinal))
        {
            entityUsings.Add("System.Collections.Generic");
        }

        if (combinedBlocks.Contains("NpgsqlPropertyBuilderExtensions", StringComparison.Ordinal) ||
            combinedBlocks.Contains("NpgsqlIndexBuilderExtensions", StringComparison.Ordinal) ||
            combinedBlocks.Contains("NpgsqlEntityTypeBuilderExtensions", StringComparison.Ordinal) ||
            combinedBlocks.Contains("NpgsqlValueGenerationStrategy", StringComparison.Ordinal))
        {
            entityUsings.Add("Npgsql.EntityFrameworkCore.PostgreSQL.Metadata");
        }

        if (parsed.Usings.Contains("NpgsqlTypes", StringComparer.Ordinal) &&
            ContainsNpgsqlTypesReference(combinedBlocks))
        {
            entityUsings.Add("NpgsqlTypes");
        }

        foreach (string u in parsed.Usings)
        {
            if (!BoilerplateDesignerUsings.Contains(u) &&
                !IsDbContextNamespaceUsing(u, parsed.Namespace, parsed.DbContextType))
            {
                entityUsings.Add(u);
            }
        }

        var sb = new StringBuilder(combinedBlocks.Length + 768);
        sb.Append("// <auto-generated />\n");
        AppendGroupedUsings(sb, entityUsings);
        sb.Append("\n#nullable disable\n\n");
        sb.Append("namespace ").Append(parsed.Namespace).Append(";\n\n");
        sb.Append(parsed.PartialClassHeader).Append('\n');
        sb.Append("{\n");
        sb.Append("    private sealed class ").Append(classIdentifier).Append(" : IEntitySnapshot\n");
        sb.Append("    {\n");
        sb.Append("        public string EntityName => \"").Append(EscapeCSharpString(entityName)).Append("\";\n\n");

        AppendPassMethod(sb, "ConfigureTable", blocks.TableBlocks);
        sb.Append('\n');
        AppendPassMethod(sb, "ConfigureForeignKeys", blocks.ForeignKeyBlocks);
        sb.Append('\n');
        AppendPassMethod(sb, "ConfigureNavigations", blocks.NavigationBlocks);

        sb.Append("    }\n");
        sb.Append("}\n");
        return sb.ToString();
    }

    private static void AppendPassMethod(StringBuilder sb, string methodName, List<string> statements)
    {
        sb.Append("        public void ").Append(methodName).Append("(ModelBuilder modelBuilder)\n");
        sb.Append("        {\n");
        if (statements.Count > 0)
        {
            sb.Append("#pragma warning disable 612, 618\n");
            for (int i = 0; i < statements.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append('\n');
                }

                sb.Append(statements[i]).Append('\n');
            }

            sb.Append("#pragma warning restore 612, 618\n");
        }

        sb.Append("        }\n");
    }

    private static bool ContainsNpgsqlTypesReference(string code) =>
        code.Contains("NpgsqlTsVector", StringComparison.Ordinal) ||
        code.Contains("NpgsqlRange", StringComparison.Ordinal) ||
        code.Contains("NpgsqlPoint", StringComparison.Ordinal) ||
        code.Contains("NpgsqlLine", StringComparison.Ordinal) ||
        code.Contains("NpgsqlLSeg", StringComparison.Ordinal) ||
        code.Contains("NpgsqlBox", StringComparison.Ordinal) ||
        code.Contains("NpgsqlPath", StringComparison.Ordinal) ||
        code.Contains("NpgsqlPolygon", StringComparison.Ordinal) ||
        code.Contains("NpgsqlCircle", StringComparison.Ordinal) ||
        code.Contains("NpgsqlInet", StringComparison.Ordinal) ||
        code.Contains("NpgsqlCidr", StringComparison.Ordinal) ||
        code.Contains("NpgsqlTid", StringComparison.Ordinal) ||
        code.Contains("NpgsqlLogSequenceNumber", StringComparison.Ordinal);

    private static bool IsDbContextNamespaceUsing(string usingNs, string migrationsNs, string dbContextType)
    {
        if (migrationsNs.StartsWith(usingNs + ".", StringComparison.Ordinal))
        {
            return true;
        }

        return dbContextType.StartsWith(usingNs + ".", StringComparison.Ordinal);
    }

    private static void AppendGroupedUsings(StringBuilder sb, IEnumerable<string> namespaces)
    {
        var systemUsings = namespaces
            .Where(static u => string.Equals(u, "System", StringComparison.Ordinal) || u.StartsWith("System.", StringComparison.Ordinal))
            .OrderBy(static u => u, StringComparer.Ordinal)
            .ToList();
        var otherUsings = namespaces
            .Where(static u => !string.Equals(u, "System", StringComparison.Ordinal) && !u.StartsWith("System.", StringComparison.Ordinal))
            .OrderBy(static u => u, StringComparer.Ordinal)
            .ToList();

        foreach (string u in systemUsings)
        {
            sb.Append("using ").Append(u).Append(";\n");
        }

        if (systemUsings.Count > 0 && otherUsings.Count > 0)
        {
            sb.Append('\n');
        }

        foreach (string u in otherUsings)
        {
            sb.Append("using ").Append(u).Append(";\n");
        }
    }

    private static List<string> ExtractUsings(string source) =>
        UsingDirectiveRegex()
            .Matches(source)
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static string ExtractNamespace(string source, string filePath) =>
        ExtractRequiredGroup(NamespaceRegex(), source, $"namespace declaration in '{filePath}'");

    private static string ExtractRequiredGroup(Regex regex, string source, string description)
    {
        var match = regex.Match(source);
        if (!match.Success)
        {
            throw new InvalidOperationException($"Could not locate {description}.");
        }

        return match.Groups[1].Value.Trim();
    }

    private static string RemovePragmaLines(string body)
    {
        var lines = body.Split('\n');
        var kept = new List<string>(lines.Length);
        foreach (string line in lines)
        {
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith("#pragma warning disable 612, 618", StringComparison.Ordinal) ||
                trimmed.StartsWith("#pragma warning restore 612, 618", StringComparison.Ordinal))
            {
                continue;
            }

            kept.Add(line);
        }

        return string.Join("\n", kept);
    }

    private static int FindNextEntityStatementStart(string body, int fromIndex)
    {
        int callIdx = body.IndexOf("modelBuilder.Entity(", fromIndex, StringComparison.Ordinal);
        if (callIdx < 0)
        {
            return -1;
        }

        int lineStart = callIdx;
        while (lineStart > 0 && body[lineStart - 1] is ' ' or '\t')
        {
            lineStart--;
        }

        return lineStart;
    }

    private static string ReindentPreamble(string preambleRaw)
    {
        var lines = preambleRaw.Split('\n');
        int start = 0;
        while (start < lines.Length && string.IsNullOrWhiteSpace(lines[start]))
        {
            start++;
        }

        int end = lines.Length - 1;
        while (end >= start && string.IsNullOrWhiteSpace(lines[end]))
        {
            end--;
        }

        if (start > end)
        {
            return string.Empty;
        }

        var reindented = new List<string>(end - start + 1);
        for (int i = start; i <= end; i++)
        {
            string line = lines[i].TrimEnd();
            if (line.Length == 0)
            {
                reindented.Add(string.Empty);
            }
            else if (line.StartsWith("            ", StringComparison.Ordinal))
            {
                reindented.Add(line[4..]);
            }
            else
            {
                reindented.Add(line);
            }
        }

        return string.Join("\n", reindented);
    }

    private static int FindMatchingDelimiter(string text, int openIndex, char openChar, char closeChar)
    {
        int depth = 0;
        int i = openIndex;
        while (i < text.Length)
        {
            int skipped = TrySkipLiteralOrComment(text, i);
            if (skipped > i)
            {
                i = skipped;
                continue;
            }

            char ch = text[i];
            if (ch == openChar)
            {
                depth++;
            }
            else if (ch == closeChar)
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }

            i++;
        }

        throw new InvalidOperationException($"Unbalanced '{openChar}'..'{closeChar}' starting at offset {openIndex}.");
    }

    private static int TrySkipLiteralOrComment(string text, int index)
    {
        if (index + 1 < text.Length && text[index] == '/' && text[index + 1] == '/')
        {
            int nl = text.IndexOf('\n', index + 2);
            return nl < 0 ? text.Length : nl + 1;
        }

        if (index + 1 < text.Length && text[index] == '/' && text[index + 1] == '*')
        {
            int endComment = text.IndexOf("*/", index + 2, StringComparison.Ordinal);
            return endComment < 0 ? text.Length : endComment + 2;
        }

        if (index + 2 < text.Length && text[index] == '"' && text[index + 1] == '"' && text[index + 2] == '"')
        {
            int quoteCount = 3;
            while (index + quoteCount < text.Length && text[index + quoteCount] == '"')
            {
                quoteCount++;
            }

            string closing = new('"', quoteCount);
            int endRaw = text.IndexOf(closing, index + quoteCount, StringComparison.Ordinal);
            return endRaw < 0 ? text.Length : endRaw + quoteCount;
        }

        if (index + 1 < text.Length && text[index] == '@' && text[index + 1] == '"')
        {
            int i = index + 2;
            while (i < text.Length)
            {
                if (text[i] == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        i += 2;
                        continue;
                    }

                    return i + 1;
                }

                i++;
            }

            return text.Length;
        }

        if (text[index] == '"')
        {
            int i = index + 1;
            while (i < text.Length)
            {
                if (text[i] == '\\')
                {
                    i += 2;
                    continue;
                }

                if (text[i] == '"')
                {
                    return i + 1;
                }

                i++;
            }

            return text.Length;
        }

        if (text[index] == '\'')
        {
            int i = index + 1;
            while (i < text.Length)
            {
                if (text[i] == '\\')
                {
                    i += 2;
                    continue;
                }

                if (text[i] == '\'')
                {
                    return i + 1;
                }

                i++;
            }

            return text.Length;
        }

        return index;
    }

    private static string ExtractFirstStringLiteral(string text, int startIndex, int limitIndex, string filePath)
    {
        int i = startIndex;
        while (i < limitIndex && char.IsWhiteSpace(text[i]))
        {
            i++;
        }

        if (i >= limitIndex || text[i] != '"')
        {
            throw new InvalidOperationException($"Expected string literal entity name in modelBuilder.Entity(...) in '{filePath}'.");
        }

        var sb = new StringBuilder();
        i++;
        while (i < limitIndex)
        {
            char ch = text[i];
            if (ch == '\\' && i + 1 < limitIndex)
            {
                char next = text[i + 1];
                sb.Append(next switch
                {
                    '\\' => '\\',
                    '"' => '"',
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    _ => next,
                });
                i += 2;
                continue;
            }

            if (ch == '"')
            {
                return sb.ToString();
            }

            sb.Append(ch);
            i++;
        }

        throw new InvalidOperationException($"Unterminated entity name string literal in '{filePath}'.");
    }

    private static string EscapeCSharpString(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static string NormalizeWhitespace(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string NormalizeSource(string raw)
    {
        string trimmedBom = raw.Length > 0 && raw[0] == '\uFEFF' ? raw[1..] : raw;
        return trimmedBom.ReplaceLineEndings("\n");
    }

    private static bool WriteUtf8IfChanged(string path, string content)
    {
        byte[] desired = Utf8NoBom.GetBytes(content);
        if (File.Exists(path))
        {
            byte[] existing = File.ReadAllBytes(path);
            if (existing.AsSpan().SequenceEqual(desired))
            {
                return false;
            }
        }

        File.WriteAllBytes(path, desired);
        return true;
    }

    private static string ShortSha256Hex(string input)
    {
        byte[] hash = SHA256.HashData(Utf8NoBom.GetBytes(input));
        return Convert.ToHexString(hash.AsSpan(0, 4)).ToLower(CultureInfo.InvariantCulture);
    }

    private enum EntityPassKind
    {
        Table,
        ForeignKeys,
        Navigations,
    }

    private sealed class EntitySnapshotBlocks
    {
        public List<string> TableBlocks { get; } = [];

        public List<string> ForeignKeyBlocks { get; } = [];

        public List<string> NavigationBlocks { get; } = [];
    }

    private sealed record ParsedSnapshot(
        List<string> Usings,
        string Namespace,
        string DbContextType,
        string SnapshotClassName,
        string RootClassHeader,
        string PartialClassHeader,
        string Preamble,
        SortedDictionary<string, EntitySnapshotBlocks> Entities);
}
