import type { DeployTarget, Ecosystem, EnvironmentConfig, ModuleConfig, PipelineConfig } from "./types";

export const ECOSYSTEMS: { value: Ecosystem; label: string; version: string; versionLabel: string }[] = [
  { value: "maven", label: "Java · Maven", version: "21", versionLabel: "Java" },
  { value: "gradle", label: "Java/Kotlin · Gradle", version: "21", versionLabel: "Java" },
  { value: "npm", label: "Node.js · npm", version: "22", versionLabel: "Node.js" },
  { value: "pnpm", label: "Node.js · pnpm", version: "22", versionLabel: "Node.js" },
  { value: "yarn", label: "Node.js · Yarn", version: "22", versionLabel: "Node.js" },
  { value: "dotNet", label: "C# · .NET", version: "10.0", versionLabel: ".NET" },
  { value: "python", label: "Python", version: "3.13", versionLabel: "Python" },
  { value: "go", label: "Go", version: "1.25", versionLabel: "Go" },
  { value: "composer", label: "PHP · Composer", version: "8.3", versionLabel: "PHP" },
];

export function defaultDeploy(app: string, key: string): DeployTarget {
  return {
    type: "none",
    kubeconfigCredentialId: "kubeconfig",
    namespace: `${app}-${key}`.slice(0, 63),
    releaseName: app,
    chartPath: "deploy/helm/chart",
    sshCredentialId: "deploy-ssh",
    knownHostsCredentialId: "deploy-known-hosts",
    host: "",
    user: "deploy",
    remoteDir: `/opt/${app}`,
    azureCredentialId: "azure-sp",
    resourceGroup: "",
    appName: `${app}-${key}`,
    slot: null,
  };
}

export function dtap(app: string): EnvironmentConfig[] {
  return [
    { key: "dev", displayName: "Development", branchPattern: "develop", requiresApproval: false, url: null, deploy: defaultDeploy(app, "dev") },
    { key: "test", displayName: "Test", branchPattern: "develop", requiresApproval: false, url: null, deploy: defaultDeploy(app, "test") },
    { key: "acc", displayName: "Acceptance", branchPattern: "release/*", requiresApproval: false, url: null, deploy: defaultDeploy(app, "acc") },
    { key: "prod", displayName: "Production", branchPattern: "main", requiresApproval: true, url: null, deploy: defaultDeploy(app, "prod") },
  ];
}

export function newModule(name: string, ecosystem: Ecosystem = "maven", path = "."): ModuleConfig {
  return { name, path, ecosystem, languageVersion: null, runTests: true, coverage: true, dockerfile: null, imageName: null };
}

export function blankConfig(name = "app"): PipelineConfig {
  return {
    name,
    agent: { type: "kubernetes", dockerLabel: "docker", kubernetesCloud: null, imageOverrides: {} },
    modules: [newModule(name)],
    registry: null,
    quality: {
      enabled: true,
      platform: "sonarQube",
      serverName: "sonarqube",
      projectKey: name,
      organization: null,
      waitForQualityGate: true,
      abortOnQualityGateFailure: true,
    },
    security: {
      secretScan: true,
      dependencyScanners: ["trivyFs"],
      sbom: true,
      licenseCheck: false,
      containerScan: true,
      iacScan: false,
      failOnSeverity: "high",
      nvdApiKeyCredentialId: "nvd-api-key",
      snykCredentialId: "snyk-token",
      dast: { enabled: false, mode: "baseline", apiSpec: null, environments: ["test"] },
    },
    compliance: { profile: "none", approverGroup: "release-managers" },
    environments: dtap(name),
    aiReview: {
      enabled: false,
      provider: "anthropic",
      model: "claude-opus-5-5",
      baseUrl: null,
      credentialId: "ai-api-key",
      failOnFindings: false,
      minSeverity: "high",
      pullRequestsOnly: true,
    },
    customStages: [],
    options: { timeoutMinutes: 60, buildsToKeep: 20, disableConcurrentBuilds: true },
  };
}

export function slug(value: string): string {
  return value.toLowerCase().replace(/[^a-z0-9._-]+/g, "-").replace(/^[-._]+|[-._]+$/g, "").slice(0, 63) || "app";
}
