#!/bin/sh
# Initialises and unseals the local Vault, enables KV v2 and an AppRole for the backend.
# DEVELOPMENT ONLY: the unseal key and root token are kept in the vault-init volume.
set -eu

export VAULT_ADDR=http://vault:8200
KEYS=/vault/init/keys.json
OUT=/vault/approle

echo "Waiting for Vault..."
while :; do
  rc=0
  vault status >/dev/null 2>&1 || rc=$?
  [ "$rc" -eq 0 ] || [ "$rc" -eq 2 ] && break
  sleep 1
done

if ! vault status 2>/dev/null | grep -Eq 'Initialized +true'; then
  echo "Initialising Vault"
  umask 077
  vault operator init -key-shares=1 -key-threshold=1 -format=json > "$KEYS"
fi

UNSEAL_KEY=$(awk '/"unseal_keys_b64"/ { getline; gsub(/[ ",]/, ""); print; exit }' "$KEYS")
ROOT_TOKEN=$(sed -n 's/.*"root_token": *"\([^"]*\)".*/\1/p' "$KEYS")

if vault status 2>/dev/null | grep -Eq 'Sealed +true'; then
  echo "Unsealing Vault"
  vault operator unseal "$UNSEAL_KEY" >/dev/null
fi

export VAULT_TOKEN="$ROOT_TOKEN"
vault secrets list | grep -q '^secret/' || vault secrets enable -path=secret kv-v2
vault auth list | grep -q '^approle/' || vault auth enable approle

cat > /tmp/pipelineforge.hcl <<'HCL'
path "secret/data/pipelineforge/*" {
  capabilities = ["create", "read", "update"]
}
path "secret/metadata/pipelineforge/*" {
  capabilities = ["read", "delete"]
}
HCL
vault policy write pipelineforge /tmp/pipelineforge.hcl >/dev/null
vault write auth/approle/role/pipelineforge token_policies=pipelineforge token_ttl=1h token_max_ttl=4h secret_id_ttl=0 >/dev/null

vault read -field=role_id auth/approle/role/pipelineforge/role-id > "$OUT/role-id"
if [ ! -s "$OUT/secret-id" ]; then
  vault write -f -field=secret_id auth/approle/role/pipelineforge/secret-id > "$OUT/secret-id"
fi
# The backend container runs as a different non-root user.
chmod 0444 "$OUT/role-id" "$OUT/secret-id"
echo "Vault ready"
