# ISO/IEC 27001:2022 mapping

The ISO 27001 profile does two things. It enforces the technical part of the Annex A controls below. It also generates `docs/iso27001-evidence.md` in the target repository, which lists where each build's evidence is stored. Organisational measures stay with the organisation: policies, access reviews, approver lists and supplier management.

| Control | Title | What the profile enforces |
|---|---|---|
| A.5.37 | Documented operating procedures | `docs/PIPELINE.md`: stages, plugins, credentials |
| A.8.8 | Management of technical vulnerabilities | dependency scan (at least one scanner), container scan, SBOM, failing on High and Critical |
| A.8.9 | Configuration management | IaC scan; environments versioned in `jenkins/config/environments.yaml` |
| A.8.15 | Logging | reports archived with fingerprints and SARIF published; build history ≥ 30 |
| A.8.25 | Secure development life cycle | blocking quality gate before images are built |
| A.8.28 | Secure coding | Sonar static analysis and secret scanning on every branch and PR |
| A.8.29 | Security testing in development and acceptance | DAST on test/acceptance environments with a URL |
| A.8.31 | Separation of development, test and production | separate environments, credentials and branch rules |
| A.8.32 | Change management | production only from the release branch, after approval by the approver group; concurrent builds disabled |

The tool itself supports the same controls:

- **A.5.15/A.8.2**: OIDC with MFA and workspace RBAC.
- **A.8.24**: a vault for secrets.
- **A.8.15**: an append-only audit log.
- **A.8.16**: rate limits and failed actions recorded in the audit log.
