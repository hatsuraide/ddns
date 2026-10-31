
import requests
import json
import os
import datetime
import argparse
import time
from pathlib import Path

CONFIG_FILE = Path(__file__).with_name("config.json")
IP_FETCH_ENDPOINT="https://icanhazip.com"
#IP_FETCH_ENDPOINT="https://api64.ipify.org"

# ---------------------- Helpers ----------------------

def log(msg):
    print(f"[{datetime.datetime.now().isoformat()}] {msg}")

def send_discord(webhook, content):
    if not webhook:
        return
    try:
        requests.post(webhook, json={"content": content}, timeout=10)
    except Exception as e:
        log(f"Discord send failed: {e}")

def load_config():
    if not CONFIG_FILE.exists():
        log("No config.json found. Run with --setup first.")
        exit(1)
    with open(CONFIG_FILE, "r") as f:
        return json.load(f)

def save_config(cfg):
    with open(CONFIG_FILE, "w") as f:
        json.dump(cfg, f, indent=2)
    log(f"Saved config to {CONFIG_FILE}")

# ---------------------- Cloudflare API ----------------------

def cf_get(endpoint, token):
    r = requests.get(
        f"https://api.cloudflare.com/client/v4/{endpoint}",
        headers={
            "Authorization": f"Bearer {token}",
            "Content-Type": "application/json"
        },
        timeout=15
    )
    r.raise_for_status()
    return r.json()

def cf_put(endpoint, token, data):
    r = requests.put(
        f"https://api.cloudflare.com/client/v4/{endpoint}",
        headers={
            "Authorization": f"Bearer {token}",
            "Content-Type": "application/json"
        },
        json=data,
        timeout=15
    )
    r.raise_for_status()
    return r.json()

# ---------------------- Setup ----------------------

def setup_mode():
    print("=== Cloudflare DDNS Setup ===")

    cfg = {"global_webhook": "", "accounts": []}
    if CONFIG_FILE.exists():
        cfg = load_config()

    if not cfg.get("global_webhook"):
        webhook = input("Enter global Discord webhook URL (or leave blank): ").strip()
        cfg["global_webhook"] = webhook

    while True:
        token = input("\nEnter your Cloudflare API token (or leave blank to finish): ").strip()
        if not token:
            break

        try:
            zones = cf_get("zones", token)["result"]
        except Exception as e:
            print(f"Error fetching zones: {e}")
            continue

        print("\nAvailable zones:")
        for i, z in enumerate(zones, 1):
            print(f"  [{i}] {z['name']}")
        selected_zone_idx = input("Select zone numbers (comma-separated, e.g. 1,2): ").strip()
        if not selected_zone_idx:
            continue
        selected_zone_idx = [int(x) - 1 for x in selected_zone_idx.split(",")]

        account = {
            "api_token": token,
            "zones": []
        }

        for idx in selected_zone_idx:
            zone = zones[idx]
            print(f"\nFetching DNS records for {zone['name']}...")
            try:
                records = cf_get(f"zones/{zone['id']}/dns_records", token)["result"]
            except Exception as e:
                print(f"Error fetching records: {e}")
                continue

            print(f"Available records for {zone['name']}:")
            for i, r in enumerate(records, 1):
                print(f"  [{i}] {r['name']} ({r['type']}) -> {r['content']}")

            rec_input = input("Select record numbers (comma-separated): ").strip()
            if not rec_input:
                continue
            rec_indexes = [int(x) - 1 for x in rec_input.split(",")]

            zone_cfg = {
                "zone_id": zone["id"],
                "zone_name": zone["name"],
                "records": []
            }

            for ridx in rec_indexes:
                rec = records[ridx]
                record_webhook = input(f"Webhook for record '{rec['name']}' (leave blank for global): ").strip()
                zone_cfg["records"].append({
                    "record_id": rec["id"],
                    "domain": rec["name"],
                    "webhook": record_webhook or None
                })

            account["zones"].append(zone_cfg)

        cfg["accounts"].append(account)

    save_config(cfg)
    print("Setup complete. Run without --setup to update records.")

# ---------------------- Config Editor ----------------------

def edit_config():
    cfg = load_config()
    print(json.dumps(cfg, indent=2))
    print("\nYou can now edit the configuration manually.")
    print(f"Location: {CONFIG_FILE}")
    print("When done, save and rerun the script.")
    exit(0)

# ---------------------- Record Updater ----------------------

def update_records():
    cfg = load_config()
    global_webhook = cfg.get("global_webhook", "")

    try:
        current_ip = requests.get(IP_FETCH_ENDPOINT, timeout=10).text.strip()
    except Exception as e:
        log(f"Failed to fetch public IP: {e}")
        send_discord(global_webhook, f"Failed to fetch public IP: {e}")
        return

    log(f"Current IP: {current_ip}")

    for acc in cfg.get("accounts", []):
        token = acc["api_token"]

        for zone in acc.get("zones", []):
            for rec in zone.get("records", []):
                domain = rec["domain"]
                zid = zone["zone_id"]
                rid = rec["record_id"]
                record_webhook = rec.get("webhook") or global_webhook

                try:
                    info = cf_get(f"zones/{zid}/dns_records/{rid}", token)
                    if not info.get("success"):
                        raise Exception(info)
                    cf_ip = info["result"]["content"]
                    last_mod = info["result"]["modified_on"]
                except Exception as e:
                    log(f"Error fetching record {domain}: {e}")
                    send_discord(record_webhook, f"Error fetching record {domain}: {e}")
                    continue

                if cf_ip == current_ip:
                    log(f"{domain}: IP unchanged ({current_ip})")
                    continue

                log(f"{domain}: IP changed {cf_ip} -> {current_ip}, updating...")
                payload = {
                    "type": "A",
                    "name": domain,
                    "content": current_ip,
                    "ttl": 1,
                    "proxied": False
                }

                try:
                    update = cf_put(f"zones/{zid}/dns_records/{rid}", token, payload)
                    if update.get("success"):
                        log(f"{domain}: updated successfully.")
                        send_discord(
                            record_webhook,
                            f"Updated {domain}\nOld IP: {cf_ip} → New IP: {current_ip}\nLast modified: {last_mod}"
                        )
                    else:
                        err = update.get("errors", [{"message": "Unknown error"}])[0]["message"]
                        log(f"{domain}: update failed ({err})")
                        send_discord(record_webhook, f"Update failed for {domain}: {err}")
                except Exception as e:
                    log(f"{domain}: exception during update: {e}")
                    send_discord(record_webhook, f"Exception during update for {domain}: {e}")

# ---------------------- Main ----------------------

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Cloudflare DDNS Updater")
    parser.add_argument("--setup", action="store_true", help="Run setup mode")
    parser.add_argument("--edit", action="store_true", help="Edit configuration")
    parser.add_argument("--daemon", action="store_true", help="Run continuously as a background service")
    parser.add_argument("--interval", type=int, default=300, help="Interval in seconds between checks")
    args = parser.parse_args()

    if args.setup:
        setup_mode()
    elif args.edit:
        edit_config()
    elif args.daemon:
        log(f"Running in daemon mode (every {args.interval} seconds)")
        while True:
            update_records()
            log(f"Sleeping for {args.interval} seconds...")
            try:
                time.sleep(args.interval)
            except KeyboardInterrupt:
                log("Service stopped by user.")
                break
    else:
        update_records()