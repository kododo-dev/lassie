import { test, expect } from '@playwright/test';

// Risk #7 (context/foundation/test-plan.md): the dark-mode toggle can flip
// ThemeState.IsDarkMode without the rendered UI ever reflecting it, because
// MudThemeProvider must live in the same @rendermode island as the toggle
// (see the MudBlazor render-scope lesson in context/foundation/lessons.md).
// `body` is the locator target because the failure this risk describes is a
// page-wide re-render gap, not a single component's own state.
test('toggling dark mode changes the rendered page background', async ({ page }) => {
  await page.goto('/');

  const body = page.locator('body');
  const initialBackground = await body.evaluate((el) => getComputedStyle(el).backgroundColor);

  const themeToggle = page.getByRole('button', { name: 'Toggle dark mode' });

  await themeToggle.click();
  await expect(body).not.toHaveCSS('background-color', initialBackground);

  await themeToggle.click();
  await expect(body).toHaveCSS('background-color', initialBackground);
});
