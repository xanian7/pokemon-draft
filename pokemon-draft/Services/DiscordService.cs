using System.Text;
using System.Text.Json;

public class DiscordService(HttpClient httpClient, IConfiguration config) : IDiscordService
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly string? _webhookUrl = config["Discord:WebhookUrl"];

    /// <inheritdoc/>
    public async Task SendMessageAsync(string message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_webhookUrl)) return;
        if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("Message cannot be empty.", nameof(message));
        try
        {
            // allowed_mentions lets @user, @&role, @everyone, and @here actually ping.
            // Remove parse entries you don't want to allow.
            var payload = JsonSerializer.Serialize(new
            {
                content = message,
                allowed_mentions = new
                {
                    parse = new[] { "users", "roles", "everyone" }
                }
            });
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            await _httpClient.PostAsync(_webhookUrl, content, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Message sending was canceled.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred while sending the message: {ex.Message}");
        }
    }
}