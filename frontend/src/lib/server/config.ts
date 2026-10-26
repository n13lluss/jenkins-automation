import "server-only";

function required(name: string): string {
  const value = process.env[name];
  if (!value) throw new Error(`Missing environment variable ${name}`);
  return value;
}

/**
 * OIDC endpoints are configured explicitly so the browser-facing URLs (authorize,
 * end-session) and the server-to-server URLs (token, JWKS) can differ, as they do
 * inside docker compose. For a cloud IdP they are all public URLs.
 */
export const config = {
  get appUrl() {
    return required("APP_URL").replace(/\/$/, "");
  },
  get backendUrl() {
    return required("BACKEND_URL").replace(/\/$/, "");
  },
  get sessionSecret() {
    const secret = required("SESSION_SECRET");
    if (secret.length < 32) throw new Error("SESSION_SECRET must be at least 32 characters");
    return secret;
  },
  oidc: {
    get issuer() {
      return required("OIDC_ISSUER");
    },
    get authorizationUrl() {
      return required("OIDC_AUTHORIZATION_URL");
    },
    get tokenUrl() {
      return required("OIDC_TOKEN_URL");
    },
    get jwksUrl() {
      return required("OIDC_JWKS_URL");
    },
    get endSessionUrl() {
      return process.env.OIDC_END_SESSION_URL || null;
    },
    get clientId() {
      return required("OIDC_CLIENT_ID");
    },
    get clientSecret() {
      return required("OIDC_CLIENT_SECRET");
    },
    get scope() {
      return process.env.OIDC_SCOPE || "openid profile email";
    },
  },
  get secureCookies() {
    return this.appUrl.startsWith("https://");
  },
};
