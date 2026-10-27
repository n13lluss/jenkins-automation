# PipelineForge

Generates secure Jenkins pipelines. Point it at a Bitbucket (Cloud or Server/Data Center) or GitHub repository, start from scratch without a repository, or import an existing Jenkinsfile. Configure what the pipeline does, then download the files or open a pull request.

- **Languages and build tools**: Java (Maven, Gradle), Node.js (npm, pnpm, Yarn), .NET, Python, Go and PHP (Composer). Monorepos get one module per build, and the modules build in parallel.
- **Multibranch and DTAP/OTAP**: environments with branch patterns (`develop` → Dev/Test, `release/*` → Acceptance, `main` → Production), manual approvals and smoke tests.
- **Deploys**: Kubernetes with Helm (a minimal chart is generated when you have none), Docker Compose over SSH, and Azure App Service with optional slot swap.
- **Deploy safety**:
  - a health check (path, expected status, N successes in a row)
  - automatic rollback when the deploy or the health check fails (Helm rollback, the previous compose release, a slot swap back)
  - Flyway/Liquibase migrations before each deploy
  - application secrets through External Secrets Operator
- **Build once, promote**: acceptance and production can deploy the exact signed digest that passed the previous environment instead of rebuilding.
- **Protected shared library**: the app repository keeps a thin Jenkinsfile. Stage logic, scripts, agent pods and environments live in a library repository that branches cannot change. The Jenkins job registers the library and only trusts fork PRs from contributors.
- **Code quality**: SonarQube or SonarCloud, with a blocking quality gate.
- **Security**:
  - Secret scanning with Gitleaks.
  - Dependency screening with Trivy, OWASP Dependency-Check, OSV-Scanner, Snyk and npm audit.
  - CycloneDX SBOM, license checks, IaC scanning, and container image scans before the push.
  - DAST with OWASP ZAP (baseline, full or API scan) against deployed environments.
- **Supply chain**:
  - Maven Enforcer pinning and a Nexus/Artifactory dependency proxy for every ecosystem
  - Hadolint with trusted base registries
  - Cosign signing, with CycloneDX SBOM and SLSA provenance attestations, verified before each deploy
  - a Kyverno admission policy so the cluster only runs signed images
- **Merge blocking**: one click makes GitHub or Bitbucket require the Jenkins build, approvals and no force pushes on the deploy branches.
- **Accepted risks**: `security/exceptions.yaml` is the single register (owner, ticket, expiry ≤ 180 days). The build fails on expired entries and renders every scanner's ignore file from it.
- **Notifications**: Slack, Microsoft Teams and e-mail for failures, recovery, approvals and production deploys.
- **Delivery metrics**: deploys report to PipelineForge for deploy frequency, lead time, change failure rate and time to restore per environment.
- **Updating existing pipelines**: "Compare with repository" shows what changed per file (generated files carry a version marker) and opens an update PR with only those files.
- **More dynamic testing**:
  - an ephemeral ZAP scan of the just-built image
  - an authenticated two-role scan (access control, logout, session fixation, token entropy)
  - scheduled scans (`Jenkinsfile.scheduled`)
  - a login rate-limit check
- **Tickets**: deduplicated Jira issues from Gitleaks, ZAP, Sonar and scanner findings. Snyk monitor runs on deploy.
- **Stage triggers**: checks on pull requests, packaging and deploys after a merge.
- **Built-in preset "Secure Java (Spring Boot) workflow"**: the full workflow from `project_workflow.docx`. The step-by-step mapping is in [docs/security-checks.md](docs/security-checks.md#coverage-of-the-project-workflow-project_workflowdocx).
- **ISO 27001 profile**: switches on the minimum controls, a production approval gate and an evidence document mapped to Annex A.
- **AI review**:
  - An optional pipeline stage sends the redacted diff to Claude or another OpenAI-compatible or Azure OpenAI model.
  - In the tool, an AI second opinion reviews the generated pipeline.
- **Presets**: team standards (for example "Java backend ISO standard") with versions. Admins can lock settings so they cannot be switched off.
- **Jenkins**: validate the Jenkinsfile with Jenkins' linter, and create or update the multibranch job.
- **CLI**: `pipelineforge generate --config pipeline.json --out .` gives the same validation and output offline, with no network and no secrets.

## Generated files

```
Jenkinsfile                           orchestration only: stages, branches, approvals
jenkins/stages/common.groovy          tool containers, caches, report publishing
jenkins/stages/build.groovy           build + tests per module
jenkins/stages/quality.groovy         Sonar analysis + quality gate
jenkins/stages/security.groovy        secrets, dependencies, SBOM, licenses, IaC
jenkins/stages/deploy.groovy          image build → scan → push, deploy per environment
jenkins/stages/dast.groovy            ZAP against a deployed environment
jenkins/stages/ai-review.groovy       AI review step (+ jenkins/scripts/ai_review.py)
jenkins/stages/tickets.groovy         Jira tickets (+ jenkins/scripts/collect_findings.py)
jenkins/scripts/*_check.py            access-control and rate-limit checks
Jenkinsfile.scheduled                 6-hourly baseline + daily authenticated scan
jenkins/config/environments.yaml      DTAP settings, editable without touching Groovy
jenkins/agents/ci-pod.yaml            Kubernetes pod templates (one container per tool)
sonar-project.properties              when the SonarScanner CLI is used
security/…                            gitleaks, trivyignore, suppressions, ZAP rules and auth plan, maven-enforcer.xml
deploy/helm/… | deploy/docker/…       values per environment, minimal chart, compose files
security/exceptions.yaml              accepted risks (with the exception register)
deploy/policies/kyverno-policies.yaml signed-image and pod-security admission policies
library/vars/… library/resources/…    the protected shared library (library layout)
docs/PIPELINE.md                      stages, required plugins and credentials
docs/iso27001-evidence.md             control mapping (ISO profile)
```

## Architecture

| Part | Tech | Folder |
|---|---|---|
| Frontend + BFF | Next.js 16 (App Router), TypeScript, Tailwind | `frontend/` |
| API | ASP.NET Core (.NET 10) | `backend/src/PipelineForge.Api` |
| Generator, scanner, importer | .NET class library, no infrastructure dependencies | `backend/src/PipelineForge.Core` |
| Database, SCM, AI, vault, Jenkins clients | EF Core + Npgsql, HttpClient, Anthropic SDK, VaultSharp, Azure SDK | `backend/src/PipelineForge.Infrastructure` |
| Identity | OIDC: Keycloak locally, or Entra ID | `deploy/compose/keycloak` |
| Secrets | HashiCorp Vault or Azure Key Vault | `deploy/compose/vault` |

More detail is in [docs/architecture.md](docs/architecture.md), [docs/security.md](docs/security.md), [docs/security-checks.md](docs/security-checks.md) and [docs/iso27001-mapping.md](docs/iso27001-mapping.md).

## Run locally

Requirements: Docker with Compose.

```sh
scripts/dev-env.sh          # writes .env with random local secrets
docker compose up --build
```

Open http://localhost:3000 and sign in as `alice`, `bob` or `victor`, using the password `PF_DEMO_PASSWORD` from `.env`. On first sign-in, Keycloak makes you set up an authenticator app (TOTP). The first person to create a workspace becomes its admin and can add the others as maintainer or viewer.

Then:

1. **Settings**: optionally allow AI calls for the workspace.
2. **Connections**: add Bitbucket or GitHub (a token with repository read and write, plus pull requests), Jenkins, and an AI provider. Tokens go straight into Vault.
3. **Pipelines**: choose Scan, Start from scratch, or Import. Go through the steps, save, and then download the zip or open a pull request.

To call internal hosts (for example a Bitbucket Server on your LAN, or the bundled Jenkins), set `OUTBOUND_ALLOW_PRIVATE=true` (and `OUTBOUND_ALLOW_HTTP=true` for plain http) in `.env`, or list the host in `OUTBOUND_PRIVATE_HOST`.

### End-to-end tests

`e2e/run.sh` runs a generated pipeline on a real Jenkins: build, scans, signing, deploy, health check, DAST, rate-limit and access-control checks against a Spring Boot sample. `e2e/ui.sh` drives the web app with Playwright. Both run in CI; see [e2e/README.md](e2e/README.md).

### CLI

```sh
dotnet run --project backend/src/PipelineForge.Cli -- presets
dotnet run --project backend/src/PipelineForge.Cli -- generate --config pipeline.json --out ./out --preset builtin-secure-java
```

### Lint generated Jenkinsfiles

```sh
docker compose --profile validation up -d jenkins
PF_SAMPLES_DIR=/tmp/pf-samples dotnet test backend --filter ExportSamples
JENKINS_TOKEN=<JENKINS_ADMIN_PASSWORD> scripts/validate-jenkinsfiles.sh /tmp/pf-samples
```

## Develop

```sh
# backend
cd backend
dotnet build && dotnet test
dotnet tool restore && dotnet ef migrations add <Name> -p src/PipelineForge.Infrastructure -s src/PipelineForge.Infrastructure -o Persistence/Migrations

# frontend (needs .env.local, see frontend/.env.example)
cd frontend
npm ci && npm run dev
npm run lint && npx tsc --noEmit && npm run build
```

Tool image versions for generated pipelines are pinned in `backend/src/PipelineForge.Core/Generation/ToolImages.cs`. Review them regularly.
