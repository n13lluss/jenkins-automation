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

All dependency scanners run, and then the stage fails once with the combined result, so one failure does not hide the others. SARIF reports are published with the Warnings NG plugin, and every report is archived and fingerprinted.

Accepting a risk is done in version control, with a reason and an expiry date:

- **Trivy**: add the ID to `security/.trivyignore` with an `exp:` date.
- **Dependency-Check**: add an entry to `security/dependency-check-suppressions.xml` with `until=`.
- **ZAP**: set the rule to `IGNORE` or `WARN` in `security/zap-rules.tsv`.
- **Gitleaks**: add an allowlist entry to `security/.gitleaks.toml`.
