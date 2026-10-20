using System;
using System.ComponentModel;
using System.Globalization;

namespace Cadence.Kernel.Serialization;

/// <summary>
/// Generic TypeConverter for clinical kernel entity identifiers, enabling seamless binding in ASP.NET Core Minimal APIs and MVC.
/// </summary>
public abstract class EntityIdTypeConverter<TId> : TypeConverter
    where TId : struct, IEntityId<TId>
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || sourceType == typeof(Guid) || base.CanConvertFrom(context, sourceType);

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        return value switch
        {
            string str when TId.TryParse(str, culture, out var id) => id,
            string str when string.IsNullOrWhiteSpace(str) => TId.Empty,
            Guid guid => TId.FromGuid(guid),
            _ => base.ConvertFrom(context, culture, value)
        };
    }

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
        destinationType == typeof(string) || destinationType == typeof(Guid) || base.CanConvertTo(context, destinationType);

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        if (value is TId id)
        {
            if (destinationType == typeof(string))
            {
                return id.ToString();
            }

            if (destinationType == typeof(Guid))
            {
                return id.Value;
            }
        }

        return base.ConvertTo(context, culture, value, destinationType);
    }
}
