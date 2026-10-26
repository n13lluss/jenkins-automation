# Security design and threat model

PipelineForge holds tokens that can write to repositories and reach CI/CD systems, and it generates code that runs in pipelines. It is therefore treated as a high-value target.

## Controls

| Area | Control |
|---|---|
| Authentication | OIDC authorization code + PKCE (S256), state and nonce, ID token signature/issuer/audience checks. The identity provider handles MFA; the bundled Keycloak realm requires TOTP. There are no local passwords. |
| Sessions | The BFF keeps tokens server-side, in an encrypted (JWE `dir`/A256GCM) httpOnly `SameSite=Strict` cookie that is `Secure` over https. The browser never sees access tokens. Refresh tokens rotate (`revokeRefreshToken`). |
| CSRF | Anything other than GET through the BFF needs `Origin` = app URL, `Sec-Fetch-Site: same-origin` and an `x-pf-csrf` header. Logout is POST only. |
| API auth | JWT bearer: issuer, audience (`pipelineforge-api`) and lifetime are validated, with 30 s clock skew. The fallback policy requires authentication. |
| Authorization | Roles are per workspace. The admin manages connections, members, locks and the audit log; the maintainer edits pipelines and runs actions; the viewer reads and downloads. Every query is also filtered by workspace id (tested against IDOR). |
| Secrets | Secrets are stored in Vault (AppRole, least-privilege policy) or Azure Key Vault (managed identity). The database stores only opaque references, and the names are generated server-side. Secrets are write-only in the API and never returned or logged. |
| SSRF | Base URLs for SCM, Jenkins and AI must be https (unless explicitly allowed) and contain no credentials. An optional host allow-list restricts them further. The resolved IP is checked when the connection is opened, which blocks private, loopback and link-local addresses and DNS rebinding; cloud metadata addresses are always blocked. Redirects are not followed, and paging links must stay on the same origin. |
| Pipeline injection | Every config value is allow-listed (`ConfigValidator`) and escaped (`GroovyEscaper`). Shell steps use single-quoted Groovy, and values reach the shell as environment variables. Custom stages must have balanced braces, so they cannot close the surrounding stage; they are flagged as unchecked. |
| Generated pipelines | Credentials are bound with `withCredentials` and kept out of logs (`set +x`) and out of the workspace (`@tmp`). Images are pinned. Images are scanned before they are pushed. Production deploys run only from the release branch, after an approval from a named group. SSH uses a pinned `known_hosts` with `StrictHostKeyChecking=yes`. Remote hosts pull images with their own registry login. Kubernetes pods do not mount service-account tokens. |
| Untrusted repositories | Scans read files through the API: nothing is cloned or executed. XML is parsed with DTDs disabled (no XXE). JSON has depth limits. File size and read count are limited. All regexes have a process-wide 2 s timeout. |
| AI data | AI is opt-in per workspace. Only generated files and the repository profile are sent, never source; the in-pipeline review sends the diff. Secrets are redacted first. Each call is audited with a payload SHA-256. Prompts mark the input as untrusted. |
| Web | Strict CSP with nonces and `strict-dynamic`, `frame-ancestors 'none'`, nosniff, HSTS (production), Referrer-Policy and Permissions-Policy. The API returns `default-src 'none'`. |
| Abuse | A per-user rate limit of 300/min, and 20/min for expensive actions (scan, PR, Jenkins, AI). Request bodies are capped at 1 MB. |
| Audit | Append-only (the DbContext rejects updates and deletes). It records workspace, member, connection, preset and pipeline changes, scans, downloads, PRs, Jenkins calls and AI calls. Grant the database user `INSERT, SELECT` only on `AuditEvents` in production. |
| Supply chain | Lockfiles, pinned base images, a chiseled .NET runtime, distroless Node, non-root and read-only containers with no capabilities. CI runs Gitleaks, Trivy, `npm audit` and the NuGet vulnerability check. |

## STRIDE summary

| Threat | Example | Mitigation |
|---|---|---|
| Spoofing | Stolen session cookie | httpOnly, Strict, encrypted cookie; short access tokens; MFA at the identity provider |
| Tampering | Injecting Groovy through a branch name or URL | Allow-list validation, escaping, single-quoted shell steps, runtime re-checks in `common.safe()` |
| Tampering | A maintainer turning off a locked ISO control | Locks re-applied server-side on every save; only admins change locks |
| Repudiation | "I didn't open that PR" | Append-only audit log with user, IP and target |
| Information disclosure | Token leakage through the API or logs | Vault references only; write-only secrets; ProblemDetails without stack traces; redaction before AI calls |
| Information disclosure | SSRF to cloud metadata via a connection URL | Connect-time IP filtering; metadata always blocked; no redirects |
| Denial of service | Huge repositories or files, ReDoS | File and read limits, regex timeouts, body limits, rate limiting |
| Elevation of privilege | Viewer creating connections; cross-workspace IDOR | Role policies plus workspace-scoped queries (integration tested) |

## Production checklist

- Serve over https only; set `APP_URL` to the https URL so cookies are `Secure`. Use a real Vault (TLS, auto-unseal) or Azure Key Vault.
- Register the app at your identity provider (Entra ID or Keycloak) with PKCE, and enforce MFA there.
- Set `Outbound:AllowedHosts` to your SCM, Jenkins and AI hosts.
- Run database migrations from a separate job; give the runtime user least privilege.
- Use fine-grained, repository-scoped SCM tokens. Use a Jenkins user limited to the folder that holds the generated jobs.
