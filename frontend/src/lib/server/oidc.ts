import "server-only";
import { base64url, createRemoteJWKSet, jwtVerify } from "jose";
import { config } from "./config";
import type { Session } from "./session";

type TokenResponse = {
  access_token: string;
  refresh_token?: string;
  id_token?: string;
  expires_in?: number;
};

let jwks: ReturnType<typeof createRemoteJWKSet> | null = null;

export async function pkceChallenge(verifier: string): Promise<string> {
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(verifier));
  return base64url.encode(new Uint8Array(digest));
}

export function redirectUri(): string {
  return `${config.appUrl}/api/auth/callback`;
}

export async function authorizeUrl(state: string, nonce: string, verifier: string): Promise<string> {
  const url = new URL(config.oidc.authorizationUrl);
  url.search = new URLSearchParams({
    response_type: "code",
    client_id: config.oidc.clientId,
    redirect_uri: redirectUri(),
    scope: config.oidc.scope,
    state,
    nonce,
    code_challenge: await pkceChallenge(verifier),
    code_challenge_method: "S256",
  }).toString();
  return url.toString();
}

async function tokenRequest(params: Record<string, string>): Promise<TokenResponse> {
  const response = await fetch(config.oidc.tokenUrl, {
    method: "POST",
    headers: {
      "content-type": "application/x-www-form-urlencoded",
      authorization: `Basic ${btoa(`${encodeURIComponent(config.oidc.clientId)}:${encodeURIComponent(config.oidc.clientSecret)}`)}`,
    },
    body: new URLSearchParams(params),
    cache: "no-store",
    redirect: "error",
  });
  if (!response.ok) throw new Error(`Token endpoint returned ${response.status}`);
  const body = (await response.json()) as TokenResponse;
  if (typeof body.access_token !== "string") throw new Error("Token endpoint returned no access token");
  return body;
}

function toSession(tokens: TokenResponse, claims: { sub: string; name?: unknown; email?: unknown; preferred_username?: unknown }, previous?: Session): Session {
  return {
    sub: claims.sub,
    name: String(claims.name ?? claims.preferred_username ?? previous?.name ?? ""),
    email: String(claims.email ?? previous?.email ?? ""),
    accessToken: tokens.access_token,
    refreshToken: tokens.refresh_token ?? previous?.refreshToken,
    expiresAt: Math.floor(Date.now() / 1000) + (tokens.expires_in ?? 300),
  };
}

export async function exchangeCode(code: string, verifier: string, nonce: string): Promise<Session> {
  const tokens = await tokenRequest({
    grant_type: "authorization_code",
    code,
    redirect_uri: redirectUri(),
    code_verifier: verifier,
  });
  if (!tokens.id_token) throw new Error("No ID token returned");
  jwks ??= createRemoteJWKSet(new URL(config.oidc.jwksUrl));
  const { payload } = await jwtVerify(tokens.id_token, jwks, {
    issuer: config.oidc.issuer,
    audience: config.oidc.clientId,
    clockTolerance: 30,
  });
  if (payload.nonce !== nonce || typeof payload.sub !== "string") throw new Error("ID token nonce mismatch");
  return toSession(tokens, payload as { sub: string });
}

export async function refresh(session: Session): Promise<Session | null> {
  if (!session.refreshToken) return null;
  try {
    const tokens = await tokenRequest({ grant_type: "refresh_token", refresh_token: session.refreshToken });
    return toSession(tokens, { sub: session.sub }, session);
  } catch {
    return null;
  }
}

export function logoutUrl(): string {
  const end = config.oidc.endSessionUrl;
  if (!end) return config.appUrl;
  const url = new URL(end);
  url.searchParams.set("client_id", config.oidc.clientId);
  url.searchParams.set("post_logout_redirect_uri", `${config.appUrl}/`);
  return url.toString();
}
