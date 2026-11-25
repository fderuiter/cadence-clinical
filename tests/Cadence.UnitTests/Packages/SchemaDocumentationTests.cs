using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Cadence.Database;
using Cadence.SchemaDocExporter;
using Cadence.ToolingCore.Services;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Xunit;

namespace Cadence.UnitTests.Packages;

public sealed class SchemaDocumentationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "schema-doc-" + Guid.NewGuid().ToString("N"));
    private readonly string _rendered;
    private readonly string _committed;

    public SchemaDocumentationTests()
    {
        _rendered = Directory.CreateDirectory(Path.Combine(_root, "rendered")).FullName;
        _committed = Directory.CreateDirectory(Path.Combine(_root, "committed")).FullName;
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Render_DescribesTablesColumnsKeysIndexesAndChecks_InSortedSectionsWithoutCountSummaryTable()
    {
        using var context = new LibraryContext();

        string markdown = SchemaDocumentation.Render(context, "`library` database schema", "generate");

        SchemaDocumentation.DefaultSchemaOf(context).Should().Be("library");
        markdown.Should().Contain("Unqualified tables live in the `library` schema.");
        markdown.Should().NotContain("| Table | Entity | Columns |", "top-of-file column-count summaries cause parallel branch merge conflicts");
        markdown.Should().NotContain("\n\n\n");

        int auditIndex = markdown.IndexOf("## audit.loans", StringComparison.Ordinal);
        int booksIndex = markdown.IndexOf("## library.books", StringComparison.Ordinal);
        auditIndex.Should().BeGreaterThan(0, "an explicit schema wins over the search path");
        booksIndex.Should().BeGreaterThan(auditIndex, "table sections stay sorted by schema then table name");

        markdown.Should().Contain("| `Id` | `uuid` | no |  | PK |");
        markdown.Should().Contain("| `Title` | `character varying(200)` | no |  |  |");
        markdown.Should().Contain("| `Subtitle` | `text` | yes |  |  |");
        markdown.Should().Contain("| `Copies` | `integer` | no | `1` |  |");
        markdown.Should().Contain("- `FK_loans_books_BookId`: (`BookId`) references `library.books` (`Id`), on delete cascade");
        markdown.Should().Contain("- `IX_books_Title`: unique (`Title`)");
        markdown.Should().Contain("- `CK_books_copies`: `\"Copies\" >= 0`");
    }

    [Fact]
    public void Index_RendersStaticModuleAndDbContextLinks_WithoutTablesCountColumn()
    {
        string readme = Cadence.SchemaDocExporter.Program.Index(
        [
            new Cadence.SchemaDocExporter.Program.ModuleDocument("ctms", "CtmsDbContext", "# ctms"),
            new Cadence.SchemaDocExporter.Program.ModuleDocument("kernel", "KernelDbContext", "# kernel"),
        ]);

        readme.Should().Contain("| Module | DbContext |\n| --- | --- |\n");
        readme.Should().Contain("| [ctms](ctms.md) | `CtmsDbContext` |\n");
        readme.Should().Contain("| [kernel](kernel.md) | `KernelDbContext` |\n");
        readme.Should().NotContain("Tables");
    }

    [Fact]
    public void Render_IsDeterministic()
    {
        using var first = new LibraryContext();
        using var second = new LibraryContext();

        SchemaDocumentation.Render(first, "t", "g").Should().Be(SchemaDocumentation.Render(second, "t", "g"));
    }

    [Fact]
    public void FindDrift_ReportsMissingChangedAndStaleDocuments_IgnoringLineEndings()
    {
        File.WriteAllText(Path.Combine(_rendered, "same.md"), "a\nb\n");
        File.WriteAllText(Path.Combine(_committed, "same.md"), "a\r\nb\r\n");
        File.WriteAllText(Path.Combine(_rendered, "changed.md"), "new\n");
        File.WriteAllText(Path.Combine(_committed, "changed.md"), "old\n");
        File.WriteAllText(Path.Combine(_rendered, "added.md"), "x\n");
        File.WriteAllText(Path.Combine(_committed, "removed.md"), "x\n");
        File.WriteAllText(Path.Combine(_committed, "index.html"), "<html/>");

        SchemaDocGeneratorService.FindDrift(_rendered, _committed).Should().Equal(
            "added.md (missing)",
            "changed.md (out of date)",
            "index.html (no module defines it)",
            "removed.md (no module defines it)");
    }

    [Fact]
    public void SchemaMigrationValidator_AcceptsAllEighteenRepositoryPersistenceModules()
    {
        string repoRoot = SecretsBaselineCleanerService.FindRepoRoot();

        int exitCode = Cadence.SchemaDocExporter.Program.Main(["--verify-only"]);

        exitCode.Should().Be(0, $"all {PersistenceModuleCatalog.All.Count} persistence modules under {repoRoot} must pass migration validation");
    }

    [Fact]
    public void SchemaMigrationValidator_FailsClosed_OnPendingModelChanges()
    {
        using var context = new UnmigratedLibraryContext();

        var errors = SchemaMigrationValidator.Validate(
            context,
            module: "library",
            migrationsDirectory: null,
            defaultSchema: "library",
            migrationTypes: []);

        errors.Should().ContainSingle()
            .Which.Should().Contain("has pending model changes without a migration");
    }

    [Fact]
    public void SchemaMigrationValidator_FailsClosed_OnMissingAttributesAndMissingDesignerCompanion()
    {
        string migrationsDir = Directory.CreateDirectory(Path.Combine(_root, "migrations-missing-designer")).FullName;
        File.WriteAllText(Path.Combine(migrationsDir, "20261101000000_ValidInitial.cs"), "public partial class ValidInitial : Migration {}");
        // Intentionally omit 20261101000000_ValidInitial.Designer.cs and add a non-migration static helper that must be ignored.
        File.WriteAllText(Path.Combine(migrationsDir, "SqlHelper.cs"), "public static class SqlHelper {}");

        using var context = new LibraryContext();
        var errors = SchemaMigrationValidator.Validate(
            context,
            module: "library",
            migrationsDirectory: migrationsDir,
            defaultSchema: "library",
            migrationTypes: [typeof(ValidInitialMigration), typeof(MissingAttributesMigration)],
            checkPendingModelChanges: false);

        string joined = string.Join("\n", errors);
        joined.Should().Contain("20261101000000_ValidInitial.Designer.cs");
        joined.Should().Contain("MissingAttributesMigration' is missing [DbContext");
        joined.Should().Contain("MissingAttributesMigration' is missing [Migration");
        joined.Should().NotContain("SqlHelper");
    }

    [Fact]
    public void SchemaMigrationValidator_FailsClosed_OnDuplicateCreateTableAndDuplicateAddColumn()
    {
        using var context = new LibraryContext();

        var errors = SchemaMigrationValidator.Validate(
            context,
            module: "library",
            migrationsDirectory: null,
            defaultSchema: "library",
            migrationTypes:
            [
                typeof(ValidInitialMigration),
                typeof(DuplicateTableMigration),
                typeof(DuplicateColumnMigration),
            ],
            checkPendingModelChanges: false);

        string joined = string.Join("\n", errors);
        joined.Should().Contain("duplicates CreateTable for 'library.books' (already created in '20261101000000_ValidInitial')");
        joined.Should().Contain("duplicates AddColumn for 'library.books.Title' (already created in '20261101000000_ValidInitial')");
    }

    [Fact]
    public void MigrationToolingService_RoutesCommandsToCatalogPaths_AndWritesScriptsToTestResults()
    {
        string repoRoot = SecretsBaselineCleanerService.FindRepoRoot();
        var invocations = new List<(string WorkDir, string Executable, IReadOnlyList<string> Args)>();
        int FakeRunner(string workDir, string exe, IReadOnlyList<string> args)
        {
            invocations.Add((workDir, exe, args));
            int outIndex = args.ToList().IndexOf("--output");
            if (outIndex >= 0 && outIndex + 1 < args.Count)
            {
                File.WriteAllText(args[outIndex + 1], "-- idempotent sql\n");
            }

            return 0;
        }

        string customOut = Path.Combine(_root, "TestResults", "migrations");
        MigrationToolingService.Add("executionmodule", "AddExecutionIndex", repoRoot, FakeRunner).Should().Be(0);
        MigrationToolingService.Script("kernel", customOut, repoRoot, FakeRunner).Should().Be(0);
        MigrationToolingService.Verify("intelligence", repoRoot, FakeRunner).Should().Be(0);
        MigrationToolingService.Add("all", "InvalidForAll", repoRoot, FakeRunner).Should().Be(1);
        MigrationToolingService.Script("unknown-module", customOut, repoRoot, FakeRunner).Should().Be(1);

        invocations.Should().HaveCount(3);
        invocations[0].Args.Should().ContainInOrder(
            "ef",
            "migrations",
            "add",
            "AddExecutionIndex",
            "--project",
            Path.Combine(repoRoot, "apps", "execution", "module", "Cadence.Modules.Execution.csproj"),
            "--output-dir",
            "Infrastructure/Migrations");

        string expectedKernelSql = Path.Combine(customOut, "kernel.sql");
        invocations[1].Args.Should().ContainInOrder(
            "ef",
            "migrations",
            "script",
            "--idempotent",
            "--project",
            Path.Combine(repoRoot, "packages", "kernel", "src", "Cadence.Kernel.Persistence", "Cadence.Kernel.Persistence.csproj"),
            "--output",
            expectedKernelSql);
        File.Exists(expectedKernelSql).Should().BeTrue();

        invocations[2].Args.Should().ContainInOrder(
            "run",
            "--project",
            "packages/tooling_core/src/Cadence.SchemaDocExporter/Cadence.SchemaDocExporter.csproj",
            "--",
            "--verify-only",
            "--module",
            "intelligence");
    }

    [Fact]
    public void MigrationToolingService_Apply_RoutesSingleModuleAllAndExplicitConnectionToDatabaseMigrator()
    {
        string repoRoot = SecretsBaselineCleanerService.FindRepoRoot();
        var invocations = new List<(string WorkDir, string Executable, IReadOnlyList<string> Args)>();
        int FakeRunner(string workDir, string exe, IReadOnlyList<string> args)
        {
            invocations.Add((workDir, exe, args));
            return 0;
        }

        MigrationToolingService.Apply("ctms", "Host=localhost;Database=cadence", repoRoot, FakeRunner).Should().Be(0);
        MigrationToolingService.Apply("all", null, repoRoot, FakeRunner).Should().Be(0);
        MigrationToolingService.Apply("no-such-module", null, repoRoot, FakeRunner).Should().Be(1);

        invocations.Should().HaveCount(2);
        invocations[0].Args.Should().Equal(
            "run",
            "--project",
            "packages/tooling_core/src/Cadence.DatabaseMigrator/Cadence.DatabaseMigrator.csproj",
            "--",
            "--module",
            "ctms",
            "--connection",
            "Host=localhost;Database=cadence");

        invocations[1].Args.Should().Equal(
            "run",
            "--project",
            "packages/tooling_core/src/Cadence.DatabaseMigrator/Cadence.DatabaseMigrator.csproj");
    }

    [Fact]
    public void MigrationToolingService_BundleAndOptimize_RespectOfflinePlaceholderConnection_AndExecutionCompiledModelPolicy()
    {
        string repoRoot = SecretsBaselineCleanerService.FindRepoRoot();
        var invocations = new List<IReadOnlyList<string>>();
        int FakeRunner(string workDir, string exe, IReadOnlyList<string> args)
        {
            invocations.Add(args);
            return 0;
        }

        string bundleDir = Path.Combine(_root, "bundles");
        Func<string, string?> unconfiguredEnv = _ => null;
        Func<string, string?> primaryConfiguredEnv = key =>
            key == "ConnectionStrings__cadence" ? "Host=db;Database=cadence;Username=u;Password=p" : null;

        MigrationToolingService.Bundle("kernel", bundleDir, repoRoot, FakeRunner, unconfiguredEnv).Should().Be(0);
        MigrationToolingService.Bundle("ctms", bundleDir, repoRoot, FakeRunner, primaryConfiguredEnv).Should().Be(0);
        MigrationToolingService.Optimize("kernel", "CustomCompiled", repoRoot, FakeRunner, unconfiguredEnv).Should().Be(0);
        MigrationToolingService.Optimize("execution", null, repoRoot, FakeRunner, unconfiguredEnv).Should().Be(1);
        MigrationToolingService.Optimize("all", null, repoRoot, FakeRunner, primaryConfiguredEnv).Should().Be(0);

        // Offline bundle supplies -- --connection placeholder
        invocations[0].Should().ContainInOrder(
            "ef",
            "migrations",
            "bundle",
            "--context",
            "KernelDbContext",
            "--output",
            Path.Combine(bundleDir, "kernel_migration_bundle"),
            "--force",
            "--",
            "--connection",
            MigrationToolingService.DesignTimePlaceholderConnection);

        // Configured ConnectionStrings__cadence omits placeholder
        invocations[1].Should().NotContain(MigrationToolingService.DesignTimePlaceholderConnection);

        // Offline optimize passes CompiledModelNamespace + placeholder
        invocations[2].Should().ContainInOrder(
            "ef",
            "dbcontext",
            "optimize",
            "--context",
            "KernelDbContext",
            "--output-dir",
            "CustomCompiled",
            "--namespace",
            "Cadence.Kernel.Persistence.CompiledModels",
            "--",
            "--connection",
            MigrationToolingService.DesignTimePlaceholderConnection);

        // Optimize("all") runs the 17 modules with SupportsCompiledModel == true and skips execution
        invocations.Skip(3).Should().HaveCount(17);
        invocations.Skip(3).SelectMany(a => a).Should().NotContain("ExecutionDbContext");
    }

    [Fact]
    public void MigrationToolingService_Seed_InvokesExecutionSeeder_SkipsNonSeedingOnAll_AndCitesDevelopmentTenantBootstrapOnKernel()
    {
        string repoRoot = SecretsBaselineCleanerService.FindRepoRoot();
        var invocations = new List<IReadOnlyList<string>>();
        int FakeRunner(string workDir, string exe, IReadOnlyList<string> args)
        {
            invocations.Add(args);
            return 0;
        }

        using var execOut = new StringWriter();
        using var execErr = new StringWriter();
        MigrationToolingService.Seed("execution", repoRoot, FakeRunner, execOut, execErr).Should().Be(0);
        invocations.Should().ContainSingle().Which.Should().Equal(
            "run",
            "--project",
            Path.Combine(repoRoot, "apps", "execution", "src", "Cadence.Apps.Execution.csproj"),
            "--",
            "--seed");

        invocations.Clear();
        using var allOut = new StringWriter();
        using var allErr = new StringWriter();
        MigrationToolingService.Seed("all", repoRoot, FakeRunner, allOut, allErr).Should().Be(0);
        invocations.Should().ContainSingle("only execution declares a CLI --seed entrypoint");
        allOut.ToString().Should().Contain("Skipping 'ctms'");
        allErr.ToString().Should().BeEmpty();

        using var kernelOut = new StringWriter();
        using var kernelErr = new StringWriter();
        MigrationToolingService.Seed("kernel", repoRoot, FakeRunner, kernelOut, kernelErr).Should().Be(1);
        kernelErr.ToString().Should().Contain("apps/kernel-api/src/DevelopmentTenantBootstrap.cs");

        using var ctmsOut = new StringWriter();
        using var ctmsErr = new StringWriter();
        MigrationToolingService.Seed("ctms", repoRoot, FakeRunner, ctmsOut, ctmsErr).Should().Be(1);
        ctmsErr.ToString().Should().Contain("apps/execution/src/Program.cs");
    }

    [Fact]
    public void MigrationToolingService_Add_RunsScopedDotnetFormatWhitespaceAndStyleOnTouchedMigrationFiles()
    {
        string fakeRepo = Directory.CreateDirectory(Path.Combine(_root, "fake-repo")).FullName;
        string projectDir = Directory.CreateDirectory(Path.Combine(fakeRepo, "apps", "ctms", "src")).FullName;
        string projectPath = Path.Combine(projectDir, "Cadence.Apps.Ctms.csproj");
        File.WriteAllText(projectPath, "<Project />");

        string migrationsDir = Directory.CreateDirectory(
            Path.Combine(projectDir, "Infrastructure", "Persistence", "Migrations")).FullName;
        string untouchedMigration = Path.Combine(migrationsDir, "20260901000000_Initial.cs");
        File.WriteAllText(untouchedMigration, "namespace Existing;\n");

        var invocations = new List<IReadOnlyList<string>>();
        int FakeRunner(string workDir, string exe, IReadOnlyList<string> args)
        {
            invocations.Add(args);
            if (args.Count >= 3 && args[0] == "ef" && args[1] == "migrations" && args[2] == "add")
            {
                File.WriteAllText(Path.Combine(migrationsDir, "20261125000000_AddWindow.cs"), "namespace Scoped;\n");
                File.WriteAllText(Path.Combine(migrationsDir, "20261125000000_AddWindow.Designer.cs"), "namespace Scoped;\n");
                File.WriteAllText(Path.Combine(migrationsDir, "CtmsDbContextModelSnapshot.cs"), "namespace Scoped;\n");
            }

            return 0;
        }

        MigrationToolingService.Add("ctms", "AddWindow", fakeRepo, FakeRunner, _ => null).Should().Be(0);

        invocations.Should().HaveCount(3);
        invocations[1].Should().Equal(
            "format",
            "whitespace",
            ".",
            "--folder",
            "--include",
            "apps/ctms/src/Infrastructure/Persistence/Migrations/20261125000000_AddWindow.Designer.cs",
            "apps/ctms/src/Infrastructure/Persistence/Migrations/20261125000000_AddWindow.cs",
            "apps/ctms/src/Infrastructure/Persistence/Migrations/CtmsDbContextModelSnapshot.cs");

        invocations[2].Should().Equal(
            "format",
            "style",
            projectPath,
            "--no-restore",
            "--include",
            "apps/ctms/src/Infrastructure/Persistence/Migrations/20261125000000_AddWindow.Designer.cs",
            "apps/ctms/src/Infrastructure/Persistence/Migrations/20261125000000_AddWindow.cs",
            "apps/ctms/src/Infrastructure/Persistence/Migrations/CtmsDbContextModelSnapshot.cs");
    }

    private sealed class LibraryContext : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseNpgsql("Host=unused;Database=unused;Search Path=library,public");

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<SchemaDocBook>(book =>
            {
                book.ToTable("books", t => t.HasCheckConstraint("CK_books_copies", "\"Copies\" >= 0"));
                book.Property(b => b.Title).HasMaxLength(200);
                book.Property(b => b.Copies).HasDefaultValue(1);
                book.HasIndex(b => b.Title).IsUnique();
            });

            modelBuilder.Entity<SchemaDocLoan>(loan =>
            {
                loan.ToTable("loans", "audit");
                loan.HasOne<SchemaDocBook>().WithMany(b => b.Loans).HasForeignKey(l => l.BookId).OnDelete(DeleteBehavior.Cascade);
            });
        }
    }

    private sealed class UnmigratedLibraryContext : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseNpgsql(
                "Host=unused;Database=unused;Search Path=library,public",
                npgsql => npgsql.MigrationsAssembly(typeof(UnmigratedLibraryContext).Assembly.FullName));

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<SchemaDocBook>(book => book.ToTable("unmigrated_books"));
    }

    [DbContext(typeof(LibraryContext))]
    [Migration("20261101000000_ValidInitial")]
    private sealed class ValidInitialMigration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "books",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", nullable: false),
                });
        }

        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
        }
    }

    [DbContext(typeof(LibraryContext))]
    [Migration("20261102000000_DuplicateTable")]
    private sealed class DuplicateTableMigration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "books",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                });
        }

        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
        }
    }

    [DbContext(typeof(LibraryContext))]
    [Migration("20261103000000_DuplicateColumn")]
    private sealed class DuplicateColumnMigration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "books",
                type: "character varying(200)",
                nullable: false,
                defaultValue: "");
        }

        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
        }
    }

    private sealed class MissingAttributesMigration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }
    }
}

internal sealed class SchemaDocBook
{
    public Guid Id { get; set; }
    public required string Title { get; set; }
    public string? Subtitle { get; set; }
    public int Copies { get; set; }
    public List<SchemaDocLoan> Loans { get; } = [];
}

internal sealed class SchemaDocLoan
{
    public Guid Id { get; set; }
    public Guid BookId { get; set; }
    public DateOnly DueOn { get; set; }
}
