# Clipt Sync Server — Deployment

Run entirely on the Ubuntu VPS (`vps-1ecd20d4`, `monkeyskin.au`), over SSH. Built and tested locally first — see the test suite under `tests/` (`python -m pytest -v`).

## 1. Copy the code to the box

From your workstation:
```bash
scp -r server/clipt-sync-server ubuntu@monkeyskin.au:~/clipt-sync-server
```

## 2. Install and set up a virtualenv (on the box)

```bash
cd ~/clipt-sync-server
python3 -m venv .venv
.venv/bin/pip install -r requirements.txt
```

## 3. Generate the bearer token

```bash
python3 -c "import secrets; print(secrets.token_urlsafe(32))"
```
Copy this into `deploy/clipt-sync.service`'s `CLIPT_SYNC_TOKEN=` line (replacing `REPLACE_WITH_GENERATED_TOKEN`) before the next step. This same token gets entered into Clipt's sync setup on every device — treat it like an API key.

## 4. Install the systemd service

```bash
sudo cp deploy/clipt-sync.service /etc/systemd/system/clipt-sync.service
sudo systemctl daemon-reload
sudo systemctl enable --now clipt-sync
sudo systemctl status clipt-sync   # should show "active (running)"
```

## 5. Add the nginx site and rate-limit zone

```bash
sudo cp deploy/nginx-clipt-sync.conf /etc/nginx/sites-available/clipt-sync.conf
sudo ln -s /etc/nginx/sites-available/clipt-sync.conf /etc/nginx/sites-enabled/clipt-sync.conf
```

Edit `/etc/nginx/nginx.conf` and add this inside the `http {}` block (alongside any other `limit_req_zone` directives already there):
```nginx
limit_req_zone $binary_remote_addr zone=clipt_sync_zone:10m rate=5r/s;
```

Then:
```bash
sudo nginx -t
sudo systemctl reload nginx
```

## 6. Get a TLS certificate

```bash
sudo certbot --nginx -d sync.monkeyskin.au
```
(Matches how the existing subdomains on this box already got their certs — no new certbot setup needed.)

## 7. Verify from any machine

```bash
curl -I https://sync.monkeyskin.au/kdf
# expect 401 (no token supplied) — confirms nginx + the service are both up

curl -H "Authorization: Bearer <the token from step 3>" https://sync.monkeyskin.au/kdf
# expect 200 with salt + argon2 params
```

## Updating the deployed code later

```bash
scp -r server/clipt-sync-server ubuntu@monkeyskin.au:~/clipt-sync-server
ssh ubuntu@monkeyskin.au 'cd ~/clipt-sync-server && .venv/bin/pip install -r requirements.txt && sudo systemctl restart clipt-sync'
```
