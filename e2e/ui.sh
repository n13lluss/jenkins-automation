#!/usr/bin/env bash
# UI end-to-end test: starts the full compose stack with a realm whose demo users skip TOTP, then runs Playwright.
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/.." && pwd)
cd "$ROOT"
WORK=$(mktemp -d /tmp/pf-ui.XXXXXX)
export E2E_REALM="$WORK/realm.json"
jq '.users |= map(.requiredActions = [])' deploy/compose/keycloak/realm.json > "$E2E_REALM"
chmod 644 "$E2E_REALM"
[ -e .env ] || scripts/dev-env.sh
COMPOSE=(docker compose -f compose.yaml -f compose.e2e.yaml)
trap '"${COMPOSE[@]}" logs --no-color > "$WORK/compose.log" 2>&1 || true; "${COMPOSE[@]}" down -v >/dev/null 2>&1 || true' EXIT
"${COMPOSE[@]}" up -d --build
for _ in $(seq 1 90); do
  curl -fsS -o /dev/null http://127.0.0.1:8081/realms/pipelineforge/.well-known/openid-configuration 2>/dev/null \
    && curl -fsS -o /dev/null http://127.0.0.1:3000/api/auth/login 2>/dev/null && break
  sleep 5
done
cd frontend
E2E_PASSWORD=$(grep '^PF_DEMO_PASSWORD=' ../.env | cut -d= -f2-) npx playwright test
