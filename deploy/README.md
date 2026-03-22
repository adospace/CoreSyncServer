# CoreSyncServer — Docker Deployment Guide

This guide walks you through deploying CoreSyncServer with Docker Compose. By the end you will have a running instance with a PostgreSQL database, a web dashboard for managing sync configurations, and API endpoints that your applications can call to synchronize their databases.

---

## Table of Contents

1. [Prerequisites](#prerequisites)
2. [Quick Start](#quick-start)
3. [Configuration](#configuration)
4. [Accessing the Dashboard](#accessing-the-dashboard)
5. [Setting Up Your First Sync](#setting-up-your-first-sync)
6. [Sync API Reference](#sync-api-reference)
7. [HTTPS Setup](#https-setup)
8. [Email Notifications](#email-notifications)
9. [Maintenance & Monitoring](#maintenance--monitoring)
10. [Backup & Restore](#backup--restore)
11. [Updating](#updating)
12. [Troubleshooting](#troubleshooting)

---

## Prerequisites

- **Docker Engine** 20.10+ and **Docker Compose** v2+
- Ports **8080** (HTTP) and **5432** (PostgreSQL) available on the host
- At least **512 MB RAM** and **1 GB disk** for a minimal deployment

---

## Quick Start

```bash
# 1. Clone the repository
git clone --recurse-submodules https://github.com/user/CoreSyncServer.git
cd CoreSyncServer/deploy

# 2. Create your environment file
cp .env.example .env

# 3. Edit .env and set a strong password
#    POSTGRES_PASSWORD=YourStrongPassword123!

# 4. Build and start
docker compose up -d

# 5. Verify both containers are healthy
docker compose ps
```

CoreSyncServer is now running at **http://localhost:8080**.

---

## Configuration

### Environment Variables

All configuration can be set via environment variables using the ASP.NET Core double-underscore convention (`Section__Key`).

| Variable | Default | Description |
|---|---|---|
| `POSTGRES_PASSWORD` | `ChangeMeNow!` | PostgreSQL password (shared by db and app) |
| `ConnectionStrings__DefaultConnection` | *(set in compose)* | Full PostgreSQL connection string |
| `ASPNETCORE_ENVIRONMENT` | `Production` | Runtime environment |
| `Monitor__IntervalMinutes` | `5` | How often data store connectivity is checked |
| `Maintenance__IntervalMinutes` | `60` | How often cleanup tasks run |
| `Maintenance__DiagnosticRetentionHours` | `24` | Hours to keep diagnostic records |
| `Maintenance__SyncTraceRetentionHours` | `24` | Hours to keep sync trace logs |
| `Maintenance__SyncSessionRetentionDays` | `7` | Days to keep sync session history |
| `Smtp__Enabled` | `false` | Enable email notifications |
| `Smtp__Host` | — | SMTP server hostname |
| `Smtp__Port` | `587` | SMTP port |
| `Smtp__FromAddress` | — | Sender email address |
| `Smtp__ToAddress` | — | Recipient email address |
| `Smtp__Username` | — | SMTP username |
| `Smtp__Password` | — | SMTP password |
| `Smtp__EnableSsl` | `true` | Use TLS for SMTP |

### Customizing the Compose File

The provided `docker-compose.override.yml` contains commented examples for HTTPS, SMTP, and maintenance tuning. Uncomment the sections you need.

---

## Accessing the Dashboard

Open **http://localhost:8080** in your browser.

### Default Credentials

| Field | Value |
|---|---|
| Username | `admin` |
| Password | `admin` |

> **Important:** Change the admin password immediately after your first login via **Settings > Profile**.

---

## Setting Up Your First Sync

Once logged in, follow these steps to configure database synchronization:

### Step 1 — Create a Project

1. Navigate to **Projects** in the sidebar.
2. Click **New Project** and give it a name (e.g., "My App").
3. A project is an organizational container that groups related data stores and endpoints.

### Step 2 — Add a Data Store

1. Inside your project, go to **Data Stores** and click **New Data Store**.
2. Choose the database type:
   - **SQLite** — provide the file path accessible from the CoreSyncServer container.
   - **SQL Server** — provide a connection string (e.g., `Server=host;Database=MyDb;User Id=sa;Password=...;TrustServerCertificate=True`). Choose between **Triggers** or **Change Tracking** mode.
   - **PostgreSQL** — provide a connection string (e.g., `Host=host;Database=MyDb;Username=user;Password=...`).
3. Save the data store.

> **Network note:** If the target database runs on the Docker host, use `host.docker.internal` as the hostname. If it runs in another Docker network, connect both containers to the same network.

### Step 3 — Create a Configuration

1. Open your data store and go to **Configurations**.
2. Click **New Configuration**. CoreSyncServer will introspect the database and discover tables.
3. For each table, configure:
   - **Sync Mode** — Upload only, Download only, or Bidirectional.
   - **Custom queries** — optional overrides for select/insert/update/delete operations.
   - **Filter parameters** — pass authenticated user claims as query parameters.
4. Save the configuration.

### Step 4 — Create and Publish an Endpoint

1. Inside the configuration, go to **Endpoints** and click **New Endpoint**.
2. Configure authentication (or leave open for testing):
   - **None** — no authentication required.
   - **Basic** — set a username and password.
   - **API Key** — generate or set a static key.
   - **JWT/JWKS** — provide a JWKS URL and issuer for token validation.
3. **Publish** the endpoint. Only published endpoints accept sync requests.
4. Note the **Endpoint ID** (a GUID) — clients use this to connect.

### Step 5 — Sync from a Client Application

Your client application uses the CoreSync HTTP client library to synchronize. The sync URL is:

```
http://localhost:8080/api/sync/{endpoint-id}
```

---

## Sync API Reference

All sync endpoints are under `api/sync/{endpointId}`. Include authentication headers as configured on the endpoint.

### Authentication Headers

| Method | Header |
|---|---|
| Basic | `Authorization: Basic <base64(username:password)>` |
| API Key | `X-Api-Key: <your-key>` |
| JWT | `Authorization: Bearer <token>` |

### Endpoints

| Method | Path | Description |
|---|---|---|
| `GET` | `store-id` | Returns the unique store identifier |
| `GET` | `sync-version` | Returns the current sync version number |
| `POST` | `save-version/{storeId}/{version}` | Saves the last synced version for resume |
| `GET` | `changes-bulk/{storeId}` | Initiates a download session, returns change set metadata |
| `GET` | `changes-bulk-item/{sessionId}/{skip}/{take}` | Streams changes as JSON (paginated) |
| `GET` | `changes-bulk-item-binary/{sessionId}/{skip}/{take}` | Streams changes as MessagePack binary |
| `POST` | `changes-bulk-begin` | Starts an upload session with change set metadata |
| `POST` | `changes-bulk-item` | Uploads a batch of changes (JSON) |
| `POST` | `changes-bulk-item-binary` | Uploads a batch of changes (MessagePack) |
| `POST` | `changes-bulk-complete/{sessionId}` | Finalizes the upload session (JSON) |
| `POST` | `changes-bulk-complete-binary/{sessionId}` | Finalizes the upload session (binary) |

### Example: Download Changes (curl)

```bash
ENDPOINT_ID="your-endpoint-guid"
STORE_ID="your-local-store-guid"
BASE_URL="http://localhost:8080/api/sync/$ENDPOINT_ID"

# Authenticate with API key
AUTH_HEADER="X-Api-Key: your-api-key"

# 1. Get changes metadata
RESPONSE=$(curl -s -H "$AUTH_HEADER" "$BASE_URL/changes-bulk/$STORE_ID")
echo "$RESPONSE"

# 2. Fetch change items (session ID is in the response)
SESSION_ID=$(echo "$RESPONSE" | jq -r '.sessionId')
curl -s -H "$AUTH_HEADER" "$BASE_URL/changes-bulk-item/$SESSION_ID/0/100"
```

### Example: Upload Changes (curl)

```bash
# 1. Begin upload session
curl -s -X POST -H "$AUTH_HEADER" -H "Content-Type: application/json" \
  -d '{"changes":[]}' "$BASE_URL/changes-bulk-begin"

# 2. Send change batches
curl -s -X POST -H "$AUTH_HEADER" -H "Content-Type: application/json" \
  -d '[{"tableName":"MyTable","values":{"Id":1,"Name":"test"},"changeType":1}]' \
  "$BASE_URL/changes-bulk-item"

# 3. Complete session
curl -s -X POST -H "$AUTH_HEADER" "$BASE_URL/changes-bulk-complete/$SESSION_ID"
```

> **Note:** In production, use the CoreSync HTTP client library instead of raw HTTP calls. The library handles session management, pagination, binary serialization, and conflict resolution automatically.

---

## HTTPS Setup

To enable HTTPS, place your PFX certificate in a `certs/` directory and uncomment the HTTPS section in `docker-compose.override.yml`:

```bash
mkdir certs
cp /path/to/your/cert.pfx certs/
```

Then update the override file with your certificate password and start with both compose files:

```bash
docker compose -f docker-compose.yml -f docker-compose.override.yml up -d
```

The application will be available on **https://localhost:8081**.

### Using a Reverse Proxy

For production, it is recommended to use a reverse proxy (nginx, Caddy, Traefik) that terminates TLS and forwards to CoreSyncServer on port 8080:

```
                     ┌──────────────┐
  Internet ──HTTPS──▶│ Reverse Proxy│──HTTP──▶ CoreSyncServer:8080
                     └──────────────┘
```

---

## Email Notifications

CoreSyncServer can send email alerts for data store connectivity issues. Enable SMTP by setting the environment variables in your `.env` or compose override:

```yaml
environment:
  - Smtp__Enabled=true
  - Smtp__Host=smtp.example.com
  - Smtp__Port=587
  - Smtp__FromAddress=noreply@yourdomain.com
  - Smtp__ToAddress=admin@yourdomain.com
  - Smtp__Username=your-smtp-user
  - Smtp__Password=your-smtp-password
  - Smtp__EnableSsl=true
```

---

## Maintenance & Monitoring

CoreSyncServer runs two background services:

- **Monitor** — checks data store connectivity at the configured interval (default: every 5 minutes). Alerts are shown in the dashboard and optionally sent via email.
- **Maintenance** — cleans up old sync sessions, traces, and diagnostics based on retention settings.

### Viewing Logs

```bash
# Application logs
docker compose logs -f coresyncserver

# Database logs
docker compose logs -f postgres
```

### Health Check

```bash
curl -s http://localhost:8080/health
```

---

## Backup & Restore

### Backup the PostgreSQL Database

```bash
docker compose exec postgres pg_dump -U coresync CoreSyncServer > backup_$(date +%Y%m%d).sql
```

### Restore from Backup

```bash
docker compose exec -T postgres psql -U coresync CoreSyncServer < backup_20260322.sql
```

### Backup the Docker Volume

```bash
docker run --rm -v deploy_pgdata:/data -v $(pwd):/backup alpine \
  tar czf /backup/pgdata_backup.tar.gz -C /data .
```

---

## Updating

```bash
# Pull latest code
git pull --recurse-submodules

# Rebuild and restart
cd deploy
docker compose build
docker compose up -d
```

Database migrations are applied automatically on startup.

---

## Troubleshooting

### Container fails to start

```bash
# Check logs for errors
docker compose logs coresyncserver
```

Common causes:
- PostgreSQL not ready yet — the health check should handle this, but on slow systems increase the `retries` count.
- Wrong `POSTGRES_PASSWORD` — ensure `.env` matches what PostgreSQL was initialized with. If the password changed after first run, delete the volume: `docker compose down -v` and start fresh.

### Cannot connect to a data store

- If the data store is on the Docker host, use `host.docker.internal` as the hostname.
- If the data store is in another Docker container, make sure both containers share the same Docker network.
- Check firewall rules on the database server.

### Database migrations fail

Migrations run automatically. If they fail:
```bash
docker compose logs coresyncserver | grep -i migration
```
Ensure the PostgreSQL user has permissions to create tables and indexes.

### Reset to a clean state

```bash
docker compose down -v   # removes containers AND volumes
docker compose up -d     # fresh start with empty database
```
