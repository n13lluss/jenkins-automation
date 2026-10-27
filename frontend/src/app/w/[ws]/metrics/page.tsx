"use client";

import { useParams } from "next/navigation";
import { useEffect, useMemo, useState } from "react";
import { Alert, Card, Field, PageHeader, Select, Spinner } from "@/components/ui";
import { api, errorMessage } from "@/lib/api";
import type { EnvironmentMetrics, MetricsResponse, ProjectSummary } from "@/lib/types";

const hours = (h: number | null | undefined) =>
  h == null ? "—" : h < 1 ? `${Math.round(h * 60)} min` : h < 48 ? `${h.toFixed(1)} h` : `${(h / 24).toFixed(1)} days`;
const percent = (v: number | null | undefined) => (v == null ? "—" : `${Math.round(v * 100)}%`);

export default function MetricsPage() {
  const { ws } = useParams<{ ws: string }>();
  const [projects, setProjects] = useState<ProjectSummary[] | null>(null);
  const [projectId, setProjectId] = useState("");
  const [days, setDays] = useState("30");
  const [data, setData] = useState<MetricsResponse | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api<ProjectSummary[]>(`workspaces/${ws}/pipelines`).then(
      (p) => {
        setProjects(p);
        setProjectId((current) => current || p[0]?.id || "");
      },
      (e) => setError(errorMessage(e)),
    );
  }, [ws]);

  useEffect(() => {
    if (!projectId) return;
    let stale = false;
    api<MetricsResponse>(`workspaces/${ws}/pipelines/${projectId}/metrics?days=${days}`).then(
      (m) => !stale && (setData(m), setError(null)),
      (e) => !stale && setError(errorMessage(e)),
    );
    return () => {
      stale = true;
    };
  }, [ws, projectId, days]);

  return (
    <>
      <PageHeader
        title="Delivery metrics"
        subtitle="Deploy frequency, lead time for changes, change failure rate and time to restore, from the deploys each pipeline reports."
      />
      {error && <Alert tone="danger" title={error} />}
      {projects === null && !error && <Spinner />}
      {projects?.length === 0 && <Alert tone="warn" title="No pipelines yet." />}
      {projects && projects.length > 0 && (
        <div className="mb-4 flex flex-wrap items-end gap-3">
          <Field label="Pipeline">
            <Select value={projectId} onChange={(e) => setProjectId(e.target.value)} options={projects.map((p) => ({ value: p.id, label: p.name }))} />
          </Field>
          <Field label="Period">
            <Select
              value={days}
              onChange={(e) => setDays(e.target.value)}
              options={[
                { value: "30", label: "Last 30 days" },
                { value: "90", label: "Last 90 days" },
                { value: "180", label: "Last 180 days" },
              ]}
            />
          </Field>
        </div>
      )}
      {data && data.environments.length === 0 && (
        <Alert tone="warn" title="No deploys reported in this period">
          Turn on delivery metrics in the pipeline (Operations), create an ingest token under Actions and store it in Jenkins.
        </Alert>
      )}
      <div className="flex flex-col gap-4">
        {data?.environments.map((e) => (
          <EnvironmentCard key={e.environment} m={e} />
        ))}
      </div>
    </>
  );
}

function Stat({ label, value, note }: { label: string; value: string; note?: string }) {
  return (
    <div className="rounded-md border border-border p-3">
      <div className="text-xs text-muted">{label}</div>
      <div className="mt-1 text-2xl font-semibold tabular-nums">{value}</div>
      {note && <div className="mt-0.5 text-xs text-muted">{note}</div>}
    </div>
  );
}

function EnvironmentCard({ m }: { m: EnvironmentMetrics }) {
  const [table, setTable] = useState(false);
  return (
    <Card
      title={`Environment: ${m.environment}`}
      actions={
        <button type="button" className="text-sm text-muted underline hover:text-text" onClick={() => setTable((t) => !t)}>
          {table ? "Show chart" : "Show table"}
        </button>
      }
    >
      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <Stat label="Deploy frequency" value={`${m.deploysPerWeek} / week`} note={`${m.deploys} deploys`} />
        <Stat label="Lead time for changes" value={hours(m.medianLeadTimeHours)} note="median, commit to deployed" />
        <Stat label="Change failure rate" value={percent(m.changeFailureRate)} note={`${m.failures} failed`} />
        <Stat label="Time to restore" value={hours(m.medianTimeToRestoreHours)} note="median, failure to next success" />
      </div>
      <div className="mt-4">{table ? <WeeklyTable m={m} /> : <WeeklyChart m={m} />}</div>
    </Card>
  );
}

type Week = { start: string; deploys: number; failures: number };

function weeks(m: EnvironmentMetrics): Week[] {
  const out: Week[] = [];
  for (let i = 0; i < m.daily.length; i += 7) {
    const slice = m.daily.slice(i, i + 7);
    out.push({ start: slice[0].day, deploys: slice.reduce((s, d) => s + d.successes + d.failures, 0), failures: slice.reduce((s, d) => s + d.failures, 0) });
  }
  return out;
}

const shortDate = (iso: string) => new Date(`${iso}T00:00:00Z`).toLocaleDateString(undefined, { month: "short", day: "numeric", timeZone: "UTC" });

/** Deploys per week, one series: bars from a shared baseline, hover for the exact counts. */
function WeeklyChart({ m }: { m: EnvironmentMetrics }) {
  const data = useMemo(() => weeks(m), [m]);
  const [hover, setHover] = useState<number | null>(null);
  const width = 640;
  const height = 160;
  const pad = { top: 12, right: 8, bottom: 22, left: 28 };
  const max = Math.max(1, ...data.map((w) => w.deploys));
  const step = (width - pad.left - pad.right) / data.length;
  const bar = Math.min(24, step - 2);
  const y = (v: number) => pad.top + (height - pad.top - pad.bottom) * (1 - v / max);
  const ticks = max <= 4 ? Array.from({ length: max + 1 }, (_, i) => i) : [0, Math.round(max / 2), max];
  const h = hover == null ? null : data[hover];

  return (
    <div className="relative">
      <div className="mb-1 text-xs text-muted">Deploys per week</div>
      <svg viewBox={`0 0 ${width} ${height}`} className="w-full" role="img" aria-label={`Deploys per week for ${m.environment}`} onMouseLeave={() => setHover(null)}>
        {ticks.map((t) => (
          <g key={t}>
            <line x1={pad.left} x2={width - pad.right} y1={y(t)} y2={y(t)} stroke="var(--border)" strokeWidth={1} />
            <text x={pad.left - 6} y={y(t) + 3} textAnchor="end" fontSize={10} fill="var(--muted)">
              {t}
            </text>
          </g>
        ))}
        {data.map((w, i) => {
          const x = pad.left + i * step + (step - bar) / 2;
          const top = y(w.deploys);
          const base = y(0);
          const r = Math.min(4, (base - top) / 2);
          return (
            <g key={w.start} onMouseEnter={() => setHover(i)}>
              {/* Hit target: the whole band, taller than the bar. */}
              <rect x={pad.left + i * step} y={pad.top} width={step} height={base - pad.top} fill="transparent" />
              {w.deploys > 0 && (
                <path
                  d={`M${x},${base} V${top + r} Q${x},${top} ${x + r},${top} H${x + bar - r} Q${x + bar},${top} ${x + bar},${top + r} V${base} Z`}
                  fill="var(--chart-1)"
                  opacity={hover == null || hover === i ? 1 : 0.55}
                />
              )}
              {(i === 0 || i === data.length - 1 || data.length <= 8) && (
                <text x={pad.left + i * step + step / 2} y={height - 6} textAnchor="middle" fontSize={10} fill="var(--muted)">
                  {shortDate(w.start)}
                </text>
              )}
            </g>
          );
        })}
      </svg>
      {h && (
        <div
          className="pointer-events-none absolute top-4 rounded-md border border-border bg-surface px-2 py-1 text-xs shadow-sm"
          style={{ left: `${Math.min(80, ((pad.left + (hover! + 0.5) * step) / width) * 100)}%` }}
        >
          <div className="font-medium">Week of {shortDate(h.start)}</div>
          <div className="tabular-nums">
            {h.deploys} deploys · {h.failures} failed
          </div>
        </div>
      )}
    </div>
  );
}

function WeeklyTable({ m }: { m: EnvironmentMetrics }) {
  return (
    <table className="w-full text-sm">
      <thead className="text-left text-muted">
        <tr className="border-b border-border">
          <th className="py-1 pr-3 font-medium">Week of</th>
          <th className="py-1 pr-3 font-medium">Deploys</th>
          <th className="py-1 font-medium">Failed</th>
        </tr>
      </thead>
      <tbody className="tabular-nums">
        {weeks(m).map((w) => (
          <tr key={w.start} className="border-b border-border last:border-0">
            <td className="py-1 pr-3">{shortDate(w.start)}</td>
            <td className="py-1 pr-3">{w.deploys}</td>
            <td className="py-1">{w.failures}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
