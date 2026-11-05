namespace Inbox.Infrastructure.Telegram;

public sealed class TelegramOptions
{
    public const string Section = "Telegram";
    public static readonly Uri ApiBaseAddress = new("https://api.telegram.org/");
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    public string BotToken { get; set; } = string.Empty;
}
