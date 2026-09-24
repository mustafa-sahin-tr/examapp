#!/usr/bin/env python3
"""Regenerate the dev-only `password_hash` values in `rabbitmq/definitions.json`
(issue #279 item 1 — per-service RabbitMQ users).

RabbitMQ's `rabbit_password_hashing_sha256` algorithm (the default since
3.6, and what `hashing_algorithm` in definitions.json declares) is:

    salt (4 random bytes) + SHA256(salt + password_utf8)   -> base64

RabbitMQ recomputes SHA256(salt + candidate_password) from the first 4
bytes of the decoded hash and compares the remaining bytes; the salt does
not need to be secret, only the resulting base64 string is stored.

This script is only needed when a dev-only RabbitMQ password below changes
(e.g. `RABBITMQ_BADGE_SERVICE_PASSWORD` in `.env.example` /
`rabbitmq-badge-service-password` in `AppHost/appsettings.json`). Keep the
username -> password mapping here in sync with those two files, run this
script, and paste the printed hash into the matching `users[].password_hash`
entry in `rabbitmq/definitions.json`.

Usage:
    python3 rabbitmq/generate-password-hashes.py
    python3 rabbitmq/generate-password-hashes.py myuser myNewDevOnlyPassword
"""
import base64
import hashlib
import os
import sys

# name -> dev-only plaintext password (must match .env.example / AppHost/appsettings.json)
DEV_PASSWORDS = {
    "rabbituser": "rabbitpass",
    "exam_outbox_pub": "devOnlyExamOutboxPubPasswordChangeMe123",
    "identity_outbox_pub": "devOnlyIdentityOutboxPubPasswordChangeMe123",
    "badge_outbox_pub": "devOnlyBadgeOutboxPubPasswordChangeMe123",
    "badge_service": "devOnlyBadgeServicePasswordChangeMe123",
    "exam_api": "devOnlyExamApiPasswordChangeMe123",
    "auth_api": "devOnlyAuthApiPasswordChangeMe123",
}


def rabbitmq_password_hash(password: str, salt: bytes | None = None) -> str:
    salt = salt or os.urandom(4)
    digest = hashlib.sha256(salt + password.encode("utf-8")).digest()
    return base64.b64encode(salt + digest).decode("ascii")


if __name__ == "__main__":
    if len(sys.argv) == 3:
        user, password = sys.argv[1], sys.argv[2]
        print(f"{user}: {rabbitmq_password_hash(password)}")
    else:
        for user, password in DEV_PASSWORDS.items():
            print(f"{user} ({password}): {rabbitmq_password_hash(password)}")
