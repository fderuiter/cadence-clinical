using Cadence.Kernel.Persistence.IdentityVault;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Kernel.Persistence.Migrations;

/// <summary>
/// Adds <c>identity_vault.find_subject_by_blind_index(text, bytea)</c> so tenant invitation and delegation workflows
/// can reuse an existing tenant person by e-mail blind index without exposing plaintext identifiers (ADR-2458, ADR-2611).
/// </summary>
public partial class IdentityVaultBlindIndexLookup : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIdentityVaultBlindIndexLookup();
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIdentityVaultBlindIndexLookup();
    }
}
