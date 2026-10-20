using Microsoft.Extensions.AI;

namespace Cadence.Modules.Intelligence.Inference;

/// <summary>Adds one entry to an optional <see cref="AdditionalPropertiesDictionary"/>.</summary>
internal static class PropertyBag
{
    public static AdditionalPropertiesDictionary With(AdditionalPropertiesDictionary? properties, string key, object? value)
    {
        var bag = properties ?? [];
        bag[key] = value;
        return bag;
    }
}
