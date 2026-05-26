# SmtpRelay

Production-ready SMTP relay service for .NET 8. Receives emails via SMTP and delivers them through all enabled output channels — Microsoft Graph API (Outlook), downstream SMTP relay, or custom webhook endpoints.

## Purpose
- **OAuth2 SMTP Relay**: Provides a workaround for homelab notifications when your email provider has deprecated traditional SMTP authentication (e.g., App Passwords, basic auth) in favor of modern OAuth2.
- **Event-Driven Workflows**: Enables automated triggering of custom workflows based on inbound and outbound email events.
- **Bring your own solution**: Offers the flexibility to process, route, or handle email payloads using your preferred downstream tools.

## Quick Start

```bash
# Run locally
dotnet run --project Backend

# Run with Docker
docker compose up
```

| Port | Protocol | Description |
|------|----------|-------------|
| 5162 | HTTP | REST API (Swagger at `/swagger`) |
| 25   | SMTP | Inbound SMTP server |
| 2525 | SMTP | Inbound SMTP (Docker, avoids root) |
| 8080 | HTTP | REST API (Docker) |

## API Endpoints

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/api/health` | Health check |
| `GET` | `/api/health/ready` | Readiness check |
| `GET` | `/api/metrics` | Queue depth and uptime |
| `GET` | `/api/admin/queue/stats` | Queue statistics |
| `GET` | `/api/admin/queue/messages?skip=0&take=50` | List queued messages |
| `POST` | `/api/admin/queue/messages/{id}/retry` | Retry a failed message |
| `DELETE` | `/api/admin/queue/messages/{id}` | Delete a message |
| `POST` | `/api/admin/queue/cleanup?retentionDays=7` | Purge old delivered messages |
| `POST` | `/api/webhook/email` | Ingest email via HTTP |

### Incoming Webhook

```bash
curl -X POST http://localhost:5162/api/webhook/email \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer your-api-key" \
  -d '{
    "from": "sender@example.com",
    "to": ["recipient@example.com"],
    "subject": "Hello",
    "body": "Plain text body",
    "bodyHtml": "<h1>HTML body</h1>",
    "attachments": [
      {
        "fileName": "report.pdf",
        "contentType": "application/pdf",
        "contentBase64": "..."
      }
    ]
  }'
```

## Architecture

```
Email Sender ──SMTP──▶ SmtpServer ──▶ SQLite Queue ──▶ QueueProcessor ──┬── Graph API (Outlook)
                      HostedService       (persistent)    BackgroundSvc  ├── SMTP Relay
                                                                         └── Webhook Out

Webhook Caller ──HTTP──▶ WebhookController ──▶ Queue ──▶ QueueProcessor ──▶ ...
```

Each received email is delivered through **all enabled output channels** simultaneously. Results are tracked per-channel. Failed messages are retried with exponential backoff up to the configured max retry count, then dead-lettered.

## Configuration

All settings live under the `SmtpRelay` key in `appsettings.json`. Override with environment variables using `__` separator:

```yaml
SmtpRelay__GraphApi__Enabled=true
SmtpRelay__GraphApi__TenantId=...
```

## Output Channel Considerations
### SMTP Server

| Key | Default | Description |
|-----|---------|-------------|
| `SmtpServer:Enabled` | `true` | Enable inbound SMTP |
| `SmtpServer:Port` | `25` | SMTP listen port |
| `SmtpServer:HostName` | `smtp-relay.local` | SMTP banner hostname |
| `SmtpServer:MaxMessageSizeMB` | `25` | Max message size |
| `SmtpServer:AllowedRecipientDomains` | `*` | Comma-separated domain allowlist |
| `SmtpServer:Auth:Username` | | SMTP AUTH username |
| `SmtpServer:Auth:Password` | | SMTP AUTH password |

### Graph API (Outlook)

| Key | Description |
|-----|-------------|
| `GraphApi:Enabled` | Enable Outlook delivery |
| `GraphApi:TenantId` | Azure AD tenant ID |
| `GraphApi:ClientId` | App registration client ID |
| `GraphApi:ClientSecret` | App registration secret |
| `GraphApi:UserId` | Target mailbox (user ID or UPN) |
| `GraphApi:SaveToSentItems` | Save to Sent Items folder |
| `GraphApi:CreateDraftsBeforeSending` | Create draft then send (matches n8n behavior) |

When using **application permissions** (client credentials), the Graph API can only send from **user mailboxes and shared mailboxes**. Attempting to set `From` to an M365 group address will fail — this is a Microsoft Graph API limitation, not a bug in this project. Groups and distribution lists are not represented as user objects in Exchange Online and cannot be used as senders with app-only auth.

| Sender Type | Graph API | SMTP Output |
|-------------|-----------|-------------|
| User mailbox | Supported | Supported |
| Shared mailbox | Supported | Supported |
| M365 Group | **Not supported** | Supported (SendAs required) |
| Distribution list | **Not supported** | Not supported |

**For M365 groups**, use the **SMTP output channel** instead. Configure it with Exchange Online's SMTP endpoint (`smtp.office365.com:587`, STARTTLS) and credentials that have `SendAs` permission on the group. SMTP AUTH respects traditional mailbox delegation, unlike Graph API app-only auth.

For Graph API permission, you must grant `Mail.Send` (and optionally `Group.Read.All` for runtime group resolution).

### SMTP Output (Relay)

| Key | Description |
|-----|-------------|
| `SmtpOutput:Enabled` | Enable SMTP relay |
| `SmtpOutput:Host` | Downstream SMTP host |
| `SmtpOutput:Port` | Downstream SMTP port (default 587) |
| `SmtpOutput:UseSsl` | Use STARTTLS |
| `SmtpOutput:Username` | Auth username |
| `SmtpOutput:Password` | Auth password |
| `SmtpOutput:FromAddress` | Rewrite envelope sender |

### Webhooks

| Key | Description |
|-----|-------------|
| `WebhookOutput:Enabled` | Enable outgoing webhooks |
| `WebhookOutput:Targets` | Array of `{ Name, Url, Headers }` objects |
| `WebhookInput:Enabled` | Enable incoming webhook endpoint |
| `WebhookInput:ApiKey` | Bearer token for incoming webhook auth |

### Queue

| Key | Default | Description |
|-----|---------|-------------|
| `Queue:DatabasePath` | `Data/queue.db` | SQLite database path |
| `Queue:MaxRetryCount` | `5` | Retries before dead-letter |
| `Queue:ProcessingIntervalSeconds` | `5` | Polling interval |
| `Queue:MaxConcurrentProcessing` | `1` | Max concurrent delivery batches |
| `Queue:RetentionDays` | `7` | Auto-delete delivered messages after N days |

## Docker

```yaml
services:
  smtp-relay:
    build:
      context: .
      dockerfile: Backend/Dockerfile
    ports:
      - "8080:8080"
      - "2525:25"
    environment:
      - SmtpRelay__SmtpServer__Port=25
      - SmtpRelay__GraphApi__Enabled=true
      - SmtpRelay__GraphApi__TenantId=${GRAPH_TENANT_ID}
      - SmtpRelay__GraphApi__ClientId=${GRAPH_CLIENT_ID}
      - SmtpRelay__GraphApi__ClientSecret=${GRAPH_CLIENT_SECRET}
      - SmtpRelay__GraphApi__UserId=${GRAPH_USER_ID}
    volumes:
      - smtp-data:/app/Data
```

## Requirements

- .NET 8 SDK
- Docker (optional)

## Dependencies

| Package | Purpose |
|---------|---------|
| SmtpServer | Inbound SMTP server |
| MimeKit / MailKit | MIME parsing and SMTP relay |
| Microsoft.Graph + Azure.Identity | Outlook/Office 365 delivery |
| Microsoft.Data.Sqlite | Persistent message queue |
| Polly | Retry policies |
| Swashbuckle | Swagger UI |

## Contribution
We welcome contributions from anyone with an engineering mindset, especially those utilizing AI-assisted development tools like Claude Code.

To respect everyone's time, please adhere to the following guidelines:

- **Pre-Submission Review**: Thoroughly review and test your code locally before opening a pull request.

- **Quality Standards**: Maintain clean, documented, and high-quality code. We reserve the right to close low-quality or spam pull requests without review.
