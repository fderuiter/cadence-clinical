using Cadence.Apps.Notifications.Domain;
using Cadence.Apps.Notifications.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Kernel.Templating;
using Cadence.Security.DependencyInjection;
using Cadence.Telemetry;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();
builder.Services.AddCadenceSecurity();
builder.AddCadencePostgresDbContext<NotificationsDbContext>();
builder.Services.AddFluidTemplating();

FluidTemplateRenderer.RegisterAssembly(typeof(Program).Assembly);
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

var api = app.MapGroup("/api/v1/notifications");
api.MapPost("/render", async (string templateName, NotificationRenderModel request, ITemplateRenderer renderer) =>
{
    string rendered = await renderer.RenderAsync(templateName, request, typeof(Program).Assembly);
    return TypedResults.Ok(new { templateName, content = rendered });
})
.WithName("RenderNotificationTemplate")
.WithSummary("Renders a Liquid notification template asynchronously.");

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
