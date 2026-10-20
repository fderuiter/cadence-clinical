using System.Text.Json;

using Cadence.Kernel.Evidence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Cadence.Kernel.Persistence.Evidence;

/// <summary>
/// Stores an <see cref="ObservationValue"/> as <c>jsonb</c> in the shape the API and events use (ADR-2623): the
/// <c>type</c> member names the variant. Values are immutable records, so equality is the record's.
/// </summary>
internal static class ObservationValueConversion
{
    // jsonb reorders object members, so the "type" member may come back after the variant's own members.
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { AllowOutOfOrderMetadataProperties = true };

    private static readonly ValueConverter<ObservationValue, string> Converter = new(
        value => JsonSerializer.Serialize(value, Options),
        json => JsonSerializer.Deserialize<ObservationValue>(json, Options)!);

    private static readonly ValueComparer<ObservationValue> Comparer = new(
        (left, right) => left == right,
        value => value.GetHashCode(),
        value => value);

    /// <summary>Maps <paramref name="property"/> to a required <c>jsonb</c> column.</summary>
    public static PropertyBuilder<ObservationValue> IsObservationValue(this PropertyBuilder<ObservationValue> property) =>
        property.HasConversion(Converter, Comparer).HasColumnType("jsonb").IsRequired();
}
