"use client";

import { useParams } from "next/navigation";
import { useCallback, useEffect, useState } from "react";
import { canEdit, isAdmin, useWorkspace } from "@/components/AppShell";
import { Alert, Badge, Button, Card, Empty, Field, Input, PageHeader, Select } from "@/components/ui";
import { api, errorMessage } from "@/lib/api";
import type { Connection, ConnectionKind } from "@/lib/types";

const PROVIDERS: Record<ConnectionKind, { value: string; label: string; baseUrl: string; user: string; scope?: string; secret: string }[]> = {
  scm: [
    { value: "BitbucketCloud", label: "Bitbucket Cloud", baseUrl: "https://api.bitbucket.org/2.0", user: "Atlassian account email (API token) or username (app password); empty for an access token", scope: "Workspace (optional)", secret: "API token, app password or access token" },
    { value: "BitbucketServer", label: "Bitbucket Server / Data Center", baseUrl: "https://bitbucket.example.com", user: "Leave empty for an HTTP access token", scope: "Project key (optional)", secret: "HTTP access token (repository read + write)" },
    { value: "GitHub", label: "GitHub / GitHub Enterprise", baseUrl: "https://api.github.com", user: "Not used", scope: "Organisation (optional)", secret: "Fine-grained personal access token (contents + pull requests)" },
  ],
  jenkins: [{ value: "jenkins", label: "Jenkins", baseUrl: "https://jenkins.example.com", user: "User that owns the API token", secret: "Jenkins API token" }],
  ai: [
    { value: "Anthropic", label: "Anthropic (Claude)", baseUrl: "", user: "Not used", secret: "API key" },
    { value: "OpenAiCompatible", label: "OpenAI-compatible", baseUrl: "https://api.openai.com", user: "Not used", secret: "API key" },
    { value: "AzureOpenAi", label: "Azure OpenAI", baseUrl: "https://your-resource.openai.azure.com", user: "Not used", secret: "API key" },
  ],
};

const KIND_LABEL: Record<ConnectionKind, string> = { scm: "Source control", jenkins: "Jenkins", ai: "AI review" };

export default function ConnectionsPage() {
  const { ws } = useParams<{ ws: string }>();
  const workspace = useWorkspace(ws);
  const [items, setItems] = useState<Connection[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [results, setResults] = useState<Record<string, { ok: boolean; message: string }>>({});
  const [testing, setTesting] = useState<string | null>(null);

  const load = useCallback(() => api<Connection[]>(`workspaces/${ws}/connections`).then(setItems, (e) => setError(errorMessage(e))), [ws]);
  useEffect(() => {
    void load();
  }, [load]);

  async function test(id: string) {
    setTesting(id);
    try {
      const r = await api<{ ok: boolean; message: string }>(`workspaces/${ws}/connections/${id}/test`, { method: "POST" });
      setResults((s) => ({ ...s, [id]: r }));
    } catch (e) {
      setResults((s) => ({ ...s, [id]: { ok: false, message: errorMessage(e) } }));
    } finally {
      setTesting(null);
    }
  }

  async function remove(c: Connection) {
    if (!confirm(`Delete connection “${c.name}”? Its secret is removed from the vault.`)) return;
    try {
      await api(`workspaces/${ws}/connections/${c.id}`, { method: "DELETE" });
      await load();
    } catch (e) {
      setError(errorMessage(e));
    }
  }

  return (
    <>
      <PageHeader title="Connections" subtitle="Tokens are stored in the vault and never shown again." />
      {error && <Alert tone="danger" title={error} />}
      <div className="grid gap-5 lg:grid-cols-[3fr_2fr]">
        <div className="flex flex-col gap-4">
          {items?.length === 0 && <Empty title="No connections">An admin can add Bitbucket, GitHub, Jenkins or an AI provider.</Empty>}
          {(["scm", "jenkins", "ai"] as ConnectionKind[]).map((kind) => {
            const list = items?.filter((c) => c.kind === kind) ?? [];
            if (list.length === 0) return null;
            return (
              <Card key={kind} title={KIND_LABEL[kind]}>
                <ul className="flex flex-col divide-y divide-border">
                  {list.map((c) => (
                    <li key={c.id} className="flex flex-wrap items-center gap-3 py-2.5 first:pt-0 last:pb-0">
                      <div className="min-w-0 flex-1">
                        <p className="font-medium">
                          {c.name} <Badge>{c.provider}</Badge>
                        </p>
                        <p className="truncate text-xs text-muted">
                          {c.baseUrl || "default endpoint"}
                          {c.scope ? ` · ${c.scope}` : ""}
                          {c.model ? ` · ${c.model}` : ""}
                        </p>
                        {results[c.id] && <p className={`mt-1 text-xs ${results[c.id].ok ? "text-ok" : "text-danger"}`}>{results[c.id].message}</p>}
                      </div>
                      {canEdit(workspace) && (
                        <Button onClick={() => test(c.id)} busy={testing === c.id}>
                          Test
                        </Button>
                      )}
                      {isAdmin(workspace) && (
                        <Button variant="danger" onClick={() => remove(c)}>
                          Delete
                        </Button>
                      )}
                    </li>
                  ))}
                </ul>
              </Card>
            );
          })}
        </div>
        {isAdmin(workspace) && <NewConnection ws={ws} onCreated={load} aiEnabled={!!workspace?.aiEnabled} />}
      </div>
    </>
  );
}

function NewConnection({ ws, onCreated, aiEnabled }: { ws: string; onCreated: () => void; aiEnabled: boolean }) {
  const [kind, setKind] = useState<ConnectionKind>("scm");
  const [provider, setProvider] = useState(PROVIDERS.scm[0].value);
  const meta = PROVIDERS[kind].find((p) => p.value === provider) ?? PROVIDERS[kind][0];
  const [form, setForm] = useState({ name: "", baseUrl: meta.baseUrl, username: "", scope: "", model: "claude-opus-5-5", secret: "" });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  function pick(k: ConnectionKind, p: string) {
    const m = PROVIDERS[k].find((x) => x.value === p) ?? PROVIDERS[k][0];
    setKind(k);
    setProvider(m.value);
    setForm((f) => ({ ...f, baseUrl: m.baseUrl, model: m.value === "Anthropic" ? "claude-opus-5-5" : f.model }));
  }

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await api(`workspaces/${ws}/connections`, {
        method: "POST",
        body: { kind, provider, name: form.name, baseUrl: form.baseUrl, username: form.username || null, scope: form.scope || null, model: kind === "ai" ? form.model : null, secret: form.secret },
      });
      setForm((f) => ({ ...f, name: "", secret: "", username: "", scope: "" }));
      onCreated();
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setBusy(false);
    }
  }

  const set = (k: keyof typeof form) => (e: React.ChangeEvent<HTMLInputElement>) => setForm((f) => ({ ...f, [k]: e.target.value }));

  return (
    <Card title="Add connection">
      <form onSubmit={submit} className="flex flex-col gap-3" autoComplete="off">
        <div className="grid grid-cols-2 gap-3">
          <Field label="Type">
            <Select value={kind} onChange={(e) => pick(e.target.value as ConnectionKind, PROVIDERS[e.target.value as ConnectionKind][0].value)} options={(["scm", "jenkins", "ai"] as ConnectionKind[]).map((k) => ({ value: k, label: KIND_LABEL[k] }))} />
          </Field>
          <Field label="Provider">
            <Select value={provider} onChange={(e) => pick(kind, e.target.value)} options={PROVIDERS[kind].map((p) => ({ value: p.value, label: p.label }))} />
          </Field>
        </div>
        {kind === "ai" && !aiEnabled && <Alert tone="warn" title="AI is disabled for this workspace">Enable it under Settings before the connection can be used.</Alert>}
        <Field label="Name">
          <Input value={form.name} onChange={set("name")} required maxLength={100} placeholder="Company Bitbucket" />
        </Field>
        <Field label="Base URL" hint={kind === "ai" ? "Leave empty for the provider default." : "Requests are limited to this host."}>
          <Input value={form.baseUrl} onChange={set("baseUrl")} required={!(kind === "ai" && provider !== "AzureOpenAi")} type="url" />
        </Field>
        {kind !== "ai" && provider !== "GitHub" && (
          <Field label="Username" hint={meta.user}>
            <Input value={form.username} onChange={set("username")} required={kind === "jenkins"} />
          </Field>
        )}
        {meta.scope && (
          <Field label={meta.scope}>
            <Input value={form.scope} onChange={set("scope")} />
          </Field>
        )}
        {kind === "ai" && (
          <Field label={provider === "AzureOpenAi" ? "Deployment name" : "Model"}>
            <Input value={form.model} onChange={set("model")} required />
          </Field>
        )}
        <Field label="Secret" hint={`${meta.secret}. Use the least privilege that works.`}>
          <Input value={form.secret} onChange={set("secret")} required type="password" autoComplete="new-password" />
        </Field>
        {error && <Alert tone="danger" title={error} />}
        <Button variant="primary" type="submit" busy={busy}>
          Save connection
        </Button>
      </form>
    </Card>
  );
}
