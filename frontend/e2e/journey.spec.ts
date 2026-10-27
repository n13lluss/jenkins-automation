import { expect, test, type Page } from "@playwright/test";

const password = process.env.E2E_PASSWORD ?? "";

async function signIn(page: Page, user: string) {
  await page.goto("/");
  await page.getByLabel(/username or email/i).fill(user);
  await page.getByLabel("Password", { exact: true }).fill(password);
  await page.getByRole("button", { name: /sign in/i }).click();
  await expect(page.getByRole("heading", { name: "Workspaces" })).toBeVisible();
}

test.beforeAll(() => {
  if (!password) throw new Error("Set E2E_PASSWORD to the demo users' password (PF_DEMO_PASSWORD).");
});

test("a maintainer builds a pipeline from scratch and downloads it; a viewer cannot edit", async ({ browser }) => {
  // Bob signs in once so he exists as a user and can be added to the workspace.
  const bobContext = await browser.newContext();
  const bob = await bobContext.newPage();
  await signIn(bob, "bob");

  const context = await browser.newContext({ acceptDownloads: true });
  const page = await context.newPage();
  await signIn(page, "alice");

  const workspace = `E2E ${Date.now()}`;
  await page.getByPlaceholder("Platform team").fill(workspace);
  await page.getByRole("button", { name: "Create workspace" }).click();
  await expect(page.getByRole("heading", { name: "Pipelines" })).toBeVisible();
  const wsId = new URL(page.url()).pathname.split("/")[2];

  await page.getByRole("link", { name: /Start from scratch/ }).click();
  await page.getByPlaceholder("orders-service").fill("e2e-orders");
  await page.getByRole("button", { name: "Continue" }).click();
  await expect(page.getByText("Valid", { exact: true })).toBeVisible();

  await page.getByRole("button", { name: "Save pipeline" }).click();
  await expect(page).toHaveURL(/\/pipelines\/[0-9a-f-]{36}$/);
  await page.getByRole("button", { name: /Review & output/ }).click();

  const [download] = await Promise.all([page.waitForEvent("download"), page.getByRole("link", { name: "Download zip" }).click()]);
  const path = await download.path();
  const zip = await import("node:fs").then((fs) => fs.readFileSync(path));
  expect(zip.subarray(0, 2).toString()).toBe("PK");
  // Zip entry names are stored uncompressed in the central directory.
  expect(zip.includes(Buffer.from("Jenkinsfile"))).toBe(true);
  expect(zip.includes(Buffer.from("docs/PIPELINE.md"))).toBe(true);

  // Add Bob as a viewer through the API from the signed-in page (same-origin request with the CSRF header).
  const status = await page.evaluate(async (ws) => {
    const r = await fetch(`/api/proxy/workspaces/${ws}/members`, {
      method: "PUT",
      headers: { "content-type": "application/json", "x-pf-csrf": "1" },
      body: JSON.stringify({ email: "bob@example.com", role: "viewer" }),
    });
    return r.status;
  }, wsId);
  expect(status).toBeLessThan(300);

  await bob.goto(`/w/${wsId}`);
  await expect(bob.getByRole("heading", { name: "Pipelines" })).toBeVisible();
  await expect(bob.getByRole("link", { name: "e2e-orders" })).toBeVisible();
  await expect(bob.getByRole("link", { name: /Start from scratch/ })).toHaveCount(0);
  await bob.getByRole("link", { name: "e2e-orders" }).click();
  await expect(bob.getByRole("button", { name: /Save/ })).toHaveCount(0);

  await context.close();
  await bobContext.close();
});
