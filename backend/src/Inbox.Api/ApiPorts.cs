namespace Inbox.Api;

/// <summary>Kestrel listens on both ports (plan §3). Only the webhook port is tunnelled by ngrok (D-020).</summary>
internal static class ApiPorts
{
    public const int Internal = 8080;
    public const int Webhook = 8081;
}
