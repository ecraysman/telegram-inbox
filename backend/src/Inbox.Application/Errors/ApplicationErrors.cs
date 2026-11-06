namespace Inbox.Application.Errors;

/// <summary>Base type for expected use-case failures; hosts map <see cref="Code"/> to a response.</summary>
public abstract class ApplicationErrorException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class ContactNotFoundException(Guid contactId)
    : ApplicationErrorException("contact_not_found", $"Contact {contactId} was not found.");

public sealed class MessageNotFoundException(Guid messageId)
    : ApplicationErrorException("message_not_found", $"Message {messageId} was not found.");

public sealed class MessageNotFailedException(Guid messageId)
    : ApplicationErrorException("message_not_failed", $"Message {messageId} is not in the failed state.");

public sealed class ValidationFailedException(string field, string error)
    : ApplicationErrorException("validation_failed", error)
{
    public string Field { get; } = field;
}

/// <summary>A queue message that can never succeed (unknown id, invalid data). The consumer dead-letters it.</summary>
public sealed class PoisonMessageException(string message) : Exception(message);

/// <summary>
/// A persistence call failed for a reason that is expected to heal (connection lost, timeout, serialization failure).
/// Raised by the persistence adapter so the inner layers can tell outages apart without knowing ADO.NET or EF Core.
/// </summary>
public sealed class TransientPersistenceException(string message, Exception innerException) : Exception(message, innerException);
