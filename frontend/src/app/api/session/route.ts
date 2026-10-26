import { getSession } from "@/lib/server/session";

export async function GET() {
  const session = await getSession();
  if (!session) return Response.json({ authenticated: false }, { status: 401, headers: { "cache-control": "no-store" } });
  return Response.json(
    { authenticated: true, user: { name: session.name, email: session.email } },
    { headers: { "cache-control": "no-store" } },
  );
}
