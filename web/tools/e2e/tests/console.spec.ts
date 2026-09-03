import { expect, test } from '@playwright/test';
import { ALICE, OPERATOR, expectAccessible, nav, signIn } from './support';

test.describe('sign-in and the shell', () => {
  test('an owner signs in by hint and lands on the dashboard with the org nav', async ({ page }) => {
    await signIn(page, ALICE);
    for (const label of ['Sites', 'Members', 'Roles', 'Settings'])
      await expect(nav(page).getByRole('link', { name: label })).toBeVisible();
    // the platform section is for operators only: hidden, not disabled
    await expect(nav(page).getByRole('link', { name: 'Operator' })).toHaveCount(0);
  });

  test('the operator wall: an owner reaching /operator directly gets nothing to act on', async ({ page }) => {
    await signIn(page, ALICE);
    await page.goto('/operator');
    // the page renders (no crash, no blank 404 - the proxy regression) and offers no org list
    await expect(nav(page).getByRole('link', { name: 'Dashboard' })).toBeVisible();
    await expect(page.getByRole('button', { name: /suspend/i })).toHaveCount(0);
  });

  test('a platform operator sees the operator surface', async ({ page }) => {
    await signIn(page, OPERATOR);
    await nav(page).getByRole('link', { name: 'Operator' }).click();
    await expect(page).toHaveURL(/\/operator$/);
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
  });

  test('the shell is keyboard navigable: Tab reaches the primary nav', async ({ page }) => {
    await signIn(page, ALICE);
    const focused = async () => page.evaluate(() => document.activeElement?.textContent?.trim() ?? '');
    let reached = false;
    for (let i = 0; i < 25 && !reached; i++) {
      await page.keyboard.press('Tab');
      reached = (await focused()) === 'Sites';
    }
    expect(reached).toBe(true);
    await page.keyboard.press('Enter');
    await expect(page).toHaveURL(/\/sites$/);
  });
});

test.describe('site management', () => {
  test('creating a site and opening it', async ({ page }) => {
    await signIn(page, ALICE);
    // a fresh org has no hierarchy yet; a site needs a node to sit on
    await page.goto('/hierarchy');
    // wait for the page to know which state it is in before deciding
    const create = page.getByRole('button', { name: 'Create hierarchy' });
    const addNode = page.getByRole('button', { name: 'Add node' });
    await expect(create.or(addNode)).toBeVisible();
    if (await create.isVisible()) {
      await page.getByLabel('Level names (root-first, comma-separated)').fill('Region, Site');
      await create.click();
      await expect(addNode).toBeVisible();
    }
    await page.goto('/sites');
    const name = `E2E Site ${Date.now()}`;
    await page.getByRole('button', { name: 'New site' }).click();
    await page.getByLabel('Name').fill(name);
    const node = page.getByLabel('Hierarchy node');
    await node.selectOption({ index: 1 }); // the first real node after "Choose…"
    await page.getByRole('button', { name: 'Create site' }).click();
    await expect(page.getByText('Site created')).toBeVisible();
    await page.getByRole('link', { name }).click();
    await expect(page).toHaveURL(/\/sites\/[0-9a-f-]+$/);
    await expect(page.getByRole('heading', { name })).toBeVisible();
  });

  test('a validation failure is reported, not swallowed', async ({ page }) => {
    await signIn(page, ALICE);
    await page.goto('/sites');
    await page.getByRole('button', { name: 'New site' }).click();
    // the create button is disabled until the form is valid: the UI blocks the bad request
    await expect(page.getByRole('button', { name: 'Create site' })).toBeDisabled();
  });
});

test.describe('accessibility (axe, WCAG 2.0/2.1 A and AA)', () => {
  for (const path of ['/', '/sites', '/members', '/roles', '/settings', '/developers', '/audit', '/account'])
    test(`no serious or critical violations on ${path}`, async ({ page }) => {
      await signIn(page, ALICE);
      await page.goto(path);
      await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
      await expectAccessible(page);
    });

  test('the operator page for an operator', async ({ page }) => {
    await signIn(page, OPERATOR);
    await page.goto('/operator');
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    await expectAccessible(page);
  });
});
