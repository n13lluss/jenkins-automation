"use client";

import { useMemo, useState } from "react";
import type { GeneratedFile } from "@/lib/types";
import { Button, cx } from "../ui";

/** File tree with selection checkboxes and a read-only code view with line numbers. */
export function FileViewer({ files, selected, onSelectedChange }: { files: GeneratedFile[]; selected: Set<string>; onSelectedChange: (s: Set<string>) => void }) {
  const [active, setActive] = useState<string | null>(null);
  const current = files.find((f) => f.path === active) ?? files[0];
  const groups = useMemo(() => {
    const map = new Map<string, GeneratedFile[]>();
    for (const f of files) {
      const dir = f.path.includes("/") ? f.path.slice(0, f.path.lastIndexOf("/")) : "";
      map.set(dir, [...(map.get(dir) ?? []), f]);
    }
    return [...map.entries()].sort(([a], [b]) => a.localeCompare(b));
  }, [files]);
  const [copied, setCopied] = useState(false);

  function toggle(path: string) {
    const next = new Set(selected);
    if (next.has(path)) next.delete(path);
    else next.add(path);
    onSelectedChange(next);
  }

  if (files.length === 0) return null;
  const lines = current.content.replace(/\n$/, "").split("\n");

  return (
    <div className="grid min-h-[28rem] overflow-hidden rounded-lg border border-border bg-surface md:grid-cols-[16rem_1fr]">
      <div className="border-b border-border p-2 text-sm md:border-r md:border-b-0">
        <div className="mb-2 flex items-center justify-between px-1 text-xs text-muted">
          <span>
            {selected.size === 0 ? "All files" : `${selected.size} selected`}
          </span>
          {selected.size > 0 && (
            <button className="hover:text-text" onClick={() => onSelectedChange(new Set())}>
              Clear
            </button>
          )}
        </div>
        {groups.map(([dir, list]) => (
          <div key={dir} className="mb-2">
            {dir && <p className="px-1 py-0.5 font-mono text-xs text-muted">{dir}/</p>}
            {list.map((f) => (
              <div key={f.path} className={cx("flex items-center gap-2 rounded px-1 py-0.5", current.path === f.path && "bg-surface-2")}>
                <input type="checkbox" aria-label={`Select ${f.path}`} checked={selected.has(f.path)} onChange={() => toggle(f.path)} className="accent-[var(--accent)]" />
                <button className={cx("truncate text-left font-mono text-xs", dir && "pl-2")} onClick={() => setActive(f.path)} title={f.path}>
                  {f.path.slice(dir ? dir.length + 1 : 0)}
                </button>
              </div>
            ))}
          </div>
        ))}
      </div>
      <div className="flex min-w-0 flex-col">
        <div className="flex items-center justify-between border-b border-border px-3 py-1.5">
          <span className="truncate font-mono text-xs">{current.path}</span>
          <Button
            variant="ghost"
            onClick={async () => {
              await navigator.clipboard.writeText(current.content);
              setCopied(true);
              setTimeout(() => setCopied(false), 1200);
            }}
          >
            {copied ? "Copied" : "Copy"}
          </Button>
        </div>
        <pre className="max-h-[36rem] flex-1 overflow-auto bg-code p-0 font-mono text-xs leading-5">
          <code className="grid grid-cols-[3rem_1fr]">
            {lines.map((l, i) => (
              <span key={i} className="contents">
                <span className="select-none pr-3 text-right text-muted">{i + 1}</span>
                <span className="whitespace-pre pr-4">{l || " "}</span>
              </span>
            ))}
          </code>
        </pre>
      </div>
    </div>
  );
}
