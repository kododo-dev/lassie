import { test as setup } from '@playwright/test';

const authFile = '.auth/user.json';

setup('authenticate', async ({ page }) => {
  await page.goto('/login');

  // The login form is static SSR with no <label for>/id association
  // (see CLAUDE.md — Login.razor stays static SSR for HttpContext.SignInAsync),
  // so there's no accessible name to target via getByLabel.
  await page.locator('input[name="Model.Email"]').fill(process.env.ADMIN_EMAIL ?? 'admin@localhost');
  await page.locator('input[name="Model.Password"]').fill(process.env.ADMIN_PASSWORD ?? 'devpassword123');
  await page.getByRole('button', { name: 'Log in' }).click();

  await page.waitForURL('/');
  await page.context().storageState({ path: authFile });
});
