using Cadence.Apps.Tickets.Application;
using Cadence.Apps.Tickets.Endpoints;
using Cadence.Apps.Tickets.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Database.Signatures;
using Cadence.Kernel.Time;
using Cadence.Security.DependencyInjection;
using Cadence.Telemetry;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();
builder.Services.AddCadenceSecurity();
builder.Services.AddCadenceDurableSigTokenStore<TicketsDbContext>();
builder.Services.AddSingleton<IClock>(SystemClock.Instance);
builder.AddCadencePostgresDbContext<TicketsDbContext>();
builder.Services.AddScoped<ITicketsUnitOfWork, TicketsUnitOfWork>();
builder.Services.AddScoped<ITicketQueryService, TicketQueryService>();
builder.Services.AddScoped<ITicketAuthoringService, TicketAuthoringService>();
builder.Services.AddScoped<ITicketWorkflowService, TicketWorkflowService>();

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

namespace Cadence.Apps.Tickets
{
    public partial class Program
    {
        protected Program()
        {
        }
    }
}
