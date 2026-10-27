import { NextResponse, type NextRequest } from "next/server";
import { config } from "@/lib/server/config";

const MAX_BODY = 16 * 1024;
const TOKEN = /^Bearer pf_ing_[A-Za-z0-9_-]{43}$/;

/**
 * Deploy reports from Jenkins. Jenkins cannot reach the API directly, so this one route is passed through
 * without a session: only the ingest token header and a small JSON body are forwarded, and no cookies are read or set.
 */
export async function POST(request: NextRequest) {
  const authorization = request.headers.get("authorization") ?? "";
  if (!TOKEN.test(authorization)) return NextResponse.json({ title: "Invalid ingest token" }, { status: 401 });
  if (!(request.headers.get("content-type") ?? "").startsWith("application/json")) {
    return NextResponse.json({ title: "Expected application/json" }, { status: 415 });
  }
  if (Number(request.headers.get("content-length") ?? "0") > MAX_BODY) return NextResponse.json({ title: "Request too large" }, { status: 413 });
  const body = await request.arrayBuffer();
  if (body.byteLength > MAX_BODY) return NextResponse.json({ title: "Request too large" }, { status: 413 });

  const headers = new Headers({ authorization, "content-type": "application/json", accept: "application/json" });
  const forwardedFor = request.headers.get("x-forwarded-for");
  if (forwardedFor) headers.set("x-forwarded-for", forwardedFor);
  const upstream = await fetch(`${config.backendUrl}/api/ingest/deployments`, { method: "POST", headers, body, cache: "no-store", redirect: "manual" });
  return new NextResponse(upstream.status === 202 ? null : await upstream.text(), {
    status: upstream.status,
    headers: { "cache-control": "no-store", "x-content-type-options": "nosniff", "content-type": "application/json" },
  });
}
