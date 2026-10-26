import { NextResponse, type NextRequest } from "next/server";
import { isSameOriginRequest } from "@/lib/server/csrf";
import { logoutUrl } from "@/lib/server/oidc";
import { clearSession } from "@/lib/server/session";

/** POST only, from our own page, so a cross-site link cannot sign people out. */
export async function POST(request: NextRequest) {
  if (!isSameOriginRequest(request, false)) return new NextResponse(null, { status: 403 });
  await clearSession();
  return NextResponse.redirect(logoutUrl(), 303);
}
