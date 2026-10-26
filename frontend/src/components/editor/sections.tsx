"use client";

import type { ReactNode } from "react";
import { ECOSYSTEMS, defaultDeploy, newModule, slug } from "@/lib/defaults";
import type { AiProvider, DastMode, DependencyScanner, DeployTargetType, Ecosystem, EnvironmentConfig, PipelineConfig, Severity } from "@/lib/types";
import { Alert, Button, Card, Field, Input, Select, TextArea, Toggle } from "../ui";

export type SectionProps = {
  config: PipelineConfig;
  update: (fn: (c: PipelineConfig) => void) => void;
  error: (path: string) => string | undefined;
  locked: (path: string) => boolean;
  readOnly: boolean;
};

const SEVERITIES: { value: Severity; label: string }[] = [
  { value: "low", label: "Low and above" },
  { value: "medium", label: "Medium and above" },
  { value: "high", label: "High and above" },
  { value: "critical", label: "Critical only" },
];

function Grid({ children }: { children: ReactNode }) {
  return <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">{children}</div>;
}

export function ProjectSection({ config: c, update, error, locked, readOnly }: SectionProps) {
  const ro = (p: string) => readOnly || locked(p);
  return (
    <div className="flex flex-col gap-4">
      <Card title="Project">
        <Grid>
          <Field label="Pipeline name" hint="Used for images, Helm releases and file names." error={error("name")}>
            <Input value={c.name} disabled={ro("name")} onChange={(e) => update((d) => void (d.name = slug(e.target.value)))} />
          </Field>
          <Field label="Jenkins agents" error={error("agent")}>
            <Select
              value={c.agent.type}
              disabled={ro("agent.type")}
              onChange={(e) => update((d) => void (d.agent.type = e.target.value as PipelineConfig["agent"]["type"]))}
              options={[
                { value: "kubernetes", label: "Kubernetes pod templates" },
                { value: "docker", label: "Docker agents" },
              ]}
            />
          </Field>
          {c.agent.type === "docker" ? (
            <Field label="Docker node label" error={error("agent.dockerLabel")}>
              <Input value={c.agent.dockerLabel} disabled={ro("agent.dockerLabel")} onChange={(e) => update((d) => void (d.agent.dockerLabel = e.target.value))} />
            </Field>
          ) : (
            <Field label="Kubernetes cloud (optional)" hint="Name of the cloud in Jenkins." error={error("agent.kubernetesCloud")}>
              <Input value={c.agent.kubernetesCloud ?? ""} disabled={ro("agent.kubernetesCloud")} onChange={(e) => update((d) => void (d.agent.kubernetesCloud = e.target.value || null))} />
            </Field>
          )}
        </Grid>
      </Card>

      <Card
        title="Modules"
        actions={
          !readOnly && (
            <Button onClick={() => update((d) => void d.modules.push(newModule(`module-${d.modules.length + 1}`, "npm", `module-${d.modules.length + 1}`)))}>Add module</Button>
          )
        }
      >
        <p className="mb-3 text-sm text-muted">One module per build: a monorepo has several. Modules build in parallel.</p>
        {error("modules") && <Alert tone="danger" title={error("modules")} />}
        <div className="flex flex-col gap-3">
          {c.modules.map((m, i) => {
            const eco = ECOSYSTEMS.find((e) => e.value === m.ecosystem)!;
            const p = `modules[${i}]`;
            return (
              <div key={i} className="rounded-md border border-border p-3">
                <Grid>
                  <Field label="Name" error={error(`${p}.name`)}>
                    <Input value={m.name} disabled={readOnly} onChange={(e) => update((d) => void (d.modules[i].name = slug(e.target.value)))} />
                  </Field>
                  <Field label="Path" hint="Relative to the repository root; . for the root." error={error(`${p}.path`)}>
                    <Input value={m.path} disabled={readOnly} onChange={(e) => update((d) => void (d.modules[i].path = e.target.value.trim() || "."))} />
                  </Field>
                  <Field label="Language / build tool">
                    <Select value={m.ecosystem} disabled={readOnly} onChange={(e) => update((d) => void (d.modules[i].ecosystem = e.target.value as Ecosystem))} options={ECOSYSTEMS.map((x) => ({ value: x.value, label: x.label }))} />
                  </Field>
                  <Field label={`${eco.versionLabel} version`} hint={`Default ${eco.version}`} error={error(`${p}.languageVersion`)}>
                    <Input value={m.languageVersion ?? ""} placeholder={eco.version} disabled={readOnly} onChange={(e) => update((d) => void (d.modules[i].languageVersion = e.target.value || null))} />
                  </Field>
                  <Field label="Dockerfile (optional)" hint="Builds, scans and pushes an image." error={error(`${p}.dockerfile`)}>
                    <Input
                      value={m.dockerfile ?? ""}
                      placeholder={m.path === "." ? "Dockerfile" : `${m.path}/Dockerfile`}
                      disabled={readOnly}
                      onChange={(e) =>
                        update((d) => {
                          d.modules[i].dockerfile = e.target.value || null;
                          if (e.target.value && !d.modules[i].imageName) d.modules[i].imageName = `${d.name}/${d.modules[i].name}`;
                        })
                      }
                    />
                  </Field>
                  {m.dockerfile && (
                    <Field label="Image name" hint="Without registry or tag." error={error(`${p}.imageName`)}>
                      <Input value={m.imageName ?? ""} disabled={readOnly} onChange={(e) => update((d) => void (d.modules[i].imageName = e.target.value || null))} />
                    </Field>
                  )}
                </Grid>
                <div className="mt-3 flex flex-wrap items-center gap-5">
                  <Toggle label="Run tests" checked={m.runTests} disabled={readOnly} onChange={(v) => update((d) => void (d.modules[i].runTests = v))} />
                  {!readOnly && c.modules.length > 1 && (
                    <Button variant="ghost" className="ml-auto text-danger" onClick={() => update((d) => void d.modules.splice(i, 1))}>
                      Remove module
                    </Button>
                  )}
                </div>
              </div>
            );
          })}
        </div>
      </Card>

      <Card title="Container registry">
        <Toggle
          label="Push images to a registry"
          hint="Required when a module has a Dockerfile."
          checked={!!c.registry}
          disabled={ro("registry")}
          onChange={(v) => update((d) => void (d.registry = v ? { host: "registry.example.com", credentialId: "registry-credentials" } : null))}
        />
        {error("registry") && <p className="mt-2 text-xs text-danger">{error("registry")}</p>}
        {c.registry && (
          <div className="mt-3">
            <Grid>
              <Field label="Registry host" error={error("registry.host")}>
                <Input value={c.registry.host} disabled={ro("registry.host")} onChange={(e) => update((d) => void (d.registry!.host = e.target.value))} />
              </Field>
              <Field label="Jenkins credential ID" hint="Username with password." error={error("registry.credentialId")}>
                <Input value={c.registry.credentialId} disabled={ro("registry.credentialId")} onChange={(e) => update((d) => void (d.registry!.credentialId = e.target.value))} />
              </Field>
            </Grid>
          </div>
        )}
      </Card>

      <Card title="Options">
        <Grid>
          <Field label="CI timeout (minutes)" error={error("options.timeoutMinutes")}>
            <Input type="number" value={c.options.timeoutMinutes} disabled={ro("options")} onChange={(e) => update((d) => void (d.options.timeoutMinutes = Number(e.target.value)))} />
          </Field>
          <Field label="Builds to keep" error={error("options.buildsToKeep")}>
            <Input type="number" value={c.options.buildsToKeep} disabled={ro("options")} onChange={(e) => update((d) => void (d.options.buildsToKeep = Number(e.target.value)))} />
          </Field>
          <Toggle label="Disable concurrent builds" checked={c.options.disableConcurrentBuilds} disabled={ro("options")} onChange={(v) => update((d) => void (d.options.disableConcurrentBuilds = v))} />
        </Grid>
      </Card>
    </div>
  );
}

export function QualitySection({ config: c, update, error, locked, readOnly }: SectionProps) {
  const q = c.quality;
  const ro = (p: string) => readOnly || locked(p) || locked("quality");
  return (
    <Card title="SonarQube / SonarCloud">
      <div className="flex flex-col gap-4">
        <Toggle label="Static analysis" hint="Runs after the build so coverage and binaries are available." checked={q.enabled} disabled={ro("quality.enabled")} onChange={(v) => update((d) => void (d.quality.enabled = v))} />
        {q.enabled && (
          <>
            <Grid>
              <Field label="Platform">
                <Select
                  value={q.platform}
                  disabled={ro("quality.platform")}
                  onChange={(e) => update((d) => void (d.quality.platform = e.target.value as PipelineConfig["quality"]["platform"]))}
                  options={[
                    { value: "sonarQube", label: "SonarQube (self-hosted)" },
                    { value: "sonarCloud", label: "SonarCloud" },
                  ]}
                />
              </Field>
              <Field label="Jenkins Sonar installation" hint="Manage Jenkins → System → SonarQube servers." error={error("quality.serverName")}>
                <Input value={q.serverName} disabled={ro("quality.serverName")} onChange={(e) => update((d) => void (d.quality.serverName = e.target.value))} />
              </Field>
              <Field label="Project key" error={error("quality.projectKey")}>
                <Input value={q.projectKey} disabled={ro("quality.projectKey")} onChange={(e) => update((d) => void (d.quality.projectKey = e.target.value))} />
              </Field>
              {q.platform === "sonarCloud" && (
                <Field label="Organisation" error={error("quality.organization")}>
                  <Input value={q.organization ?? ""} disabled={ro("quality.organization")} onChange={(e) => update((d) => void (d.quality.organization = e.target.value || null))} />
                </Field>
              )}
            </Grid>
            <div className="flex flex-wrap gap-6">
              <Toggle label="Wait for the quality gate" checked={q.waitForQualityGate} disabled={ro("quality.waitForQualityGate")} onChange={(v) => update((d) => void (d.quality.waitForQualityGate = v))} />
              <Toggle label="Fail the build when the gate fails" checked={q.abortOnQualityGateFailure} disabled={ro("quality.abortOnQualityGateFailure") || !q.waitForQualityGate} onChange={(v) => update((d) => void (d.quality.abortOnQualityGateFailure = v))} />
            </div>
          </>
        )}
      </div>
    </Card>
  );
}

const SCANNERS: { value: DependencyScanner; label: string; hint: string }[] = [
  { value: "trivyFs", label: "Trivy", hint: "Lockfiles of all ecosystems; fast." },
  { value: "owaspDependencyCheck", label: "OWASP Dependency-Check", hint: "NVD-based; needs an NVD API key credential." },
  { value: "osvScanner", label: "OSV-Scanner", hint: "Google OSV database." },
  { value: "snyk", label: "Snyk", hint: "Needs a Snyk token credential." },
  { value: "npmAudit", label: "npm audit", hint: "Node modules only." },
];

export function SecuritySection({ config: c, update, error, locked, readOnly }: SectionProps) {
  const s = c.security;
  const ro = (p: string) => readOnly || locked(p) || locked("security");
  const iso = c.compliance.profile === "iso27001";
  return (
    <div className="flex flex-col gap-4">
      <Card title="Compliance">
        <div className="flex flex-col gap-3">
          <Toggle
            label="ISO/IEC 27001 profile"
            hint="Turns on the minimum scans, a production approval gate and an evidence document mapped to Annex A controls."
            checked={iso}
            disabled={readOnly || locked("compliance.profile") || locked("compliance")}
            onChange={(v) => update((d) => void (d.compliance.profile = v ? "iso27001" : "none"))}
          />
          <Field label="Approvers for production" hint="Jenkins user or group allowed to approve deploys." error={error("compliance.approverGroup")}>
            <Input value={c.compliance.approverGroup} disabled={readOnly || locked("compliance.approverGroup")} onChange={(e) => update((d) => void (d.compliance.approverGroup = e.target.value))} />
          </Field>
        </div>
      </Card>
      <Card title="Scans">
        <div className="grid gap-4 md:grid-cols-2">
          <Toggle label="Secret scanning (Gitleaks)" checked={s.secretScan} disabled={ro("security.secretScan")} onChange={(v) => update((d) => void (d.security.secretScan = v))} />
          <Toggle label="SBOM (CycloneDX)" checked={s.sbom} disabled={ro("security.sbom")} onChange={(v) => update((d) => void (d.security.sbom = v))} />
          <Toggle label="License check" hint="Fails on forbidden/restricted licenses." checked={s.licenseCheck} disabled={ro("security.licenseCheck")} onChange={(v) => update((d) => void (d.security.licenseCheck = v))} />
          <Toggle label="Container image scan" hint="Scans the image before it is pushed." checked={s.containerScan} disabled={ro("security.containerScan")} onChange={(v) => update((d) => void (d.security.containerScan = v))} />
          <Toggle label="Infrastructure-as-code scan" hint="Dockerfiles, Kubernetes, Helm, Terraform." checked={s.iacScan} disabled={ro("security.iacScan")} onChange={(v) => update((d) => void (d.security.iacScan = v))} />
          <Field label="Fail the build on">
            <Select value={s.failOnSeverity} disabled={ro("security.failOnSeverity")} onChange={(e) => update((d) => void (d.security.failOnSeverity = e.target.value as Severity))} options={SEVERITIES} />
          </Field>
        </div>
      </Card>
      <Card title="Dependency screening">
        {error("security.dependencyScanners") && <Alert tone="danger" title={error("security.dependencyScanners")} />}
        <div className="grid gap-3 md:grid-cols-2">
          {SCANNERS.map((sc) => (
            <Toggle
              key={sc.value}
              label={sc.label}
              hint={sc.hint}
              checked={s.dependencyScanners.includes(sc.value)}
              disabled={ro("security.dependencyScanners")}
              onChange={(v) =>
                update((d) => {
                  d.security.dependencyScanners = v ? [...d.security.dependencyScanners, sc.value] : d.security.dependencyScanners.filter((x) => x !== sc.value);
                })
              }
            />
          ))}
        </div>
        {(s.dependencyScanners.includes("owaspDependencyCheck") || s.dependencyScanners.includes("snyk")) && (
          <div className="mt-4">
            <Grid>
              {s.dependencyScanners.includes("owaspDependencyCheck") && (
                <Field label="NVD API key credential ID" error={error("security.nvdApiKeyCredentialId")}>
                  <Input value={s.nvdApiKeyCredentialId} disabled={ro("security.nvdApiKeyCredentialId")} onChange={(e) => update((d) => void (d.security.nvdApiKeyCredentialId = e.target.value))} />
                </Field>
              )}
              {s.dependencyScanners.includes("snyk") && (
                <Field label="Snyk token credential ID" error={error("security.snykCredentialId")}>
                  <Input value={s.snykCredentialId} disabled={ro("security.snykCredentialId")} onChange={(e) => update((d) => void (d.security.snykCredentialId = e.target.value))} />
                </Field>
              )}
            </Grid>
          </div>
        )}
      </Card>
    </div>
  );
}

const DEPLOY_TYPES: { value: DeployTargetType; label: string }[] = [
  { value: "none", label: "No deploy" },
  { value: "helm", label: "Kubernetes (Helm)" },
  { value: "dockerSsh", label: "Docker Compose over SSH" },
  { value: "azureAppService", label: "Azure App Service" },
];

export function EnvironmentsSection({ config: c, update, error, locked, readOnly }: SectionProps) {
  const ro = (p: string) => readOnly || locked(p) || locked("environments");
  const dast = c.security.dast;
  return (
    <div className="flex flex-col gap-4">
      <Card
        title="DTAP / OTAP environments"
        actions={
          !ro("environments") && (
            <Button
              onClick={() =>
                update((d) => {
                  const key = `env${d.environments.length + 1}`;
                  d.environments.push({ key, displayName: key, branchPattern: "develop", requiresApproval: false, url: null, deploy: defaultDeploy(d.name, key) });
                })
              }
            >
              Add environment
            </Button>
          )
        }
      >
        <p className="mb-3 text-sm text-muted">
          Every branch and pull request runs CI. An environment deploys when the branch matches its pattern (glob, e.g. <code>release/*</code>).
        </p>
        {error("environments") && <Alert tone="danger" title={error("environments")} />}
        <div className="flex flex-col gap-3">
          {c.environments.map((env, i) => (
            <EnvironmentCard key={i} env={env} index={i} earlier={c.environments.slice(0, i)} {...{ update, error, readOnly: ro("environments") }} />
          ))}
        </div>
      </Card>
      <Card title="DAST (OWASP ZAP)">
        <div className="flex flex-col gap-3">
          <Toggle label="Scan deployed environments" hint="Runs after the deploy stage of each selected environment; needs the environment URL." checked={dast.enabled} disabled={ro("security.dast")} onChange={(v) => update((d) => void (d.security.dast.enabled = v))} />
          {dast.enabled && (
            <>
              <Grid>
                <Field label="Scan type">
                  <Select
                    value={dast.mode}
                    disabled={ro("security.dast.mode")}
                    onChange={(e) => update((d) => void (d.security.dast.mode = e.target.value as DastMode))}
                    options={[
                      { value: "baseline", label: "Baseline (passive, safe)" },
                      { value: "full", label: "Full (active attacks)" },
                      { value: "api", label: "API (OpenAPI)" },
                    ]}
                  />
                </Field>
                {dast.mode === "api" && (
                  <Field label="OpenAPI spec" hint="URL or repository path." error={error("security.dast.apiSpec")}>
                    <Input value={dast.apiSpec ?? ""} disabled={ro("security.dast.apiSpec")} onChange={(e) => update((d) => void (d.security.dast.apiSpec = e.target.value || null))} />
                  </Field>
                )}
              </Grid>
              {dast.mode === "full" && <Alert tone="warn" title="Full scans send attacks">Only point them at test or acceptance environments you own.</Alert>}
              {error("security.dast.environments") && <Alert tone="danger" title={error("security.dast.environments")} />}
              <div className="flex flex-wrap gap-4">
                {c.environments.map((env) => (
                  <Toggle
                    key={env.key}
                    label={env.displayName}
                    checked={dast.environments.includes(env.key)}
                    disabled={ro("security.dast.environments")}
                    onChange={(v) =>
                      update((d) => {
                        d.security.dast.environments = v ? [...d.security.dast.environments, env.key] : d.security.dast.environments.filter((k) => k !== env.key);
                      })
                    }
                  />
                ))}
              </div>
            </>
          )}
        </div>
      </Card>
    </div>
  );
}

function EnvironmentCard({
  env,
  index: i,
  earlier,
  update,
  error,
  readOnly,
}: {
  env: EnvironmentConfig;
  index: number;
  earlier: EnvironmentConfig[];
  update: SectionProps["update"];
  error: SectionProps["error"];
  readOnly: boolean;
}) {
  const p = `environments[${i}]`;
  const d = env.deploy;
  const setDeploy = <K extends keyof EnvironmentConfig["deploy"]>(k: K, v: EnvironmentConfig["deploy"][K]) => update((c) => void (c.environments[i].deploy[k] = v));
  const text = (k: keyof EnvironmentConfig["deploy"], label: string, hint?: string) => (
    <Field label={label} hint={hint} error={error(`${p}.deploy.${k}`)}>
      <Input value={String(d[k] ?? "")} disabled={readOnly} onChange={(e) => setDeploy(k, (e.target.value || (k === "slot" ? null : "")) as never)} />
    </Field>
  );
  return (
    <div className="rounded-md border border-border p-3">
      <Grid>
        <Field label="Key" hint="dev, test, acc, prod…" error={error(`${p}.key`)}>
          <Input value={env.key} disabled={readOnly} onChange={(e) => update((c) => void (c.environments[i].key = e.target.value.toLowerCase()))} />
        </Field>
        <Field label="Name" error={error(`${p}.displayName`)}>
          <Input value={env.displayName} disabled={readOnly} onChange={(e) => update((c) => void (c.environments[i].displayName = e.target.value))} />
        </Field>
        <Field label="Branch pattern" error={error(`${p}.branchPattern`)}>
          <Input value={env.branchPattern} disabled={readOnly} onChange={(e) => update((c) => void (c.environments[i].branchPattern = e.target.value))} />
        </Field>
        <Field label="URL (optional)" hint="Smoke test and DAST target." error={error(`${p}.url`)}>
          <Input value={env.url ?? ""} disabled={readOnly} onChange={(e) => update((c) => void (c.environments[i].url = e.target.value || null))} />
        </Field>
        <Field label="Deploy target" error={error(`${p}.deploy`)}>
          <Select value={d.type} disabled={readOnly} onChange={(e) => setDeploy("type", e.target.value as DeployTargetType)} options={DEPLOY_TYPES} />
        </Field>
        {earlier.length > 0 && d.type !== "none" && (
          <Field label="Image source" hint="Promote reuses the exact signed image that passed the earlier environment." error={error(`${p}.promoteFrom`)}>
            <Select
              value={env.promoteFrom ?? ""}
              disabled={readOnly}
              onChange={(e) => update((c) => void (c.environments[i].promoteFrom = e.target.value || null))}
              options={[{ value: "", label: "Build from this branch" }, ...earlier.map((e) => ({ value: e.key, label: `Promote from ${e.displayName}` }))]}
            />
          </Field>
        )}
        <div className="flex items-end">
          <Toggle label="Manual approval" checked={env.requiresApproval} disabled={readOnly} onChange={(v) => update((c) => void (c.environments[i].requiresApproval = v))} />
        </div>
        {d.type === "helm" && (
          <>
            {text("kubeconfigCredentialId", "Kubeconfig credential ID", "Secret file, namespace-scoped.")}
            {text("namespace", "Namespace")}
            {text("releaseName", "Helm release")}
            {text("chartPath", "Chart path", "deploy/helm/chart gets a generated minimal chart.")}
          </>
        )}
        {d.type === "dockerSsh" && (
          <>
            {text("host", "Host", "host or host:port")}
            {text("user", "SSH user")}
            {text("remoteDir", "Remote directory")}
            {text("sshCredentialId", "SSH key credential ID")}
            {text("knownHostsCredentialId", "known_hosts credential ID", "Secret file with the pinned host key.")}
          </>
        )}
        {d.type === "azureAppService" && (
          <>
            {text("azureCredentialId", "Service principal credential ID")}
            {text("resourceGroup", "Resource group")}
            {text("appName", "App name")}
            {text("slot", "Slot (optional)", "Deploys to the slot, then swaps.")}
          </>
        )}
      </Grid>
      {!readOnly && (
        <div className="mt-2 flex justify-end">
          <Button variant="ghost" className="text-danger" onClick={() => update((c) => void c.environments.splice(i, 1))}>
            Remove
          </Button>
        </div>
      )}
    </div>
  );
}

export function AiSection({ config: c, update, error, locked, readOnly }: SectionProps) {
  const ai = c.aiReview;
  const ro = (p: string) => readOnly || locked(p) || locked("aiReview");
  return (
    <Card title="AI security review in the pipeline">
      <div className="flex flex-col gap-4">
        <Toggle
          label="Review changes with an AI model"
          hint="Adds a stage that sends the redacted diff (never the whole repository) to the provider using a Jenkins credential."
          checked={ai.enabled}
          disabled={ro("aiReview.enabled")}
          onChange={(v) => update((d) => void (d.aiReview.enabled = v))}
        />
        {ai.enabled && (
          <>
            <Grid>
              <Field label="Provider">
                <Select
                  value={ai.provider}
                  disabled={ro("aiReview.provider")}
                  onChange={(e) =>
                    update((d) => {
                      d.aiReview.provider = e.target.value as AiProvider;
                      if (e.target.value === "anthropic") d.aiReview.model = "claude-opus-5-5";
                    })
                  }
                  options={[
                    { value: "anthropic", label: "Anthropic (Claude)" },
                    { value: "openAiCompatible", label: "OpenAI-compatible" },
                    { value: "azureOpenAi", label: "Azure OpenAI" },
                  ]}
                />
              </Field>
              <Field label={ai.provider === "azureOpenAi" ? "Deployment" : "Model"} error={error("aiReview.model")}>
                <Input value={ai.model} disabled={ro("aiReview.model")} onChange={(e) => update((d) => void (d.aiReview.model = e.target.value))} />
              </Field>
              <Field label="Base URL" hint={ai.provider === "azureOpenAi" ? "Required: your resource URL." : "Optional; https only."} error={error("aiReview.baseUrl")}>
                <Input value={ai.baseUrl ?? ""} disabled={ro("aiReview.baseUrl")} onChange={(e) => update((d) => void (d.aiReview.baseUrl = e.target.value || null))} />
              </Field>
              <Field label="API key credential ID" hint="Secret text in Jenkins." error={error("aiReview.credentialId")}>
                <Input value={ai.credentialId} disabled={ro("aiReview.credentialId")} onChange={(e) => update((d) => void (d.aiReview.credentialId = e.target.value))} />
              </Field>
              <Field label="Severity that counts">
                <Select value={ai.minSeverity} disabled={ro("aiReview.minSeverity")} onChange={(e) => update((d) => void (d.aiReview.minSeverity = e.target.value as Severity))} options={SEVERITIES} />
              </Field>
            </Grid>
            <div className="flex flex-wrap gap-6">
              <Toggle label="Pull requests only" checked={ai.pullRequestsOnly} disabled={ro("aiReview.pullRequestsOnly")} onChange={(v) => update((d) => void (d.aiReview.pullRequestsOnly = v))} />
              <Toggle label="Fail the build on findings" hint="Otherwise the review is advisory." checked={ai.failOnFindings} disabled={ro("aiReview.failOnFindings")} onChange={(v) => update((d) => void (d.aiReview.failOnFindings = v))} />
            </div>
          </>
        )}
      </div>
    </Card>
  );
}

export function CustomStagesSection({ config: c, update, error, readOnly }: SectionProps) {
  return (
    <Card title="Custom stages" actions={!readOnly && <Button onClick={() => update((d) => void d.customStages.push({ name: "Custom", steps: "echo 'Hello'", origin: "user" }))}>Add stage</Button>}>
      <p className="mb-3 text-sm text-muted">Declarative steps inserted as written, after the security scans. They are not checked by PipelineForge: review them.</p>
      {c.customStages.length === 0 && <p className="text-sm text-muted">None.</p>}
      <div className="flex flex-col gap-3">
        {c.customStages.map((s, i) => (
          <div key={i} className="rounded-md border border-border p-3">
            <div className="mb-2 flex items-end gap-3">
              <Field label={`Stage name${s.origin === "imported" ? " (imported)" : ""}`} error={error(`customStages[${i}].name`)}>
                <Input value={s.name} disabled={readOnly} onChange={(e) => update((d) => void (d.customStages[i].name = e.target.value))} />
              </Field>
              {!readOnly && (
                <Button variant="ghost" className="text-danger" onClick={() => update((d) => void d.customStages.splice(i, 1))}>
                  Remove
                </Button>
              )}
            </div>
            <Field label="Steps" error={error(`customStages[${i}].steps`)}>
              <TextArea rows={6} value={s.steps} disabled={readOnly} spellCheck={false} onChange={(e) => update((d) => void (d.customStages[i].steps = e.target.value))} />
            </Field>
          </div>
        ))}
      </div>
    </Card>
  );
}
