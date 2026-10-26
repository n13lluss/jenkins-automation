#!/bin/sh
# Lints every Jenkinsfile under a directory with Jenkins' declarative linter.
# Usage: JENKINS_URL=http://localhost:8082 JENKINS_USER=admin JENKINS_TOKEN=... scripts/validate-jenkinsfiles.sh <dir>
#   Start the linter with: docker compose --profile validation up -d jenkins
#   Export samples with:   PF_SAMPLES_DIR=/tmp/samples dotnet test backend --filter ExportSamples
set -eu
DIR=${1:?usage: validate-jenkinsfiles.sh <dir>}
: "${JENKINS_URL:=http://localhost:8082}" "${JENKINS_USER:=admin}" "${JENKINS_TOKEN:?set JENKINS_TOKEN (password or API token)}"
failed=0
for f in $(find "$DIR" -name Jenkinsfile | sort); do
  out=$(curl -sS --fail-with-body -u "$JENKINS_USER:$JENKINS_TOKEN" -X POST --data-urlencode "jenkinsfile@$f" "$JENKINS_URL/pipeline-model-converter/validate")
  if echo "$out" | grep -q "successfully validated"; then
    echo "ok    $f"
  else
    echo "FAIL  $f"
    echo "$out" | sed 's/^/      /'
    failed=1
  fi
done
exit $failed
