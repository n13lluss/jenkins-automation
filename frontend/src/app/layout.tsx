import type { Metadata } from "next";
import { connection } from "next/server";
import "./globals.css";

export const metadata: Metadata = {
  title: "PipelineForge",
  description: "Generate secure Jenkins pipelines",
};

export default async function RootLayout({ children }: LayoutProps<"/">) {
  // Every page is rendered per request so the CSP nonce from proxy.ts can be applied.
  await connection();
  return (
    <html lang="en" className="h-full antialiased">
      <body className="min-h-full">{children}</body>
    </html>
  );
}
