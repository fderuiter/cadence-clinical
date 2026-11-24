using System.Collections.Generic;

namespace Cadence.Apps.Execution.Application.Abstractions;

/// <summary>
/// Encrypts and reveals treatment arms (#5607): randomisation list slots, allocations and kit labels. It holds the
/// randomisation key (<c>RANDOMIZATION_ALLOCATION_KEY</c>), which no other column uses, so a reader of the database or
/// of the demographics or field-encryption keys learns no arm. Every value is bound to a purpose naming its row, so a
/// ciphertext copied to another row fails to reveal. Only randomisation itself and the unblinded subject read
/// (<c>SubjectApplicationService.GetDetailAsync</c> for an unblinded caller) call <see cref="Reveal"/>.
/// @req:PRD-SUB-001
/// </summary>
public interface IAllocationProtector
{
    /// <summary>Encrypts <paramref name="arm"/> for <paramref name="purpose"/>.</summary>
    string Protect(string arm, string purpose);

    /// <summary>
    /// Encrypts a batch of <c>(Arm, Purpose)</c> pairs, reusing one cipher context for the batch while drawing a fresh
    /// random nonce for each slot.
    /// </summary>
    IReadOnlyList<string> ProtectBatch(IReadOnlyList<(string Arm, string Purpose)> items);

    /// <summary>Decrypts a value <see cref="Protect"/> produced for the same <paramref name="purpose"/>; throws on any mismatch or tampering.</summary>
    string Reveal(string ciphertext, string purpose);
}
