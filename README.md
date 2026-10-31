# DDNS

A small, modular dynamic-DNS worker that keeps `A`/`AAAA` records pointed at your
current public IP address. It started as [mittwald-ddns](https://github.com/BethelTraunCode/mittwald-ddns)
and now speaks multiple providers, currently **mittwald** (API v1 and v2) and
**Cloudflare**.

> **Note:** mittwald API v2 support remains less battle-tested than v1.

## Features

- Provider-agnostic core: one `IDnsProvider` interface, one config format.
- mittwald API v1 and v2, Cloudflare DNS.
- Interactive config workspace with provider discovery (list zones/records and pick them).
- Encrypted config file (AES-256-GCM) or plain JSON.
- Daemon with config hot reload, plus a one-shot `check` command.
- Webhook notifications on IP changes and failures (Discord-compatible payload).
- IPv4 and IPv6, depending on the IP service you configure.
- Automatic import of existing `mittwald-ddns` and `dyndns.py` config files.
- Docker image, single-file binaries, tests.

## Requirements

- .NET 10 SDK to build; no runtime needed for the self-contained release binaries.
- A public IP service URL, for example `https://api64.ipify.org` (IPv6 preferred),
  `https://api.ipify.org` (IPv4) or `https://icanhazip.com`.
- Provider credentials:
  - **mittwald v1:** account name/numeric UID + application token (`uuid:secret`).
    Create one with `ddns login --v1 --username r1234`.
  - **mittwald v2:** API token with DNS access. Create one with
    `ddns login --v2 --email you@example.com`.
  - **Cloudflare:** API token with `Zone:Read` and `DNS:Edit` permissions.

## Quick start

```bash
# build
dotnet build Ddns.slnx

# create a provider key (mittwald)
dotnet run --project Ddns -- login --v1 --username r1234

# configure interactively (adds accounts and discovers records)
DDNS_SECRET="$(openssl rand -hex 32)" dotnet run --project Ddns -- config

# run one update cycle
dotnet run --project Ddns -- check

# run the daemon
dotnet run --project Ddns --
```

`DDNS_SECRET` is only needed for encrypted config files. Without it, the
interactive workspace can save plain JSON (it asks first).

## Commands

| Command | Description |
| --- | --- |
| `ddns` | Run the daemon (default command). |
| `ddns check` | Resolve the public IP and update all records once. |
| `ddns config` | Open the interactive config workspace. |
| `ddns config export [--file config.json]` | Print or write the effective config as plain JSON. |
| `ddns config save --file config.json` | Import a config file (migrates legacy formats). |
| `ddns login [--v1 --username USER \| --v2 --email EMAIL]` | Create a mittwald API key. |

## Interactive config (TUI)

`ddns config` opens a boxed terminal menu in the style of
[hatsuraide/LSAC](https://github.com/hatsuraide/LSAC): alternate screen buffer,
single-key navigation, a scrolling list and a hint bar.

| Key | Action |
| --- | --- |
| `↑` / `↓`, `j` / `k` | Move |
| `PageUp` / `PageDown`, `Home` / `End` | Jump |
| `Enter` | Select (or confirm a multi-select) |
| `Space` | Toggle a row in record discovery |
| `Esc` / `Backspace` | Back (or cancel) |
| `q` | Quit from the main menu |

Menus stay in the alternate screen; text prompts, secret input and confirmations
temporarily drop back to the normal screen. Errors and save confirmations appear
as centered message boxes.

When stdout is redirected (CI, cron, `docker compose run` without a TTY) the
workspace falls back to plain Spectre.Console prompts, so it stays scriptable.

## Configuration

Default location: `~/.config/ddns.json` (`/config/ddns.json` in the container).
Override with `DDNS_CONFIG_PATH`.

The config is provider-agnostic: an account has a `type`, credentials and a list
of `records`. See [`config.example.json`](config.example.json) for a complete
example with both providers.

```json
{
  "global_webhook": "https://discord.com/api/webhooks/123/abc",
  "default_api_version": 2,
  "timeout": "00:05:00",
  "ip_service_url": "https://api64.ipify.org",
  "notify_on_change": true,
  "notify_on_error": true,
  "accounts": [
    {
      "type": "mittwald",
      "name": "r1234",
      "api_key": "uuid:secret",
      "api_version": 1,
      "webhook": null,
      "records": [
        {
          "domain": "example.com",
          "zone_id": "12345"
        }
      ]
    },
    {
      "type": "mittwald",
      "name": "project-uid",
      "api_key": "mw-api-token",
      "api_version": 2,
      "records": [
        {
          "domain": "shop.example.com",
          "zone_id": "dns-zone-id",
          "project_id": "project-id"
        }
      ]
    },
    {
      "type": "cloudflare",
      "name": "cf-main",
      "api_key": "cloudflare-api-token",
      "records": [
        {
          "domain": "home.example.com",
          "zone_id": "cloudflare-zone-id",
          "record_id": "cloudflare-record-id",
          "proxied": false,
          "ttl": 1
        }
      ]
    }
  ]
}
```

### Common fields

| Field | Description |
| --- | --- |
| `global_webhook` | Default webhook for changes and errors. |
| `timeout` | Delay between daemon cycles (`00:05:00`, or short forms in the workspace: `5m`). |
| `ip_service_url` | Plain-text or JSON (`{"ip": "..."}`) public IP endpoint. |
| `default_api_version` | mittwald API version when an account has no `api_version`. |
| `notify_on_change` / `notify_on_error` | Notification toggles (default `true`). |
| `accounts[].type` | `mittwald` or `cloudflare`. Defaults to `mittwald`. |
| `accounts[].name` | Unique label. For mittwald v1 this is also the API account identifier. |
| `accounts[].api_key` | API key/token for the selected provider. |
| `accounts[].webhook` | Optional account-level webhook override. |
| `accounts[].records[]` | Records to manage. |
| `records[].domain` | Record name, e.g. `home.example.com`. |
| `records[].webhook` | Optional per-record webhook override. |

### Provider-specific fields

- **mittwald v1** (`api_version: 1`): `records[].domain` is the domain name;
  `zone_id` is not required.
- **mittwald v2** (`api_version: 2`): `records[].zone_id` is the DNS zone id.
  If it is empty, `project_id` is used to look the zone up by domain.
- **Cloudflare:** `zone_id` is the Cloudflare zone. `record_id` is optional; when
  empty the record is resolved by name. If `zone_id` is empty, the longest
  matching zone name is used. `proxied` and `ttl` are preserved from the existing
  record unless set explicitly. Records are not created automatically — create
  the `A`/`AAAA` record in Cloudflare first.

### Webhooks

Webhooks receive `{"content": "..."}`, which works with Discord out of the box.
Notifications are sent when a record is updated, when a provider update fails,
and when the public IP cannot be resolved. Unchanged records do not notify.

## Environment variables

| Variable | Description |
| --- | --- |
| `DDNS_CONFIG_PATH` | Config file path. Default `~/.config/ddns.json`. |
| `DDNS_SECRET` | Passphrase for encrypted config files (SHA-256 → AES-256-GCM). |
| `DDNS_IP_SERVICE_URL` | Overrides `ip_service_url`. |
| `MITTWALD_CONFIG_PATH`, `MITTWALD_SECRET`, `MITTWALD_IP_SERVICE_URL` | Legacy fallbacks, still honoured. |

## Docker

```bash
cp .env.example .env
# set DDNS_SECRET in .env
docker compose up -d
```

One-off commands:

```bash
docker compose run --rm ddns login
docker compose run --rm ddns check
docker compose run --rm ddns config export
```

The image is published as `ghcr.io/hatsuraide/ddns`.

## Migrating from mittwald-ddns / dyndns.py

Old config files are imported automatically by `ddns config save`, `config export`
and the daemon. Supported shapes:

- mittwald-ddns flat config: top-level `api_key` + `domains`
- mittwald-ddns account config: `accounts[].api_key` + `accounts[].domains`
- dyndns.py Cloudflare config: `accounts[].api_token` + `accounts[].zones[].records`

Legacy configs are converted to `accounts[].records` in memory. Save them once
(`ddns config export --file ddns.json`) to persist the new shape.
Encrypted `mittwald-ddns.encrypted-config` files are still readable; new files
use the `ddns.encrypted-config` envelope.

The original Python script is kept in [`legacy/dyndns.py`](legacy/dyndns.py) for
reference; it is no longer needed and is not part of the build.

## Architecture

```
Ddns/
├── Core/            IDnsProvider, ProviderRegistry, DdnsRunner, PublicIpService
├── Config/          model, validation, encrypted store, legacy migration
├── Providers/
│   ├── Mittwald/    MittwaldProvider + v1/v2 API clients
│   └── CloudflareProvider.cs
├── Notifications/   WebhookNotifier
├── Daemon/          Worker (loop + config hot reload)
└── Cli/             commands, interactive workspace and the TUI
    ├── Commands/
    ├── Interactive/ config workspace menus
    └── Tui/         LSAC-style renderer and key handling (unit tested)
```

The worker only knows two things: how to get the public IP and how to ask the
registry for a provider. `DdnsRunner` compares the resolved IP with the last
successfully applied address per record, so providers are not called again while
the address is unchanged.

### Adding a provider

1. Implement `IDnsProvider` in `Ddns/Providers/` (`ValidateAsync`, `DiscoverAsync`,
   `UpdateAsync`).
2. Add a constant to `ProviderTypes` and a factory entry to `ProviderRegistry`.
3. Add tests mirroring `CloudflareProviderTests`.

No changes to the worker, config store or CLI are required.

## Development

```bash
dotnet build Ddns.slnx
dotnet test Ddns.slnx
```

Tests mirror the source layout:
menu helpers and TUI renderer/session
```
Ddns.Tests/
├── Cli/         command routing, prompts and validators
├── Config/      store, encryption and legacy migration
├── Core/        runner, notifications and de-duplication
└── Providers/   Cloudflare and mittwald API clients
```

## License

MIT. See [LICENSE](LICENSE).
