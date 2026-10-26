#!/bin/sh
# Writes a .env with random local secrets. Refuses to overwrite an existing file.
set -eu
cd "$(dirname "$0")/.."
[ -e .env ] && { echo ".env already exists"; exit 1; }
rand() { head -c 48 /dev/urandom | base64 | tr -dc 'A-Za-z0-9' | head -c "${1:-32}"; }
umask 077
cat > .env <<ENV
POSTGRES_PASSWORD=$(rand)
KEYCLOAK_ADMIN_PASSWORD=$(rand 20)
PF_CLIENT_SECRET=$(rand)
PF_DEMO_PASSWORD=$(rand 16)
SESSION_SECRET=$(rand 48)
JENKINS_ADMIN_PASSWORD=$(rand 20)
OUTBOUND_ALLOW_HTTP=false
OUTBOUND_ALLOW_PRIVATE=false
OUTBOUND_PRIVATE_HOST=jenkins
ENV
echo "Wrote .env. Demo users alice, bob and victor use PF_DEMO_PASSWORD; they set up TOTP at first sign-in."
