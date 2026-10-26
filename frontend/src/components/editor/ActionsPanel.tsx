"use client";

import { useEffect, useState } from "react";
import { api, errorMessage } from "@/lib/api";
import type { AiReview, Connection, Project, Run } from "@/lib/types";
import { Alert, Badge, Button, Card, Field, Input, Select } from "../ui";

export function ActionsPanel({ ws, project, dirty, selected, canEdit, valid }: { ws: string; project: Project; dirty: boolean; selected: Set<string>; canEdit: boolean; valid: boolean }) {
  const [connections, setConnections] = useState<Connection[]>([]);
  const [runs, setRuns] = useState<Run[]>([]);
  const files = [...selected].join(",");

  useEffect(() => {
    api<Connection[]>(`workspaces/${ws}/connections`).then(setConnections, () => setConnections([]));
    api<Run[]>(`workspaces/${ws}/pipelines/${project.id}/runs`).then(setRuns, () => setRuns([]));
  }, [ws, project.id]);

  const jenkins = connections.filter((c) => c.kind === "jenkins");
  const ai = connections.filter((c) => c.kind === "ai");
  const disabled = dirty || !valid;

  return (
    <div className="flex flex-col gap-4">
      {dirty && <Alert tone="warn" title="Save your changes first: the actions below use the saved pipeline." />}
      <div className="grid gap-4 md:grid-cols-2">
        <Card title="Download">
          <p className="mb-3 text-sm text-muted">{selected.size ? `${selected.size} selected file(s)` : "All files"} as a zip.</p>
          <a
            aria-disabled={disabled}
            className={`inline-flex rounded-md bg-accent px-3 py-1.5 text-sm font-medium text-accent-text ${disabled ? "pointer-events-none opacity-50" : ""}`}
            href={`/api/proxy/workspaces/${ws}/pipelines/${project.id}/download${files ? `?files=${encodeURIComponent(files)}` : ""}`}
          >
            Download zip
          </a>
        </Card>
        {canEdit && <PullRequestCard ws={ws} project={project} files={[...selected]} disabled={disabled} />}
        {canEdit && project.repository && <BranchProtectionCard ws={ws} project={project} disabled={disabled} />}
        {canEdit && <JenkinsCard ws={ws} project={project} connections={jenkins} disabled={disabled} />}
        {canEdit && <AiCard ws={ws} project={project} connections={ai} disabled={disabled} />}
      </div>
      {runs.length > 0 && (
        <Card title="History">
          <ul className="flex flex-col gap-1 text-sm">
            {runs.map((r) => (
              <li key={r.id} className="flex flex-wrap gap-3">
                <span className="text-muted">{new Date(r.createdAt).toLocaleString()}</span>
                <span>{r.files.length} files</span>
                <code className="text-xs text-muted">{r.configSha256.slice(0, 12)}</code>
                {r.pullRequestUrl && (
                  <a className="text-accent underline" href={r.pullRequestUrl} target="_blank" rel="noreferrer noopener">
                    pull request
                  </a>
                )}
              </li>
            ))}
          </ul>
        </Card>
      )}
    </div>
  );
}

function PullRequestCard({ ws, project, files, disabled }: { ws: string; project: Project; files: string[]; disabled: boolean }) {
  const [target, setTarget] = useState(project.branch ?? "main");
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<{ url: string } | null>(null);
  const [error, setError] = useState<string | null>(null);
  if (!project.repository) {
    return (
      <Card title="Pull request">
        <p className="text-sm text-muted">This pipeline is not linked to a repository. Download the files instead, or start from a scan.</p>
      </Card>
    );
  }
  async function open() {
    setBusy(true);
    setError(null);
    try {
      setResult(await api<{ url: string }>(`workspaces/${ws}/pipelines/${project.id}/pull-request`, { method: "POST", body: { targetBranch: target, files: files.length ? files : null } }));
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setBusy(false);
    }
  }
  return (
    <Card title="Pull request">
      <div className="flex flex-col gap-3">
        <p className="text-sm text-muted">Creates a branch in {project.repository} with the files and opens a pull request.</p>
        <Field label="Target branch">
          <Input value={target} onChange={(e) => setTarget(e.target.value)} />
        </Field>
        <Button variant="primary" onClick={open} busy={busy} disabled={disabled}>
          Open pull request
        </Button>
        {result && (
          <Alert tone="ok" title="Pull request opened">
            <a className="underline" href={result.url} target="_blank" rel="noreferrer noopener">
              {result.url}
            </a>
          </Alert>
        )}
        {error && <Alert tone="danger" title={error} />}
      </div>
    </Card>
  );
}

type BranchRuleResult = { branch: string; rule: string; ok: boolean; message: string };

function BranchProtectionCard({ ws, project, disabled }: { ws: string; project: Project; disabled: boolean }) {
  const [branches, setBranches] = useState(() => [...new Set(project.config.environments.map((e) => e.branchPattern))].join(", "));
  const [approvals, setApprovals] = useState("1");
  const [busy, setBusy] = useState(false);
  const [results, setResults] = useState<BranchRuleResult[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  async function apply() {
    setBusy(true);
    setError(null);
    try {
      const list = branches.split(",").map((b) => b.trim()).filter(Boolean);
      setResults(
        await api<BranchRuleResult[]>(`workspaces/${ws}/pipelines/${project.id}/branch-protection`, {
          method: "POST",
          body: { branches: list, approvals: Number(approvals) || 0 },
        }),
      );
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setBusy(false);
    }
  }
  return (
    <Card title="Require the Jenkins build before merge">
      <div className="flex flex-col gap-3">
        <p className="text-sm text-muted">
          Protects the deploy branches in {project.repository}: changes only through pull requests, a green Jenkins build and approvals, no force pushes. The token needs admin rights on the repository.
        </p>
        <Field label="Branches" hint="Comma separated; defaults to the environment branches.">
          <Input value={branches} onChange={(e) => setBranches(e.target.value)} />
        </Field>
        <Field label="Required approvals">
          <Input type="number" min={0} max={10} value={approvals} onChange={(e) => setApprovals(e.target.value)} />
        </Field>
        <Button onClick={apply} busy={busy} disabled={disabled}>
          Apply branch rules
        </Button>
        {results && (
          <ul className="flex flex-col gap-1 text-sm">
            {results.map((r, i) => (
              <li key={i} className="flex flex-wrap items-center gap-2">
                <Badge tone={r.ok ? "ok" : "danger"}>{r.ok ? "ok" : "failed"}</Badge>
                <code className="text-xs">{r.branch}</code>
                <span>{r.rule}</span>
                <span className="text-muted">{r.message}</span>
              </li>
            ))}
          </ul>
        )}
        {error && <Alert tone="danger" title={error} />}
      </div>
    </Card>
  );
}

function JenkinsCard({ ws, project, connections, disabled }: { ws: string; project: Project; connections: Connection[]; disabled: boolean }) {
  const [conn, setConn] = useState("");
  const [jobName, setJobName] = useState(project.name);
  const [scmCred, setScmCred] = useState("scm-credentials");
  const [scheduled, setScheduled] = useState(project.config.security.dast.schedule.enabled);
  const [busy, setBusy] = useState<string | null>(null);
  const [message, setMessage] = useState<{ ok: boolean; text: string } | null>(null);
  const connectionId = conn || connections[0]?.id || "";

  if (connections.length === 0) {
    return (
      <Card title="Jenkins">
        <p className="text-sm text-muted">Add a Jenkins connection to validate the Jenkinsfile and create the multibranch job.</p>
      </Card>
    );
  }

  async function validate() {
    setBusy("validate");
    try {
      const r = await api<{ valid: boolean; output: string }>(`workspaces/${ws}/pipelines/${project.id}/validate`, { method: "POST", body: { jenkinsConnectionId: connectionId } });
      setMessage({ ok: r.valid, text: r.output });
    } catch (e) {
      setMessage({ ok: false, text: errorMessage(e) });
    } finally {
      setBusy(null);
    }
  }

  async function createJob() {
    setBusy("job");
    try {
      const r = await api<{ url: string; scheduledUrl?: string }>(`workspaces/${ws}/pipelines/${project.id}/jenkins-job`, { method: "POST", body: { jenkinsConnectionId: connectionId, jobName, scmCredentialId: scmCred, createScheduledJob: scheduled } });
      setMessage({ ok: true, text: `Job ready: ${r.url}${r.scheduledUrl ? `\nScheduled scans: ${r.scheduledUrl}` : ""}` });
    } catch (e) {
      setMessage({ ok: false, text: errorMessage(e) });
    } finally {
      setBusy(null);
    }
  }

  return (
    <Card title="Jenkins">
      <div className="flex flex-col gap-3">
        <Field label="Connection">
          <Select value={connectionId} onChange={(e) => setConn(e.target.value)} options={connections.map((c) => ({ value: c.id, label: c.name }))} />
        </Field>
        <Button onClick={validate} busy={busy === "validate"} disabled={disabled}>
          Validate Jenkinsfile
        </Button>
        {project.repository && (
          <>
            <div className="grid grid-cols-2 gap-3">
              <Field label="Job name">
                <Input value={jobName} onChange={(e) => setJobName(e.target.value)} />
              </Field>
              <Field label="SCM credential ID in Jenkins">
                <Input value={scmCred} onChange={(e) => setScmCred(e.target.value)} />
              </Field>
            </div>
            {project.config.security.dast.schedule.enabled && (
              <label className="flex items-center gap-2 text-sm">
                <input type="checkbox" checked={scheduled} onChange={(e) => setScheduled(e.target.checked)} /> Also create the scheduled-scans job (Jenkinsfile.scheduled)
              </label>
            )}
            <Button onClick={createJob} busy={busy === "job"} disabled={disabled}>
              Create or update multibranch job
            </Button>
          </>
        )}
        {message && (
          <Alert tone={message.ok ? "ok" : "danger"}>
            <pre className="whitespace-pre-wrap font-mono text-xs">{message.text}</pre>
          </Alert>
        )}
      </div>
    </Card>
  );
}

function AiCard({ ws, project, connections, disabled }: { ws: string; project: Project; connections: Connection[]; disabled: boolean }) {
  const [conn, setConn] = useState("");
  const [busy, setBusy] = useState(false);
  const [review, setReview] = useState<AiReview | null>(null);
  const [error, setError] = useState<string | null>(null);
  const connectionId = conn || connections[0]?.id || "";

  if (connections.length === 0) {
    return (
      <Card title="AI review of this pipeline">
        <p className="text-sm text-muted">Add an AI connection (Claude or another provider) to get a second opinion on the generated files.</p>
      </Card>
    );
  }

  async function run() {
    setBusy(true);
    setError(null);
    try {
      setReview(await api<AiReview>(`workspaces/${ws}/pipelines/${project.id}/ai-review`, { method: "POST", body: { aiConnectionId: connectionId } }));
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Card title="AI review of this pipeline">
      <div className="flex flex-col gap-3">
        <p className="text-xs text-muted">Sends the generated files and repository profile (not source code), with secrets redacted. The call is audited.</p>
        <Field label="Connection">
          <Select value={connectionId} onChange={(e) => setConn(e.target.value)} options={connections.map((c) => ({ value: c.id, label: `${c.name} (${c.model ?? c.provider})` }))} />
        </Field>
        <Button onClick={run} busy={busy} disabled={disabled}>
          Review with AI
        </Button>
        {error && <Alert tone="danger" title={error} />}
        {review?.problem && <Alert tone="warn" title={review.problem} />}
        {review && !review.problem && (
          <div className="flex flex-col gap-2 text-sm">
            <p>{review.summary}</p>
            {review.suggestions.map((s, i) => (
              <div key={i} className="rounded-md border border-border p-2">
                <p className="font-medium">
                  <Badge tone={s.severity === "high" ? "danger" : s.severity === "medium" ? "warn" : "neutral"}>{s.severity}</Badge> {s.title}
                </p>
                <p className="text-xs text-muted">{s.file}</p>
                <p className="mt-1">{s.detail}</p>
              </div>
            ))}
          </div>
        )}
      </div>
    </Card>
  );
}
