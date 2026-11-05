using Inbox.Domain.Common;

namespace Inbox.Domain.Messages;

/// <summary>The text of a message: not blank and at most <see cref="MaxLength"/> UTF-16 code units (Telegram's limit).</summary>
public sealed record MessageText
{
    public const int MaxLength = 4096;

    private MessageText(string value) => Value = value;

    public string Value { get; }

    public static MessageText Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException("Message text must not be blank.");
        }

        if (value.Length > MaxLength)
        {
            throw new DomainException($"Message text must be at most {MaxLength} characters.");
        }

        return new MessageText(value);
    }

    public override string ToString() => Value;
}
