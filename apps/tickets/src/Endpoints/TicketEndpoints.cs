using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Tickets.Endpoints;

/// <summary>
/// Minimal API endpoint registration for audited support ticket creation, scoped listing, reference lookup,
/// comments, workflow transitions, 21 CFR Part 11 electronic signatures, and audit trails
/// (PRD-TCK-001, PRD-TCK-002, ADR-2610, SRS-SYS-005).
/// @req:PRD-TCK-001
/// @req:PRD-TCK-002
/// @req:PRD-SYS-192
/// @req:SRS-SYS-005
/// </summary>
public static class TicketEndpoints
{
    public const string SignTicketSemanticAction = TicketWorkflowEndpoints.SignTicketSemanticAction;

    public static IEndpointRouteBuilder MapTicketEndpoints(this IEndpointRouteBuilder app)
    {
        TicketCrudEndpoints.MapTicketCrudRoutes(app);
        TicketWorkflowEndpoints.MapTicketWorkflowRoutes(app);
        return app;
    }

    public static string SignTicketAction(string ticketId) =>
        TicketWorkflowEndpoints.SignTicketAction(ticketId);
}
