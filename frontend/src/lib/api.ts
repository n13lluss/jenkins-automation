"use client";

export class ApiError extends Error {
  constructor(
    public status: number,
    message: string,
    public body?: unknown,
  ) {
    super(message);
  }
}

/** Calls the backend through the same-origin BFF proxy. */
export async function api<T>(path: string, init: { method?: string; body?: unknown } = {}): Promise<T> {
  const response = await fetch(`/api/proxy/${path.replace(/^\//, "")}`, {
    method: init.method ?? "GET",
    headers: {
      accept: "application/json",
      "x-pf-csrf": "1",
      ...(init.body !== undefined ? { "content-type": "application/json" } : {}),
    },
    body: init.body !== undefined ? JSON.stringify(init.body) : undefined,
    credentials: "same-origin",
    cache: "no-store",
  });
  if (response.status === 401) {
    // A full page load is required: sign-in is a route handler that redirects to the identity provider.
    // eslint-disable-next-line @next/next/no-location-assign-relative-destination
    window.location.href = `/api/auth/login?returnTo=${encodeURIComponent(window.location.pathname + window.location.search)}`;
    throw new ApiError(401, "Signing in again…");
  }
  if (response.status === 204) return undefined as T;
  const text = await response.text();
  const data = text ? JSON.parse(text) : undefined;
  if (!response.ok) {
    const title = (data && typeof data === "object" && "title" in data && typeof data.title === "string" && data.title) || `Request failed (${response.status})`;
    throw new ApiError(response.status, title, data);
  }
  return data as T;
}

export function errorMessage(e: unknown): string {
  if (e instanceof ApiError) return e.message;
  if (e instanceof Error) return e.message;
  return "Something went wrong.";
}
