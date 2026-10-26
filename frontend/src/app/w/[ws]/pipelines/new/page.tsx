"use client";

import Link from "next/link";
import { useParams, useSearchParams } from "next/navigation";
import { Suspense, useEffect, useState } from "react";
import { PipelineEditor } from "@/components/editor/PipelineEditor";
import { Alert, Badge, Button, Card, Field, Input, PageHeader, Select, Spinner, TextArea } from "@/components/ui";
import { api, errorMessage } from "@/lib/api";
import { blankConfig, slug } from "@/lib/defaults";
import type { Connection, ImportResult, PipelineConfig, RepoProfile, ScanResponse, ScmBranch, ScmRepository } from "@/lib/types";

type Started = {
  config: PipelineConfig;
  source: "scan" | "blank" | "import";
  link?: { scmConnectionId: string; repository: string; branch: string };
  profile?: RepoProfile;
  intro?: React.ReactNode;
};

function NewPipeline() {
  const { ws } = useParams<{ ws: string }>();
  const mode = (useSearchParams().get("mode") ?? "blank") as Started["source"];
  const [started, setStarted] = useState<Started | null>(null);

  if (started) {
    return (
      <>
        <PageHeader title={`New pipeline · ${started.config.name}`} subtitle={started.link ? `${started.link.repository} @ ${started.link.branch}` : "Not linked to a repository"} />
        <PipelineEditor ws={ws} initial={started.config} source={started.source} link={started.link} profile={started.profile} intro={started.intro} />
      </>
    );
  }

  const titles = { scan: "Scan a repository", blank: "Start from scratch", import: "Import a Jenkinsfile" };
  return (
    <>
      <PageHeader
        title={titles[mode]}
        actions={
          <div className="flex gap-2 text-sm">
            {(Object.keys(titles) as Started["source"][]).map((m) => (
              <Link key={m} href={`/w/${ws}/pipelines/new?mode=${m}`} className={m === mode ? "font-medium" : "text-muted hover:text-text"}>
                {titles[m]}
              </Link>
            ))}
          </div>
        }
      />
      {mode === "scan" && <ScanStart ws={ws} onStart={setStarted} />}
      {mode === "blank" && <BlankStart onStart={setStarted} />}
      {mode === "import" && <ImportStart ws={ws} onStart={setStarted} />}
    </>
  );
}

function useScmConnections(ws: string) {
  const [list, setList] = useState<Connection[] | null>(null);
  useEffect(() => {
    api<Connection[]>(`workspaces/${ws}/connections`).then((c) => setList(c.filter((x) => x.kind === "scm")), () => setList([]));
  }, [ws]);
  return list;
}

function RepoPicker({ ws, onPicked }: { ws: string; onPicked: (v: { connectionId: string; repository: string; branch: string } | null) => void }) {
  const connections = useScmConnections(ws);
  const [conn, setConn] = useState("");
  const [query, setQuery] = useState("");
  const [repos, setRepos] = useState<ScmRepository[] | null>(null);
  const [repo, setRepo] = useState("");
  const [branches, setBranches] = useState<ScmBranch[] | null>(null);
  const [branch, setBranch] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const connectionId = conn || connections?.[0]?.id || "";

  useEffect(() => {
    onPicked(connectionId && repo && branch ? { connectionId, repository: repo, branch } : null);
  }, [connectionId, repo, branch, onPicked]);

  if (connections === null) return <Spinner />;
  if (connections.length === 0) return <Alert tone="warn" title="No source control connection">An admin can add Bitbucket or GitHub under Connections.</Alert>;

  async function search(e?: React.FormEvent) {
    e?.preventDefault();
    setBusy(true);
    setError(null);
    try {
      setRepos(await api<ScmRepository[]>(`workspaces/${ws}/connections/${connectionId}/repos${query ? `?q=${encodeURIComponent(query)}` : ""}`));
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setBusy(false);
    }
  }

  async function pickRepo(r: ScmRepository) {
    setRepo(r.fullName);
    setBranches(null);
    setBranch("");
    try {
      const list = await api<ScmBranch[]>(`workspaces/${ws}/connections/${connectionId}/branches?repo=${encodeURIComponent(r.fullName)}`);
      setBranches(list);
      setBranch(list.find((b) => b.name === r.defaultBranch)?.name ?? list[0]?.name ?? "");
    } catch (err) {
      setError(errorMessage(err));
    }
  }

  return (
    <div className="flex flex-col gap-3">
      <form onSubmit={search} className="flex flex-wrap items-end gap-3">
        <Field label="Connection">
          <Select value={connectionId} onChange={(e) => setConn(e.target.value)} options={connections.map((c) => ({ value: c.id, label: `${c.name} (${c.provider})` }))} />
        </Field>
        <Field label="Search repositories">
          <Input value={query} onChange={(e) => setQuery(e.target.value)} placeholder="name contains…" />
        </Field>
        <Button type="submit" busy={busy}>
          Search
        </Button>
      </form>
      {error && <Alert tone="danger" title={error} />}
      {repos && (
        <div className="max-h-72 overflow-auto rounded-md border border-border">
          {repos.length === 0 && <p className="p-3 text-sm text-muted">No repositories found.</p>}
          {repos.map((r) => (
            <button key={r.fullName} onClick={() => pickRepo(r)} className={`flex w-full items-center justify-between border-b border-border px-3 py-2 text-left text-sm last:border-0 hover:bg-surface-2 ${repo === r.fullName ? "bg-surface-2 font-medium" : ""}`}>
              <span>{r.fullName}</span>
              {r.isPrivate && <Badge>private</Badge>}
            </button>
          ))}
        </div>
      )}
      {repo && (
        <Field label={`Branch of ${repo}`}>
          {branches ? <Select value={branch} onChange={(e) => setBranch(e.target.value)} options={branches.map((b) => ({ value: b.name, label: b.name }))} /> : <Spinner />}
        </Field>
      )}
    </div>
  );
}

function ScanStart({ ws, onStart }: { ws: string; onStart: (s: Started) => void }) {
  const [picked, setPicked] = useState<{ connectionId: string; repository: string; branch: string } | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function scan() {
    if (!picked) return;
    setBusy(true);
    setError(null);
    try {
      const r = await api<ScanResponse>(`workspaces/${ws}/scans`, { method: "POST", body: { connectionId: picked.connectionId, repository: picked.repository, branch: picked.branch } });
      onStart({
        config: r.config,
        source: "scan",
        link: { scmConnectionId: picked.connectionId, repository: picked.repository, branch: picked.branch },
        profile: r.profile,
        intro: (
          <Card title={`Scan result · ${r.profile.fileCount} files`}>
            <ul className="list-disc pl-5 text-sm">
              {r.reasons.map((x) => (
                <li key={x}>{x}</li>
              ))}
            </ul>
          </Card>
        ),
      });
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Card title="Repository">
      <div className="flex flex-col gap-4">
        <p className="text-sm text-muted">The scan reads the file list and build manifests through the API. Nothing is cloned or executed.</p>
        <RepoPicker ws={ws} onPicked={setPicked} />
        {error && <Alert tone="danger" title={error} />}
        <div>
          <Button variant="primary" onClick={scan} busy={busy} disabled={!picked}>
            Scan and suggest a pipeline
          </Button>
        </div>
      </div>
    </Card>
  );
}

function BlankStart({ onStart }: { onStart: (s: Started) => void }) {
  const [name, setName] = useState("");
  return (
    <Card title="Project name">
      <form
        className="flex flex-wrap items-end gap-3"
        onSubmit={(e) => {
          e.preventDefault();
          onStart({ config: blankConfig(slug(name)), source: "blank" });
        }}
      >
        <Field label="Name" hint="You add modules (languages, paths, Dockerfiles) in the next step.">
          <Input value={name} onChange={(e) => setName(e.target.value)} required placeholder="orders-service" />
        </Field>
        <Button variant="primary" type="submit">
          Continue
        </Button>
      </form>
    </Card>
  );
}

function ImportStart({ ws, onStart }: { ws: string; onStart: (s: Started) => void }) {
  const [content, setContent] = useState("");
  const [name, setName] = useState("");
  const [picked, setPicked] = useState<{ connectionId: string; repository: string; branch: string } | null>(null);
  const [path, setPath] = useState("Jenkinsfile");
  const [fromRepo, setFromRepo] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function importIt() {
    setBusy(true);
    setError(null);
    try {
      const body = fromRepo && picked ? { connectionId: picked.connectionId, repository: picked.repository, branch: picked.branch, path } : { content, name: name || "app" };
      const r = await api<ImportResult>(`workspaces/${ws}/imports/jenkinsfile`, { method: "POST", body });
      onStart({
        config: r.config,
        source: "import",
        link: fromRepo && picked ? { scmConnectionId: picked.connectionId, repository: picked.repository, branch: picked.branch } : undefined,
        intro: (
          <div className="grid gap-4 md:grid-cols-2">
            <Card title="Stages found">
              <ul className="flex flex-col gap-1 text-sm">
                {r.stages.map((s, i) => (
                  <li key={i}>
                    <span className="font-medium">{s.name}</span> → <span className={s.custom ? "text-warn" : "text-muted"}>{s.mappedTo}</span>
                  </li>
                ))}
              </ul>
              {r.credentialIds.length > 0 && <p className="mt-2 text-xs text-muted">Credentials referenced: {r.credentialIds.join(", ")}</p>}
            </Card>
            <Card title="Gaps">
              {r.gaps.length === 0 ? (
                <p className="text-sm text-muted">No gaps found.</p>
              ) : (
                <ul className="list-disc pl-5 text-sm">
                  {r.gaps.map((g) => (
                    <li key={g}>{g}</li>
                  ))}
                </ul>
              )}
              {r.warnings.map((w) => (
                <p key={w} className="mt-2 text-xs text-warn">
                  {w}
                </p>
              ))}
            </Card>
          </div>
        ),
      });
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Card title="Jenkinsfile">
      <div className="flex flex-col gap-4">
        <div className="flex gap-4 text-sm">
          <label className="flex items-center gap-2">
            <input type="radio" checked={!fromRepo} onChange={() => setFromRepo(false)} /> Paste
          </label>
          <label className="flex items-center gap-2">
            <input type="radio" checked={fromRepo} onChange={() => setFromRepo(true)} /> From a repository
          </label>
        </div>
        {fromRepo ? (
          <>
            <RepoPicker ws={ws} onPicked={setPicked} />
            <Field label="Path">
              <Input value={path} onChange={(e) => setPath(e.target.value)} />
            </Field>
          </>
        ) : (
          <>
            <Field label="Project name">
              <Input value={name} onChange={(e) => setName(e.target.value)} placeholder="orders-service" />
            </Field>
            <Field label="Contents" hint="Declarative pipelines map best. Nothing is executed.">
              <TextArea rows={14} value={content} onChange={(e) => setContent(e.target.value)} spellCheck={false} placeholder="pipeline { … }" />
            </Field>
          </>
        )}
        {error && <Alert tone="danger" title={error} />}
        <div>
          <Button variant="primary" onClick={importIt} busy={busy} disabled={fromRepo ? !picked : content.trim().length === 0}>
            Import
          </Button>
        </div>
      </div>
    </Card>
  );
}

export default function Page() {
  return (
    <Suspense fallback={<Spinner />}>
      <NewPipeline />
    </Suspense>
  );
}
