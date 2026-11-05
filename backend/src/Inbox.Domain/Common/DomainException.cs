namespace Inbox.Domain.Common;

/// <summary>Raised when a domain invariant or state transition rule is violated.</summary>
public sealed class DomainException(string message) : Exception(message);
