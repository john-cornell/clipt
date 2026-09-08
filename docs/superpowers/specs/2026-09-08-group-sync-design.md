# Clipt Group Sync — Design

Date: 2026-09-08

## Goal

Sync Clipt's saved **Groups** (not the live clipboard History) across multiple Windows PCs — home, work, wherever — via a small self-hosted, end-to-end-encrypted API on the user's existing Ubuntu VPS. Sync is strictly additive: Clipt must work exactly as it does today with the server unreachable or never configured.

## Non-goals

- Syncing History (everything that passes through the clipboard) — too high-volume, too sensitive, and not what groups are for. Groups are the deliberate "I archived this" layer; that's what's worth syncing.
- Multi-user accounts. This is a single person's own devices.
- Real-time push (WebSockets/SignalR). Debounced push + periodic poll is enough at this scale and keeps both sides simple.

## Architecture

The Ubuntu box (`vps-1ecd20d4`, public IP, domain `monkeyskin.au`) already runs nginx directly terminating TLS for other subdomains (confirmed via `curl -I` showing `Server: nginx/1.28.3 (Ubuntu)`, no CDN/tunnel in front) and an existing gunicorn app on `127.0.0.1:8000`. No VPN/tunnel software is needed — this is a public VPS, not a NAT'd home box.

The sync API is one more service on that box, following the same pattern as the existing gunicorn app:

```
Clipt (Windows, N devices)
   │  HTTPS + Bearer token
   ▼
nginx (existing, TLS via certbot)
   │  proxy_pass to 127.0.0.1:<port>
   ▼
FastAPI + gunicorn/uvicorn workers (new systemd service)
   │
   ▼
SQLite (single file on disk)
```

## Encryption model

The server is deliberately unable to read anything meaningful. Every group — name, folder assignment, entry names, entry content/blobs — is serialized and encrypted **client-side** into one opaque ciphertext blob (AES-256-GCM) before it's ever sent. The server stores only: an id, the ciphertext, a version counter, and timestamps.

**Key derivation:** the user types one passphrase into Clipt once per device (a new Settings field). The client fetches a shared KDF salt from the server (`GET /kdf` — not secret, just so every device derives the identical key from the same passphrase) and runs `passphrase + salt` through Argon2id to get the AES-256 key. The passphrase itself never leaves the device and is never sent to the server in any form.

**Two separate secrets, two separate purposes:**
- **Bearer token** — proves "this is one of my devices" to stop randoms writing garbage into the SQLite DB or churning storage. Generated once, copied to each device like any API key. Cheap to rotate if leaked.
- **Passphrase** — the only thing that can decrypt group content. Never transmitted, never stored server-side even in derived form. If forgotten, synced groups are unrecoverable — worth writing it down in a password manager, not in Clipt itself.

Both are required on every request; losing either one blocks sync without exposing data.

## Server: API surface

FastAPI app, SQLite backing store, single-user (no accounts/registration — the bearer token *is* the identity).

| Endpoint | Purpose |
|---|---|
| `GET /kdf` | Returns the salt + Argon2id params so a new device can derive the same key from the shared passphrase. |
| `GET /groups?since=<version>` | Returns every group (including tombstoned deletes) with `version > since`, for incremental pull. `since=0` on first sync returns everything. |
| `PUT /groups/{id}` | Body `{ciphertext, based_on_version}`. Upserts. Server compares `based_on_version` to the row's current version; mismatch → `409 Conflict` (optimistic concurrency — stops one device silently clobbering a change another device made moments ago). On success, increments version, returns the new version. |
| `DELETE /groups/{id}` | Body `{based_on_version}`. Soft-delete: sets a tombstone flag + timestamp, same concurrency check as `PUT`. Hard deletes are never issued from the client; tombstones are what let other devices learn about a delete on their next pull. |

### Data model (SQLite)

```sql
CREATE TABLE groups (
    id TEXT PRIMARY KEY,          -- client-generated GUID; never collides across devices
    ciphertext BLOB NOT NULL,     -- AES-256-GCM output (includes nonce/tag)
    version INTEGER NOT NULL,     -- monotonically increasing per row, server-assigned
    updated_at TEXT NOT NULL,
    deleted INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE kdf_params (
    id INTEGER PRIMARY KEY CHECK (id = 1),  -- singleton row
    salt BLOB NOT NULL,
    argon2_time_cost INTEGER NOT NULL,
    argon2_memory_cost_kib INTEGER NOT NULL,
    argon2_parallelism INTEGER NOT NULL,
    created_at TEXT NOT NULL
);
```

### Auth

Every endpoint requires `Authorization: Bearer <token>`, checked with a constant-time comparison against a token stored in the service's environment/config (not the database). No token, wrong token → `401`.

### Abuse hardening

nginx `limit_req` (or fail2ban watching the access log) on repeated `401`s from this vhost, so the token can't be brute-forced. A `robots.txt: Disallow: /` on the subdomain keeps it out of search indexes — cosmetic, since every route 401s without the token regardless of who calls it or which HTTP method they use (crawlers only ever issue `GET`/`HEAD`; nothing in HTML produces a `DELETE`, so that method is never a crawler concern here).

## Client: sync behavior

New `GroupSyncService` in Clipt, alongside the existing `ClipboardGroupService`.

**Triggers:**
- Any local group mutation (create, rename, reorder, add/remove/rename an entry, delete the group) marks that group dirty and schedules a debounced push (a couple of seconds after the last change, so a burst of edits collapses into one push).
- A background pull runs on a timer (e.g. every 60s while the app is running) and when the tray popup opens, using `GET /groups?since=<last-seen version>`.

**Push:** encrypt the group's current state, `PUT /groups/{id}` with the version the client last knew about. On `409`, pull first (picking up whatever changed on the other device), then retry the push — last-write-wins is not automatic; the client only overwrites a version it has actually seen.

**Pull:** for each returned row, decrypt with the local key. A tombstoned row removes the local group (if present). An active row upserts into local storage — but only for groups that aren't currently locally dirty, so an in-flight local edit is never silently overwritten by an incoming pull; it resolves naturally on that group's next push/conflict cycle.

**First-time device setup:** user enters server URL, bearer token, and passphrase once (Settings). Client fetches `/kdf`, derives the key, pushes any groups that already exist locally on that device (they're always fresh GUIDs — no id collisions are possible across devices), and pulls everything else down.

**Offline/unreachable:** sync retries silently on its next timer tick; the UI never blocks on it and Clipt's local group functionality is entirely unaffected. A simple status indicator (e.g. in the sync settings panel) shows last-successful-sync time and surfaces auth failures once (not repeatedly) rather than nagging.

## Deployment

- New systemd unit running gunicorn (uvicorn workers) bound to `127.0.0.1:<port>`, matching the existing gunicorn app's pattern on this box.
- New nginx server block for the chosen subdomain (e.g. `sync.monkeyskin.au`), TLS via certbot like the existing sites, `proxy_pass` to that port.
- SQLite file lives on local disk; back it up the same way as anything else on the box (out of scope for this spec — assume existing box backup practice covers it, or note as a follow-up if there isn't one).

## Testing

- **Server:** pytest against the FastAPI app with a temp-file SQLite — upsert, version-conflict (`409`), tombstone-then-pull, `since` filtering, auth rejection.
- **Client:** unit tests for the Argon2id derivation (deterministic given salt+passphrase), the AES-256-GCM encrypt/decrypt round-trip, and `GroupSyncService`'s push/pull/conflict-retry logic against a mocked HTTP client — no real network or real server needed for these.

## Open follow-ups (not blocking this spec)

- SQLite backup/retention policy on the VPS.
- Whether to prune old tombstones eventually (not needed yet — dataset is small).
