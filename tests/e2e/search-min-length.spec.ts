import { test, expect } from '@playwright/test';
import * as path from 'path';

test.describe('Search Minimum Length Validation Suite (>= 3 chars)', () => {
  const screenshotsDir = 'C:\\Users\\kaedee206\\.gemini\\antigravity-ide\\brain\\54277069-3b8b-4e86-b855-4178e4a0ce00\\screenshots';

  test.beforeEach(async ({ page }) => {
    // Authenticate as Admin/HR
    await page.goto('/dang-nhap');
    await page.fill('input[name="LoginInput.Email"]', 'nam.dang@noveratech.digital');
    await page.fill('input[name="LoginInput.Password"]', '123456@@');
    await page.click('form[action*="StaffLogin"] button[type="submit"]');
    await page.waitForURL(url => !url.pathname.includes('/dang-nhap'), { timeout: 15000 });
  });

  test('Search Candidate: typing 2 chars shows warning hint; 3+ chars filters list', async ({ page }) => {
    await page.goto('/dashboard/ung-vien');
    await page.waitForLoadState('networkidle');

    const searchInput = page.locator('#candidateSearchInput');
    const searchHint = page.locator('#candSearchHint');
    await expect(searchInput).toBeVisible({ timeout: 10000 });

    // 1. Type 2 characters
    await searchInput.fill('ng');
    await page.waitForTimeout(300);

    // Verify warning hint is visible
    await expect(searchHint).toBeVisible();
    await expect(searchHint).toContainText('Nhập tối thiểu 3 ký tự');

    // Take screenshot of hint
    await page.screenshot({ path: path.join(screenshotsDir, '24_search_min_length_candidate.png'), fullPage: false });

    // 2. Type 3 or more characters
    await searchInput.fill('Nguyen');
    await page.waitForTimeout(300);

    // Verify hint is hidden
    await expect(searchHint).toBeHidden();

    // 3. Clear search
    await searchInput.fill('');
    await page.waitForTimeout(300);
    await expect(searchHint).toBeHidden();
  });

  test('Search Jobs: typing 2 chars shows warning hint; 3+ chars filters', async ({ page }) => {
    await page.goto('/Jobs');
    await page.waitForLoadState('networkidle');

    const jobSearchInput = page.locator('#jobSearchInput');
    const jobSearchHint = page.locator('#jobSearchHint');
    await expect(jobSearchInput).toBeVisible({ timeout: 10000 });

    // 1. Type 2 characters
    await jobSearchInput.fill('re');
    await page.waitForTimeout(300);

    // Verify hint is shown
    await expect(jobSearchHint).toBeVisible();
    await expect(jobSearchHint).toContainText('Nhập tối thiểu 3 ký tự');

    // Take screenshot
    await page.screenshot({ path: path.join(screenshotsDir, '25_search_min_length_jobs.png'), fullPage: false });

    // 2. Type 3+ characters
    await jobSearchInput.fill('React');
    await page.waitForTimeout(300);
    await expect(jobSearchHint).toBeHidden();
  });

  test('Search Question Banks: typing 2 chars shows warning hint', async ({ page }) => {
    await page.goto('/question-banks');
    await page.waitForLoadState('networkidle');

    const filterKeyword = page.locator('#filterKeyword');
    const questionSearchHint = page.locator('#questionSearchHint');
    await expect(filterKeyword).toBeVisible({ timeout: 10000 });

    // Type 2 characters
    await filterKeyword.fill('ja');
    await page.waitForTimeout(300);

    // Verify hint
    await expect(questionSearchHint).toBeVisible();
    await expect(questionSearchHint).toContainText('Nhập tối thiểu 3 ký tự');

    // Take screenshot
    await page.screenshot({ path: path.join(screenshotsDir, '26_search_min_length_question_banks.png'), fullPage: false });

    // Type 4 characters
    await filterKeyword.fill('java');
    await page.waitForTimeout(300);
    await expect(questionSearchHint).toBeHidden();
  });
});
