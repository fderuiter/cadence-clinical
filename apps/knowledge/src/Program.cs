using Cadence.Apps.Knowledge.Application;
using Cadence.Apps.Knowledge.Endpoints;
using Cadence.Apps.Knowledge.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Database.Signatures;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;
using Cadence.Telemetry;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();
builder.Services.AddCadenceSecurity();
builder.Services.AddSingleton<IClock>(SystemClock.Instance);
builder.AddCadencePostgresDbContext<KnowledgeDbContext>(configureOptions: static (sp, options) =>
{
    options.AddInterceptors(new GxPAuditInterceptor(
        () => KnowledgeAuditActorScope.CurrentUserId
              ?? ResolveAuthenticatedActor(sp)
              ?? throw new InvalidOperationException("Authenticated caller or audit actor scope is required for GxP writes."),
        sp.GetRequiredService<IClock>()));
});
// Delegation sign-off is e-signed; each token is single use across instances and restarts (ADR-2515, #5434).
builder.Services.AddCadenceDurableSigTokenStore<KnowledgeDbContext>();
if ((builder.Configuration.GetSection(CadenceMigrationOptions.SectionName).Get<CadenceMigrationOptions>() is { MigrateOnStartup: true, MigrateOnly: false })
    || string.Equals(builder.Configuration["Database:SeedOnStartup"], "true", System.StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHostedService<KnowledgeStartupSeederHostedService>();
}

var app = builder.Build();
if (args.Contains("--seed", System.StringComparer.Ordinal))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<KnowledgeDbContext>();
    var clock = scope.ServiceProvider.GetRequiredService<IClock>();
    await db.Database.MigrateAsync();
    await KnowledgeCatalogSeeder.EnsureSeededAsync(db, clock);
    return;
}

app.UseCadenceWebDefaults();
app.UseCadenceSecurity();
app.MapCadenceHealthAndStatusEndpoints(
    "Cadence.Apps.Knowledge",
    "knowledge",
    "Knowledge base and study training health probe.",
    "Returns Knowledge base operational status.");

app.MapKnowledgeEndpoints();
app.MapTrainingEndpoints();

await app.RunAsync();

static string? ResolveAuthenticatedActor(System.IServiceProvider sp)
{
    HttpContext? httpContext = sp.GetService<IHttpContextAccessor>()?.HttpContext;
    if (httpContext is null)
    {
        return null;
    }

    return CallerScope.Principal(httpContext)?.UserId
           ?? httpContext.User.FindFirst(CadenceClaimTypes.Subject)?.Value;
}

namespace Cadence.Apps.Knowledge
{
    public partial class Program
    {
        protected Program()
        {
        }
    }
}
