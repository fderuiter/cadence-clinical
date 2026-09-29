using Cadence.Apps.Notifications.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Telemetry;

using Microsoft.AspNetCore.Builder;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();
builder.AddCadencePostgresDbContext<NotificationsDbContext>();

var app = builder.Build();
app.UseCadenceWebDefaults();
app.MapCadenceHealthAndStatusEndpoints(
    "Cadence.Apps.Notifications",
    "notifications",
    "Notifications dispatch health probe.",
    "Returns Notifications operational status.");

await app.RunAsync();

namespace Cadence.Apps.Notifications
{
    public partial class Program
    {
        protected Program()
        {
        }
    }
}
