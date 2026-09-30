using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;

namespace Cadence.Kernel.Identity;

/// <summary>
/// A layer of identity kept in the IdentityVault (spec §9.2, ADR-2253). Each layer has its own reveal capability
/// (K-IDN-1). The pseudonymous <see cref="ParticipantId"/> and <see cref="PersonId"/> are kernel identity; these layers
/// are what they resolve to.
/// </summary>
public enum IdentityLayer
{
    /// <summary>A person's direct identifiers: name, date of birth, contact details. Keyed by <see cref="PersonId"/>.</summary>
    Person,

    /// <summary>A participant's site-local identifier, such as the screening log number. Keyed by <see cref="ParticipantId"/>.</summary>
    SiteLocal,
}

/// <summary>
/// Direct identifiers of one layer. They exist in plaintext only in memory: the vault stores them sealed, commands
/// never serialize them, and <see cref="ToString"/> prints no value, so a log line that formats a command carrying them
/// discloses nothing.
/// </summary>
public sealed partial record IdentityDetails
{
    /// <summary>The longest value any field accepts.</summary>
    public const int MaxFieldLength = 255;

    /// <summary>Given name (person layer).</summary>
    public string? GivenName { get; init; }

    /// <summary>Family name (person layer).</summary>
    public string? FamilyName { get; init; }

    /// <summary>Date of birth (person layer).</summary>
    public DateOnly? BirthDate { get; init; }

    /// <summary>E-mail address (person layer); its blind index supports exact-match lookup.</summary>
    public string? Email { get; init; }

    /// <summary>Telephone number (person layer).</summary>
    public string? Phone { get; init; }

    /// <summary>Site-local identifier such as the screening log number (site-local layer); its blind index supports lookup.</summary>
    public string? SiteLocalNumber { get; init; }

    /// <summary>
    /// The value the layer's blind index is computed over, normalized: the e-mail in lower case for
    /// <see cref="IdentityLayer.Person"/>, the site-local number in upper case for <see cref="IdentityLayer.SiteLocal"/>.
    /// Null when the layer's lookup field is absent.
    /// </summary>
    public string? LookupValue(IdentityLayer layer) => layer switch
    {
        IdentityLayer.Person => Email?.Trim().ToLowerInvariant(),
        IdentityLayer.SiteLocal => SiteLocalNumber?.Trim().ToUpperInvariant(),
        _ => throw new ArgumentOutOfRangeException(nameof(layer), layer, "Unknown identity layer."),
    };

    /// <summary>
    /// The first rule these details break for <paramref name="layer"/>, or null. A layer takes only its own fields,
    /// at least one of them, none blank and none longer than <see cref="MaxFieldLength"/>.
    /// </summary>
    public KernelError? Validate(IdentityLayer layer)
    {
        var personFields = new[] { GivenName, FamilyName, Email, Phone };
        var hasPerson = Array.Exists(personFields, f => f is not null) || BirthDate is not null;
        var hasSiteLocal = SiteLocalNumber is not null;
        var (own, other) = layer switch
        {
            IdentityLayer.Person => (hasPerson, hasSiteLocal),
            IdentityLayer.SiteLocal => (hasSiteLocal, hasPerson),
            _ => throw new ArgumentOutOfRangeException(nameof(layer), layer, "Unknown identity layer."),
        };

        if (other)
        {
            return IdentityVaultErrors.DetailsInvalid;
        }

        if (!own)
        {
            return IdentityVaultErrors.DetailsRequired;
        }

        foreach (var field in (string?[])[.. personFields, SiteLocalNumber])
        {
            if (field is not null && (string.IsNullOrWhiteSpace(field) || field.Length > MaxFieldLength))
            {
                return IdentityVaultErrors.DetailsInvalid;
            }
        }

        return Email is not null && !EmailPattern().IsMatch(Email.Trim()) ? IdentityVaultErrors.DetailsInvalid : null;
    }

    /// <summary>Prints the fields that are present, never their values.</summary>
    public override string ToString() => $"IdentityDetails {{ {string.Join(", ", PresentFields())} }}";

    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append(string.Join(", ", PresentFields()));
        return true;
    }

    private IEnumerable<string> PresentFields()
    {
        if (GivenName is not null) yield return nameof(GivenName);
        if (FamilyName is not null) yield return nameof(FamilyName);
        if (BirthDate is not null) yield return nameof(BirthDate);
        if (Email is not null) yield return nameof(Email);
        if (Phone is not null) yield return nameof(Phone);
        if (SiteLocalNumber is not null) yield return nameof(SiteLocalNumber);
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();
}

/// <summary>
/// The IdentityVault port (spec §9.2, ADR-2253, ADR-2458): the only way to read direct identifiers. Writes are the
/// identity commands (<see cref="RecordPersonIdentity"/>, <see cref="RecordSiteLocalIdentity"/>, the erase and link
/// commands), sent through the kernel pipeline like any other. Every read here first commits an audited reveal command,
/// so a disclosure without an audit entry cannot happen; a denied or failed command discloses nothing.
/// </summary>
public interface IIdentityVault
{
    /// <summary>
    /// Sends <paramref name="command"/> and, once it has committed, decrypts the subject's details. Fails with
    /// <see cref="IdentityVaultErrors.NotRecorded"/> or <see cref="IdentityVaultErrors.Erased"/> when there is nothing
    /// to disclose, and refuses a replayed idempotency key: every disclosure is its own audited activity.
    /// </summary>
    Task<CommandResult<IdentityDetails>> RevealAsync(IIdentityRevealCommand command, CancellationToken cancellationToken);

    /// <summary>
    /// Sends <paramref name="command"/> and, once it has committed, lists the participants linked to the person
    /// (K-IDN-2). A replayed idempotency key is refused.
    /// </summary>
    Task<CommandResult<IReadOnlyList<ParticipantId>>> RevealLinksAsync(RevealPersonLinks command, CancellationToken cancellationToken);
}

/// <summary>A reveal command: one per layer, so each layer has its own capability (K-IDN-1).</summary>
public interface IIdentityRevealCommand : ICommand<IdentityRevealReceipt>
{
    /// <summary>The layer the command reveals.</summary>
    IdentityLayer Layer { get; }

    /// <summary>The pseudonymous id the layer is keyed by.</summary>
    Guid SubjectId { get; }
}

/// <summary>What a committed reveal command returns and stores as its idempotent result: no identifier, only the layer.</summary>
public sealed record IdentityRevealReceipt(IdentityLayer Layer, Guid SubjectId);

/// <summary>Errors raised by the IdentityVault commands.</summary>
public static class IdentityVaultErrors
{
    public static readonly KernelError DetailsRequired =
        new("K.IdentityVault.DetailsRequired", 422, "At least one identifier of the layer is required.");

    public static readonly KernelError DetailsInvalid =
        new("K.IdentityVault.DetailsInvalid", 422, "An identifier is blank, too long, malformed or belongs to another layer.");

    public static readonly KernelError SubjectUnknown =
        new("K.IdentityVault.SubjectUnknown", 404, "The person or participant does not exist in this tenant.");

    public static readonly KernelError ResidencyMismatch =
        new("K.IdentityVault.ResidencyMismatch", 409, "The tenant's data residency does not match this vault's region.");

    public static readonly KernelError Erased =
        new("K.IdentityVault.Erased", 409, "The identity record was erased; it cannot be changed or linked.");

    public static readonly KernelError NotRecorded =
        new("K.IdentityVault.NotRecorded", 404, "No identity is recorded for the subject in this layer.");

    public static readonly KernelError RevealReplayed =
        new("K.IdentityVault.RevealReplayed", 409, "A reveal needs its own idempotency key; each disclosure is audited separately.");

    public static readonly KernelError AlreadyLinked =
        new("K.IdentityVault.AlreadyLinked", 409, "The participant is already linked to another person.");
}
