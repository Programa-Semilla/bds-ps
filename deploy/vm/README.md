# Single-VM deployment — capitalsemilla-dev

Fixed-cost alternative to the Azure Container Apps stack. One Linux VM runs
everything via Docker Compose:

```
Caddy (auto-TLS, 80/443)
  └─ webapp   (.NET 10, existing src/FundingPlatform.Web/Dockerfile)
  └─ mssql    (SQL Server 2022 Developer, loopback-only)
  └─ attachments → Azure Blob (durable, ~cents/GB) via VM managed identity
                   — or LocalFilesystem on the VM disk if you prefer
```

## Why a VM

Azure has **no native "stop at $X"** — budgets only alert, and Container Apps +
Azure SQL bill by usage. A VM's compute cost is **fixed** whether idle or busy,
so the bill can't surprise you. If you want a literal kill-switch, wire a budget
alert → automation runbook that **deallocates** the VM (see bottom).

## Cost (centralus, ~fixed/month)

| Item | Choice | ~Cost |
|---|---|---|
| VM | `Standard_B2s` (2 vCPU, 4 GB) | $30–38 |
| OS disk | 64 GB StandardSSD | $5 |
| Static public IP | Standard | $3–4 |
| Egress | low for this app | ~$0–5 |
| **Total** | | **~$40, predictable** |

> B2s (4 GB) is the lowest size that runs SQL Server + the webapp. It's tight
> once Syncfusion launches Chromium for a PDF. If you hit OOM kills, set
> `VM_SIZE=Standard_B2ms` (8 GB, ~$60) before running `provision-vm.sh`, or
> resize later: `az vm resize -g rg-CapitalSemilla-D -n vm-capitalsemilla-dev --size Standard_B2ms`.

## One-time setup

```bash
cd deploy/vm

# 1. Provision the VM (creates the resource group if missing, locks SSH to your
#    current IP, opens 80/443).
./provision-vm.sh
#    -> prints the VM public IP.

# 2. Provision the attachments storage account + grant the VM managed identity.
#    Prints the BLOB_CONNECTION endpoint URI for .env. (Skip if you set
#    STORAGE_PROVIDER=LocalFilesystem instead.)
./provision-storage.sh

# 3. DNS: point an A record at that IP and wait for it to resolve:
#       capitalsemilla-dev.programasemilla.com -> <VM IP>
#    Caddy can't issue the TLS cert until this resolves publicly.

# 4. Ship the deploy files to the VM.
scp -r ../vm azureuser@<VM IP>:~/deploy

# 5. On the VM: configure secrets and start SQL.
ssh azureuser@<VM IP>
cd ~/deploy
cp .env.example .env && nano .env      # set MSSQL_SA_PASSWORD, ADMIN_DEFAULT_PASSWORD,
                                       # STORAGE_PROVIDER + BLOB_CONNECTION (from step 2), etc.
docker compose up -d mssql             # wait until healthy: docker compose ps

# 6. From your DEV MACHINE: first deploy — sync source, publish schema, build, start.
#    deploy.sh is idempotent; --schema also publishes the dacpac.
MSSQL_SA_PASSWORD='<same as VM .env>' ./deploy.sh <VM IP> --schema --logs
```

Visit https://capitalsemilla-dev.programasemilla.com — Caddy serves a valid
Let's Encrypt cert automatically.

## Day-to-day — deploy updates

One command from your **dev machine** handles every redeploy. It rsyncs the repo
to the VM (never touching the VM's `.env`), rebuilds the image **on the VM**, and
recreates only what changed. Safe to run repeatedly.

```bash
# Code change — push the update:
./deploy.sh <VM IP>

# Code + schema change — also publish the dacpac:
MSSQL_SA_PASSWORD='…' ./deploy.sh <VM IP> --schema

# Recreate without rebuilding / watch logs after:
./deploy.sh <VM IP> --no-build
./deploy.sh <VM IP> --logs
```

> The image builds on the VM (the Dockerfile COPYs from the synced source). On a
> 4 GB B2s the .NET SDK build is heavy but fine; it won't interrupt the running
> container until the new image is ready.

## Logs

Everything runs on the VM, so **there is no Azure log cost at all** — no Log
Analytics ingestion, no retention bill. Two ways to look:

### 1. Live tail (always on, zero setup)

```bash
docker compose logs -f webapp           # live stream
docker compose logs --since 30m webapp  # last 30 minutes
docker compose logs --tail 200 webapp   # last 200 lines
```

Cap how much docker keeps on disk (the on-VM equivalent of the LAW cap). Add to
`/etc/docker/daemon.json` on the VM, then `sudo systemctl restart docker`:

```json
{ "log-driver": "json-file", "log-opts": { "max-size": "10m", "max-file": "3" } }
```

That's ~30 MB/container, oldest rotated out. Note: docker caps by **size**, not
time — there's no native "keep 30 min" knob; size rotation is the lever.

### 2. Aspire Dashboard (structured logs + traces + errors)

In-memory telemetry UI. **Nothing is persisted → $0, and it self-evicts** (keeps
the last 5000 logs/traces, oldest dropped; all gone on restart). Off by default
to save RAM; start it only when investigating.

**Quick toggle (from your dev machine)** — `aspire-dashboard.sh` resolves the VM,
flips export, starts/stops the container, and opens/closes the SSH tunnel for you:

```bash
./aspire-dashboard.sh on       # -> prints http://localhost:18888 and exits (tunnel stays up)
./aspire-dashboard.sh status   # tunnel / container / export state
./aspire-dashboard.sh off      # close tunnel, stop dashboard, disable export
```

It restarts the webapp on `on`/`off` so it re-reads `OTEL_ENDPOINT` (a few seconds
of downtime). The manual equivalent, if you'd rather drive it by hand:

```bash
# On the VM: point the app at the dashboard, then start it.
nano .env        # uncomment: OTEL_ENDPOINT=http://aspire-dashboard:18889
docker compose up -d webapp                       # picks up the new OTEL endpoint
docker compose --profile debug up -d aspire-dashboard

# From your dev machine: tunnel the loopback-bound UI and open it.
ssh -L 18888:localhost:18888 azureuser@<VM IP>
#   then browse http://localhost:18888  (Structured logs / Traces / Metrics tabs)

# When done — free the RAM and stop exporting.
docker compose stop aspire-dashboard
nano .env        # re-comment OTEL_ENDPOINT
docker compose up -d webapp
```

**RAM cost:** dashboard ~150–250 MB while running. On a 4 GB B2s that's tight
alongside SQL — fine for short debugging bursts, but if you want it **always on**
move to `Standard_B2ms` (8 GB). Lower the `MAXLOGCOUNT`/`MAXTRACECOUNT` env in
`docker-compose.yml` to shrink its footprint.

**Security:** the UI runs `AUTHMODE=Unsecured` and is bound to `127.0.0.1` only —
never published to the public NSG. Always reach it through the SSH tunnel above.
Do **not** add an `aspire-dashboard` entry to the `Caddyfile` without putting auth
in front of it.

## Storage (attachments)

Default is **Azure Blob** — durable (survives VM loss), virtually unlimited, and
cheap (Standard_LRS hot ~$0.018/GB-mo + trivial per-transaction). Storage was
never the cost driver; Log Analytics ingestion was. `provision-storage.sh`
creates the account and grants the VM's **managed identity** `Storage Blob Data
Owner`, so the app authenticates with **no secret on disk** — `.env` holds only
the blob endpoint URI. (Owner, not just Contributor: startup calls
`GetAccessPolicy` to assert containers are private per FR-027, which Contributor
can't do.) The app auto-creates its per-category containers at startup (spec
014). **RBAC takes a few minutes to propagate** — the webapp fail-fasts on blob
auth until then, so on a fresh deploy it may restart a few times before the role
is live; it self-heals once propagation completes.

Auth options for `BLOB_CONNECTION` in `.env`:
- **Managed identity (recommended):** the `https://<acct>.blob.core.windows.net`
  URI. No key stored; the Production storage guard stays Healthy.
- **Account key:** a full connection string. Simpler (no role setup); fine for dev.

The webapp container reaches the managed identity over Azure IMDS
(`169.254.169.254`), normally routable from the docker bridge. If blob auth fails
with a credential error, fall back to a key connection string.

Prefer files on the VM disk instead? Set `STORAGE_PROVIDER=LocalFilesystem` —
they live in the `app_storage` volume and are captured by `backup.sh`. (Bounded
by disk size; no offsite durability — that's why Blob is the default.)

## Backups (you own them now)

```bash
# On the VM — schedule nightly 03:30 SQL + storage backup, keep 7 days:
chmod +x ~/deploy/backup.sh
( crontab -l 2>/dev/null; echo "30 3 * * * ~/deploy/backup.sh >> ~/backup.log 2>&1" ) | crontab -
```
Restore: copy a `*.bak` into the `mssql` container and `RESTORE DATABASE`. For
off-VM durability, uncomment the `az storage blob upload-batch` line in `backup.sh`.

## Power schedule (cut cost ~half)

Deallocate the VM off-hours — compute billing stops while stopped (disk + static
IP still bill, ~$8/mo floor). `provision-schedule.sh` sets up **auto-start
Mon–Fri 06:45** (Azure Automation runbook + managed identity) and **auto-stop
daily 19:00** (DevTest schedule), Costa Rica time. Containers auto-recover on
boot (`restart: unless-stopped`). Always-on ~$40/mo → scheduled ~$20/mo.

```bash
./provision-schedule.sh provision   # create/refresh both schedules (idempotent)
./provision-schedule.sh start       # start now (after-hours testing) — runs until next 19:00
./provision-schedule.sh stop        # deallocate now (stop billing early)
./provision-schedule.sh status      # power state
./provision-schedule.sh disable     # pause both schedules;  enable = resume
```

Manual `start` works anytime regardless of the schedule; the daily 19:00 stop is
a safety net so a forgotten after-hours start never runs forever. Configure
times/timezone/weekdays via env (`START_TIME`, `STOP_TIME`, `TIMEZONE_IANA`,
`WEEKDAYS`, …). Static IP persists across stop/start, so DNS + TLS are unaffected.

## Optional: literal cost kill-switch

Fixed VM cost already removes surprises, but if you want the site to **stop** at
a threshold:

1. Create a budget on `rg-CapitalSemilla-D`.
2. Budget alert → Action Group → Automation Runbook running
   `az vm deallocate -g rg-CapitalSemilla-D -n vm-capitalsemilla-dev`.

Deallocated VM = $0 compute (you keep paying only the disk + IP). The site goes
down until you `az vm start` — exactly the "stop rather than grow" behavior.

## Decommission the old Container Apps stack

Once this VM serves traffic, tear down the serverless stack to stop double-billing
(Container Apps, Azure SQL, Storage, the now-capped Log Analytics workspace, ACR).
Keep a final dacpac + data export first.

## Files

| File | Purpose |
|---|---|
| `provision-vm.sh` | Create the resource group (if missing) + VM + NSG (run from dev machine, needs `az`). |
| `provision-storage.sh` | Create the attachments Blob account + grant the VM managed identity (run after `provision-vm.sh`). |
| `cloud-init.yaml` | First-boot Docker install + host firewall. |
| `docker-compose.yml` | caddy + webapp + mssql services (+ aspire-dashboard under the `debug` profile). |
| `Caddyfile` | Domain + auto-TLS reverse proxy. |
| `.env.example` | Secrets/config template — copy to `.env` on the VM. |
| `deploy.sh` | Idempotent deploy/update — sync + build + recreate (run from dev machine). First deploy and every update. |
| `publish-dacpac-vm.sh` | Publish schema via SSH tunnel (run from dev machine; or via `deploy.sh --schema`). |
| `backup.sh` | Nightly SQL + storage backup (cron on the VM). |
| `provision-schedule.sh` | Auto start/stop power schedule + manual `start`/`stop`/`status` control (run from dev machine). |
| `aspire-dashboard.sh` | Toggle the Aspire Dashboard `on`/`off`/`status` — flips export, starts/stops the container, opens/closes the SSH tunnel (run from dev machine). |
| `vm-metrics.sh` | CPU / memory / per-container metrics with a verdict column — `snapshot` (one-shot) or `stream [secs]` (live). Pulled over SSH, no agent/cost (run from dev machine). |
| `vm-logs.sh` | Container logs over SSH — `follow [service...]` (live `-f`) or `tail [N] [service...]` (last N lines). Defaults to `webapp` (run from dev machine). |
