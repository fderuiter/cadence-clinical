using System.Collections.Generic;
using System.Text.Json;

namespace Cadence.Kernel.Standards;

/// <summary>
/// Validates the content of one kind of Metadata Registry library item before it is drafted. A module registers one
/// per kind whose content has a schema (for example rule packs, <c>Cadence.Rules</c>); content of a kind with no
/// validator only has to be a JSON object. It lives in Abstractions so modules that reference only Abstractions can
/// provide one (K-XS-2).
/// </summary>
public interface ILibraryContentValidator
{
    LibraryItemKind Kind { get; }

    /// <summary>Every problem with <paramref name="content"/>; empty when it is valid.</summary>
    IReadOnlyList<string> Validate(JsonElement content);
}
