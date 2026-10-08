import { test, expect } from '@playwright/test';
import * as path from 'path';

const screenshotDir = path.resolve('C:/Users/kaedee206/.gemini/antigravity-ide/brain/dc9d8fcc-eb3a-4b66-b168-2b4a20d76fbc/screenshots');

test('Capture full walkthrough screenshots', async ({ page }) => {
  test.setTimeout(90000);
  await page.setViewportSize({ width: 1366, height: 850 });

  // Login
  await page.goto('/dang-nhap');
  await page.fill('input[name="LoginInput.Email"]', 'phuong.nguyen@noveratech.digital');
  await page.fill('input[name="LoginInput.Password"]', '123456@@');
  await page.click('form[action*="StaffLogin"] button[type="submit"]');
  await page.waitForURL(url => !url.pathname.includes('/dang-nhap'), { timeout: 20000 });

  // 1. Danh sách Khung năng lực
  await page.goto('/competency-frameworks');
  await page.waitForLoadState('networkidle');
  await page.waitForTimeout(2000);
  const shot1 = path.join(screenshotDir, '01_competency_frameworks_list.png');
  await page.screenshot({ path: shot1, fullPage: false });
  console.log('CAPTURED_1:', shot1);

  // 2. Chi tiết Khung năng lực
  const firstDetailLink = page.locator('tbody tr a[href*="competency-frameworks/details"], tbody tr a[href*="Details"]').first();
  if (await firstDetailLink.count() > 0) {
    const href = await firstDetailLink.getAttribute('href');
    if (href) {
      await page.goto(href);
      await page.waitForLoadState('networkidle');
      await page.waitForTimeout(2000);
      const shot2 = path.join(screenshotDir, '02_competency_framework_details.png');
      await page.screenshot({ path: shot2, fullPage: false });
      console.log('CAPTURED_2:', shot2);
    }
  }

  // 3. Form tạo mới Khung năng lực (với Rubric & Thanh Trọng số)
  await page.goto('/competency-frameworks/create');
  await page.waitForLoadState('networkidle');
  await page.waitForTimeout(1000);
  const rubricSection = page.locator('#criteriaContainer, #criteriaList, .card:has-text("Tiêu chí")').first();
  if (await rubricSection.count() > 0) {
    await rubricSection.scrollIntoViewIfNeeded();
  }
  await page.waitForTimeout(1000);
  const shot3 = path.join(screenshotDir, '03_competency_framework_create.png');
  await page.screenshot({ path: shot3, fullPage: false });
  console.log('CAPTURED_3:', shot3);

  // 4. Form tạo Chức danh (Dropdown Khung năng lực chuẩn)
  await page.goto('/job-positions/create');
  await page.waitForLoadState('networkidle');
  await page.waitForTimeout(1000);
  const cfSelect = page.locator('select[name*="CompetencyFrameworkId"], .card:has-text("Khung năng lực")').first();
  if (await cfSelect.count() > 0) {
    await cfSelect.scrollIntoViewIfNeeded();
  }
  await page.waitForTimeout(1000);
  const shot4 = path.join(screenshotDir, '04_job_position_create.png');
  await page.screenshot({ path: shot4, fullPage: false });
  console.log('CAPTURED_4:', shot4);

  // 5. Danh sách Yêu cầu tuyển dụng - Tab Bản nháp (Nút Sao chép)
  await page.goto('/requisitions/drafts');
  await page.waitForLoadState('networkidle');
  await page.waitForTimeout(2000);
  const shot5 = path.join(screenshotDir, '05_requisitions_drafts.png');
  await page.screenshot({ path: shot5, fullPage: false });
  console.log('CAPTURED_5:', shot5);
});
