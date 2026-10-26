import "server-only";
import type { NextRequest } from "next/server";
import { config } from "./config";

/**
 * State-changing requests must come from our own pages: same Origin, a same-origin fetch
 * (Sec-Fetch-Site) and a custom header that cross-site forms cannot set.
 */
export function isSameOriginRequest(request: NextRequest, requireHeader = true): boolean {
  const origin = request.headers.get("origin");
  if (origin !== config.appUrl) return false;
  const site = request.headers.get("sec-fetch-site");
  if (site && site !== "same-origin") return false;
  return !requireHeader || request.headers.get("x-pf-csrf") === "1";
}
