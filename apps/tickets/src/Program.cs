using Cadence.Apps.Tickets.Endpoints;
using Cadence.Apps.Tickets.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;
using Cadence.Telemetry;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();
builder.Services.AddCadenceSecurity();
builder.Services.AddSingleton<IClock>(SystemClock.Instance);
builder.AddCadencePostgresDbContext<TicketsDbContext>(configureOptions: static (sp, options) =>
{
    options.AddInterceptors(new GxPAuditInterceptor(
        () => ResolveAuthenticatedActor(sp)
              ?? throw new InvalidOperationException("Authenticated caller identity is required for GxP ticket audit entries."),
        sp.GetRequiredService<IClock>()));
});

var app = builder.Build();
app.UseCadenceWebDefaults();
app.UseCadenceSecurity();
app.MapCadenceHealthAndStatusEndpoints(
    "Cadence.Apps.Tickets",
    "tickets",
    "Support tickets health probe.",
    "Returns Tickets operational status.");

app.MapTicketEndpoints();

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

namespace Cadence.Apps.Tickets
{
    public partial class Program
    {
        protected Program()
        {
        }
    }
}
