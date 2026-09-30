using System;
using System.Collections.Generic;
using System.IO;

using Cadence.Database;
using Cadence.ToolingCore.Services;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

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
    public void Render_DescribesTablesColumnsKeysIndexesAndChecks_InTheModuleSchema()
    {
        using var context = new LibraryContext();

        string markdown = SchemaDocumentation.Render(context, "`library` database schema", "generate");

        SchemaDocumentation.DefaultSchemaOf(context).Should().Be("library");
        markdown.Should().Contain("Unqualified tables live in the `library` schema.");
        markdown.Should().Contain("| [`library.books`](#librarybooks) | `SchemaDocBook` | 4 |");
        markdown.Should().Contain("| [`audit.loans`](#auditloans) | `SchemaDocLoan` | 3 |", "an explicit schema wins over the search path");
        markdown.Should().Contain("| `Id` | `uuid` | no |  | PK |");
        markdown.Should().Contain("| `Title` | `character varying(200)` | no |  |  |");
        markdown.Should().Contain("| `Subtitle` | `text` | yes |  |  |");
        markdown.Should().Contain("| `Copies` | `integer` | no | `1` |  |");
        markdown.Should().Contain("- `FK_loans_books_BookId`: (`BookId`) references `library.books` (`Id`), on delete cascade");
        markdown.Should().Contain("- `IX_books_Title`: unique (`Title`)");
        markdown.Should().Contain("- `CK_books_copies`: `\"Copies\" >= 0`");
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
