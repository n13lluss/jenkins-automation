#!/usr/bin/env bash
# Renders every generated Helm chart and validates it, the Kyverno policies and the Compose files.
# Usage: scripts/validate-manifests.sh <samples dir>   (export samples with PF_SAMPLES_DIR=... dotnet test --filter ExportSamples)
# Needs helm and kubeconform; CRDs (Kyverno, External Secrets) are validated with the datree CRD catalog.
set -euo pipefail
DIR=${1:?usage: validate-manifests.sh <samples dir>}
CATALOG='https://raw.githubusercontent.com/datreeio/CRDs-catalog/main/{{.Group}}/{{.ResourceKind}}_{{.ResourceAPIVersion}}.json'
DIGEST="sha256:$(printf '%064d' 0)"
validate() { kubeconform -strict -summary -schema-location default -schema-location "$CATALOG" "$@"; }

failed=0
for chart in $(find "$DIR" -path '*/deploy/helm/chart/Chart.yaml' | sort); do
  sample=$(dirname "$(dirname "$(dirname "$(dirname "$chart")")")")
  for values in "$sample"/deploy/helm/values-*.yaml; do
    sets=()
    # Image keys are the direct children of the top-level "images:" block.
    for img in $(awk '/^images:/{inside=1; next} /^[^ ]/{inside=0} inside && /^  [a-z0-9-]+:$/{sub(/^  /,""); sub(/:$/,""); print}' "$values"); do
      sets+=(--set-string "images.$img.tag=e2e" --set-string "images.$img.digest=$DIGEST")
    done
    echo "== $values"
    helm template release "$(dirname "$chart")" -f "$values" "${sets[@]}" > "$values.rendered" || { failed=1; continue; }
    validate "$values.rendered" || failed=1
  done
done
for policy in $(find "$DIR" -path '*/deploy/policies/*.yaml' | sort); do
  echo "== $policy"
  validate "$policy" || failed=1
done
if command -v docker >/dev/null 2>&1 && docker compose version >/dev/null 2>&1; then
  for compose in $(find "$DIR" -path '*/deploy/docker/compose.*.yml' | sort); do
    echo "== $compose"
    vars=()
    for v in $(grep -o 'IMAGE_DIGEST_[A-Z0-9_]*' "$compose" | sort -u); do vars+=("$v=$DIGEST"); done
    # The env file lives on the deploy host; an empty one stands in for it.
    (cd "$(dirname "$compose")" && touch app.env && env "${vars[@]}" docker compose -f "$(basename "$compose")" config -q) || failed=1
  done
fi
exit $failed
