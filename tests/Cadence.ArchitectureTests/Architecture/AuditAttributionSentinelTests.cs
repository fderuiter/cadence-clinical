using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Cadence.Core.Domain;
using Cadence.Core.Domain.Audit;
using Cadence.Database;
using Cadence.Testing;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cadence.ArchitectureTests.Architecture;

/// <summary>
/// Keeps anonymous attribution out of the codebase (#4767): no actor-style member (<c>CreatedBy</c>,
/// <c>ReaderId</c>, <c>UserId</c>, and so on) may default to a literal such as <c>"system"</c>. An actor is
/// either the authenticated caller or a named <c>svc:</c> service account set explicitly at the point of work.
/// </summary>
public partial class AuditAttributionSentinelTests
{
    [Fact(DisplayName = "ActorMembers_MustNotDefaultTo_AnonymousSystemIdentity")]
    [Trait("Category", "Architecture")]
    public void ActorMembersMustNotDefaultToAnonymousSystemIdentity()
    {
        string root = FindRepositoryRoot();
        var offenders = new[] { "apps", "packages" }
            .Select(dir => Path.Combine(root, dir))
            .Where(Directory.Exists)
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            .Where(IsProductionSource)
            .SelectMany(file => File.ReadLines(file)
                .Select((line, index) => (file, line, number: index + 1)))
            .Where(x => AnonymousActorDefault().IsMatch(x.line))
            .Select(x => $"{Path.GetRelativePath(root, x.file)}:{x.number}: {x.line.Trim()}")
            .ToList();

        offenders.Should().BeEmpty(
            "writes must be attributed to the authenticated caller or a named svc: service account, never a default such as \"system\"");
    }

    [Theory(DisplayName = "AnonymousActorDefault_Pattern_CatchesKnownShapes")]
    [Trait("Category", "Architecture")]
    [InlineData("    public string CreatedBy { get; set; } = \"system\";", true)]
    [InlineData("    public string ReaderId { get; set; } = \"system\";", true)]
    [InlineData("    private const string DefaultUserId = \"system\";", true)]
    [InlineData("        string userId = \"system\";", true)]
    [InlineData("    public const string SystemUserId = \"system-pubsub\";", true)]
    [InlineData("    public string CreatedBy { get; set; } = string.Empty;", false)]
    [InlineData("    public required string CreatedBy { get; set; }", false)]
    [InlineData("        new(\"svc:econsent-consumer\", \"Automated consent-completion enrollment\");", false)]
    public void AnonymousActorDefaultPatternCatchesKnownShapes(string line, bool flagged)
    {
        AnonymousActorDefault().IsMatch(line).Should().Be(flagged);
    }

    // An actor-named member or local (…By, …UserId, …ReaderId, …Actor), optionally a { get; set; } property,
    // assigned a "system…" literal.
    [GeneratedRegex(@"\b\w*(By|UserId|ReaderId|Actor)\b(\s*\{[^}]*\})?\s*=\s*""system[^""]*""", RegexOptions.IgnoreCase)]
    private static partial Regex AnonymousActorDefault();

    [GeneratedRegex(@"\?\?\s*""(system|system-pubsub)""", RegexOptions.IgnoreCase)]
    private static partial Regex AnonymousActorNullCoalesce();

    [Fact(DisplayName = "ProductionSource_MustNotCoalesceTo_AnonymousSystemActor")]
    [Trait("Category", "Architecture")]
    public void ProductionSourceMustNotCoalesceToAnonymousSystemActor()
    {
        string root = FindRepositoryRoot();
        var offenders = new[] { "apps", "packages" }
            .Select(dir => Path.Combine(root, dir))
            .Where(Directory.Exists)
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            .Where(IsProductionSource)
            .SelectMany(file => File.ReadLines(file)
                .Select((line, index) => (file, line, number: index + 1)))
            .Where(x => AnonymousActorNullCoalesce().IsMatch(x.line))
            .Select(x => $"{Path.GetRelativePath(root, x.file)}:{x.number}: {x.line.Trim()}")
            .ToList();

        offenders.Should().BeEmpty(
            "actor resolution must fail closed or use an explicit svc: service account via IAuditActorScope, never coalesce to \"system\"");
    }

    [Fact(DisplayName = "ProductionSource_MustNotFallbackTo_SystemOperationChangeReason")]
    [Trait("Category", "Architecture")]
    public void ProductionSourceMustNotFallbackToSystemOperationChangeReason()
    {
        string root = FindRepositoryRoot();
        var offenders = new[] { "apps", "packages" }
            .Select(dir => Path.Combine(root, dir))
            .Where(Directory.Exists)
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            .Where(IsProductionSource)
            .SelectMany(file => File.ReadLines(file)
                .Select((line, index) => (file, line, number: index + 1)))
            .Where(x => x.line.Contains("\"system_operation\"", StringComparison.OrdinalIgnoreCase))
            .Select(x => $"{Path.GetRelativePath(root, x.file)}:{x.number}: {x.line.Trim()}")
            .ToList();

        offenders.Should().BeEmpty(
            "change-reason resolution must fail closed when X-Change-Reason and IAuditActorScope are absent, never defaulting to \"system_operation\"");
    }

    [Fact(DisplayName = "AllStandaloneDbContexts_AttachGxPAuditInterceptorByDefault_AndMapAuditAndOutbox")]
    [Trait("Category", "Architecture")]
    public void AllStandaloneDbContextsAttachGxPAuditInterceptorByDefaultAndMapAuditAndOutbox()
    {
        var contextTypes = new (Type ContextType, string Schema)[]
        {
            (typeof(Cadence.Apps.Ctms.Infrastructure.Persistence.CtmsDbContext), "ctms"),
            (typeof(Cadence.Apps.Econsent.Infrastructure.Persistence.EconsentDbContext), "econsent"),
            (typeof(Cadence.Apps.Eisf.Infrastructure.Persistence.EisfDbContext), "eisf"),
            (typeof(Cadence.Apps.Etmf.Infrastructure.Persistence.EtmfDbContext), "etmf"),
            (typeof(Cadence.Apps.Execution.Infrastructure.Persistence.ExecutionDbContext), "execution"),
            (typeof(Cadence.Apps.Fileshare.Infrastructure.Persistence.FileshareDbContext), "fileshare"),
            (typeof(Cadence.Apps.Imaging.Infrastructure.Persistence.ImagingDbContext), "imaging"),
            (typeof(Cadence.Apps.Interop.Infrastructure.Persistence.InteropDbContext), "interop"),
            (typeof(Cadence.Apps.Knowledge.Infrastructure.Persistence.KnowledgeDbContext), "knowledge"),
            (typeof(Cadence.Apps.Notifications.Infrastructure.Persistence.NotificationsDbContext), "notifications"),
            (typeof(Cadence.Apps.Org.Infrastructure.Persistence.OrgDbContext), "org"),
            (typeof(Cadence.Apps.Payments.Infrastructure.Persistence.PaymentsDbContext), "payments"),
            (typeof(Cadence.Apps.Quality.Infrastructure.Persistence.QualityDbContext), "quality"),
            (typeof(Cadence.Apps.Safety.Infrastructure.Persistence.SafetyDbContext), "safety"),
            (typeof(Cadence.Apps.Tickets.Infrastructure.Persistence.TicketsDbContext), "tickets"),
        };

        var addMethod = typeof(Cadence.Database.CadenceDatabaseExtensions)
            .GetMethod(nameof(Cadence.Database.CadenceDatabaseExtensions.AddCadencePostgresDbContext))!;

        foreach (var (contextType, schema) in contextTypes)
        {
            var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder();
            builder.Environment.EnvironmentName = Microsoft.Extensions.Hosting.Environments.Development;
            builder.Configuration["ConnectionStrings:cadence"] =
                "Host=localhost;Port=5432;Database=cadence_test;Username=cadence_test";
            builder.Services.AddSingleton(TestFieldProtection.CreateEncryptor());

            addMethod.MakeGenericMethod(contextType).Invoke(null, [builder, schema, "cadence", null]);

            using var app = builder.Build();
            using var scope = app.Services.CreateScope();

            var optionsType = typeof(Microsoft.EntityFrameworkCore.DbContextOptions<>).MakeGenericType(contextType);
            var options = (Microsoft.EntityFrameworkCore.DbContextOptions)scope.ServiceProvider.GetRequiredService(optionsType);
            var coreExt = options.FindExtension<Microsoft.EntityFrameworkCore.Infrastructure.CoreOptionsExtension>();
            coreExt.Should().NotBeNull();
            coreExt!.Interceptors.Should().NotBeNull();
            coreExt.Interceptors!.OfType<Cadence.Database.GxPAuditInterceptor>()
                .Should().ContainSingle("{0} must attach GxPAuditInterceptor by default via AddCadencePostgresDbContext", contextType.Name);

            var db = (Microsoft.EntityFrameworkCore.DbContext)scope.ServiceProvider.GetRequiredService(contextType);
            db.Model.FindEntityType(typeof(AuditLogEntry))
                .Should().NotBeNull("{0} must map AuditLogEntry", contextType.Name);
            db.Model.FindEntityType(typeof(TransactionalOutboxMessage))
                .Should().NotBeNull("{0} must map TransactionalOutboxMessage", contextType.Name);
        }
    }

    private static bool IsProductionSource(string path)
    {
        string normalized = path.Replace('\\', '/');
        return !normalized.Contains("/obj/", StringComparison.Ordinal)
            && !normalized.Contains("/bin/", StringComparison.Ordinal)
            && !normalized.Contains("/Migrations/", StringComparison.Ordinal)
            && !normalized.Contains("/tests/", StringComparison.Ordinal)
            && !normalized.Contains("/packages/testing/", StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Cadence.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Cadence.sln not found above the test output directory.");
    }
}
