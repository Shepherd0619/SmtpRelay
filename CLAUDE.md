# CLAUDE.md

This file provides guidance to Claude Code when working in this repository.

## Build & Test

```bash
dotnet build Backend          # Build
dotnet run --project Backend  # Run (HTTP :5162 + SMTP :25)
dotnet test                   # Run tests (when added)
```

Build must produce 0 warnings and 0 errors.

## Project Structure

```
Backend/
  Configuration/    — Typed IOptions<T> config classes bound to "SmtpRelay" section
  Models/           — Domain types: RelayMessage, ChannelDeliveryResult, IncomingWebhookPayload
  Services/
    Queue/          — IQueueService, SqliteQueueService, QueueProcessorService (BackgroundService)
    Output/         — IOutputHandler + GraphApi/Smtp/Webhook output handlers
  SmtpServer/       — SmtpServer integration: MailboxFilter, MessageStore, HostedService
  Controllers/      — Health, Webhook, Admin, Metrics
  Extensions/       — ServiceCollectionExtensions (DI registration)
  GlobalUsings.cs   — Aliases to avoid namespace collision with SmtpServer NuGet package
  Program.cs        — Minimal API host, queue init on startup
```

## Architecture

Inbound email flow:
1. SmtpServer (HostedService) receives SMTP connection
2. RelayMessageStore.SaveAsync() copies the raw bytes, returns OK immediately (fire-and-forget)
3. EmailProcessingService parses MIME with MimeKit, enqueues RelayMessage into SQLite
4. QueueProcessorService (BackgroundService) polls queue every N seconds
5. For each pending message, all enabled IOutputHandlers deliver in parallel
6. Failed messages retry up to MaxRetryCount, then dead-letter

Output channels are independent — each implements `IOutputHandler` with `ChannelName`, `IsEnabled`, and `DeliverAsync()`.

## SmtpServer Namespace Collision

The project namespace `SmtpRelay.App.SmtpServer` collides with the `SmtpServer` NuGet package. Global using aliases in `GlobalUsings.cs` resolve this:

```csharp
global using SmtpServerApi = SmtpServer;           // ISessionContext, IMessageTransaction, SmtpServerOptionsBuilder, SmtpServer class
global using SmtpServerMail = SmtpServer.Mail;     // IMailbox
global using SmtpServerProtocol = SmtpServer.Protocol;  // SmtpResponse, SmtpReplyCode
global using SmtpServerStorage = SmtpServer.Storage;    // IMessageStore, MessageStore, IMailboxFilter
```

Always use the alias prefix (e.g. `SmtpServerStorage.IMessageStore`) inside any file within the `SmtpRelay.App.SmtpServer` namespace. Outside that namespace, use `using SmtpServer.Storage;` directly.

## Key Design Decisions

- **SQLite, not Redis/RabbitMQ** — zero external deps for the queue, survives restarts
- **Fire-and-forget in MessageStore** — SMTP protocol requires fast ACK, delivery is async
- **All-channels delivery** — each enabled handler runs per message, results tracked in `ChannelResults` dictionary
- **Channel-level retry** — QueueProcessorService retries each failing channel 3× with exponential backoff before reporting failure back to the queue
- **Queue-level retry** — If any channels fail, the queue service increments `RetryCount`; after `MaxRetryCount` (default 5) the message is dead-lettered
- **Configuration via env vars** — standard .NET `IOptions<T>` pattern with `__` separator: `SmtpRelay__GraphApi__Enabled=true`

## Adding a New Output Channel

1. Implement `IOutputHandler` with `ChannelName`, `IsEnabled`, and `DeliverAsync()`
2. Read config from `IOptions<SmtpRelayConfig>`
3. Register in `ServiceCollectionExtensions.AddSmtpRelay()`:
   ```csharp
   services.AddSingleton<IOutputHandler, MyNewHandler>();
   ```

## Configuration Schema

All config lives under the `SmtpRelay` section. See `appsettings.json` for the full schema. Environment variables use `__` as separator: `SmtpRelay__SmtpServer__Port=2525`.
