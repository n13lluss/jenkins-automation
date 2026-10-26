"use client";

import { useParams } from "next/navigation";
import { AppShell } from "@/components/AppShell";

export default function WorkspaceLayout({ children }: { children: React.ReactNode }) {
  const { ws } = useParams<{ ws: string }>();
  return <AppShell workspaceId={ws}>{children}</AppShell>;
}
