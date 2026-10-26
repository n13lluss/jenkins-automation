"use client";

import type { ReactNode } from "react";
import { defaultProxy } from "@/lib/defaults";
import type { LoginConfig, PipelineConfig, ScannerTool, Severity, StageTrigger, TicketSource } from "@/lib/types";
import { Alert, Card, Field, Input, Select, TextArea, Toggle } from "../ui";
import type { SectionProps } from "./sections";

function Grid({ children }: { children: ReactNode }) {
  return <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">{children}</div>;
}

/** Edits a string list as one value per line. */
function LinesInput({ value, onChange, disabled, rows = 3 }: { value: string[]; onChange: (v: string[]) => void; disabled?: boolean; rows?: number }) {
  return (
    <TextArea
      rows={rows}
      value={value.join("\n")}
      disabled={disabled}
      spellCheck={false}
      onChange={(e) => onChange(e.target.value.split("\n").map((s) => s.trim()).filter(Boolean))}
    />
  );
}

const SEVERITIES: { value: Severity; label: string }[] = [
  { value: "low", label: "Low and above" },
  { value: "medium", label: "Medium and above" },
  { value: "high", label: "High and above" },
  { value: "critical", label: "Critical only" },
];

const TRIGGERS: { value: StageTrigger; label: string }[] = [
  { value: "always", label: "Every branch and pull request" },
  { value: "pullRequests", label: "Pull requests only" },
  { value: "deployBranches", label: "Deploy branches only (after merge)" },
];

export function TriggersCard({ config: c, update, readOnly, locked }: SectionProps) {
  const ro = readOnly || locked("triggers");
  return (
    <Card title="When stages run">
      <p className="mb-3 text-sm text-muted">Build and unit tests always run. Feature branches without a pull request get no further gates when checks are set to pull requests only.</p>
      <Grid>
        <Field label="Analysis and security checks" hint="Sonar, dependency, secret, license and IaC scans.">
          <Select value={c.triggers.checks} disabled={ro} onChange={(e) => update((d) => void (d.triggers.checks = e.target.value as StageTrigger))} options={TRIGGERS} />
        </Field>
        <Field label="Packaging" hint="Image build, image scan and ephemeral DAST.">
          <Select value={c.triggers.package} disabled={ro} onChange={(e) => update((d) => void (d.triggers.package = e.target.value as StageTrigger))} options={TRIGGERS} />
        </Field>
      </Grid>
    </Card>
  );
}

export function ScannerOptionsCard({ config: c, update, readOnly, locked }: SectionProps) {
  const s = c.security;
  const ro = (p: string) => readOnly || locked(p) || locked("security");
  const scanner = (p: "containerScanner" | "iacScanner") => (
    <Select
      value={s[p]}
      disabled={ro(`security.${p}`)}
      onChange={(e) => update((d) => void (d.security[p] = e.target.value as ScannerTool))}
      options={[
        { value: "trivy", label: "Trivy" },
        { value: "snyk", label: "Snyk" },
      ]}
    />
  );
  return (
    <Card title="Scanner options">
      <div className="grid gap-4 md:grid-cols-2">
        <Field label="Secret scan scope">
          <Select
            value={s.secretScanScope}
            disabled={ro("security.secretScanScope")}
            onChange={(e) => update((d) => void (d.security.secretScanScope = e.target.value as PipelineConfig["security"]["secretScanScope"]))}
            options={[
              { value: "history", label: "Full git history" },
              { value: "diff", label: "Only the commits of the change" },
            ]}
          />
        </Field>
        <Field label="Container image scanner">{scanner("containerScanner")}</Field>
        <Field label="IaC scanner">{scanner("iacScanner")}</Field>
        <Toggle label="IaC scan only when infrastructure changed" hint="Terraform, Kubernetes, Helm, deploy/ and Dockerfiles." checked={s.iacOnlyWhenChanged} disabled={ro("security.iacOnlyWhenChanged")} onChange={(v) => update((d) => void (d.security.iacOnlyWhenChanged = v))} />
        <Toggle label="Snyk monitor on deploy" hint="Registers deployed dependencies and images; Snyk alerts on new CVEs." checked={s.snykMonitor} disabled={ro("security.snykMonitor")} onChange={(v) => update((d) => void (d.security.snykMonitor = v))} />
      </div>
    </Card>
  );
}

export function SupplyChainSection({ config: c, update, error, locked, readOnly }: SectionProps) {
  const ro = (p: string) => readOnly || locked(p);
  const p = c.dependencyProxy;
  const g = c.signing;
  const path = (k: "mavenPath" | "npmPath" | "pypiPath" | "nugetPath" | "goPath" | "composerPath", label: string) => (
    <Field label={label} hint="Empty keeps the public registry." error={error(`dependencyProxy.${k}`)}>
      <Input value={p?.[k] ?? ""} disabled={ro("dependencyProxy")} onChange={(e) => update((d) => void (d.dependencyProxy![k] = e.target.value || null))} />
    </Field>
  );
  return (
    <div className="flex flex-col gap-4">
      <Card title="Dependency integrity">
        <div className="flex flex-col gap-4">
          <Toggle
            label="Fail on unpinned dependency versions (Maven Enforcer)"
            hint="Bans LATEST, RELEASE, version ranges and plugins without a version. Adds security/maven-enforcer.xml for your pom.xml."
            checked={c.security.dependencyPinning}
            disabled={ro("security.dependencyPinning")}
            onChange={(v) => update((d) => void (d.security.dependencyPinning = v))}
          />
          {error("security.dependencyPinning") && <Alert tone="danger" title={error("security.dependencyPinning")} />}
          <Toggle
            label="Resolve dependencies through a repository manager"
            hint="Nexus or Artifactory as the single source, so new or hallucinated packages cannot come straight from the internet."
            checked={!!p}
            disabled={ro("dependencyProxy")}
            onChange={(v) => update((d) => void (d.dependencyProxy = v ? defaultProxy() : null))}
          />
          {p && (
            <Grid>
              <Field label="Type">
                <Select
                  value={p.type}
                  disabled={ro("dependencyProxy")}
                  onChange={(e) => update((d) => void (d.dependencyProxy!.type = e.target.value as "nexus" | "artifactory"))}
                  options={[
                    { value: "nexus", label: "Nexus Repository" },
                    { value: "artifactory", label: "Artifactory" },
                  ]}
                />
              </Field>
              <Field label="Base URL" error={error("dependencyProxy.baseUrl")}>
                <Input value={p.baseUrl} disabled={ro("dependencyProxy")} onChange={(e) => update((d) => void (d.dependencyProxy!.baseUrl = e.target.value))} />
              </Field>
              <Field label="Credential ID" hint="Username with password." error={error("dependencyProxy.credentialId")}>
                <Input value={p.credentialId} disabled={ro("dependencyProxy")} onChange={(e) => update((d) => void (d.dependencyProxy!.credentialId = e.target.value))} />
              </Field>
              {path("mavenPath", "Maven / Gradle repository")}
              {path("npmPath", "npm repository")}
              {path("pypiPath", "PyPI repository")}
              {path("nugetPath", "NuGet repository")}
              {path("goPath", "Go proxy")}
              {path("composerPath", "Composer repository")}
            </Grid>
          )}
        </div>
      </Card>
      <Card title="Image signing (Cosign)">
        <div className="flex flex-col gap-4">
          <Toggle
            label="Sign images and verify before every deploy"
            hint="Signs the pushed digest; deploys verify the signature and deploy by digest."
            checked={g.enabled}
            disabled={ro("signing.enabled") || ro("signing")}
            onChange={(v) => update((d) => void (d.signing.enabled = v))}
          />
          {error("signing") && <Alert tone="danger" title={error("signing")} />}
          {g.enabled && (
            <Grid>
              <Field label="Key credential ID" hint="Secret file with cosign.key." error={error("signing.keyCredentialId")}>
                <Input value={g.keyCredentialId} disabled={ro("signing")} onChange={(e) => update((d) => void (d.signing.keyCredentialId = e.target.value))} />
              </Field>
              <Field label="Password credential ID" hint="Secret text." error={error("signing.passwordCredentialId")}>
                <Input value={g.passwordCredentialId} disabled={ro("signing")} onChange={(e) => update((d) => void (d.signing.passwordCredentialId = e.target.value))} />
              </Field>
              <Field label="Public key path" hint="Commit cosign.pub here." error={error("signing.publicKeyPath")}>
                <Input value={g.publicKeyPath} disabled={ro("signing")} onChange={(e) => update((d) => void (d.signing.publicKeyPath = e.target.value))} />
              </Field>
              <Toggle label="Upload to the public Rekor log" hint="Off for private images." checked={g.transparencyLog} disabled={ro("signing")} onChange={(v) => update((d) => void (d.signing.transparencyLog = v))} />
            </Grid>
          )}
        </div>
      </Card>
    </div>
  );
}

function LoginFields({ login, prefix, onChange, error, disabled }: { login: LoginConfig; prefix: string; onChange: (fn: (l: LoginConfig) => void) => void; error: SectionProps["error"]; disabled: boolean }) {
  return (
    <>
      <Field label="Login path" error={error(`${prefix}.loginPath`)}>
        <Input value={login.loginPath} disabled={disabled} onChange={(e) => onChange((l) => void (l.loginPath = e.target.value))} />
      </Field>
      <Field label="Login format">
        <Select
          value={login.format}
          disabled={disabled}
          onChange={(e) => onChange((l) => void (l.format = e.target.value as "form" | "json"))}
          options={[
            { value: "form", label: "Form post" },
            { value: "json", label: "JSON body" },
          ]}
        />
      </Field>
      <Field label="Username field" error={error(`${prefix}.usernameField`)}>
        <Input value={login.usernameField} disabled={disabled} onChange={(e) => onChange((l) => void (l.usernameField = e.target.value))} />
      </Field>
      <Field label="Password field" error={error(`${prefix}.passwordField`)}>
        <Input value={login.passwordField} disabled={disabled} onChange={(e) => onChange((l) => void (l.passwordField = e.target.value))} />
      </Field>
      <Field label="Logout path" error={error(`${prefix}.logoutPath`)}>
        <Input value={login.logoutPath} disabled={disabled} onChange={(e) => onChange((l) => void (l.logoutPath = e.target.value))} />
      </Field>
    </>
  );
}

export function DynamicTestingSection({ config: c, update, error, locked, readOnly }: SectionProps) {
  const d = c.security.dast;
  const ro = (p: string) => readOnly || locked(p) || locked("security.dast") || locked("security");
  const envOptions = c.environments.map((e) => ({ value: e.key, label: `${e.displayName}${e.url ? "" : " (no URL)"}` }));
  const imageModules = c.modules.filter((m) => m.dockerfile);
  return (
    <div className="flex flex-col gap-4">
      <Card title="Ephemeral DAST (just-built image)">
        <div className="flex flex-col gap-4">
          <Toggle
            label="Scan the image built in this run"
            hint={c.agent.type === "kubernetes" ? "Pushes a scan tag and runs the app next to ZAP in a short-lived pod." : "Runs the app and ZAP on an isolated Docker network on the agent."}
            checked={d.ephemeral.enabled}
            disabled={ro("security.dast.ephemeral")}
            onChange={(v) => update((x) => void (x.security.dast.ephemeral.enabled = v))}
          />
          {error("security.dast.ephemeral") && <Alert tone="danger" title={error("security.dast.ephemeral")} />}
          {d.ephemeral.enabled && (
            <Grid>
              <Field label="Module" error={error("security.dast.ephemeral.module")}>
                <Select
                  value={d.ephemeral.module ?? ""}
                  disabled={ro("security.dast.ephemeral")}
                  onChange={(e) => update((x) => void (x.security.dast.ephemeral.module = e.target.value || null))}
                  options={[{ value: "", label: "First image module" }, ...imageModules.map((m) => ({ value: m.name, label: m.name }))]}
                />
              </Field>
              <Field label="Container port" error={error("security.dast.ephemeral.containerPort")}>
                <Input type="number" value={d.ephemeral.containerPort} disabled={ro("security.dast.ephemeral")} onChange={(e) => update((x) => void (x.security.dast.ephemeral.containerPort = Number(e.target.value)))} />
              </Field>
              <Field label="Health path" error={error("security.dast.ephemeral.healthPath")}>
                <Input value={d.ephemeral.healthPath} disabled={ro("security.dast.ephemeral")} onChange={(e) => update((x) => void (x.security.dast.ephemeral.healthPath = e.target.value))} />
              </Field>
              {c.agent.type === "kubernetes" && (
                <Field label="Image pull secret (optional)" error={error("security.dast.ephemeral.imagePullSecret")}>
                  <Input value={d.ephemeral.imagePullSecret ?? ""} disabled={ro("security.dast.ephemeral")} onChange={(e) => update((x) => void (x.security.dast.ephemeral.imagePullSecret = e.target.value || null))} />
                </Field>
              )}
              <Field label="App environment (KEY=value per line)" hint="No secrets: these are written into the pipeline." error={error("security.dast.ephemeral.appEnvironment")}>
                <LinesInput value={d.ephemeral.appEnvironment} disabled={ro("security.dast.ephemeral")} onChange={(v) => update((x) => void (x.security.dast.ephemeral.appEnvironment = v))} />
              </Field>
            </Grid>
          )}
        </div>
      </Card>

      <Card title="Authenticated two-role scan">
        <div className="flex flex-col gap-4">
          <Toggle
            label="Test access control with two accounts"
            hint="Logs in as zap-user and zap-admin: the user must be refused on admin-only paths; also checks logout, session fixation and token entropy, plus a ZAP active scan."
            checked={d.authenticated.enabled}
            disabled={ro("security.dast.authenticated")}
            onChange={(v) => update((x) => void (x.security.dast.authenticated.enabled = v))}
          />
          {d.authenticated.enabled && (
            <Grid>
              <Field label="Environment" error={error("security.dast.authenticated.environment")}>
                <Select value={d.authenticated.environment} disabled={ro("security.dast.authenticated")} onChange={(e) => update((x) => void (x.security.dast.authenticated.environment = e.target.value))} options={envOptions} />
              </Field>
              <Field label="User credential ID" hint="Username with password; a test account." error={error("security.dast.authenticated.userCredentialId")}>
                <Input value={d.authenticated.userCredentialId} disabled={ro("security.dast.authenticated")} onChange={(e) => update((x) => void (x.security.dast.authenticated.userCredentialId = e.target.value))} />
              </Field>
              <Field label="Admin credential ID" error={error("security.dast.authenticated.adminCredentialId")}>
                <Input value={d.authenticated.adminCredentialId} disabled={ro("security.dast.authenticated")} onChange={(e) => update((x) => void (x.security.dast.authenticated.adminCredentialId = e.target.value))} />
              </Field>
              <LoginFields login={d.authenticated.login} prefix="security.dast.authenticated.login" error={error} disabled={ro("security.dast.authenticated")} onChange={(fn) => update((x) => fn(x.security.dast.authenticated.login))} />
              <Field label="Page any logged-in user may open" error={error("security.dast.authenticated.protectedPath")}>
                <Input value={d.authenticated.protectedPath} disabled={ro("security.dast.authenticated")} onChange={(e) => update((x) => void (x.security.dast.authenticated.protectedPath = e.target.value))} />
              </Field>
              <Field label="Admin-only paths (one per line)" error={error("security.dast.authenticated.adminOnlyPaths")}>
                <LinesInput value={d.authenticated.adminOnlyPaths} disabled={ro("security.dast.authenticated")} onChange={(v) => update((x) => void (x.security.dast.authenticated.adminOnlyPaths = v))} />
              </Field>
              <Toggle label="Also run before the production approval" checked={d.authenticated.beforeProduction} disabled={ro("security.dast.authenticated")} onChange={(v) => update((x) => void (x.security.dast.authenticated.beforeProduction = v))} />
            </Grid>
          )}
        </div>
      </Card>

      <Card title="Scheduled scans">
        <div className="flex flex-col gap-4">
          <Toggle
            label="Scan a live environment on a schedule"
            hint="Generates Jenkinsfile.scheduled: a passive baseline independent of code changes."
            checked={d.schedule.enabled}
            disabled={ro("security.dast.schedule")}
            onChange={(v) => update((x) => void (x.security.dast.schedule.enabled = v))}
          />
          {d.schedule.enabled && (
            <Grid>
              <Field label="Environment" error={error("security.dast.schedule.environment")}>
                <Select value={d.schedule.environment} disabled={ro("security.dast.schedule")} onChange={(e) => update((x) => void (x.security.dast.schedule.environment = e.target.value))} options={envOptions} />
              </Field>
              <Field label="Cron" hint="Jenkins syntax; H */6 * * * = every 6 hours." error={error("security.dast.schedule.baselineCron")}>
                <Input value={d.schedule.baselineCron} disabled={ro("security.dast.schedule")} onChange={(e) => update((x) => void (x.security.dast.schedule.baselineCron = e.target.value))} />
              </Field>
              <Toggle
                label="Daily authenticated scan"
                hint="The first scheduled run each day also runs the two-role scan."
                checked={d.schedule.authenticatedDaily}
                disabled={ro("security.dast.schedule")}
                onChange={(v) => update((x) => void (x.security.dast.schedule.authenticatedDaily = v))}
              />
              {error("security.dast.schedule.authenticatedDaily") && <Alert tone="danger" title={error("security.dast.schedule.authenticatedDaily")} />}
            </Grid>
          )}
        </div>
      </Card>

      <Card title="Login rate-limit check">
        <div className="flex flex-col gap-4">
          <Toggle
            label="Check throttling after deploy"
            hint="Sends repeated failed logins for a probe account and expects HTTP 429/423. Never use a real account."
            checked={c.rateLimit.enabled}
            disabled={readOnly || locked("rateLimit")}
            onChange={(v) => update((x) => void (x.rateLimit.enabled = v))}
          />
          {c.rateLimit.enabled && (
            <Grid>
              <LoginFields login={c.rateLimit.login} prefix="rateLimit.login" error={error} disabled={readOnly || locked("rateLimit")} onChange={(fn) => update((x) => fn(x.rateLimit.login))} />
              <Field label="Probe username" error={error("rateLimit.testUsername")}>
                <Input value={c.rateLimit.testUsername} disabled={readOnly || locked("rateLimit")} onChange={(e) => update((x) => void (x.rateLimit.testUsername = e.target.value))} />
              </Field>
              <Field label="Attempts" error={error("rateLimit.attempts")}>
                <Input type="number" value={c.rateLimit.attempts} disabled={readOnly || locked("rateLimit")} onChange={(e) => update((x) => void (x.rateLimit.attempts = Number(e.target.value)))} />
              </Field>
              <Field label="Expected statuses" hint="Comma separated." error={error("rateLimit.expectedStatuses")}>
                <Input
                  value={c.rateLimit.expectedStatuses.join(",")}
                  disabled={readOnly || locked("rateLimit")}
                  onChange={(e) => update((x) => void (x.rateLimit.expectedStatuses = e.target.value.split(",").map((v) => Number(v.trim())).filter((n) => n > 0)))}
                />
              </Field>
              <div className="flex flex-col gap-1.5">
                <span className="text-sm font-medium">Environments</span>
                {error("rateLimit.environments") && <span className="text-xs text-danger">{error("rateLimit.environments")}</span>}
                {c.environments.map((e) => (
                  <Toggle
                    key={e.key}
                    label={e.displayName}
                    checked={c.rateLimit.environments.includes(e.key)}
                    disabled={readOnly || locked("rateLimit")}
                    onChange={(v) => update((x) => void (x.rateLimit.environments = v ? [...x.rateLimit.environments, e.key] : x.rateLimit.environments.filter((k) => k !== e.key)))}
                  />
                ))}
              </div>
            </Grid>
          )}
        </div>
      </Card>
    </div>
  );
}

const SOURCES: { value: TicketSource; label: string }[] = [
  { value: "secretScan", label: "Secret scan" },
  { value: "dependencies", label: "Dependency scans" },
  { value: "container", label: "Container scan" },
  { value: "iac", label: "IaC scan" },
  { value: "sonar", label: "SonarQube" },
  { value: "dast", label: "ZAP (DAST)" },
  { value: "accessControl", label: "Access-control check" },
  { value: "aiReview", label: "AI review" },
];

export function TicketsSection({ config: c, update, error, locked, readOnly }: SectionProps) {
  const t = c.ticketing;
  const ro = readOnly || locked("ticketing");
  return (
    <Card title="Tickets for findings">
      <div className="flex flex-col gap-4">
        <Field label="Ticket system" hint="More providers can be added later.">
          <Select
            value={t.provider}
            disabled={ro}
            onChange={(e) => update((d) => void (d.ticketing.provider = e.target.value as "none" | "jira"))}
            options={[
              { value: "none", label: "None" },
              { value: "jira", label: "Jira (Jira Pipeline Steps plugin)" },
            ]}
          />
        </Field>
        {t.provider === "jira" && (
          <>
            <Grid>
              <Field label="Jira site name" hint="As configured in the Jira Steps plugin." error={error("ticketing.jiraSite")}>
                <Input value={t.jiraSite} disabled={ro} onChange={(e) => update((d) => void (d.ticketing.jiraSite = e.target.value))} />
              </Field>
              <Field label="Project key" error={error("ticketing.projectKey")}>
                <Input value={t.projectKey} disabled={ro} onChange={(e) => update((d) => void (d.ticketing.projectKey = e.target.value.toUpperCase()))} />
              </Field>
              <Field label="Issue type" error={error("ticketing.issueType")}>
                <Input value={t.issueType} disabled={ro} onChange={(e) => update((d) => void (d.ticketing.issueType = e.target.value))} />
              </Field>
              <Field label="Minimum severity">
                <Select value={t.minSeverity} disabled={ro} onChange={(e) => update((d) => void (d.ticketing.minSeverity = e.target.value as Severity))} options={SEVERITIES} />
              </Field>
              <Field label="Maximum per build" error={error("ticketing.maxPerBuild")}>
                <Input type="number" value={t.maxPerBuild} disabled={ro} onChange={(e) => update((d) => void (d.ticketing.maxPerBuild = Number(e.target.value)))} />
              </Field>
              <Toggle label="Also from pull request builds" checked={t.pullRequests} disabled={ro} onChange={(v) => update((d) => void (d.ticketing.pullRequests = v))} />
            </Grid>
            <div>
              <p className="mb-2 text-sm font-medium">Sources</p>
              {error("ticketing.sources") && <p className="mb-2 text-xs text-danger">{error("ticketing.sources")}</p>}
              <div className="grid gap-2 md:grid-cols-4">
                {SOURCES.map((s) => (
                  <Toggle
                    key={s.value}
                    label={s.label}
                    checked={t.sources.includes(s.value)}
                    disabled={ro}
                    onChange={(v) => update((d) => void (d.ticketing.sources = v ? [...d.ticketing.sources, s.value] : d.ticketing.sources.filter((x) => x !== s.value)))}
                  />
                ))}
              </div>
            </div>
            <p className="text-xs text-muted">Duplicates are avoided with a <code>pf-&lt;fingerprint&gt;</code> label while the issue is open. For Snyk findings, Snyk&apos;s own Jira integration can be used instead.</p>
          </>
        )}
      </div>
    </Card>
  );
}
