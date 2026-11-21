using System;
using System.Collections.Generic;

namespace Cadence.TestSupport.Database;

/// <summary>
/// The design-time <c>DbContext</c> factory of every service with its own schema. The migration snapshot tests
/// (unit) and the per-schema migration tests (integration) both iterate this list.
/// </summary>
public static class ServiceDbContextFactories
{
    public static IReadOnlyList<Type> All { get; } =
    [
        typeof(Cadence.Apps.Ctms.Infrastructure.Persistence.CtmsDbContextFactory),
        typeof(Cadence.Apps.Econsent.Infrastructure.Persistence.EconsentDbContextFactory),
        typeof(Cadence.Apps.Eisf.Infrastructure.Persistence.EisfDbContextFactory),
        typeof(Cadence.Apps.Etmf.Infrastructure.Persistence.EtmfDbContextFactory),
        typeof(Cadence.Apps.Execution.Infrastructure.Persistence.ExecutionDbContextFactory),
        typeof(Cadence.Apps.Fileshare.Infrastructure.Persistence.FileshareDbContextFactory),
        typeof(Cadence.Apps.Imaging.Infrastructure.Persistence.ImagingDbContextFactory),
        typeof(Cadence.Apps.Interop.Infrastructure.Persistence.InteropDbContextFactory),
        typeof(Cadence.Apps.Knowledge.Infrastructure.Persistence.KnowledgeDbContextFactory),
        typeof(Cadence.Apps.Notifications.Infrastructure.Persistence.NotificationsDbContextFactory),
        typeof(Cadence.Apps.Payments.Infrastructure.Persistence.PaymentsDbContextFactory),
        typeof(Cadence.Apps.Quality.Infrastructure.Persistence.QualityDbContextFactory),
        typeof(Cadence.Apps.Safety.Infrastructure.Persistence.SafetyDbContextFactory),
        typeof(Cadence.Apps.Tickets.Infrastructure.Persistence.TicketsDbContextFactory),
    ];
}
