import { NextResponse, type NextRequest } from "next/server";
import { authorizeUrl } from "@/lib/server/oidc";
import { randomToken, safeReturnTo, setTransaction } from "@/lib/server/session";

export async function GET(request: NextRequest) {
  const state = randomToken();
  const nonce = randomToken();
  const verifier = randomToken(48);
  await setTransaction({ state, nonce, verifier, returnTo: safeReturnTo(request.nextUrl.searchParams.get("returnTo")) });
  return NextResponse.redirect(await authorizeUrl(state, nonce, verifier));
}
