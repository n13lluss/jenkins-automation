"use client";

import { useParams } from "next/navigation";
import { useCallback, useEffect, useState } from "react";
import { Alert, Badge, Button, PageHeader } from "@/components/ui";
import { api, errorMessage } from "@/lib/api";
import type { AuditEntry } from "@/lib/types";

export default function AuditPage() {
  const { ws } = useParams<{ ws: string }>();
  const [rows, setRows] = useState<AuditEntry[]>([]);
  const [done, setDone] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const fetchPage = useCallback((before?: number) => api<AuditEntry[]>(`workspaces/${ws}/audit?take=100${before ? `&before=${before}` : ""}`), [ws]);

  useEffect(() => {
    fetchPage().then(
      (page) => {
        setRows(page);
        setDone(page.length < 100);
      },
      (e) => setError(errorMessage(e)),
    );
  }, [fetchPage]);

  async function more(before: number) {
    try {
      const page = await fetchPage(before);
      setRows((r) => [...r, ...page]);
      setDone(page.length < 100);
    } catch (e) {
      setError(errorMessage(e));
    }
  }

  return (
    <>
      <PageHeader title="Audit log" subtitle="Append-only record of changes, scans, generations, pull requests, Jenkins calls and AI calls." />
      {error && <Alert tone="danger" title={error} />}
      <div className="overflow-x-auto rounded-lg border border-border bg-surface">
        <table className="w-full text-sm">
          <thead className="text-left text-muted">
            <tr className="border-b border-border">
              <th className="px-3 py-2 font-medium">When</th>
              <th className="px-3 py-2 font-medium">Who</th>
              <th className="px-3 py-2 font-medium">Action</th>
              <th className="px-3 py-2 font-medium">Target</th>
              <th className="px-3 py-2 font-medium">Detail</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.id} className="border-b border-border align-top last:border-0">
                <td className="whitespace-nowrap px-3 py-2 text-muted">{new Date(r.at).toLocaleString()}</td>
                <td className="px-3 py-2">{r.userEmail ?? "—"}</td>
                <td className="px-3 py-2">
                  <code className="text-xs">{r.action}</code> {!r.success && <Badge tone="danger">failed</Badge>}
                </td>
                <td className="px-3 py-2">{r.target}</td>
                <td className="max-w-md break-all px-3 py-2 text-xs text-muted">{r.detail}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {!done && rows.length > 0 && (
        <div className="mt-3">
          <Button onClick={() => more(rows[rows.length - 1].id)}>Load older</Button>
        </div>
      )}
    </>
  );
}
