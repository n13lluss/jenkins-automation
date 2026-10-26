# PipelineForge frontend

Next.js App Router UI plus a backend-for-frontend:

- OIDC sign-in in route handlers
- an encrypted session cookie
- a proxy to the ASP.NET API

The proxy keeps tokens out of the browser. See the root README for running the full stack.

```sh
cp .env.example .env.local   # fill in the secrets
npm ci
npm run dev
```
