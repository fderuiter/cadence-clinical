using System;

using Cadence.Apps.Notifications.Domain;
using Cadence.Apps.Notifications.Infrastructure.Persistence;
using Cadence.Kernel.Templating;
using Cadence.Telemetry;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();

string connectionString = builder.Configuration["POSTGRES_CONNECTION_STRING"]
    is { Length: > 0 } configuredConnectionString
        ? configuredConnectionString
        : "Host=localhost;Port=5432;Database=cadence_edc;Username=cadence;Password=cadence_password";

builder.Services.AddDbContext<NotificationsDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddFluidTemplating();

FluidTemplateRenderer.RegisterAssembly(typeof(Program).Assembly);
FluidTemplateRenderer.RegisterType<NotificationPayload>();
FluidTemplateRenderer.RegisterType<NotificationRenderModel>();

var app = builder.Build();
app.UseCadenceWebDefaults();

app.MapGet("/healthz", () => TypedResults.Ok(new { status = "healthy", service = "Cadence.Apps.Notifications" }))
    .WithName("NotificationsHealthCheck")
    .WithSummary("Notifications dispatch health probe.");

var api = app.MapGroup("/api/v1/notifications");
api.MapGet("/status", () => TypedResults.Ok(new { mode = "Active", service = "Cadence.Apps.Notifications" }))
    .WithName("NotificationsStatus")
    .WithSummary("Returns Notifications operational status.");

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
