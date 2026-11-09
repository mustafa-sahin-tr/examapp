#!/usr/bin/env python3
"""Prepare the closed demo (#1065): deploy/.env.demo with random secrets and a clean realm in keycloak/import.

Existing values in .env.demo are kept, so the script is safe to run again. Secrets are never printed.
Keycloak imports a realm only once: after changing the base URL or SMTP values, recreate the demo volumes.
"""
import base64
import json
import secrets
from pathlib import Path

DEPLOY = Path(__file__).resolve().parent.parent
ENV_FILE = DEPLOY / ".env.demo"
DEV_REALM = DEPLOY / "keycloak" / "dev-import" / "realm-export.json"
DEMO_REALM = DEPLOY / "keycloak" / "import" / "realm-demo.json"


def token(length=40):
    return secrets.token_urlsafe(length)


DEFAULTS = {
    "DOMAIN": "hedefokul.com",
    "PUBLIC_BASE_URL": "https://hedefokul.com",
    "LETSENCRYPT_EMAIL": "",
    "POSTGRES_USER": "examuser",
    "POSTGRES_PASSWORD": token(),
    "REDIS_PASSWORD": token(),
    "RABBITMQ_USER": "rabbituser",
    "RABBITMQ_PASS": token(),
    "RABBITMQ_EXAM_OUTBOX_PASSWORD": token(),
    "RABBITMQ_IDENTITY_OUTBOX_PASSWORD": token(),
    "RABBITMQ_BADGE_OUTBOX_PASSWORD": token(),
    "RABBITMQ_BADGE_SERVICE_PASSWORD": token(),
    "RABBITMQ_EXAM_API_PASSWORD": token(),
    "RABBITMQ_AUTH_API_PASSWORD": token(),
    "MINIO_ROOT_USER": "demo-root",
    "MINIO_ROOT_PASSWORD": token(),
    "MINIO_PRESIGN_ACCESS_KEY": "exam-presign",
    "MINIO_PRESIGN_SECRET_KEY": token(),
    "PARENT_INVITE_CODE_PEPPER": token(),
    "PARENT_INVITATION_LINK_KEY": base64.b64encode(secrets.token_bytes(32)).decode(),
    "KEYCLOAK_ADMIN": "demo-admin",
    "KEYCLOAK_ADMIN_PASSWORD": token(),
    "JWT_KEY": token(64),
    "KEYCLOAK_CLIENT_ID": "exam-client",
    "KEYCLOAK_CLIENT_SECRET": token(),
    "KEYCLOAK_ADMIN_CLIENT_ID": "exam-admin",
    "KEYCLOAK_ADMIN_CLIENT_SECRET": token(),
    "KEYCLOAK_SERVICE_CLIENT_ID": "exam-service",
    "KEYCLOAK_SERVICE_CLIENT_SECRET": token(),
    "BADGE_AI_ACTIVE": "true",
    "JITSI_PUBLIC_URL": "",
    # Filled in by the owner.
    "ANTHROPIC_API_KEY": "",
    "SMTP_HOST": "smtp.gmail.com",
    "SMTP_PORT": "587",
    "SMTP_USERNAME": "",
    "SMTP_PASSWORD": "",
    "SMTP_FROM_ADDRESS": "",
    "SMTP_FROM_NAME": "Hedef Okul",
}


def load_env():
    values = dict(DEFAULTS)
    if ENV_FILE.exists():
        for line in ENV_FILE.read_text().splitlines():
            if line and not line.startswith("#") and "=" in line:
                key, value = line.split("=", 1)
                if key in DEFAULTS:
                    values[key] = value
    ENV_FILE.write_text("".join(f"{k}={v}\n" for k, v in values.items()))
    ENV_FILE.chmod(0o600)
    return values


def build_realm(env):
    realm = json.loads(DEV_REALM.read_text())
    base = env["PUBLIC_BASE_URL"]
    # No dev login, no social login without its secret, and fresh signing keys from Keycloak.
    realm["users"] = [u for u in realm.get("users", []) if u.get("serviceAccountClientId")]
    realm["identityProviders"] = []
    realm["identityProviderMappers"] = []
    realm.get("components", {}).pop("org.keycloak.keys.KeyProvider", None)
    realm["smtpServer"] = {}
    if env["SMTP_USERNAME"]:
        realm["smtpServer"] = {
            "host": env["SMTP_HOST"],
            "port": env["SMTP_PORT"],
            "starttls": "true",
            "auth": "true",
            "user": env["SMTP_USERNAME"],
            "password": env["SMTP_PASSWORD"],
            "from": env["SMTP_FROM_ADDRESS"],
            "fromDisplayName": env["SMTP_FROM_NAME"],
        }
    client_secrets = {
        "exam-client": env["KEYCLOAK_CLIENT_SECRET"],
        "exam-admin": env["KEYCLOAK_ADMIN_CLIENT_SECRET"],
        "exam-service": env["KEYCLOAK_SERVICE_CLIENT_SECRET"],
    }
    for client in realm["clients"]:
        if client["clientId"] in client_secrets:
            client["secret"] = client_secrets[client["clientId"]]
        if client["clientId"] == "exam-client":
            client["redirectUris"] = [f"{base}/app/callback"]
            client["webOrigins"] = [base]
            client.setdefault("attributes", {})["post.logout.redirect.uris"] = f"{base}/*"
    DEMO_REALM.write_text(json.dumps(realm, ensure_ascii=False, indent=2))
    DEMO_REALM.chmod(0o644)


if __name__ == "__main__":
    build_realm(load_env())
    missing = [k for k in ("ANTHROPIC_API_KEY", "SMTP_USERNAME", "SMTP_PASSWORD", "SMTP_FROM_ADDRESS") if not load_env()[k]]
    print(f"Ready: {ENV_FILE}, {DEMO_REALM}")
    if missing:
        print("Still empty in .env.demo: " + ", ".join(missing))
