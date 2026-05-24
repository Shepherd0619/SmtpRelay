# SmtpRelay

Production-ready SMTP relay service for .NET 8. Receives emails via SMTP and delivers them through all enabled output channels — Microsoft Graph API (Outlook), downstream SMTP relay, or custom webhook endpoints.

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
