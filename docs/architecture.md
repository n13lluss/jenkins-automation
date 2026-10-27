# Architecture

```
Browser ──► Next.js (BFF) ──► ASP.NET Core API ──► PostgreSQL
               │   ▲                 │  ├──► Vault / Azure Key Vault (secrets)
               │   │                 │  ├──► Bitbucket Cloud / Server, GitHub
               ▼   │                 │  ├──► Jenkins
           OIDC provider (Keycloak, Entra ID)  └──► Claude / OpenAI-compatible
```

## Flow

1. **Input**: produces a `PipelineConfig`. There are three ways in:
   - **Scan**: `RepoScanner` reads the file list and a bounded number of manifests (`pom.xml`, `package.json`, `go.mod`, …) through the SCM API. `SuggestionEngine` turns the resulting `RepoProfile` into a config, with a reason for each choice.
   - **Blank**: the user adds modules by hand.
   - **Import**: `JenkinsfileImporter` parses a declarative Jenkinsfile. It maps known stages onto the model, keeps unknown stages as custom stages, and reports the gaps.
2. **Preset**: optionally merged with JSON Merge Patch. Locked paths are re-applied on every save.
3. **Compliance**: `ComplianceEnforcer` applies the ISO 27001 minimums and reports every change.
4. **Validation**: `ConfigValidator` allow-lists every value that reaches a generated file.
5. **Generation**: `PipelineGenerator` renders the files with code writers, not text templates, so escaping is done in one place (`GroovyEscaper`).
6. **Output**: zip download, a pull request (branch + commit + PR) through the SCM API, Jenkins linting and job creation. Each output records a `GenerationRun` with file hashes, plus an audit event.
   - In the shared library layout, `LibraryWriter` moves pipeline-owned files under `library/`, and the pull request action opens a second PR on the library repository.
7. **Drift**: `VersionMarker` stamps every generated file. `DriftComparer` compares the files with the repository copies (missing / identical / changed / edited by hand) and renders unified diffs, so an update PR can carry only what changed.
8. **Delivery metrics**: deploys post to `POST /api/ingest/deployments` with a per-pipeline token. `DeliveryMetrics` computes deploy frequency, lead time, change failure rate and time to restore per environment.

The same generator runs offline in `PipelineForge.Cli`, which `e2e/run.sh` uses to test a generated pipeline end to end on a real Jenkins.

## Backend projects

- `PipelineForge.Core` has no infrastructure dependencies and is fully unit tested. It holds:
  - the model, validator, scanner, importer, generator, compliance, presets and redactor
  - the `ISourceControlProvider` and `IAiReviewProvider` contracts
- `PipelineForge.Infrastructure` holds:
  - the EF Core `AppDbContext`, with an append-only audit table
  - SCM providers and the SSRF-guarded `HttpClient`
  - Vault and Key Vault secret stores
  - the Anthropic and OpenAI-compatible providers
  - the Jenkins client
- `PipelineForge.Api` holds:
  - controllers and JWT validation
  - workspace RBAC (`WorkspaceRoleHandler`)
  - rate limits, security headers and ProblemDetails

## Frontend

- `src/proxy.ts`: per-request CSP nonce and security headers, and a redirect to sign-in.
- `src/app/api/auth/*`: OIDC authorization code flow with PKCE, state and nonce, run on the server. Tokens are kept in an encrypted (A256GCM), httpOnly, SameSite=Strict cookie, split into chunks.
- `src/app/api/proxy/[...path]`: forwards calls to the API with the bearer token and refreshes it when needed. It requires the request to come from the same origin and carry the `x-pf-csrf` header for anything other than GET.
- `src/components/editor/*`: the wizard. It previews the generated files live (`POST /pipelines/preview`) and sends you to the step that has an error.

## Data model

| Table | Holds |
|---|---|
| Users | users keyed by OIDC issuer + subject |
| Workspaces, Memberships | teams and roles (admin, maintainer, viewer) |
| Connections | SCM, Jenkins and AI connections; only a reference to the secret is stored |
| Presets, PresetVersions | team standards and their versions |
| Projects | saved pipeline configs |
| GenerationRuns | file hashes, config hash and PR URL for each generation |
| IngestTokens | per-pipeline deploy-report tokens (SHA-256 only), revocation and last use |
| DeploymentEvents | reported deploys: environment, digest, commit, timings, result |
| AuditEvents | append-only audit log |
