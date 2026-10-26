# Security checks in generated pipelines

| Check | Tool | Stage | Fails the build | Report |
|---|---|---|---|---|
| Secret scanning | Gitleaks (git history) | Secret scan | any finding | `reports/gitleaks.sarif` |
| Dependency vulnerabilities | Trivy fs | Dependency scan | at or above the threshold | `reports/trivy-fs.sarif` |
| Dependency vulnerabilities | OWASP Dependency-Check (NVD) | Dependency scan | CVSS at or above the threshold | `reports/dependency-check/` |
| Dependency vulnerabilities | OSV-Scanner | Dependency scan | any finding | `reports/osv.sarif` |
| Dependency vulnerabilities | Snyk | Dependency scan | at or above the threshold | `reports/snyk.sarif` |
| Dependency vulnerabilities | npm audit (prod deps) | Dependency scan | at or above the threshold | build log |
| SBOM | Trivy, CycloneDX | SBOM | never | `reports/sbom.cdx.json` |
| Licenses | Trivy license scanner | License check | restricted/forbidden at or above the threshold | `reports/licenses.txt` |
| IaC misconfiguration | Trivy config | IaC scan | at or above the threshold | `reports/trivy-iac.sarif` |
| Static analysis | SonarQube / SonarCloud | Analysis + Quality gate | gate failure (configurable) | Sonar |
| Container image | Trivy image (scans the tarball before push) | Container scan | at or above the threshold | `reports/trivy-image-*.sarif` |
| DAST | OWASP ZAP baseline/full/API | DAST <env> | FAIL rules in `security/zap-rules.tsv` | `reports/zap-<env>.html/json` |
| AI review | Claude / OpenAI-compatible / Azure OpenAI | AI security review | optional, at or above the minimum severity | `reports/ai-review.md/.sarif` |

| Dependency pinning | Maven Enforcer (`security/maven-enforcer.xml`) | Dependency pinning | LATEST/RELEASE, ranges, unpinned plugins | build log |
| Ephemeral DAST | OWASP ZAP baseline against the just-built image | Ephemeral DAST | FAIL rules | `reports/zap-ephemeral.*` |
| Access control | `access_control_check.py` + ZAP Automation Framework (two roles) | Authenticated scan | user reaches admin path, logout/session issues, High ZAP alerts | `reports/access-control-<env>.*`, `reports/zap-auth-<env>.*` |
| Login throttling | `rate_limit_check.py` | Rate-limit check <env> | no 429/423 within N attempts | `reports/rate-limit-<env>.json` |
| Image integrity | Cosign sign + verify (by digest) | Sign image / Deploy | signature does not verify | `reports/image-digests/` |
| Continuous CVEs | Snyk monitor | Deploy | never (alerts in Snyk) | Snyk |
| Scheduled DAST | ZAP baseline (`Jenkinsfile.scheduled`) | Scheduled security scans | FAIL rules | `reports/zap-scheduled-<env>.*` |

All dependency scanners run, and then the stage fails once with the combined result, so one failure does not hide the others. SARIF reports are published with the Warnings NG plugin, and every report is archived and fingerprinted.

Accepting a risk is done in version control, with a reason and an expiry date:

- **Trivy**: add the ID to `security/.trivyignore` with an `exp:` date.
- **Dependency-Check**: add an entry to `security/dependency-check-suppressions.xml` with `until=`.
- **ZAP**: set the rule to `IGNORE` or `WARN` in `security/zap-rules.tsv`.
- **Gitleaks**: add an allowlist entry to `security/.gitleaks.toml`.

## Coverage of the project workflow (project_workflow.docx)

The built-in preset **"Secure Java (Spring Boot) workflow"** switches all of these on. Each step is also available as a separate option.

| # | Trigger | Step | Generated as |
|---|---|---|---|
| 1 | PR → develop | Build + Maven Enforcer | `Dependency pinning` stage, `Build & test` (Maven via Nexus `maven-settings.xml`) |
| 2 | PR → develop | Unit tests | `Build & test` |
| 3 | PR → develop | SonarQube with injection rules, blocking gate | `SonarQube analysis` + `Quality gate` (quality profile guidance in `docs/PIPELINE.md`) |
| 4 | PR → develop | Snyk SCA through Nexus | `Dependency scan` (`snyk test` inside `withProxy`) |
| 5 | PR → develop | Gitleaks on the diff | `Secret scan` with scope "diff" |
| 6 | PR → develop, IaC changed | Snyk IaC | `IaC scan` (Snyk, only when infrastructure changed) |
| 7–8 | Merge to develop | Package, containerize | `Build image` (trigger: deploy branches) |
| 9 | Merge to develop | Cosign sign | `Sign image` (after push: Cosign signs registry digests) |
| 10 | Merge to develop | Ephemeral ZAP baseline | `Ephemeral DAST` |
| 11 | Merge to develop | Snyk container scan | `Container scan` (Snyk) |
| 12 | Merge to develop | Cosign verify | start of every `Deploy <env>` |
| 13 | Merge to develop | Deploy | `Deploy <env>` by digest |
| 14 | Post-deploy | Login rate-limit check | `Rate-limit check <env>` |
| 15 | Daily / pre-deploy | Authenticated two-role ZAP | `Jenkinsfile.scheduled` (first run of the day) and optionally before production |
| 16 | Every 6 hours | ZAP baseline on the live app | `Jenkinsfile.scheduled` cron `H */6 * * *` |
| 17–18, 20 | On deploy / continuous | Snyk monitor and alerts | `snyk monitor` after each deploy; alerting and Jira in Snyk |
| 19, 21 | After Gitleaks/ZAP/Sonar | Deduplicated Jira tickets | `tickets.groovy` (Jira Pipeline Steps) |

One deliberate difference: signing happens after the push, because Cosign signs a registry digest. The image is still scanned (as a tarball) before it is pushed, and every deploy verifies the signature first.
