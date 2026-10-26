import { NextResponse, type NextRequest } from "next/server";
import { config } from "@/lib/server/config";
import { isSameOriginRequest } from "@/lib/server/csrf";
import { refresh } from "@/lib/server/oidc";
import { clearSession, getSession, setSession } from "@/lib/server/session";

const MAX_BODY = 1024 * 1024;
const SEGMENT = /^[A-Za-z0-9._~-]+$/;
const PASS_RESPONSE_HEADERS = ["content-type", "content-disposition", "retry-after"];

/**
 * Backend-for-frontend proxy: the browser never sees access tokens. Requests are
 * forwarded to the API with the session's bearer token, refreshing it when needed.
 */
async function handle(request: NextRequest, ctx: { params: Promise<{ path: string[] }> }) {
  const { path } = await ctx.params;
  if (path.length === 0 || path.some((s) => !SEGMENT.test(s) || s === "." || s === "..")) {
    return NextResponse.json({ title: "Invalid path" }, { status: 400 });
  }
  const unsafe = !["GET", "HEAD"].includes(request.method);
  if (unsafe && !isSameOriginRequest(request)) return NextResponse.json({ title: "Cross-site request blocked" }, { status: 403 });

  let session = await getSession();
  if (!session) return NextResponse.json({ title: "Not signed in" }, { status: 401 });
  if (session.expiresAt - 30 < Math.floor(Date.now() / 1000)) {
    const renewed = await refresh(session);
    if (!renewed) {
      await clearSession();
      return NextResponse.json({ title: "Session expired" }, { status: 401 });
    }
    session = renewed;
    await setSession(session);
  }

  let body: ArrayBuffer | undefined;
  if (unsafe) {
    if (Number(request.headers.get("content-length") ?? "0") > MAX_BODY) return NextResponse.json({ title: "Request too large" }, { status: 413 });
    body = await request.arrayBuffer();
    if (body.byteLength > MAX_BODY) return NextResponse.json({ title: "Request too large" }, { status: 413 });
  }

  const target = new URL(`${config.backendUrl}/api/${path.map(encodeURIComponent).join("/")}`);
  target.search = request.nextUrl.search;
  const headers = new Headers({ authorization: `Bearer ${session.accessToken}`, accept: request.headers.get("accept") ?? "application/json" });
  const contentType = request.headers.get("content-type");
  if (contentType) headers.set("content-type", contentType);
  const forwardedFor = request.headers.get("x-forwarded-for");
  if (forwardedFor) headers.set("x-forwarded-for", forwardedFor);

  const upstream = await fetch(target, { method: request.method, headers, body, cache: "no-store", redirect: "manual" });
  const out = new Headers({ "cache-control": "no-store", "x-content-type-options": "nosniff" });
  for (const h of PASS_RESPONSE_HEADERS) {
    const v = upstream.headers.get(h);
    if (v) out.set(h, v);
  }
  return new NextResponse(upstream.body, { status: upstream.status, headers: out });
}

export const GET = handle;
export const POST = handle;
export const PUT = handle;
export const PATCH = handle;
export const DELETE = handle;
