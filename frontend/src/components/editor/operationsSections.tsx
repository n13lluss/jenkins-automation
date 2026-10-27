"use client";

import type { ReactNode } from "react";
import type { AppSecrets, EnvironmentConfig, MigrationTool, NotifyEvent } from "@/lib/types";
import { Alert, Button, Card, Field, Input, Select, Toggle } from "../ui";
import type { SectionProps } from "./sections";

function Grid({ children }: { children: ReactNode }) {
  return <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">{children}</div>;
}

const num = (v: string, fallback: number) => (Number.isFinite(Number(v)) && v !== "" ? Number(v) : fallback);

/** Health check, rollback, database and application secrets of one environment. */
export function EnvironmentSafety({
  env,
  index: i,
  migrations,
  update,
  error,
  readOnly,
}: {
  env: EnvironmentConfig;
  index: number;
  migrations: MigrationTool;
  update: SectionProps["update"];
  error: SectionProps["error"];
  readOnly: boolean;
}) {
  const p = `environments[${i}]`;
  const h = env.health;
  const set = (fn: (e: EnvironmentConfig) => void) => update((c) => fn(c.environments[i]));
  const usesDatabase = migrations === "flyway" || migrations === "liquibase";
  return (
    <div className="mt-3 flex flex-col gap-3 border-t border-border pt-3">
      <Grid>
        <Field label="Health path" hint="Polled on the URL after the deploy." error={error(`${p}.health.path`)}>
          <Input value={h.path} disabled={readOnly} onChange={(e) => set((x) => void (x.health.path = e.target.value))} />
        </Field>
        <Field label="Expected status" error={error(`${p}.health.expectedStatus`)}>
          <Input type="number" value={h.expectedStatus} disabled={readOnly} onChange={(e) => set((x) => void (x.health.expectedStatus = num(e.target.value, 200)))} />
        </Field>
        <Field label="Successes in a row / timeout (s)" error={error(`${p}.health.successes`) ?? error(`${p}.health.timeoutSeconds`)}>
          <div className="flex gap-2">
            <Input type="number" value={h.successes} disabled={readOnly} onChange={(e) => set((x) => void (x.health.successes = num(e.target.value, 3)))} />
            <Input type="number" value={h.timeoutSeconds} disabled={readOnly} onChange={(e) => set((x) => void (x.health.timeoutSeconds = num(e.target.value, 300)))} />
          </div>
        </Field>
        <Toggle
          label="Roll back when the deploy or health check fails"
          checked={env.rollbackOnFailure}
          disabled={readOnly}
          onChange={(v) => set((x) => void (x.rollbackOnFailure = v))}
        />
        {usesDatabase && (
          <Toggle
            label="Run database migrations before deploying"
            checked={!!env.database}
            disabled={readOnly}
            onChange={(v) => set((x) => void (x.database = v ? { jdbcUrl: "", credentialId: `db-${x.key}` } : null))}
          />
        )}
        {env.deploy.type === "helm" && (
          <Toggle
            label="Application secrets from a secret store"
            hint="External Secrets Operator; only names and paths are stored here."
            checked={!!env.appSecrets}
            disabled={readOnly}
            onChange={(v) =>
              set((x) => void (x.appSecrets = v ? { storeName: "vault", storeKind: "clusterSecretStore", refreshInterval: "1h", keys: [{ name: "", remoteKey: "", property: null }] } : null))
            }
          />
        )}
      </Grid>
      {error(`${p}.database`) && <Alert tone="danger" title={error(`${p}.database`)} />}
      {usesDatabase && env.database && (
        <Grid>
          <Field label="JDBC URL" hint="jdbc:postgresql://host:5432/db" error={error(`${p}.database.jdbcUrl`)}>
            <Input value={env.database.jdbcUrl} disabled={readOnly} onChange={(e) => set((x) => void (x.database!.jdbcUrl = e.target.value.trim()))} />
          </Field>
          <Field label="Migration credential ID" hint="Username with password." error={error(`${p}.database.credentialId`)}>
            <Input value={env.database.credentialId} disabled={readOnly} onChange={(e) => set((x) => void (x.database!.credentialId = e.target.value))} />
          </Field>
        </Grid>
      )}
      {error(`${p}.appSecrets`) && <Alert tone="danger" title={error(`${p}.appSecrets`)} />}
      {env.appSecrets && <AppSecretsEditor secrets={env.appSecrets} prefix={`${p}.appSecrets`} error={error} readOnly={readOnly} set={(fn) => set((x) => fn(x.appSecrets!))} />}
    </div>
  );
}

function AppSecretsEditor({ secrets: s, prefix, error, readOnly, set }: { secrets: AppSecrets; prefix: string; error: SectionProps["error"]; readOnly: boolean; set: (fn: (s: AppSecrets) => void) => void }) {
  return (
    <div className="flex flex-col gap-2">
      <Grid>
        <Field label="Secret store" error={error(`${prefix}.storeName`)}>
          <Input value={s.storeName} disabled={readOnly} onChange={(e) => set((x) => void (x.storeName = e.target.value))} />
        </Field>
        <Field label="Store kind">
          <Select
            value={s.storeKind}
            disabled={readOnly}
            onChange={(e) => set((x) => void (x.storeKind = e.target.value as AppSecrets["storeKind"]))}
            options={[
              { value: "clusterSecretStore", label: "ClusterSecretStore" },
              { value: "secretStore", label: "SecretStore (namespace)" },
            ]}
          />
        </Field>
        <Field label="Refresh interval" error={error(`${prefix}.refreshInterval`)}>
          <Input value={s.refreshInterval} disabled={readOnly} onChange={(e) => set((x) => void (x.refreshInterval = e.target.value))} />
        </Field>
      </Grid>
      {error(`${prefix}.keys`) && <Alert tone="danger" title={error(`${prefix}.keys`)} />}
      {s.keys.map((k, j) => (
        <div key={j} className="grid gap-2 sm:grid-cols-[1fr_1fr_1fr_auto]">
          <Input aria-label="Variable" placeholder="DB_PASSWORD" value={k.name} disabled={readOnly} onChange={(e) => set((x) => void (x.keys[j].name = e.target.value.toUpperCase()))} />
          <Input aria-label="Remote key" placeholder="app/prod/db" value={k.remoteKey} disabled={readOnly} onChange={(e) => set((x) => void (x.keys[j].remoteKey = e.target.value))} />
          <Input aria-label="Property" placeholder="property (optional)" value={k.property ?? ""} disabled={readOnly} onChange={(e) => set((x) => void (x.keys[j].property = e.target.value || null))} />
          {!readOnly && (
            <Button variant="ghost" onClick={() => set((x) => void x.keys.splice(j, 1))}>
              Remove
            </Button>
          )}
        </div>
      ))}
      {!readOnly && (
        <div>
          <Button onClick={() => set((x) => void x.keys.push({ name: "", remoteKey: "", property: null }))}>Add key</Button>
        </div>
      )}
    </div>
  );
}

const EVENTS: { value: NotifyEvent; label: string }[] = [
  { value: "failure", label: "Build failed" },
  { value: "fixed", label: "Back to normal" },
  { value: "approval", label: "Waiting for approval" },
  { value: "productionDeployed", label: "Deployed to production" },
];

function NotificationsCard({ config: c, update, error, locked, readOnly }: SectionProps) {
  const n = c.notifications;
  const ro = readOnly || locked("notifications");
  return (
    <Card title="Notifications">
      <div className="flex flex-col gap-3">
        <Grid>
          <Toggle label="Slack" checked={n.slack.enabled} disabled={ro} onChange={(v) => update((d) => void (d.notifications.slack.enabled = v))} />
          <Toggle label="Microsoft Teams (Workflows webhook)" checked={n.teams.enabled} disabled={ro} onChange={(v) => update((d) => void (d.notifications.teams.enabled = v))} />
          <Toggle label="E-mail" checked={n.email.enabled} disabled={ro} onChange={(v) => update((d) => void (d.notifications.email.enabled = v))} />
        </Grid>
        {(n.slack.enabled || n.teams.enabled || n.email.enabled) && (
          <Grid>
            {n.slack.enabled && (
              <>
                <Field label="Slack channel" error={error("notifications.slack.channel")}>
                  <Input value={n.slack.channel} disabled={ro} onChange={(e) => update((d) => void (d.notifications.slack.channel = e.target.value.trim()))} />
                </Field>
                <Field label="Slack token credential ID (optional)" hint="Empty uses the Slack plugin's global settings." error={error("notifications.slack.tokenCredentialId")}>
                  <Input value={n.slack.tokenCredentialId ?? ""} disabled={ro} onChange={(e) => update((d) => void (d.notifications.slack.tokenCredentialId = e.target.value.trim() || null))} />
                </Field>
              </>
            )}
            {n.teams.enabled && (
              <Field label="Teams webhook credential ID" hint="Secret text with the webhook URL." error={error("notifications.teams.webhookCredentialId")}>
                <Input value={n.teams.webhookCredentialId} disabled={ro} onChange={(e) => update((d) => void (d.notifications.teams.webhookCredentialId = e.target.value.trim()))} />
              </Field>
            )}
            {n.email.enabled && (
              <Field label="Recipients" hint="Comma separated." error={error("notifications.email.recipients")}>
                <Input
                  value={n.email.recipients.join(", ")}
                  disabled={ro}
                  onChange={(e) => update((d) => void (d.notifications.email.recipients = e.target.value.split(",").map((r) => r.trim()).filter(Boolean)))}
                />
              </Field>
            )}
          </Grid>
        )}
        <div className="flex flex-wrap gap-4">
          {EVENTS.map((ev) => (
            <Toggle
              key={ev.value}
              label={ev.label}
              checked={n.events.includes(ev.value)}
              disabled={ro}
              onChange={(v) => update((d) => void (d.notifications.events = v ? [...d.notifications.events, ev.value] : d.notifications.events.filter((x) => x !== ev.value)))}
            />
          ))}
        </div>
      </div>
    </Card>
  );
}

export function OperationsSection(props: SectionProps) {
  const { config: c, update, error, locked, readOnly } = props;
  const m = c.migrations;
  const ro = readOnly || locked("migrations");
  return (
    <div className="flex flex-col gap-4">
      <NotificationsCard {...props} />
      <Card title="Delivery metrics">
        <div className="flex flex-col gap-3">
          <Toggle
            label="Report every deploy to PipelineForge"
            hint="Environment, digest, commit and timings only. Feeds the Metrics page: deploy frequency, lead time, change failure rate, time to restore."
            checked={c.metrics.enabled}
            disabled={readOnly || locked("metrics")}
            onChange={(v) => update((d) => void (d.metrics.enabled = v))}
          />
          {c.metrics.enabled && (
            <Grid>
              <Field label="PipelineForge URL" hint="As Jenkins reaches it, e.g. https://pipelineforge.example.com" error={error("metrics.url")}>
                <Input value={c.metrics.url} disabled={readOnly || locked("metrics")} onChange={(e) => update((d) => void (d.metrics.url = e.target.value.trim()))} />
              </Field>
              <Field label="Token credential ID" hint="Secret text; create the token under Actions." error={error("metrics.tokenCredentialId")}>
                <Input value={c.metrics.tokenCredentialId} disabled={readOnly || locked("metrics")} onChange={(e) => update((d) => void (d.metrics.tokenCredentialId = e.target.value.trim()))} />
              </Field>
            </Grid>
          )}
        </div>
      </Card>
      <Card title="Accepted risks">
        <Toggle
          label="Keep accepted risks in one register"
          hint="security/exceptions.yaml lists every accepted finding with owner, ticket and expiry (max 180 days). The build fails on expired entries and renders the scanners' ignore files from it."
          checked={c.security.exceptionRegister}
          disabled={readOnly || locked("security.exceptionRegister")}
          onChange={(v) => update((d) => void (d.security.exceptionRegister = v))}
        />
      </Card>
      <Card title="Database migrations">
        <div className="flex flex-col gap-3">
          <Grid>
            <Field label="Tool" error={error("migrations")}>
              <Select
                value={m.tool}
                disabled={ro}
                onChange={(e) =>
                  update((d) => {
                    const tool = e.target.value as MigrationTool;
                    d.migrations.tool = tool;
                    if (tool === "liquibase" && d.migrations.path.endsWith("/migration")) d.migrations.path = "src/main/resources/db/changelog/db.changelog-master.yaml";
                    if (tool === "flyway" && d.migrations.path.endsWith(".yaml")) d.migrations.path = "src/main/resources/db/migration";
                  })
                }
                options={[
                  { value: "none", label: "None" },
                  { value: "inApp", label: "In the application (e.g. Spring Boot)" },
                  { value: "flyway", label: "Flyway, before each deploy" },
                  { value: "liquibase", label: "Liquibase, before each deploy" },
                ]}
              />
            </Field>
            {(m.tool === "flyway" || m.tool === "liquibase") && (
              <Field label={m.tool === "flyway" ? "Migrations directory" : "Changelog file"} error={error("migrations.path")}>
                <Input value={m.path} disabled={ro} onChange={(e) => update((d) => void (d.migrations.path = e.target.value))} />
              </Field>
            )}
          </Grid>
          {(m.tool === "flyway" || m.tool === "liquibase") && (
            <p className="text-sm text-muted">Set the database per environment under Environments. Migrations are not rolled back with a release: keep them backwards compatible.</p>
          )}
        </div>
      </Card>
    </div>
  );
}
