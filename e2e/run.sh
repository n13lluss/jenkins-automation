#!/usr/bin/env bash
# End-to-end test: generates the pipeline for e2e/sample-app with the PipelineForge CLI, runs it on a real
# Jenkins with Docker agents, and checks that it built, scanned, signed, deployed and verified the app.
# Needs Docker with compose, the .NET SDK, git, curl and jq. In CI it also configures the Docker daemon.
set -euo pipefail

ROOT=$(cd "$(dirname "$0")/.." && pwd)
WORK=${E2E_WORK:-$(mktemp -d /tmp/pf-e2e.XXXXXX)}
export E2E_HOME="$WORK/jenkins_home" E2E_SECRETS="$WORK/secrets" E2E_REPO="$WORK/repo"
export DOCKER_GID=$(stat -c %g /var/run/docker.sock)
COMPOSE=(docker compose -f "$ROOT/e2e/compose.yaml")
JENKINS=http://127.0.0.1:8090
TIMEOUT_MIN=${E2E_TIMEOUT_MIN:-40}

log() { printf '\n== %s\n' "$*"; }
cleanup() {
  if [ "${E2E_KEEP:-}" != 1 ]; then
    "${COMPOSE[@]}" down -v --remove-orphans >/dev/null 2>&1 || true
    # The deploy target started the app as compose project "orders" on the host daemon.
    docker ps -aq --filter label=com.docker.compose.project=orders | xargs -r docker rm -f >/dev/null 2>&1 || true
  fi
}
trap cleanup EXIT

# The registry is reached as <docker bridge gateway>:5000 by the host daemon (push, compose pull) and by tool
# containers (cosign); the daemon must allow plain http for it.
GW=$(docker network inspect bridge -f '{{(index .IPAM.Config 0).Gateway}}')
REGISTRY="$GW:5000"
if [ "${CI:-}" = true ]; then
  log "Allowing the insecure test registry $REGISTRY in the Docker daemon"
  conf=/etc/docker/daemon.json
  current=$( [ -s "$conf" ] && sudo cat "$conf" || echo '{}')
  echo "$current" | jq --arg r "$REGISTRY" '."insecure-registries" = ((."insecure-registries" // []) + [$r] | unique)' | sudo tee "$conf" >/dev/null
  sudo systemctl restart docker
  for _ in $(seq 1 30); do docker info >/dev/null 2>&1 && break; sleep 1; done
fi

log "Secrets and keys in $WORK"
mkdir -p "$E2E_HOME" "$E2E_SECRETS" "$E2E_REPO"
umask 022
openssl rand -hex 16 > "$E2E_SECRETS/jenkins-admin-password"
openssl rand -hex 16 > "$E2E_SECRETS/user-password"
openssl rand -hex 16 > "$E2E_SECRETS/admin-password"
openssl rand -hex 16 > "$E2E_SECRETS/cosign-password"
ssh-keygen -q -t ed25519 -N '' -f "$E2E_SECRETS/deploy_key"
printf 'E2E_USER_PASSWORD=%s\nE2E_ADMIN_PASSWORD=%s\n' "$(cat "$E2E_SECRETS/user-password")" "$(cat "$E2E_SECRETS/admin-password")" > "$E2E_SECRETS/app.env"
docker run --rm -u "$(id -u):$(id -g)" -e COSIGN_PASSWORD="$(cat "$E2E_SECRETS/cosign-password")" -v "$E2E_SECRETS:/keys" -w /keys \
  gcr.io/projectsigstore/cosign:v2.6.1 generate-key-pair >/dev/null
chmod 644 "$E2E_SECRETS"/*
# Jenkins runs as uid 1000.
sudo chown -R 1000:1000 "$E2E_HOME" 2>/dev/null || chmod 777 "$E2E_HOME"

log "Generating the pipeline with the CLI"
sed "s/__REGISTRY__/$REGISTRY/; s/__APP_HOST__/$GW/" "$ROOT/e2e/config.json" > "$WORK/config.json"
cp -r "$ROOT/e2e/sample-app/." "$E2E_REPO/"
rm -rf "$E2E_REPO/target"
dotnet run --project "$ROOT/backend/src/PipelineForge.Cli" -c Release -- generate --config "$WORK/config.json" --out "$E2E_REPO"
mkdir -p "$E2E_REPO/security" && cp "$E2E_SECRETS/cosign.pub" "$E2E_REPO/security/cosign.pub"
git -C "$E2E_REPO" init -q -b develop
git -C "$E2E_REPO" -c user.name=e2e -c user.email=e2e@example.com add -A
git -C "$E2E_REPO" -c user.name=e2e -c user.email=e2e@example.com commit -q -m "Sample app with a PipelineForge pipeline"

log "Starting registry, deploy target and Jenkins"
"${COMPOSE[@]}" up -d --build registry deploy-target
# known_hosts pins the target's host key, read from the container rather than scanned over the network.
for _ in $(seq 1 30); do "${COMPOSE[@]}" exec -T deploy-target test -s /opt/orders/app.env 2>/dev/null && break; sleep 1; done
"${COMPOSE[@]}" exec -T deploy-target cat /home/deploy/sshd/ssh_host_ed25519_key.pub | awk '{print "[deploy-target]:2222 " $1 " " $2}' > "$E2E_SECRETS/known_hosts"
chmod 644 "$E2E_SECRETS/known_hosts"
test -s "$E2E_SECRETS/known_hosts"
"${COMPOSE[@]}" up -d --build jenkins

PASS=$(cat "$E2E_SECRETS/jenkins-admin-password")
api() { curl -fsS -u "admin:$PASS" "$@"; }
log "Waiting for Jenkins"
for _ in $(seq 1 120); do
  api -o /dev/null "$JENKINS/job/orders/api/json" 2>/dev/null && break
  sleep 5
done
api -o /dev/null "$JENKINS/job/orders/api/json"

log "Starting the build"
CRUMB=$(curl -fsS -c "$WORK/cookies" -u "admin:$PASS" "$JENKINS/crumbIssuer/api/json" | jq -r '.crumbRequestField + ":" + .crumb')
curl -fsS -b "$WORK/cookies" -u "admin:$PASS" -H "$CRUMB" -X POST "$JENKINS/job/orders/build" -o /dev/null
for _ in $(seq 1 60); do
  api -o /dev/null "$JENKINS/job/orders/1/api/json" 2>/dev/null && break
  sleep 2
done

deadline=$(( $(date +%s) + TIMEOUT_MIN * 60 ))
result=null
while [ "$(date +%s)" -lt "$deadline" ]; do
  result=$(api "$JENKINS/job/orders/1/api/json" | jq -r '.result')
  [ "$result" != null ] && break
  sleep 15
done
api "$JENKINS/job/orders/1/consoleText" > "$WORK/console.log" || true
echo "Build result: $result (console log: $WORK/console.log)"
if [ "$result" != SUCCESS ]; then
  tail -n 200 "$WORK/console.log"
  exit 1
fi

log "Checking the results"
artifacts=$(api "$JENKINS/job/orders/1/api/json?tree=artifacts%5BrelativePath%5D" | jq -r '.artifacts[].relativePath')
echo "$artifacts"
for expected in reports/gitleaks.sarif reports/trivy-fs.sarif reports/trivy-image-orders.sarif reports/zap-ephemeral reports/access-control-test.json reports/exceptions.md reports/image-digests/orders.txt; do
  echo "$artifacts" | grep -q "^$expected" || { echo "missing artifact $expected"; exit 1; }
done
grep -q "Rate limiting works" "$WORK/console.log" || { echo "rate-limit check did not pass"; exit 1; }
grep -q "Healthy: 3 answers" "$WORK/console.log" || { echo "health check did not pass"; exit 1; }

digest=$(api "$JENKINS/job/orders/1/artifact/reports/image-digests/orders.txt")
echo "Deployed $REGISTRY/e2e/orders@$digest"
docker run --rm --network host -v "$E2E_SECRETS:/keys:ro" gcr.io/projectsigstore/cosign:v2.6.1 \
  verify --key /keys/cosign.pub --insecure-ignore-tlog=true --allow-insecure-registry "localhost:5000/e2e/orders@$digest" >/dev/null
echo "Signature verified"
curl -fsS "http://127.0.0.1:18080/actuator/health" | grep -q '"UP"'
echo "The deployed app is healthy"
log "End-to-end test passed"
