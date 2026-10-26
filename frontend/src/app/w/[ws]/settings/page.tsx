"use client";

import { useParams } from "next/navigation";
import { useCallback, useEffect, useState } from "react";
import { isAdmin, useMe, useWorkspace } from "@/components/AppShell";
import { Alert, Button, Card, Field, Input, PageHeader, Select, Toggle } from "@/components/ui";
import { api, errorMessage } from "@/lib/api";
import type { Member, WorkspaceRole } from "@/lib/types";

const ROLES: { value: WorkspaceRole; label: string }[] = [
  { value: "viewer", label: "Viewer: read pipelines and download" },
  { value: "maintainer", label: "Maintainer: create and change pipelines" },
  { value: "admin", label: "Admin: connections, members, locks, audit" },
];

export default function SettingsPage() {
  const { ws } = useParams<{ ws: string }>();
  const workspace = useWorkspace(ws);
  const { me, reload } = useMe();
  const [members, setMembers] = useState<Member[]>([]);
  const [email, setEmail] = useState("");
  const [role, setRole] = useState<WorkspaceRole>("maintainer");
  const [error, setError] = useState<string | null>(null);
  const [name, setName] = useState(workspace?.name ?? "");

  const load = useCallback(() => api<Member[]>(`workspaces/${ws}/members`).then(setMembers, (e) => setError(errorMessage(e))), [ws]);
  useEffect(() => {
    void load();
  }, [load]);

  if (!isAdmin(workspace)) return <Alert tone="warn" title="Only workspace admins can change settings." />;

  async function call(fn: () => Promise<unknown>) {
    setError(null);
    try {
      await fn();
      await Promise.all([load(), reload()]);
    } catch (e) {
      setError(errorMessage(e));
    }
  }

  return (
    <>
      <PageHeader title="Settings" subtitle={workspace?.name} />
      {error && <Alert tone="danger" title={error} />}
      <div className="grid gap-5 lg:grid-cols-2">
        <Card title="Workspace">
          <div className="flex flex-col gap-4">
            <form className="flex items-end gap-3" onSubmit={(e) => { e.preventDefault(); void call(() => api(`workspaces/${ws}`, { method: "PATCH", body: { name } })); }}>
              <Field label="Name">
                <Input value={name} onChange={(e) => setName(e.target.value)} />
              </Field>
              <Button type="submit">Rename</Button>
            </form>
            <Toggle
              label="Allow AI calls"
              hint="Off by default. When on, maintainers can send generated pipelines (never repository source) to the configured AI provider; every call is audited."
              checked={!!workspace?.aiEnabled}
              onChange={(v) => void call(() => api(`workspaces/${ws}`, { method: "PATCH", body: { aiEnabled: v } }))}
            />
          </div>
        </Card>
        <Card title="Members">
          <ul className="mb-4 flex flex-col divide-y divide-border text-sm">
            {members.map((m) => (
              <li key={m.userId} className="flex flex-wrap items-center gap-3 py-2">
                <span className="min-w-0 flex-1 truncate">
                  {m.displayName} <span className="text-muted">{m.email}</span>
                </span>
                {m.userId === me.id ? (
                  <span className="text-muted">{m.role} (you)</span>
                ) : (
                  <>
                    <Select value={m.role} onChange={(e) => void call(() => api(`workspaces/${ws}/members`, { method: "PUT", body: { email: m.email, role: e.target.value } }))} options={ROLES.map((r) => ({ value: r.value, label: r.value }))} />
                    <Button variant="ghost" className="text-danger" onClick={() => void call(() => api(`workspaces/${ws}/members/${m.userId}`, { method: "DELETE" }))}>
                      Remove
                    </Button>
                  </>
                )}
              </li>
            ))}
          </ul>
          <form className="flex flex-col gap-3" onSubmit={(e) => { e.preventDefault(); void call(() => api(`workspaces/${ws}/members`, { method: "PUT", body: { email, role } })).then(() => setEmail("")); }}>
            <Field label="Add member by email" hint="They must have signed in once.">
              <Input type="email" value={email} onChange={(e) => setEmail(e.target.value)} required />
            </Field>
            <Field label="Role">
              <Select value={role} onChange={(e) => setRole(e.target.value as WorkspaceRole)} options={ROLES} />
            </Field>
            <Button type="submit">Add or update</Button>
          </form>
        </Card>
      </div>
    </>
  );
}
