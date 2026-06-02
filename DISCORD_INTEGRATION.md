# Discord Integration Guide

This guide covers two approaches for sending messages from the Pokémon Draft app to a Discord server.

---

## Who Is the Message "From"?

### Webhooks
Messages appear from the **webhook's configured name and avatar** — whatever you set when you created the webhook in Discord (e.g. "PokéDraft Bot"). It is **not** linked to any real Discord user account. You can also override the display name and avatar URL per-message inside the JSON payload.

### Bot
Messages appear from your **Discord bot's account** (the name and avatar you set on the bot in the Discord Developer Portal). Bots can also send DMs, react to messages, respond to slash commands, and appear in the member list.

---

## Option A: Incoming Webhook (Recommended to Start)

Webhooks are built into Discord — no bot hosting, no OAuth, no gateway connection. You create a URL in Discord and POST JSON to it from your backend.

### 1. Create the Webhook in Discord

1. Open the Discord server where you want messages to appear.
2. Click the gear icon on the target channel → **Integrations** → **Webhooks**.
3. Click **New Webhook**, give it a name (e.g. `PokéDraft`) and optionally upload an avatar image.
4. Click **Copy Webhook URL**. It looks like:
   ```
   https://discord.com/api/webhooks/1234567890/abcdefghijk...
   ```
5. Keep this URL secret — anyone with it can post to your channel.

### 2. Store the URL Securely

Add it to your environment/secrets (never commit it to git):

**`appsettings.Development.json`** (local only, already gitignored — double-check `.gitignore`):
```json
{
  "Discord": {
    "WebhookUrl": "https://discord.com/api/webhooks/YOUR_ID/YOUR_TOKEN"
  }
}
```

**Production (Azure / Fly.io):** Set it as an environment variable or app secret:
- Azure App Service: **Configuration → Application Settings** → add `Discord__WebhookUrl`
- Fly.io: `fly secrets set Discord__WebhookUrl="https://discord.com/api/webhooks/..."`

### 3. Add a DiscordService to the Backend

Create `pokemon-draft/Services/DiscordService.cs`:

```csharp
using System.Text;
using System.Text.Json;

namespace PokemonDraft.Services;

public interface IDiscordService
{
    Task SendAsync(string message);
}

public class DiscordService(HttpClient http, IConfiguration config) : IDiscordService
{
    private readonly string? _webhookUrl = config["Discord:WebhookUrl"];

    public async Task SendAsync(string message)
    {
        if (string.IsNullOrWhiteSpace(_webhookUrl)) return;

        var payload = JsonSerializer.Serialize(new { content = message });
        var content = new StringContent(payload, Encoding.UTF8, "application/json");
        await http.PostAsync(_webhookUrl, content);
    }
}
```

Register it in `Program.cs`:

```csharp
builder.Services.AddHttpClient<IDiscordService, DiscordService>();
```

### 4. Inject and Call It

Inject `IDiscordService` wherever you want to fire a notification. For example, in `DraftController.cs` after a pick is made:

```csharp
[HttpPost("{code}/pick")]
public async Task<IActionResult> MakePick(string code, MakePickRequest req)
{
    var (success, error, completed) = LeagueService.MakePick(code, req.PlayerId, req.Pin, req.PokemonId);
    if (!success) return BadRequest(error);

    await discord.SendAsync($"🎯 **{req.PlayerId}** just drafted Pokémon #{req.PokemonId} in league `{code}`!");

    if (completed)
        await discord.SendAsync($"✅ Draft complete in league `{code}`!");

    await BroadcastLeague(code);
    return Ok();
}
```

### 5. Richer Messages with Embeds

Instead of plain `content`, you can send a Discord embed for formatted output:

```csharp
var payload = new
{
    embeds = new[]
    {
        new
        {
            title = "Draft Pick Made!",
            description = $"**{playerName}** drafted **{pokemonName}**",
            color = 0x3498db, // blue
            footer = new { text = $"League: {leagueCode}" },
            timestamp = DateTime.UtcNow.ToString("o")
        }
    }
};
```

### Supported Events You Could Notify

| Event | Suggested Message |
|---|---|
| Draft pick made | 🎯 Player drafted Pokémon X |
| Draft complete | ✅ Draft is over, rosters locked |
| Trade proposed | 🔄 Player A proposed a trade to Player B |
| Trade accepted | ✅ Trade accepted between A and B |
| Trade rejected | ❌ Trade rejected |
| Match result reported | 🏆 Player A beat Player B 2–1 in Week 3 |
| New player joined | 👋 Player X joined the league |

---

## Option B: Discord Bot

A bot gives you much more power: slash commands, DMs, embeds, reactions, and the ability to interact bidirectionally with users. The tradeoff is that you need to host the bot as a separate process (or add it to your existing backend).

### 1. Create the Bot Application

1. Go to [https://discord.com/developers/applications](https://discord.com/developers/applications).
2. Click **New Application**, give it a name.
3. Go to **Bot** → **Add Bot**.
4. Copy the **Bot Token** — treat it like a password.
5. Under **Privileged Gateway Intents**, enable any intents your bot needs (e.g. **Message Content Intent** if reading messages).
6. Go to **OAuth2 → URL Generator**, select `bot` scope and the permissions you need (e.g. **Send Messages**, **Embed Links**).
7. Use the generated URL to invite the bot to your server.

### 2. Store the Token Securely

```json
// appsettings (dev only — use secrets/env vars in production)
{
  "Discord": {
    "BotToken": "YOUR_BOT_TOKEN",
    "ChannelId": "1234567890123456789"
  }
}
```

> ⚠️ Never commit the bot token to git.

### 3. Choose a Library

The most popular .NET Discord library is **Discord.Net**:

```bash
dotnet add package Discord.Net
```

### 4. Create a Bot Service

Create `pokemon-draft/Services/DiscordBotService.cs`:

```csharp
using Discord;
using Discord.WebSocket;

namespace PokemonDraft.Services;

public interface IDiscordBotService
{
    Task SendToChannelAsync(string message);
}

public class DiscordBotService(IConfiguration config, ILogger<DiscordBotService> logger)
    : IDiscordBotService, IHostedService
{
    private readonly DiscordSocketClient _client = new();
    private readonly string _token = config["Discord:BotToken"] ?? string.Empty;
    private readonly ulong _channelId = ulong.Parse(config["Discord:ChannelId"] ?? "0");

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_token)) return;

        _client.Log += msg =>
        {
            logger.LogInformation("[Discord] {Msg}", msg.ToString());
            return Task.CompletedTask;
        };

        await _client.LoginAsync(TokenType.Bot, _token);
        await _client.StartAsync();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _client.LogoutAsync();
        await _client.StopAsync();
    }

    public async Task SendToChannelAsync(string message)
    {
        if (_client.GetChannel(_channelId) is IMessageChannel channel)
            await channel.SendMessageAsync(message);
    }
}
```

Register it in `Program.cs`:

```csharp
builder.Services.AddSingleton<IDiscordBotService, DiscordBotService>();
builder.Services.AddHostedService(sp => (DiscordBotService)sp.GetRequiredService<IDiscordBotService>());
```

Then inject `IDiscordBotService` into your controllers the same way as Option A.

### Bot vs. Webhook Comparison

| Feature | Webhook | Bot |
|---|---|---|
| Setup complexity | Very low | Medium |
| Hosting required | No | Yes (or integrate into API) |
| Send messages | ✅ | ✅ |
| Embeds | ✅ | ✅ |
| DM users | ❌ | ✅ |
| Slash commands | ❌ | ✅ |
| Read messages | ❌ | ✅ |
| React to messages | ❌ | ✅ |
| Appears in member list | ❌ | ✅ |
| Who message is "from" | Webhook name (configurable) | Bot account |

---

## Recommendation

**Start with Option A (Webhooks).**
It requires no additional infrastructure, takes about 20 minutes to integrate end-to-end, and covers all the basic notification use cases (picks, trades, results). If you later need slash commands or DMs (e.g. tagging a specific Discord user when it's their draft turn), you can migrate to Option B without changing your event-triggering logic.
