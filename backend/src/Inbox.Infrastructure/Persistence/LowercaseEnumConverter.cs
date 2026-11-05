using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Inbox.Infrastructure.Persistence;

/// <summary>Stores enums as lowercase strings matching the CHECK constraints (contracts §7.2).</summary>
internal sealed class LowercaseEnumConverter<TEnum>() : ValueConverter<TEnum, string>(
    value => ToProvider(value),
    value => Enum.Parse<TEnum>(value, ignoreCase: true))
    where TEnum : struct, Enum
{
    public static string ToProvider(TEnum value) => value.ToString().ToLowerInvariant();
}
