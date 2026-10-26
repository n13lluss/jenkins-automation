import type { NextRequest } from "next/server";
import { exchangeCode } from "@/lib/server/oidc";
import { safeReturnTo, setSession, takeTransaction } from "@/lib/server/session";

function page(title: string, body: string, refreshTo?: string) {
  const escape = (s: string) => s.replace(/[&<>"']/g, (c) => `&#${c.charCodeAt(0)};`);
  const meta = refreshTo ? `<meta http-equiv="refresh" content="0;url=${escape(refreshTo)}">` : "";
  return new Response(
    `<!doctype html><html><head><meta charset="utf-8">${meta}<title>${escape(title)}</title></head><body><p>${escape(body)}</p><p><a href="/">Continue</a></p></body></html>`,
    { status: refreshTo ? 200 : 400, headers: { "content-type": "text/html; charset=utf-8", "cache-control": "no-store" } },
  );
}

/**
 * Completes the authorization code flow. The final hop is a same-site meta refresh rather
 * than a redirect, so the new SameSite=Strict session cookie is sent on the next request.
 */
export async function GET(request: NextRequest) {
  const params = request.nextUrl.searchParams;
  const tx = await takeTransaction();
  if (params.get("error")) return page("Sign-in failed", "The identity provider refused the sign-in.");
  const code = params.get("code");
  if (!tx || !code || params.get("state") !== tx.state) return page("Sign-in failed", "The sign-in request expired or was tampered with. Try again.");
  try {
    await setSession(await exchangeCode(code, tx.verifier, tx.nonce));
  } catch {
    return page("Sign-in failed", "Could not complete the sign-in.");
  }
  return page("Signed in", "Signed in. Redirecting…", safeReturnTo(tx.returnTo));
}
