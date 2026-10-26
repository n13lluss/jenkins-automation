// Mirrors the backend contracts (camelCase JSON, enums as camelCase strings).

export type Ecosystem = "maven" | "gradle" | "npm" | "pnpm" | "yarn" | "dotNet" | "python" | "go" | "composer";
export type AgentType = "kubernetes" | "docker";
export type QualityPlatform = "sonarQube" | "sonarCloud";
export type DependencyScanner = "owaspDependencyCheck" | "trivyFs" | "osvScanner" | "snyk" | "npmAudit";
export type Severity = "low" | "medium" | "high" | "critical";
export type DastMode = "baseline" | "full" | "api";
export type ComplianceProfile = "none" | "iso27001";
export type DeployTargetType = "none" | "helm" | "dockerSsh" | "azureAppService";
export type AiProvider = "anthropic" | "openAiCompatible" | "azureOpenAi";
export type WorkspaceRole = "viewer" | "maintainer" | "admin";
export type ConnectionKind = "scm" | "jenkins" | "ai";

export type ModuleConfig = {
  name: string;
  path: string;
  ecosystem: Ecosystem;
  languageVersion?: string | null;
  runTests: boolean;
  coverage: boolean;
  dockerfile?: string | null;
  imageName?: string | null;
};

export type DeployTarget = {
  type: DeployTargetType;
  kubeconfigCredentialId: string;
  namespace: string;
  releaseName: string;
  chartPath: string;
  sshCredentialId: string;
  knownHostsCredentialId: string;
  host: string;
  user: string;
  remoteDir: string;
  azureCredentialId: string;
  resourceGroup: string;
  appName: string;
  slot?: string | null;
};

export type EnvironmentConfig = {
  key: string;
  displayName: string;
  branchPattern: string;
  url?: string | null;
  requiresApproval: boolean;
  deploy: DeployTarget;
};

export type PipelineConfig = {
  name: string;
  agent: { type: AgentType; dockerLabel: string; kubernetesCloud?: string | null; imageOverrides: Record<string, string> };
  modules: ModuleConfig[];
  registry?: { host: string; credentialId: string } | null;
  quality: {
    enabled: boolean;
    platform: QualityPlatform;
    serverName: string;
    projectKey: string;
    organization?: string | null;
    waitForQualityGate: boolean;
    abortOnQualityGateFailure: boolean;
  };
  security: {
    secretScan: boolean;
    dependencyScanners: DependencyScanner[];
    sbom: boolean;
    licenseCheck: boolean;
    containerScan: boolean;
    iacScan: boolean;
    failOnSeverity: Severity;
    nvdApiKeyCredentialId: string;
    snykCredentialId: string;
    dast: { enabled: boolean; mode: DastMode; apiSpec?: string | null; environments: string[] };
  };
  compliance: { profile: ComplianceProfile; approverGroup: string };
  environments: EnvironmentConfig[];
  aiReview: {
    enabled: boolean;
    provider: AiProvider;
    model: string;
    baseUrl?: string | null;
    credentialId: string;
    failOnFindings: boolean;
    minSeverity: Severity;
    pullRequestsOnly: boolean;
  };
  customStages: { name: string; steps: string; origin: "user" | "imported" }[];
  options: { timeoutMinutes: number; buildsToKeep: number; disableConcurrentBuilds: boolean };
};

export type ValidationError = { path: string; message: string };
export type GeneratedFile = { path: string; content: string };
export type RequiredCredential = { id: string; kind: string; purpose: string };

export type GenerationResponse = {
  success: boolean;
  errors: ValidationError[];
  files: GeneratedFile[];
  compliance: { changes: string[]; warnings: string[] };
  credentials: RequiredCredential[];
  plugins: string[];
  effectiveConfig: PipelineConfig;
  lockedPaths: string[];
};

export type DetectedModule = { name: string; path: string; ecosystem: Ecosystem; languageVersion?: string; dockerfile?: string; evidence: string[] };
export type RepoProfile = {
  modules: DetectedModule[];
  helmCharts: string[];
  hasKubernetesManifests: boolean;
  hasTerraform: boolean;
  hasDockerCompose: boolean;
  existingJenkinsfile?: string;
  sonarProjectKey?: string;
  fileCount: number;
  truncated: boolean;
  notes: string[];
};

export type ScanResponse = { profile: RepoProfile; config: PipelineConfig; reasons: string[] };
export type ImportResult = {
  config: PipelineConfig;
  stages: { name: string; mappedTo: string; custom: boolean }[];
  gaps: string[];
  credentialIds: string[];
  warnings: string[];
};

export type Workspace = { id: string; name: string; aiEnabled: boolean; role: WorkspaceRole };
export type Me = { id: string; email: string; displayName: string; workspaces: Workspace[] };
export type Member = { userId: string; email: string; displayName: string; role: WorkspaceRole };
export type Connection = {
  id: string;
  kind: ConnectionKind;
  name: string;
  provider: string;
  baseUrl: string;
  username?: string;
  scope?: string;
  model?: string;
  createdAt: string;
};
export type ScmRepository = { fullName: string; name: string; defaultBranch?: string; webUrl?: string; isPrivate: boolean };
export type ScmBranch = { name: string; commitId?: string };
export type Preset = { id: string; name: string; description: string; version: number; config: Partial<PipelineConfig>; lockedPaths: string[]; updatedAt: string };
export type ProjectSummary = { id: string; name: string; source: string; repository?: string; branch?: string; updatedAt: string };
export type Project = {
  id: string;
  name: string;
  source: "scan" | "blank" | "import";
  scmConnectionId?: string;
  repository?: string;
  branch?: string;
  presetId?: string;
  presetVersion?: number;
  config: PipelineConfig;
  profile?: RepoProfile;
  createdAt: string;
  updatedAt: string;
};
export type Run = { id: string; createdAt: string; configSha256: string; pullRequestUrl?: string; files: { path: string; sha256: string }[] };
export type AuditEntry = { id: number; at: string; userEmail?: string; action: string; target?: string; success: boolean; detail?: string; ipAddress?: string };
export type AiReview = { summary: string; suggestions: { severity: string; title: string; file: string; detail: string }[]; problem?: string; payloadSha256: string };
