"use client";

import { useParams } from "next/navigation";
import { useCallback, useEffect, useState } from "react";
import { canEdit, isAdmin, useWorkspace } from "@/components/AppShell";
import { Alert, Badge, Button, Card, Empty, Field, Input, PageHeader, TextArea } from "@/components/ui";
import { api, errorMessage } from "@/lib/api";
import type { Preset } from "@/lib/types";

const EXAMPLE = JSON.stringify(
  {
    compliance: { profile: "iso27001", approverGroup: "release-managers" },
    security: { dependencyScanners: ["trivyFs", "owaspDependencyCheck"], failOnSeverity: "high" },
    quality: { serverName: "sonarqube", waitForQualityGate: true },
    agent: { type: "kubernetes" },
  },
  null,
  2,
);

const LOCKABLE = ["compliance.profile", "compliance.approverGroup", "security.secretScan", "security.dependencyScanners", "security.failOnSeverity", "security.sbom", "quality.enabled", "quality.waitForQualityGate", "aiReview.enabled", "agent.type"];

export default function PresetsPage() {
  const { ws } = useParams<{ ws: string }>();
  const workspace = useWorkspace(ws);
  const [presets, setPresets] = useState<Preset[] | null>(null);
  const [editing, setEditing] = useState<Preset | "new" | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(() => api<Preset[]>(`workspaces/${ws}/presets`).then(setPresets, (e) => setError(errorMessage(e))), [ws]);
  useEffect(() => {
    void load();
  }, [load]);

  async function remove(p: Preset) {
    if (!confirm(`Delete preset “${p.name}”?`)) return;
    try {
      await api(`workspaces/${ws}/presets/${p.id}`, { method: "DELETE" });
      await load();
    } catch (e) {
      setError(errorMessage(e));
    }
  }

  return (
    <>
      <PageHeader
        title="Presets"
        subtitle="Team standards applied on top of a scan, blank project or import. Admins can lock settings."
        actions={canEdit(workspace) && <Button variant="primary" onClick={() => setEditing("new")}>New preset</Button>}
      />
      {error && <Alert tone="danger" title={error} />}
      <div className="grid gap-5 lg:grid-cols-2">
        <div className="flex flex-col gap-3">
          {presets?.length === 0 && <Empty title="No presets yet">For example: “Java backend ISO standard”.</Empty>}
          {presets?.map((p) => (
            <Card
              key={p.id}
              title={
                <span>
                  {p.name} <Badge>v{p.version}</Badge>
                </span>
              }
              actions={
                <>
                  {canEdit(workspace) && <Button onClick={() => setEditing(p)}>Edit</Button>}
                  {isAdmin(workspace) && (
                    <Button variant="danger" onClick={() => remove(p)}>
                      Delete
                    </Button>
                  )}
                </>
              }
            >
              {p.description && <p className="mb-2 text-sm text-muted">{p.description}</p>}
              {p.lockedPaths.length > 0 && (
                <p className="mb-2 text-xs">
                  Locked: {p.lockedPaths.map((l) => <Badge key={l} tone="accent">{l}</Badge>)}
                </p>
              )}
              <pre className="max-h-48 overflow-auto rounded bg-code p-2 font-mono text-xs">{JSON.stringify(p.config, null, 2)}</pre>
            </Card>
          ))}
        </div>
        {editing && <PresetForm ws={ws} preset={editing === "new" ? null : editing} admin={isAdmin(workspace)} onDone={() => { setEditing(null); void load(); }} />}
      </div>
    </>
  );
}

function PresetForm({ ws, preset, admin, onDone }: { ws: string; preset: Preset | null; admin: boolean; onDone: () => void }) {
  const [name, setName] = useState(preset?.name ?? "");
  const [description, setDescription] = useState(preset?.description ?? "");
  const [json, setJson] = useState(preset ? JSON.stringify(preset.config, null, 2) : EXAMPLE);
  const [locks, setLocks] = useState<string[]>(preset?.lockedPaths ?? []);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function save(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const config = JSON.parse(json);
      const body = { name, description, config, lockedPaths: locks };
      if (preset) await api(`workspaces/${ws}/presets/${preset.id}`, { method: "PUT", body });
      else await api(`workspaces/${ws}/presets`, { method: "POST", body });
      onDone();
    } catch (err) {
      setError(err instanceof SyntaxError ? "The configuration is not valid JSON." : errorMessage(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Card title={preset ? `Edit ${preset.name} (saves v${preset.version + 1})` : "New preset"}>
      <form onSubmit={save} className="flex flex-col gap-3">
        <Field label="Name">
          <Input value={name} onChange={(e) => setName(e.target.value)} required maxLength={100} />
        </Field>
        <Field label="Description">
          <Input value={description} onChange={(e) => setDescription(e.target.value)} maxLength={1000} />
        </Field>
        <Field label="Configuration (partial, JSON)" hint="Merged onto the pipeline config. Use null to clear a value.">
          <TextArea rows={14} value={json} onChange={(e) => setJson(e.target.value)} spellCheck={false} />
        </Field>
        <fieldset className="flex flex-col gap-1.5">
          <legend className="mb-1 text-sm font-medium">Locked settings {!admin && <span className="text-xs text-muted">(admins only)</span>}</legend>
          <div className="grid grid-cols-2 gap-1.5">
            {LOCKABLE.map((l) => (
              <label key={l} className="flex items-center gap-2 font-mono text-xs">
                <input type="checkbox" disabled={!admin} checked={locks.includes(l)} onChange={(e) => setLocks((s) => (e.target.checked ? [...s, l] : s.filter((x) => x !== l)))} />
                {l}
              </label>
            ))}
          </div>
        </fieldset>
        {error && <Alert tone="danger" title={error} />}
        <div className="flex gap-2">
          <Button variant="primary" type="submit" busy={busy}>
            Save
          </Button>
          <Button type="button" onClick={onDone}>
            Cancel
          </Button>
        </div>
      </form>
    </Card>
  );
}
