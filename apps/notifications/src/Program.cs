using Cadence.Apps.Notifications.Domain;
using Cadence.Apps.Notifications.Endpoints;
using Cadence.Apps.Notifications.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Security.DependencyInjection;
using Cadence.Telemetry;
using Cadence.Templating;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();
builder.Services.AddCadenceSecurity();
builder.AddCadencePostgresDbContext<NotificationsDbContext>();
builder.Services.AddFluidTemplating();

FluidTemplateRenderer.RegisterType<NotificationPayload>();
FluidTemplateRenderer.RegisterType<NotificationRenderModel>();

var app = builder.Build();
app.UseCadenceWebDefaults();
app.UseCadenceSecurity();
app.MapCadenceHealthAndStatusEndpoints(
    "Cadence.Apps.Notifications",
    "notifications",
    "Notifications dispatch health probe.",
    "Returns Notifications operational status.");

app.MapGroup("/api/v1/notifications").MapNotificationTemplateEndpoints();

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
