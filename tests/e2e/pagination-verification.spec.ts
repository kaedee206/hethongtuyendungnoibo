import { test, expect } from '@playwright/test';
import * as path from 'path';

test.describe('Pagination Verification Suite (Backend & Frontend)', () => {
  const screenshotsDir = 'C:\\Users\\kaedee206\\.gemini\\antigravity-ide\\brain\\54277069-3b8b-4e86-b855-4178e4a0ce00\\screenshots';

  test.beforeEach(async ({ page }) => {
    // Authenticate as Admin/HR
    await page.goto('/dang-nhap');
    await page.fill('input[name="LoginInput.Email"]', 'nam.dang@noveratech.digital');
    await page.fill('input[name="LoginInput.Password"]', '123456@@');
    await page.click('form[action*="StaffLogin"] button[type="submit"]');
    await page.waitForURL(url => !url.pathname.includes('/dang-nhap'), { timeout: 15000 });
  });

  test('Pagination: Candidate Pipeline (/dashboard/ung-vien)', async ({ page }) => {
    await page.goto('/dashboard/ung-vien');
    await page.waitForLoadState('networkidle');

    // Verify Candidate Pipeline panel and pagination footer
    const panel = page.locator('#feature-section-staff-ung-vien');
    await expect(panel).toBeVisible({ timeout: 10000 });

    const paginationContainer = page.locator('#candidatePaginationContainer');
    await expect(paginationContainer).toBeVisible();
    await expect(page.locator('#candTotalRecords')).toBeVisible();
    await expect(page.locator('#candPageSizeSelect')).toBeVisible();

    // Scroll to show table and pagination
    await paginationContainer.scrollIntoViewIfNeeded();
    await page.screenshot({ path: path.join(screenshotsDir, '19_pagination_candidate_pipeline.png'), fullPage: false });

    // Test page transition if multiple pages exist
    const page2Btn = page.locator('#candPaginationUl button:has-text("2")');
    if (await page2Btn.isVisible()) {
      await page2Btn.click();
      await page.waitForTimeout(500);
      await paginationContainer.scrollIntoViewIfNeeded();
      await page.screenshot({ path: path.join(screenshotsDir, '20_pagination_candidate_page2.png'), fullPage: false });
    }
  });

  test('Pagination: Interview Question Banks (/question-banks)', async ({ page }) => {
    await page.goto('/question-banks');
    await page.waitForLoadState('networkidle');

    // Wait for AJAX questions load and footer
    const pageSizeSelect = page.locator('#questionPageSizeSelect');
    await expect(pageSizeSelect).toBeVisible({ timeout: 10000 });
    await expect(page.locator('#totalCount')).toBeVisible();

    await pageSizeSelect.scrollIntoViewIfNeeded();
    await page.screenshot({ path: path.join(screenshotsDir, '21_pagination_question_banks.png'), fullPage: false });
  });

  test('Pagination: Jobs Public Career Board (/Jobs)', async ({ page }) => {
    await page.goto('/Jobs');
    await page.waitForLoadState('networkidle');

    // Verify jobs and pagination
    const paginationNav = page.locator('nav[aria-label="Phân trang việc làm"]');
    await paginationNav.scrollIntoViewIfNeeded();
    await expect(paginationNav).toBeVisible();

    await page.screenshot({ path: path.join(screenshotsDir, '22_pagination_jobs_careers.png'), fullPage: false });
  });

  test('Pagination: Recruitment Catalogs (/recruitment-catalogs)', async ({ page }) => {
    await page.goto('/recruitment-catalogs');
    await page.waitForLoadState('networkidle');

    // Verify pagination footer
    const paginationContainer = page.locator('#catalogPaginationContainer');
    await expect(paginationContainer).toBeVisible({ timeout: 10000 });
    await expect(page.locator('#catalogPageSizeSelect')).toBeVisible();
    await expect(page.locator('#catalogTotalRecords')).toBeVisible();

    await paginationContainer.scrollIntoViewIfNeeded();
    await page.screenshot({ path: path.join(screenshotsDir, '23_pagination_recruitment_catalogs.png'), fullPage: false });
  });
});
