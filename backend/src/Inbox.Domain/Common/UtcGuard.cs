namespace Inbox.Domain.Common;

/// <summary>Every instant in the domain is a UTC <see cref="DateTimeOffset"/> (ADR-001).</summary>
internal static class UtcGuard
{
    public static DateTimeOffset EnsureUtc(DateTimeOffset value, string name) =>
        value.Offset == TimeSpan.Zero
            ? value
            : throw new DomainException($"{name} must be expressed in UTC (offset +00:00).");
}
