"use client";

import { useParams, useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { canEdit, useWorkspace } from "@/components/AppShell";
import { PipelineEditor } from "@/components/editor/PipelineEditor";
import { Alert, Button, PageHeader, Spinner } from "@/components/ui";
import { api, errorMessage } from "@/lib/api";
import type { Project } from "@/lib/types";

export default function PipelinePage() {
  const { ws, id } = useParams<{ ws: string; id: string }>();
  const workspace = useWorkspace(ws);
  const router = useRouter();
  const [project, setProject] = useState<Project | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api<Project>(`workspaces/${ws}/pipelines/${id}`).then(setProject, (e) => setError(errorMessage(e)));
  }, [ws, id]);

  async function remove() {
    if (!project || !confirm(`Delete pipeline “${project.name}”? Generated files already in repositories are not touched.`)) return;
    try {
      await api(`workspaces/${ws}/pipelines/${id}`, { method: "DELETE" });
      router.push(`/w/${ws}`);
    } catch (e) {
      setError(errorMessage(e));
    }
  }

  if (error) return <Alert tone="danger" title={error} />;
  if (!project) return <Spinner />;
  return (
    <>
      <PageHeader
        title={project.name}
        subtitle={project.repository ? `${project.repository} @ ${project.branch}` : `${project.source} · not linked to a repository`}
        actions={
          canEdit(workspace) && (
            <Button variant="danger" onClick={remove}>
              Delete
            </Button>
          )
        }
      />
      <PipelineEditor key={project.updatedAt} ws={ws} initial={project.config} project={project} source={project.source} profile={project.profile} />
    </>
  );
}
