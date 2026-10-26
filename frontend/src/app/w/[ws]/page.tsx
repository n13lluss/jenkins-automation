"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useEffect, useState } from "react";
import { canEdit, useWorkspace } from "@/components/AppShell";
import { Alert, Badge, Empty, PageHeader } from "@/components/ui";
import { api, errorMessage } from "@/lib/api";
import type { ProjectSummary } from "@/lib/types";

const starts = [
  { mode: "scan", title: "Scan a repository", text: "Bitbucket or GitHub: detect languages, build tools and deploy hints." },
  { mode: "blank", title: "Start from scratch", text: "No repository needed: pick languages and modules yourself." },
  { mode: "import", title: "Import a Jenkinsfile", text: "Keep your stages, add the missing security controls." },
];

export default function PipelinesPage() {
  const { ws } = useParams<{ ws: string }>();
  const workspace = useWorkspace(ws);
  const [projects, setProjects] = useState<ProjectSummary[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api<ProjectSummary[]>(`workspaces/${ws}/pipelines`).then(setProjects, (e) => setError(errorMessage(e)));
  }, [ws]);

  return (
    <>
      <PageHeader title="Pipelines" subtitle={workspace?.name} />
      {canEdit(workspace) && (
        <div className="mb-6 grid gap-3 md:grid-cols-3">
          {starts.map((s) => (
            <Link key={s.mode} href={`/w/${ws}/pipelines/new?mode=${s.mode}`} className="rounded-lg border border-border bg-surface p-4 hover:border-accent">
              <p className="font-medium">{s.title}</p>
              <p className="mt-1 text-sm text-muted">{s.text}</p>
            </Link>
          ))}
        </div>
      )}
      {error && <Alert tone="danger" title={error} />}
      {projects?.length === 0 && <Empty title="No pipelines yet">Start with one of the options above.</Empty>}
      {projects && projects.length > 0 && (
        <div className="overflow-x-auto rounded-lg border border-border bg-surface">
          <table className="w-full text-sm">
            <thead className="text-left text-muted">
              <tr className="border-b border-border">
                <th className="px-4 py-2 font-medium">Name</th>
                <th className="px-4 py-2 font-medium">Source</th>
                <th className="px-4 py-2 font-medium">Repository</th>
                <th className="px-4 py-2 font-medium">Updated</th>
              </tr>
            </thead>
            <tbody>
              {projects.map((p) => (
                <tr key={p.id} className="border-b border-border last:border-0 hover:bg-surface-2">
                  <td className="px-4 py-2">
                    <Link className="font-medium text-accent" href={`/w/${ws}/pipelines/${p.id}`}>
                      {p.name}
                    </Link>
                  </td>
                  <td className="px-4 py-2">
                    <Badge>{p.source}</Badge>
                  </td>
                  <td className="px-4 py-2 text-muted">{p.repository ? `${p.repository}${p.branch ? ` @ ${p.branch}` : ""}` : "—"}</td>
                  <td className="px-4 py-2 text-muted">{new Date(p.updatedAt).toLocaleString()}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </>
  );
}
