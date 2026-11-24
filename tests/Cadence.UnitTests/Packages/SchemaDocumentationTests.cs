using System;
using System.Collections.Generic;
using System.Diagnostics;
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
            "Infrastructure/Migrations",
            "--context",
            "ExecutionModuleDbContext");

        string expectedKernelSql = Path.Combine(customOut, "kernel.sql");
        invocations[1].Args.Should().ContainInOrder(
            "ef",
            "migrations",
            "script",
            "--idempotent",
            "--project",
            Path.Combine(repoRoot, "packages", "kernel", "src", "Cadence.Kernel.Persistence", "Cadence.Kernel.Persistence.csproj"),
            "--output",
            expectedKernelSql,
            "--context",
            "KernelDbContext");
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
    public void SchemaMigrationValidator_FailsClosed_OnUnsplitSnapshotOrphanPartialsAndUncompactedDesigner()
    {
        string unsplitDir = Directory.CreateDirectory(Path.Combine(_root, "migrations-unsplit")).FullName;
        string unsplitSnapshotsDir = Directory.CreateDirectory(Path.Combine(unsplitDir, "Snapshots")).FullName;

        File.WriteAllText(
            Path.Combine(unsplitDir, "20261101000000_ValidInitial.cs"),
            "public partial class ValidInitial : Migration {}");
        File.WriteAllText(
            Path.Combine(unsplitDir, "20261101000000_ValidInitial.Designer.cs"),
            """
            // <auto-generated />
            namespace Demo;
            partial class ValidInitial
            {
                protected override void BuildTargetModel(ModelBuilder modelBuilder)
                {
                }
            }
            """);
        File.WriteAllText(
            Path.Combine(unsplitDir, "LibraryContextModelSnapshot.cs"),
            """
            // <auto-generated />
            namespace Demo;
            partial class LibraryContextModelSnapshot : ModelSnapshot
            {
                protected override void BuildModel(ModelBuilder modelBuilder)
                {
                    modelBuilder.Entity("Cadence.UnitTests.Packages.SchemaDocBook", b => { });
                }
            }
            """);
        File.WriteAllText(
            Path.Combine(unsplitSnapshotsDir, "Cadence.UnitTests.Packages.OrphanEntity.Snapshot.cs"),
            "// <auto-generated />\nnamespace Demo;\n");

        using var context = new LibraryContext();
        var errors = SchemaMigrationValidator.Validate(
            context,
            module: "library",
            migrationsDirectory: unsplitDir,
            defaultSchema: "library",
            migrationTypes: [typeof(ValidInitialMigration)],
            checkPendingModelChanges: false);

        string joined = string.Join("\n", errors);
        joined.Should().Contain("LibraryContextModelSnapshot.cs' is unsplit");
        joined.Should().Contain("20261101000000_ValidInitial.Designer.cs' contains an uncompacted BuildTargetModel body");
        joined.Should().Contain("Orphan snapshot partial");
        joined.Should().Contain("Cadence.UnitTests.Packages.OrphanEntity.Snapshot.cs");

        string missingPartialDir = Directory.CreateDirectory(Path.Combine(_root, "migrations-missing-partial")).FullName;
        string missingPartialSnapshotsDir = Directory.CreateDirectory(Path.Combine(missingPartialDir, "Snapshots")).FullName;
        File.WriteAllText(
            Path.Combine(missingPartialDir, "20261101000000_ValidInitial.cs"),
            "public partial class ValidInitial : Migration {}");
        File.WriteAllText(
            Path.Combine(missingPartialDir, "20261101000000_ValidInitial.Designer.cs"),
            "// <auto-generated />\nnamespace Demo;\npartial class ValidInitial {}\n");
        File.WriteAllText(
            Path.Combine(missingPartialDir, "LibraryContextModelSnapshot.cs"),
            "// <auto-generated />\nnamespace Demo;\npartial class LibraryContextModelSnapshot : ModelSnapshot { private interface IEntitySnapshot {} }\n");
        File.WriteAllText(
            Path.Combine(missingPartialSnapshotsDir, "Cadence.UnitTests.Packages.SchemaDocBook.Snapshot.cs"),
            "// <auto-generated />\nnamespace Demo;\n");

        var missingPartialErrors = SchemaMigrationValidator.Validate(
            context,
            module: "library",
            migrationsDirectory: missingPartialDir,
            defaultSchema: "library",
            migrationTypes: [typeof(ValidInitialMigration)],
            checkPendingModelChanges: false);

        missingPartialErrors.Should().ContainSingle()
            .Which.Should().Contain("Missing entity snapshot partial")
            .And.Contain("Cadence.UnitTests.Packages.SchemaDocLoan.Snapshot.cs");
    }

    [Fact]
    public void SchemaMigrationValidator_FailsClosed_OnDuplicateIndexForeignKeyUniqueAndCheckConstraints()
    {
        using var context = new LibraryContext();

        var errors = SchemaMigrationValidator.Validate(
            context,
            module: "library",
            migrationsDirectory: null,
            defaultSchema: "library",
            migrationTypes:
            [
                typeof(ConstraintBaselineMigration),
                typeof(DuplicateConstraintsMigration),
            ],
            checkPendingModelChanges: false);

        string joined = string.Join("\n", errors);
        joined.Should().Contain("duplicates index 'IX_books_Title' on 'library.books'");
        joined.Should().Contain("duplicates index definition 'IX_books_Title_AltName' on 'library.books'");
        joined.Should().Contain("duplicates foreign key 'FK_loans_books_BookId' on 'audit.loans'");
        joined.Should().Contain("duplicates foreign key 'FK_loans_books_BookId_AltName' on 'audit.loans'");
        joined.Should().Contain("duplicates unique constraint 'AK_books_Title' on 'library.books'");
        joined.Should().Contain("duplicates unique constraint 'AK_books_Title_AltName' on 'library.books'");
        joined.Should().Contain("duplicates check constraint 'CK_books_copies' on 'library.books'");
        joined.Should().Contain("duplicates check constraint 'CK_books_copies_alt' on 'library.books'");
    }

    [Fact]
    public void SchemaMigrationValidator_AllowsConstraintReplacementViaDropAndSqlOperationBaselineTables()
    {
        using var context = new LibraryContext();

        var errors = SchemaMigrationValidator.Validate(
            context,
            module: "library",
            migrationsDirectory: null,
            defaultSchema: "library",
            migrationTypes:
            [
                typeof(ConstraintBaselineMigration),
                typeof(SqlBaselineAndDropRecreateMigration),
            ],
            checkPendingModelChanges: false);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void MigrationSnapshotCompactor_SplitsSnapshotCompactsDesigner_AndMergesParallelBranchesCleanly()
    {
        string baseDir = Directory.CreateDirectory(Path.Combine(_root, "merge-base")).FullName;
        string branchADir = Directory.CreateDirectory(Path.Combine(_root, "merge-branch-a")).FullName;
        string branchBDir = Directory.CreateDirectory(Path.Combine(_root, "merge-branch-b")).FullName;

        const string baseMonolithic = """
            // <auto-generated />
            using System;
            using Cadence.Apps.Library.Infrastructure;
            using Microsoft.EntityFrameworkCore;
            using Microsoft.EntityFrameworkCore.Infrastructure;
            using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

            #nullable disable

            namespace Cadence.Apps.Library.Infrastructure.Migrations
            {
                [DbContext(typeof(LibraryDbContext))]
                partial class LibraryDbContextModelSnapshot : ModelSnapshot
                {
                    protected override void BuildModel(ModelBuilder modelBuilder)
                    {
            #pragma warning disable 612, 618 // synthetic EF Core snapshot fixture
                        modelBuilder
                            .HasDefaultSchema("library")
                            .HasAnnotation("ProductVersion", "10.0.15")
                            .HasAnnotation("Relational:MaxIdentifierLength", 63);

                        NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

                        modelBuilder.Entity("Cadence.Apps.Library.Domain.Book", b =>
                            {
                                b.Property<Guid>("Id").HasColumnType("uuid");
                                b.HasKey("Id");
                                b.ToTable("books", "library");
                            });

                        modelBuilder.Entity("Cadence.Apps.Library.Domain.Loan", b =>
                            {
                                b.Property<Guid>("Id").HasColumnType("uuid");
                                b.Property<Guid>("BookId").HasColumnType("uuid");
                                b.HasKey("Id");
                                b.ToTable("loans", "library");
                            });

                        modelBuilder.Entity("Cadence.Apps.Library.Domain.Loan", b =>
                            {
                                b.HasOne("Cadence.Apps.Library.Domain.Book", null)
                                    .WithMany("Loans")
                                    .HasForeignKey("BookId")
                                    .OnDelete(DeleteBehavior.Cascade)
                                    .IsRequired();
                            });

                        modelBuilder.Entity("Cadence.Apps.Library.Domain.Book", b =>
                            {
                                b.Navigation("Loans");
                            });
            #pragma warning restore 612, 618
                    }
                }
            }
            """;

        const string baseDesigner = """
            // <auto-generated />
            using System;
            using Cadence.Apps.Library.Infrastructure;
            using Microsoft.EntityFrameworkCore;
            using Microsoft.EntityFrameworkCore.Infrastructure;
            using Microsoft.EntityFrameworkCore.Migrations;
            using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

            #nullable disable

            namespace Cadence.Apps.Library.Infrastructure.Migrations
            {
                [DbContext(typeof(LibraryDbContext))]
                [Migration("20261101000000_Initial")]
                partial class Initial
                {
                    protected override void BuildTargetModel(ModelBuilder modelBuilder)
                    {
                        modelBuilder.Entity("Cadence.Apps.Library.Domain.Book", b => { });
                    }
                }
            }
            """;

        string branchAMonolithic = baseMonolithic
            .Replace(
                "            modelBuilder.Entity(\"Cadence.Apps.Library.Domain.Book\", b =>\n                {\n                    b.Property<Guid>(\"Id\").HasColumnType(\"uuid\");",
                """
                        modelBuilder.Entity("Cadence.Apps.Library.Domain.Author", b =>
                            {
                                b.Property<Guid>("Id").HasColumnType("uuid");
                                b.HasKey("Id");
                                b.ToTable("authors", "library");
                            });

                        modelBuilder.Entity("Cadence.Apps.Library.Domain.Book", b =>
                            {
                                b.Property<Guid>("Id").HasColumnType("uuid");
            """,
                StringComparison.Ordinal)
            .Replace(
                "            modelBuilder.Entity(\"Cadence.Apps.Library.Domain.Book\", b =>\n                {\n                    b.Navigation(\"Loans\");\n                });",
                """
                        modelBuilder.Entity("Cadence.Apps.Library.Domain.Book", b =>
                            {
                                b.Navigation("Loans");
                            });

                        modelBuilder.Entity("Cadence.Apps.Library.Domain.Loan", b =>
                            {
                                b.Navigation("Book");
                            });
            """,
                StringComparison.Ordinal);

        string branchBMonolithic = baseMonolithic.Replace(
            "            modelBuilder.Entity(\"Cadence.Apps.Library.Domain.Book\", b =>\n                {\n                    b.Navigation(\"Loans\");\n                });",
            """
                        modelBuilder.Entity("Cadence.Apps.Library.Domain.Patron", b =>
                            {
                                b.Property<Guid>("Id").HasColumnType("uuid");
                                b.HasKey("Id");
                                b.ToTable("patrons", "library");
                            });

                        modelBuilder.Entity("Cadence.Apps.Library.Domain.Book", b =>
                            {
                                b.Navigation("Loans");
                                b.Navigation("Reviews");
                            });

                        modelBuilder.Entity("Cadence.Apps.Library.Domain.Patron", b =>
                            {
                                b.Navigation("Checkouts");
                            });
            """,
            StringComparison.Ordinal);

        File.WriteAllText(Path.Combine(baseDir, "LibraryDbContextModelSnapshot.cs"), baseMonolithic);
        File.WriteAllText(Path.Combine(baseDir, "20261101000000_Initial.Designer.cs"), baseDesigner);
        MigrationSnapshotCompactor.NormalizeModuleMigrations(baseDir);

        File.WriteAllText(Path.Combine(branchADir, "LibraryDbContextModelSnapshot.cs"), branchAMonolithic);
        File.WriteAllText(Path.Combine(branchADir, "20261101000000_Initial.Designer.cs"), baseDesigner);
        File.WriteAllText(
            Path.Combine(branchADir, "20261102000000_AddAuthor.Designer.cs"),
            baseDesigner.Replace("20261101000000_Initial", "20261102000000_AddAuthor", StringComparison.Ordinal)
                        .Replace("partial class Initial", "partial class AddAuthor", StringComparison.Ordinal));
        MigrationSnapshotCompactor.NormalizeModuleMigrations(branchADir);

        File.WriteAllText(Path.Combine(branchBDir, "LibraryDbContextModelSnapshot.cs"), branchBMonolithic);
        File.WriteAllText(Path.Combine(branchBDir, "20261101000000_Initial.Designer.cs"), baseDesigner);
        File.WriteAllText(
            Path.Combine(branchBDir, "20261103000000_AddPatron.Designer.cs"),
            baseDesigner.Replace("20261101000000_Initial", "20261103000000_AddPatron", StringComparison.Ordinal)
                        .Replace("partial class Initial", "partial class AddPatron", StringComparison.Ordinal));
        MigrationSnapshotCompactor.NormalizeModuleMigrations(branchBDir);

        string compactedDesigner = File.ReadAllText(Path.Combine(baseDir, "20261101000000_Initial.Designer.cs"));
        compactedDesigner.Should().StartWith("// <auto-generated />\n");
        compactedDesigner.Should().NotContain("\r\n");
        compactedDesigner.Should().NotContain("BuildTargetModel");
        compactedDesigner.Should().Contain("[DbContext(typeof(LibraryDbContext))]");
        compactedDesigner.Should().Contain("[Migration(\"20261101000000_Initial\")]");

        // Verify root snapshot and shared entity partials merge cleanly via git merge-file with exit code 0.
        string mergedRoot = Path.Combine(_root, "merged-LibraryDbContextModelSnapshot.cs");
        File.Copy(Path.Combine(branchADir, "LibraryDbContextModelSnapshot.cs"), mergedRoot);
        RunGitMergeFile(
            mergedRoot,
            Path.Combine(baseDir, "LibraryDbContextModelSnapshot.cs"),
            Path.Combine(branchBDir, "LibraryDbContextModelSnapshot.cs")).Should().Be(0);

        foreach (string sharedPartial in new[] { "Cadence.Apps.Library.Domain.Book.Snapshot.cs", "Cadence.Apps.Library.Domain.Loan.Snapshot.cs" })
        {
            string mergedPartial = Path.Combine(_root, "merged-" + sharedPartial);
            File.Copy(Path.Combine(branchADir, "Snapshots", sharedPartial), mergedPartial);
            RunGitMergeFile(
                mergedPartial,
                Path.Combine(baseDir, "Snapshots", sharedPartial),
                Path.Combine(branchBDir, "Snapshots", sharedPartial)).Should().Be(0);
        }

        File.ReadAllText(Path.Combine(_root, "merged-Cadence.Apps.Library.Domain.Book.Snapshot.cs"))
            .Should().Contain("b.Navigation(\"Reviews\");");
        File.ReadAllText(Path.Combine(_root, "merged-Cadence.Apps.Library.Domain.Loan.Snapshot.cs"))
            .Should().Contain("b.Navigation(\"Book\");");

        File.Exists(Path.Combine(branchADir, "Snapshots", "Cadence.Apps.Library.Domain.Author.Snapshot.cs")).Should().BeTrue();
        File.Exists(Path.Combine(branchBDir, "Snapshots", "Cadence.Apps.Library.Domain.Patron.Snapshot.cs")).Should().BeTrue();
    }

    private static int RunGitMergeFile(string currentPath, string basePath, string otherPath)
    {
        var psi = new ProcessStartInfo("git")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("merge-file");
        psi.ArgumentList.Add(currentPath);
        psi.ArgumentList.Add(basePath);
        psi.ArgumentList.Add(otherPath);
        using var proc = Process.Start(psi)!;
        proc.WaitForExit();
        return proc.ExitCode;
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

    [DbContext(typeof(LibraryContext))]
    [Migration("20261101000000_ConstraintBaseline")]
    private sealed class ConstraintBaselineMigration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE TABLE IF NOT EXISTS audit_log_entries (\"Id\" text PRIMARY KEY, \"EntityName\" text NOT NULL);");

            migrationBuilder.CreateTable(
                name: "books",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", nullable: false),
                    Copies = table.Column<int>(type: "integer", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_books", x => x.Id);
                    table.UniqueConstraint("AK_books_Title", x => x.Title);
                    table.CheckConstraint("CK_books_copies", "\"Copies\" >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_books_Title",
                table: "books",
                column: "Title",
                unique: true);

            migrationBuilder.CreateTable(
                name: "loans",
                schema: "audit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BookId = table.Column<Guid>(type: "uuid", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_loans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_loans_books_BookId",
                        column: x => x.BookId,
                        principalTable: "books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }
    }

    [DbContext(typeof(LibraryContext))]
    [Migration("20261102000000_DuplicateConstraints")]
    private sealed class DuplicateConstraintsMigration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_books_Title",
                table: "books",
                column: "Copies");

            migrationBuilder.CreateIndex(
                name: "IX_books_Title_AltName",
                table: "books",
                column: "Title",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_loans_books_BookId",
                schema: "audit",
                table: "loans",
                column: "BookId",
                principalTable: "books",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_loans_books_BookId_AltName",
                schema: "audit",
                table: "loans",
                column: "BookId",
                principalTable: "books",
                principalColumn: "Id");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_books_Title",
                table: "books",
                column: "Copies");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_books_Title_AltName",
                table: "books",
                column: "Title");

            migrationBuilder.AddCheckConstraint(
                name: "CK_books_copies",
                table: "books",
                sql: "\"Copies\" >= 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_books_copies_alt",
                table: "books",
                sql: "  \"Copies\"   >=   0  ");
        }
    }

    [DbContext(typeof(LibraryContext))]
    [Migration("20261102000000_SqlBaselineAndDropRecreate")]
    private sealed class SqlBaselineAndDropRecreateMigration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_audit_log_entries_EntityName_Timestamp",
                table: "audit_log_entries");

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_entries_EntityName_Timestamp",
                table: "audit_log_entries",
                column: "EntityName");

            migrationBuilder.DropForeignKey(
                name: "FK_loans_books_BookId",
                schema: "audit",
                table: "loans");

            migrationBuilder.AddForeignKey(
                name: "FK_loans_books_BookId",
                schema: "audit",
                table: "loans",
                column: "BookId",
                principalTable: "books",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.DropUniqueConstraint(
                name: "AK_books_Title",
                table: "books");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_books_Title",
                table: "books",
                column: "Title");

            migrationBuilder.DropCheckConstraint(
                name: "CK_books_copies",
                table: "books");

            migrationBuilder.AddCheckConstraint(
                name: "CK_books_copies",
                table: "books",
                sql: "\"Copies\" >= 0 AND \"Copies\" <= 1000");
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
