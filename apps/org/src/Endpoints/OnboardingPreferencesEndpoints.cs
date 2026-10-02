using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Org.Domain;
using Cadence.Apps.Org.Infrastructure.Persistence;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Org.Endpoints;

public static class OnboardingPreferencesEndpoints
{
    public record UserOnboardingPreferenceDto(
        string Persona,
        int CurrentStep,
        bool IsActive,
        bool Disabled,
        string EventsJson,
        DateTimeOffset UpdatedAt
    );

    public record SaveUserOnboardingPreferenceRequest(
        string? Persona,
        int CurrentStep,
        bool IsActive,
        bool Disabled,
        string? EventsJson,
        DateTimeOffset? UpdatedAt
    );

    public static IEndpointRouteBuilder MapOnboardingPreferencesEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/org/preferences/onboarding");

        group.MapGet("/{persona}", GetUserOnboardingPreferenceAsync)
            .WithName("GetUserOnboardingPreferenceByRoute")
            .WithSummary("Fetch user onboarding preferences by persona route parameter");

        group.MapGet("/", GetUserOnboardingPreferenceQueryAsync)
            .WithName("GetUserOnboardingPreference")
            .WithSummary("Fetch user onboarding preferences by persona query parameter");

        group.MapPut("/{persona}", SaveUserOnboardingPreferenceByRouteAsync)
            .WithName("SaveUserOnboardingPreferenceByRoute")
            .WithSummary("Save user onboarding preferences by persona route parameter");

        group.MapPut("/", SaveUserOnboardingPreferenceAsync)
            .WithName("SaveUserOnboardingPreference")
            .WithSummary("Save user onboarding preferences");

        return routes;
    }

    private static string GetUserId(ClaimsPrincipal user)
    {
        return user.FindFirst(CadenceClaimTypes.Subject)?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.Identity?.Name
            ?? "anonymous_user";
    }

    public static async Task<IResult> GetUserOnboardingPreferenceAsync(
        string persona,
        ClaimsPrincipal user,
        OrgDbContext dbContext,
        CancellationToken ct)
    {
        string userId = GetUserId(user);
        string normalizedPersona = (persona ?? "super_admin").Trim().ToLowerInvariant();

        var pref = await dbContext.UserOnboardingPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId && p.Persona == normalizedPersona, ct);

        if (pref is null)
        {
            return TypedResults.Ok(new UserOnboardingPreferenceDto(
                Persona: normalizedPersona,
                CurrentStep: 1,
                IsActive: true,
                Disabled: false,
                EventsJson: "[]",
                UpdatedAt: DateTimeOffset.UnixEpoch
            ));
        }

        return TypedResults.Ok(new UserOnboardingPreferenceDto(
            Persona: pref.Persona,
            CurrentStep: pref.CurrentStep,
            IsActive: pref.IsActive,
            Disabled: pref.Disabled,
            EventsJson: pref.EventsJson ?? "[]",
            UpdatedAt: pref.UpdatedAt
        ));
    }

    public static Task<IResult> GetUserOnboardingPreferenceQueryAsync(
        string? persona,
        ClaimsPrincipal user,
        OrgDbContext dbContext,
        CancellationToken ct)
    {
        return GetUserOnboardingPreferenceAsync(persona ?? "super_admin", user, dbContext, ct);
    }

    public static async Task<IResult> SaveUserOnboardingPreferenceByRouteAsync(
        string persona,
        SaveUserOnboardingPreferenceRequest request,
        ClaimsPrincipal user,
        OrgDbContext dbContext,
        [FromServices] IClock clock,
        CancellationToken ct)
    {
        var reqWithPersona = request with { Persona = persona };
        return await SaveUserOnboardingPreferenceAsync(reqWithPersona, user, dbContext, clock, ct);
    }

    public static async Task<IResult> SaveUserOnboardingPreferenceAsync(
        SaveUserOnboardingPreferenceRequest request,
        ClaimsPrincipal user,
        OrgDbContext dbContext,
        [FromServices] IClock clock,
        CancellationToken ct)
    {
        string userId = GetUserId(user);
        string persona = (request.Persona ?? "super_admin").Trim().ToLowerInvariant();
        DateTimeOffset now = clock.UtcNow;
        DateTimeOffset updatedAt = request.UpdatedAt ?? now;

        var existing = await dbContext.UserOnboardingPreferences
            .FirstOrDefaultAsync(p => p.UserId == userId && p.Persona == persona, ct);

        if (existing is null)
        {
            existing = new UserOnboardingPreference
            {
                UserId = userId,
                Persona = persona,
                CurrentStep = request.CurrentStep,
                IsActive = request.IsActive,
                Disabled = request.Disabled,
                EventsJson = request.EventsJson ?? "[]",
                UpdatedAt = updatedAt,
                CreatedAt = now,
                CreatedBy = userId,
                ReasonForChange = "Updated user onboarding preferences",
                VersionIndex = 1
            };
            dbContext.UserOnboardingPreferences.Add(existing);
        }
        else
        {
            if (updatedAt >= existing.UpdatedAt)
            {
                existing.CurrentStep = request.CurrentStep;
                existing.IsActive = request.IsActive;
                existing.Disabled = request.Disabled;
                existing.EventsJson = request.EventsJson ?? "[]";
                existing.UpdatedAt = updatedAt;
                existing.ReasonForChange = "Updated user onboarding preferences";
                existing.VersionIndex++;
            }
        }

        await dbContext.SaveChangesAsync(ct);

        return TypedResults.Ok(new UserOnboardingPreferenceDto(
            Persona: existing.Persona,
            CurrentStep: existing.CurrentStep,
            IsActive: existing.IsActive,
            Disabled: existing.Disabled,
            EventsJson: existing.EventsJson,
            UpdatedAt: existing.UpdatedAt
        ));
    }
}
