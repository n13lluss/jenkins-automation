"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { AppShell, useMe } from "@/components/AppShell";
import { Alert, Button, Card, Empty, Field, Input, PageHeader } from "@/components/ui";
import { api, errorMessage } from "@/lib/api";
import type { Workspace } from "@/lib/types";

function Home() {
  const { me, reload } = useMe();
  const router = useRouter();
  const [name, setName] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function create(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const ws = await api<Workspace>("workspaces", { method: "POST", body: { name } });
      await reload();
      router.push(`/w/${ws.id}`);
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <>
      <PageHeader title="Workspaces" subtitle="A workspace holds a team's connections, presets and pipelines." />
      <div className="grid gap-5 md:grid-cols-[2fr_1fr]">
        <div className="flex flex-col gap-2">
          {me.workspaces.length === 0 && <Empty title="No workspaces yet">Create one to get started, or ask an admin to add you.</Empty>}
          {me.workspaces.map((w) => (
            <Link key={w.id} href={`/w/${w.id}`} className="flex items-center justify-between rounded-lg border border-border bg-surface px-4 py-3 hover:bg-surface-2">
              <span className="font-medium">{w.name}</span>
              <span className="text-sm text-muted">{w.role}</span>
            </Link>
          ))}
        </div>
        <Card title="New workspace">
          <form onSubmit={create} className="flex flex-col gap-3">
            <Field label="Name">
              <Input value={name} onChange={(e) => setName(e.target.value)} required maxLength={100} placeholder="Platform team" />
            </Field>
            {error && <Alert tone="danger" title={error} />}
            <Button variant="primary" type="submit" busy={busy}>
              Create workspace
            </Button>
            <p className="text-xs text-muted">You become its admin.</p>
          </form>
        </Card>
      </div>
    </>
  );
}

export default function Page() {
  return (
    <AppShell>
      <Home />
    </AppShell>
  );
}
