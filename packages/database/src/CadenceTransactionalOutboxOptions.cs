using System;

using MassTransit;

namespace Cadence.Database;

/// <summary>
/// Configuration options for MassTransit Entity Framework Core Transactional Outbox persistence with PostgreSQL.
/// </summary>
public sealed class CadenceTransactionalOutboxOptions
{
    /// <summary>
    /// Gets or sets the delay between polling queries for pending outbox messages in PostgreSQL. Default is 1 second.
    /// </summary>
    public TimeSpan? QueryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets the maximum number of messages fetched per polling batch. Default is 100.
    /// </summary>
    public int? QueryMessageLimit { get; set; } = 100;

    /// <summary>
    /// Gets or sets the query timeout when polling the outbox table. Default is 30 seconds.
    /// </summary>
    public TimeSpan? QueryTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the window during which duplicate messages are detected. Default is 30 minutes.
    /// </summary>
    public TimeSpan? DuplicateDetectionWindow { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Gets or sets the maximum retry attempt cap for publishing outbox messages. Default is 5.
    /// </summary>
    public int RetryAttemptCap { get; set; } = 5;

    /// <summary>
    /// Gets or sets the retry interval between message publish attempts. Default is 2 seconds.
    /// </summary>
    public TimeSpan RetryInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Optional custom configuration callback for the underlying MassTransit EF Core Outbox configurator.
    /// </summary>
    public Action<IEntityFrameworkOutboxConfigurator>? ConfigureOutbox { get; set; }
}
