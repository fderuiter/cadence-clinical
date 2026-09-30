using System;
using System.Collections.Frozen;

namespace Cadence.Kernel.Persistence.IdentityVault;

/// <summary>
/// Column names that would hold a direct identifier (spec §9.2, K-IDN-1). The kernel schema has none: such data is
/// kept only in the IdentityVault. The schema tests check the EF model and the migrated database against this list.
/// </summary>
internal static class DirectIdentifierColumns
{
    private static readonly FrozenSet<string> Names = FrozenSet.Create(
        StringComparer.Ordinal,
        "email", "email_address", "first_name", "last_name", "given_name", "family_name", "full_name", "middle_name",
        "maiden_name", "birth_date", "date_of_birth", "dob", "phone", "phone_number", "mobile", "address", "street_address",
        "postal_code", "zip_code", "mrn", "medical_record_number", "national_id", "ssn", "social_security_number",
        "passport_number", "initials", "screening_number", "site_local_number");

    /// <summary>True when <paramref name="column"/> (snake_case) is named like a direct identifier.</summary>
    public static bool IsIdentifier(string column) => Names.Contains(column);
}
