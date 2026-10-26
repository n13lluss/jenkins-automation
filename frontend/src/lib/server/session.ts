import "server-only";
import { EncryptJWT, jwtDecrypt, base64url } from "jose";
import { cookies } from "next/headers";
import { config } from "./config";

export type Session = {
  sub: string;
  name: string;
  email: string;
  accessToken: string;
  refreshToken?: string;
  /** Epoch seconds when the access token expires. */
  expiresAt: number;
};

export type LoginTransaction = {
  state: string;
  nonce: string;
  verifier: string;
  returnTo: string;
};

const SESSION_COOKIE = "pf_session";
const TX_COOKIE = "pf_login";
const CHUNK = 3800;
const MAX_CHUNKS = 4;
const SESSION_TTL_SECONDS = 8 * 60 * 60;

let keyPromise: Promise<Uint8Array> | null = null;

/** A 256-bit key derived from SESSION_SECRET; cookies are encrypted with A256GCM (JWE). */
function key(): Promise<Uint8Array> {
  keyPromise ??= crypto.subtle
    .digest("SHA-256", new TextEncoder().encode(`pipelineforge-session:${config.sessionSecret}`))
    .then((buf) => new Uint8Array(buf));
  return keyPromise;
}

async function seal(payload: Record<string, unknown>, ttlSeconds: number): Promise<string> {
  return new EncryptJWT(payload)
    .setProtectedHeader({ alg: "dir", enc: "A256GCM" })
    .setIssuedAt()
    .setExpirationTime(`${ttlSeconds}s`)
    .encrypt(await key());
}

async function unseal<T>(token: string): Promise<T | null> {
  try {
    const { payload } = await jwtDecrypt(token, await key());
    return payload as T;
  } catch {
    return null;
  }
}

function cookieOptions(maxAge: number, sameSite: "strict" | "lax") {
  return { httpOnly: true, secure: config.secureCookies, sameSite, path: "/", maxAge } as const;
}

export async function getSession(): Promise<Session | null> {
  const jar = await cookies();
  const count = Number(jar.get(`${SESSION_COOKIE}.n`)?.value ?? "0");
  if (!Number.isInteger(count) || count < 1 || count > MAX_CHUNKS) return null;
  let token = "";
  for (let i = 0; i < count; i++) {
    const part = jar.get(`${SESSION_COOKIE}.${i}`)?.value;
    if (!part) return null;
    token += part;
  }
  return unseal<Session>(token);
}

/** Session cookies are SameSite=Strict, split into chunks to stay under the 4 KB cookie limit. */
export async function setSession(session: Session): Promise<void> {
  const token = await seal(session, SESSION_TTL_SECONDS);
  const chunks = token.match(new RegExp(`.{1,${CHUNK}}`, "g")) ?? [];
  if (chunks.length > MAX_CHUNKS) throw new Error("Session too large");
  const jar = await cookies();
  const options = cookieOptions(SESSION_TTL_SECONDS, "strict");
  chunks.forEach((c, i) => jar.set(`${SESSION_COOKIE}.${i}`, c, options));
  jar.set(`${SESSION_COOKIE}.n`, String(chunks.length), options);
  for (let i = chunks.length; i < MAX_CHUNKS; i++) jar.delete(`${SESSION_COOKIE}.${i}`);
}

export async function clearSession(): Promise<void> {
  const jar = await cookies();
  jar.delete(`${SESSION_COOKIE}.n`);
  for (let i = 0; i < MAX_CHUNKS; i++) jar.delete(`${SESSION_COOKIE}.${i}`);
}

/** The login transaction must survive the cross-site redirect back from the IdP, so it is SameSite=Lax. */
export async function setTransaction(tx: LoginTransaction): Promise<void> {
  (await cookies()).set(TX_COOKIE, await seal(tx, 600), cookieOptions(600, "lax"));
}

export async function takeTransaction(): Promise<LoginTransaction | null> {
  const jar = await cookies();
  const value = jar.get(TX_COOKIE)?.value;
  jar.delete(TX_COOKIE);
  return value ? unseal<LoginTransaction>(value) : null;
}

export function randomToken(bytes = 32): string {
  return base64url.encode(crypto.getRandomValues(new Uint8Array(bytes)));
}

/** Only same-app relative paths are accepted as a post-login destination. */
export function safeReturnTo(value: string | null | undefined): string {
  if (!value || !value.startsWith("/") || value.startsWith("//") || value.includes("\\")) return "/";
  return value;
}
