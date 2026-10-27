# End-to-end tests

Both run in GitHub Actions (`.github/workflows/ci.yml`) and locally with Docker.

## Generated pipeline on a real Jenkins (`e2e/run.sh`)

1. Creates throwaway secrets: Cosign key pair, SSH deploy key, test-user and Jenkins passwords.
2. Generates the pipeline for `sample-app/` from `config.json` with the PipelineForge CLI. The config uses Docker agents, Gitleaks, Trivy, Hadolint, ephemeral ZAP, Cosign, an exception register, a Docker-over-SSH deploy with health check and rollback, DAST, the login rate-limit check and the two-role access-control check.
3. Commits the app plus the pipeline to a local git repository. Then it starts a registry, an SSH deploy target and a Jenkins controller configured as code (`jenkins/casc.yaml`).
4. Runs the job and requires `SUCCESS`, the expected reports, a valid Cosign signature on the deployed digest and a healthy app.

```bash
e2e/run.sh                    # E2E_KEEP=1 keeps the stack running; Jenkins is on http://127.0.0.1:8090
```

On a developer machine, add the registry printed by the script (`<docker bridge gateway>:5000`) to the Docker daemon's `insecure-registries` first; in CI the script does this itself. Sonar, Snyk and Jira are left out because they need accounts.

## Browser journey (`e2e/ui.sh`)

1. Starts the full compose stack with `compose.e2e.yaml`. The demo users skip TOTP enrolment there.
2. Runs Playwright (`frontend/e2e/`):
   - sign in
   - create a workspace and a pipeline from scratch
   - save it and download the zip
   - check that a viewer cannot edit
