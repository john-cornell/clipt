# Clipt Sync Server — Deployment

Run entirely on the Ubuntu VPS (`vps-1ecd20d4.vps.ovh.us`, public-facing as `clipt.monkeyskin.au`), over SSH. Built and tested locally first — see the test suite under `tests/` (`python -m pytest -v`).

## 1. SSH in and get the code

```bash
ssh ubuntu@vps-1ecd20d4.vps.ovh.us
git clone --depth 1 https://github.com/john-cornell/clipt.git ~/clipt-checkout
ln -s ~/clipt-checkout/server/clipt-sync-server ~/clipt-sync-server
cd ~/clipt-sync-server
```

## 2. Install and set up a virtualenv

```bash
python3 -m venv .venv
.venv/bin/pip install -r requirements.txt
```

If `pip install` fails building `pydantic-core` from source, the box's `python3` is likely newer than what the pinned `pydantic` version has prebuilt wheels for — check `requirements.txt` here has the latest fix applied (`git pull` in `~/clipt-checkout` and retry), rather than trying to install an older Python interpreter.

## 3. Generate the bearer token

```bash
TOKEN=$(python3 -c "import secrets; print(secrets.token_urlsafe(32))")
sed -i "s/REPLACE_WITH_GENERATED_TOKEN/$TOKEN/" deploy/clipt-sync.service
echo $TOKEN
```
Save the printed value somewhere safe (password manager) now. This same token gets entered into Clipt's sync setup on every device — treat it like an API key. (If you lose it before saving it, recover it with `grep CLIPT_SYNC_TOKEN deploy/clipt-sync.service`.)

## 4. Install the systemd service

```bash
sudo cp deploy/clipt-sync.service /etc/systemd/system/clipt-sync.service
sudo systemctl daemon-reload
sudo systemctl enable --now clipt-sync
sudo systemctl status clipt-sync   # should show "active (running)"
```

## 5. DNS

Add an A record for the subdomain you're using (e.g. `clipt.monkeyskin.au`) pointing at this box's public IP, in whatever DNS panel manages `monkeyskin.au`. Confirm it resolves (`nslookup clipt.monkeyskin.au` from any machine) before continuing — certbot in step 7 will fail if it isn't live yet.

## 6. Add the nginx site and rate-limit zone

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

## 7. Get a TLS certificate

```bash
sudo certbot --nginx -d clipt.monkeyskin.au
```
(Matches how the existing subdomains on this box already got their certs — no new certbot setup needed.)

## 8. Verify from any machine (not the VPS)

```bash
curl -I https://clipt.monkeyskin.au/kdf
# expect 401 (no token supplied) — confirms nginx + the service are both up

curl -H "Authorization: Bearer <the token from step 3>" https://clipt.monkeyskin.au/kdf
# expect 200 with salt + argon2 params
```

## Updating the deployed code later

```bash
ssh ubuntu@vps-1ecd20d4.vps.ovh.us 'cd ~/clipt-checkout && git pull && cd ~/clipt-sync-server && .venv/bin/pip install -r requirements.txt && sudo systemctl restart clipt-sync'
```
