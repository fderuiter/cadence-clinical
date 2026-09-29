using System;

namespace Cadence.Core.Domain;

/// <summary>
/// Indicates an entity supports soft deletion with query filtering and deletion timestamp tracking.
/// @req:PRD-SYS-001
/// </summary>
public interface ISoftDelete
{
    bool IsDeleted { get; set; }
    DateTimeOffset? DeletedAt { get; set; }
}
