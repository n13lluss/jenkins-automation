"use client";

import { useRouter } from "next/navigation";
import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { api, errorMessage } from "@/lib/api";
import type { BuiltInPreset, GenerationResponse, PipelineConfig, Preset, Project, RepoProfile } from "@/lib/types";
import { canEdit, isAdmin, useWorkspace } from "../AppShell";
import { Alert, Badge, Button, Card, cx, Field, Select, Spinner } from "../ui";
import { ActionsPanel } from "./ActionsPanel";
import { OperationsSection } from "./operationsSections";
import { FileViewer } from "./FileViewer";
import { AiSection, CustomStagesSection, EnvironmentsSection, ProjectSection, QualitySection, SecuritySection, type SectionProps } from "./sections";
import { DynamicTestingSection, ScannerOptionsCard, SupplyChainSection, TicketsSection, TriggersCard } from "./workflowSections";

type Step = { id: string; label: string; prefixes: string[]; render: (p: SectionProps) => ReactNode };

const STEPS: Step[] = [
  { id: "project", label: "Project & modules", prefixes: ["name", "layout", "library", "agent", "modules", "registry", "options"], render: (p) => <ProjectSection {...p} /> },
  { id: "supply", label: "Supply chain", prefixes: ["dependencyProxy", "signing", "security.dependencyPinning", "security.dockerfileLint", "security.allowedBaseRegistries", "clusterPolicy"], render: (p) => <SupplyChainSection {...p} /> },
  { id: "quality", label: "Code quality", prefixes: ["quality"], render: (p) => <QualitySection {...p} /> },
  {
    id: "security",
    label: "Security & compliance",
    prefixes: ["security.secretScan", "security.dependencyScanners", "security.nvd", "security.snyk", "security.containerScanner", "security.iac", "compliance"],
    render: (p) => (
      <div className="flex flex-col gap-4">
        <SecuritySection {...p} />
        <ScannerOptionsCard {...p} />
      </div>
    ),
  },
  {
    id: "environments",
    label: "Environments & deploy",
    prefixes: ["environments", "triggers", "security.dast.enabled", "security.dast.environments", "security.dast.apiSpec"],
    render: (p) => (
      <div className="flex flex-col gap-4">
        <EnvironmentsSection {...p} />
        <TriggersCard {...p} />
      </div>
    ),
  },
  { id: "dynamic", label: "Dynamic testing", prefixes: ["security.dast.ephemeral", "security.dast.authenticated", "security.dast.schedule", "rateLimit"], render: (p) => <DynamicTestingSection {...p} /> },
  { id: "operations", label: "Operations", prefixes: ["migrations", "notifications", "metrics", "security.exceptionRegister"], render: (p) => <OperationsSection {...p} /> },
  { id: "ai", label: "AI review", prefixes: ["aiReview"], render: (p) => <AiSection {...p} /> },
  { id: "tickets", label: "Tickets", prefixes: ["ticketing"], render: (p) => <TicketsSection {...p} /> },
  { id: "custom", label: "Custom stages", prefixes: ["customStages"], render: (p) => <CustomStagesSection {...p} /> },
];

export type EditorProps = {
  ws: string;
  initial: PipelineConfig;
  project?: Project;
  source: "scan" | "blank" | "import";
  link?: { scmConnectionId: string; repository: string; branch: string };
  profile?: RepoProfile;
  intro?: ReactNode;
};

export function PipelineEditor({ ws, initial, project, source, link, profile, intro }: EditorProps) {
  const workspace = useWorkspace(ws);
  const readOnly = !canEdit(workspace);
  const router = useRouter();
  const [config, setConfig] = useState<PipelineConfig>(initial);
  const [step, setStep] = useState<string>("project");
  const [preview, setPreview] = useState<GenerationResponse | null>(null);
  const [previewing, setPreviewing] = useState(false);
  const [previewError, setPreviewError] = useState<string | null>(null);
  const [presets, setPresets] = useState<Preset[]>([]);
  const [builtIns, setBuiltIns] = useState<BuiltInPreset[]>([]);
  const [builtInApplied, setBuiltInApplied] = useState<string | null>(null);
  const [presetId, setPresetId] = useState<string>(project?.presetId ?? "");
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [dirty, setDirty] = useState(!project);
  const seq = useRef(0);

  useEffect(() => {
    api<Preset[]>(`workspaces/${ws}/presets`).then(setPresets, () => setPresets([]));
    api<BuiltInPreset[]>("presets/built-in").then(setBuiltIns, () => setBuiltIns([]));
  }, [ws]);

  const update = useCallback((fn: (c: PipelineConfig) => void) => {
    setConfig((c) => {
      const next = structuredClone(c);
      fn(next);
      return next;
    });
    setDirty(true);
  }, []);

  const runPreview = useCallback(
    async (cfg: PipelineConfig, applyPreset = false) => {
      if (readOnly) return null;
      const n = ++seq.current;
      setPreviewing(true);
      try {
        const r = await api<GenerationResponse>(`workspaces/${ws}/pipelines/preview`, { method: "POST", body: { config: cfg, presetId: presetId || null, applyPreset } });
        if (n === seq.current) {
          setPreview(r);
          setPreviewError(null);
        }
        return r;
      } catch (e) {
        if (n === seq.current) setPreviewError(errorMessage(e));
        return null;
      } finally {
        if (n === seq.current) setPreviewing(false);
      }
    },
    [ws, presetId, readOnly],
  );

  useEffect(() => {
    const t = setTimeout(() => void runPreview(config), 400);
    return () => clearTimeout(t);
  }, [config, runPreview]);

  const errors = useMemo(() => new Map((preview?.errors ?? []).map((e) => [e.path, e.message])), [preview]);
  const error = useCallback((path: string) => errors.get(path), [errors]);
  const lockedPaths = useMemo(() => preview?.lockedPaths ?? [], [preview]);
  const locked = useCallback((path: string) => lockedPaths.some((l) => path === l || path.startsWith(`${l}.`)), [lockedPaths]);
  const stepErrors = (s: Step) => (preview?.errors ?? []).filter((e) => s.prefixes.some((p) => e.path === p || e.path.startsWith(p))).length;

  async function applyPreset(id: string) {
    if (id.startsWith("builtin:")) {
      const builtInPresetId = id.slice("builtin:".length);
      const r = await api<GenerationResponse>(`workspaces/${ws}/pipelines/preview`, { method: "POST", body: { config, presetId: presetId || null, applyPreset: false, builtInPresetId } }).catch(() => null);
      if (r) {
        setConfig(r.effectiveConfig);
        setBuiltInApplied(builtIns.find((b) => b.id === builtInPresetId)?.name ?? null);
        setDirty(true);
      }
      return;
    }
    setPresetId(id);
    if (!id) return;
    const r = await api<GenerationResponse>(`workspaces/${ws}/pipelines/preview`, { method: "POST", body: { config, presetId: id, applyPreset: true } }).catch(() => null);
    if (r) {
      setConfig(r.effectiveConfig);
      setDirty(true);
    }
  }

  async function save() {
    setSaving(true);
    setSaveError(null);
    const body = {
      name: config.name,
      source,
      config,
      scmConnectionId: project?.scmConnectionId ?? link?.scmConnectionId ?? null,
      repository: project?.repository ?? link?.repository ?? null,
      branch: project?.branch ?? link?.branch ?? null,
      presetId: presetId || null,
      profile: profile ?? null,
    };
    try {
      if (project) {
        const saved = await api<Project>(`workspaces/${ws}/pipelines/${project.id}`, { method: "PUT", body });
        setConfig(saved.config);
        setDirty(false);
      } else {
        const saved = await api<Project>(`workspaces/${ws}/pipelines`, { method: "POST", body });
        router.replace(`/w/${ws}/pipelines/${saved.id}`);
      }
    } catch (e) {
      setSaveError(errorMessage(e));
    } finally {
      setSaving(false);
    }
  }

  const sectionProps: SectionProps = { config, update, error, locked, readOnly };
  const current = STEPS.find((s) => s.id === step);

  return (
    <div className="flex flex-col gap-5">
      {intro}
      <div className="flex flex-wrap items-end gap-3 rounded-lg border border-border bg-surface p-3">
        <Field label="Preset">
          <Select
            value={presetId}
            disabled={readOnly}
            onChange={(e) => void applyPreset(e.target.value)}
            options={[
              { value: "", label: "No preset" },
              ...presets.map((p) => ({ value: p.id, label: `${p.name} (v${p.version})${p.lockedPaths.length ? " · locked" : ""}` })),
              ...builtIns.map((b) => ({ value: `builtin:${b.id}`, label: `Built-in: ${b.name}` })),
            ]}
          />
        </Field>
        {builtInApplied && <p className="pb-1.5 text-xs text-muted">Applied built-in preset: {builtInApplied}. Review the steps for values to fill in (URLs, credentials).</p>}
        {lockedPaths.length > 0 && <p className="pb-1.5 text-xs text-muted">Locked by preset: {lockedPaths.join(", ")}</p>}
        <div className="ml-auto flex items-center gap-3">
          {previewing && <Spinner />}
          {preview && (preview.success ? <Badge tone="ok">Valid</Badge> : <Badge tone="danger">{preview.errors.length} to fix</Badge>)}
          {!readOnly && (
            <Button variant="primary" onClick={save} busy={saving} disabled={!dirty && !!project}>
              {project ? (dirty ? "Save changes" : "Saved") : "Save pipeline"}
            </Button>
          )}
        </div>
      </div>
      {saveError && <Alert tone="danger" title="Could not save">{saveError}</Alert>}
      {previewError && <Alert tone="danger" title="Preview failed">{previewError}</Alert>}

      <div className="grid gap-5 lg:grid-cols-[14rem_1fr]">
        <nav className="flex flex-row gap-1 overflow-x-auto lg:flex-col" aria-label="Steps">
          {[...STEPS, { id: "review", label: "Review & output", prefixes: [], render: () => null } as Step].map((s, i) => {
            const count = s.id === "review" ? 0 : stepErrors(s);
            return (
              <button
                key={s.id}
                onClick={() => setStep(s.id)}
                className={cx("flex items-center justify-between gap-2 whitespace-nowrap rounded-md px-3 py-2 text-left text-sm", step === s.id ? "bg-surface font-medium shadow-sm ring-1 ring-border" : "text-muted hover:text-text")}
              >
                <span>
                  {i + 1}. {s.label}
                </span>
                {count > 0 && <Badge tone="danger">{count}</Badge>}
              </button>
            );
          })}
        </nav>

        <div className="min-w-0">
          {current && current.render(sectionProps)}
          {step === "review" && (
            <Review preview={preview} selected={selected} setSelected={setSelected} onJump={(path) => setStep(STEPS.find((s) => s.prefixes.some((p) => path.startsWith(p)))?.id ?? "project")}>
              {project ? (
                <ActionsPanel ws={ws} project={project} dirty={dirty} selected={selected} canEdit={!readOnly} admin={isAdmin(workspace)} valid={!!preview?.success} onSelect={(paths) => setSelected(new Set(paths))} />
              ) : (
                <Alert title="Save the pipeline to download it, open a pull request or create the Jenkins job." />
              )}
            </Review>
          )}
          {step !== "review" && (
            <div className="mt-4 flex justify-end">
              <Button onClick={() => setStep(STEPS[STEPS.findIndex((s) => s.id === step) + 1]?.id ?? "review")}>Next</Button>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

function Review({ preview, selected, setSelected, onJump, children }: { preview: GenerationResponse | null; selected: Set<string>; setSelected: (s: Set<string>) => void; onJump: (path: string) => void; children: ReactNode }) {
  if (!preview) return <Spinner />;
  return (
    <div className="flex flex-col gap-4">
      {!preview.success && (
        <Alert tone="danger" title="Fix these before generating">
          <ul className="mt-1 list-disc pl-5">
            {preview.errors.map((e) => (
              <li key={e.path + e.message}>
                <button className="underline" onClick={() => onJump(e.path)}>
                  {e.path}
                </button>
                : {e.message}
              </li>
            ))}
          </ul>
        </Alert>
      )}
      {preview.compliance.changes.length > 0 && (
        <Alert tone="info" title="Applied by the compliance profile">
          <ul className="mt-1 list-disc pl-5">
            {preview.compliance.changes.map((c) => (
              <li key={c}>{c}</li>
            ))}
          </ul>
        </Alert>
      )}
      {preview.compliance.warnings.map((w) => (
        <Alert key={w} tone="warn" title={w} />
      ))}
      {preview.success && (
        <>
          <FileViewer files={preview.files} selected={selected} onSelectedChange={setSelected} />
          <div className="grid gap-4 md:grid-cols-2">
            <Card title="Jenkins credentials to create">
              {preview.credentials.length === 0 ? (
                <p className="text-sm text-muted">None.</p>
              ) : (
                <ul className="flex flex-col gap-1.5 text-sm">
                  {preview.credentials.map((c) => (
                    <li key={c.id}>
                      <code className="text-xs">{c.id}</code> <Badge>{c.kind}</Badge>
                      <p className="text-xs text-muted">{c.purpose}</p>
                    </li>
                  ))}
                </ul>
              )}
            </Card>
            <Card title="Required Jenkins plugins">
              <ul className="list-disc pl-5 text-sm">
                {preview.plugins.map((p) => (
                  <li key={p}>{p}</li>
                ))}
              </ul>
            </Card>
          </div>
          {children}
        </>
      )}
    </div>
  );
}
