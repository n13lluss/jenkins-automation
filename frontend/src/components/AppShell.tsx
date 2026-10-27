"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from "react";
import { api, errorMessage } from "@/lib/api";
import type { Me, Workspace } from "@/lib/types";
import { Alert, cx, Spinner } from "./ui";

type MeContext = { me: Me; reload: () => Promise<void> };
const Ctx = createContext<MeContext | null>(null);

export function useMe(): MeContext {
  const v = useContext(Ctx);
  if (!v) throw new Error("useMe outside AppShell");
  return v;
}

export function useWorkspace(id: string): Workspace | undefined {
  return useMe().me.workspaces.find((w) => w.id === id);
}

export const canEdit = (w?: Workspace) => w?.role === "maintainer" || w?.role === "admin";
export const isAdmin = (w?: Workspace) => w?.role === "admin";

export function AppShell({ workspaceId, children }: { workspaceId?: string; children: ReactNode }) {
  const [me, setMe] = useState<Me | null>(null);
  const [error, setError] = useState<string | null>(null);
  const pathname = usePathname();
  const router = useRouter();

  const reload = useCallback(async () => {
    try {
      setMe(await api<Me>("me"));
    } catch (e) {
      setError(errorMessage(e));
    }
  }, []);

  useEffect(() => {
    api<Me>("me").then(setMe, (e) => setError(errorMessage(e)));
  }, []);

  const ws = me?.workspaces.find((w) => w.id === workspaceId);
  const nav = workspaceId
    ? [
        { href: `/w/${workspaceId}`, label: "Pipelines" },
        { href: `/w/${workspaceId}/connections`, label: "Connections" },
        { href: `/w/${workspaceId}/presets`, label: "Presets" },
        { href: `/w/${workspaceId}/metrics`, label: "Metrics" },
        ...(isAdmin(ws) ? [{ href: `/w/${workspaceId}/audit`, label: "Audit log" }, { href: `/w/${workspaceId}/settings`, label: "Settings" }] : []),
      ]
    : [];

  return (
    <div className="flex min-h-screen flex-col">
      <header className="border-b border-border bg-surface">
        <div className="mx-auto flex max-w-7xl flex-wrap items-center gap-4 px-4 py-2.5">
          <Link href="/" className="text-sm font-semibold tracking-tight">
            PipelineForge
          </Link>
          {me && me.workspaces.length > 0 && (
            <select
              aria-label="Workspace"
              className="rounded-md border border-border bg-surface px-2 py-1 text-sm"
              value={workspaceId ?? ""}
              onChange={(e) => router.push(e.target.value ? `/w/${e.target.value}` : "/")}
            >
              <option value="">All workspaces</option>
              {me.workspaces.map((w) => (
                <option key={w.id} value={w.id}>
                  {w.name}
                </option>
              ))}
            </select>
          )}
          <nav className="flex flex-wrap gap-1">
            {nav.map((n) => (
              <Link
                key={n.href}
                href={n.href}
                className={cx("rounded-md px-2.5 py-1 text-sm", pathname === n.href ? "bg-surface-2 font-medium" : "text-muted hover:text-text")}
              >
                {n.label}
              </Link>
            ))}
          </nav>
          <div className="ml-auto flex items-center gap-3 text-sm">
            {ws && <span className="text-muted">{ws.role}</span>}
            {me && <span>{me.displayName || me.email}</span>}
            <form method="post" action="/api/auth/logout">
              <button className="rounded-md px-2 py-1 text-muted hover:text-text" type="submit">
                Sign out
              </button>
            </form>
          </div>
        </div>
      </header>
      <main className="mx-auto w-full max-w-7xl flex-1 px-4 py-6">
        {error && <Alert tone="danger" title="Could not load your account">{error}</Alert>}
        {!me && !error && (
          <div className="flex items-center gap-2 text-sm text-muted">
            <Spinner /> Loading…
          </div>
        )}
        {me && workspaceId && !ws && <Alert tone="danger" title="Workspace not found">You are not a member of this workspace.</Alert>}
        {me && (!workspaceId || ws) && <Ctx.Provider value={{ me, reload }}>{children}</Ctx.Provider>}
      </main>
    </div>
  );
}
