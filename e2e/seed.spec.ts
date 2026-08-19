import { test, expect } from '@playwright/test';

// Seed test (references/seed-test-pattern.md) — the exemplar future generated
// tests are modeled on. Demonstrates: role-based locators, test independence
// (unique label, own setup/action/assertion/cleanup), waiting for state not
// time, and a name tied to an observable business outcome.
test('created license persists after page reload', async ({ page }) => {
  const label = `Seed License ${Date.now()}`;

  await page.goto('/licenses/new');
  // InteractiveServer pages prerender static HTML before the SignalR circuit attaches
  // event handlers; without this, a click can land before Blazor is actually listening.
  await page.waitForLoadState('networkidle');
  await page.getByRole('textbox', { name: 'Label' }).fill(label);
  await page.getByRole('button', { name: 'Create License' }).click();
  await expect(page.getByText(`License ${label} created.`)).toBeVisible();

  await page.goto('/');
  await expect(page.getByRole('cell', { name: label })).toBeVisible();

  await page.reload();
  await expect(page.getByRole('cell', { name: label })).toBeVisible();

  // Cleanup: this app has no delete feature (see CreateLicense.razor / PanelHome.razor —
  // deactivation is the only lifecycle action available). Deactivating leaves a harmless,
  // uniquely-labeled inactive row rather than an active one; no other test depends on this
  // license not existing, so that's a sufficient terminal state for test independence.
  await page.getByRole('row', { name: label }).getByRole('link').click();
  await page.waitForLoadState('networkidle');
  await page.getByRole('switch').click();
  await page.getByRole('button', { name: 'Deactivate' }).click();
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByText(`License "${label}" updated`)).toBeVisible();
});
