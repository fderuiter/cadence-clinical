using Cadence.Apps.Notifications.Domain;
using Cadence.Apps.Notifications.Endpoints;
using Cadence.Apps.Notifications.Infrastructure;
using Cadence.Apps.Notifications.Infrastructure.Consumers;
using Cadence.Apps.Notifications.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Security.DependencyInjection;
using Cadence.Telemetry;
using Cadence.Templating;

using MassTransit;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();
builder.Services.AddCadenceSecurity();
builder.AddCadencePostgresDbContext<NotificationsDbContext>();
builder.Services.AddFluidTemplating();

builder.Services.AddMassTransit(x =>
{
    x.AddEntityFrameworkOutbox<NotificationsDbContext>(o =>
    {
        o.UsePostgres();
        o.UseBusOutbox();
    });
    x.AddConsumer<ClinicalQueryRaisedConsumer>();
    x.UsingInMemory((context, cfg) =>
    {
        cfg.ConfigureEndpoints(context);
    });
});

// The brand name and portal link notifications carry come from configuration, never the request (#4983).
builder.Services.AddNotificationBranding(builder.Configuration);

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
