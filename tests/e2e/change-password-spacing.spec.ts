import { test, expect } from '@playwright/test';
import * as path from 'path';

test('Verify Change Password Spacing and Padding', async ({ page }) => {
  const screenshotsDir = 'C:\\Users\\kaedee206\\.gemini\\antigravity-ide\\brain\\54277069-3b8b-4e86-b855-4178e4a0ce00\\screenshots';

  // 1. Authenticate as Staff
  await page.goto('/dang-nhap');
  await page.fill('input[name="LoginInput.Email"]', 'nam.dang@noveratech.digital');
  await page.fill('input[name="LoginInput.Password"]', '123456@@');
  await page.click('form[action*="StaffLogin"] button[type="submit"]');
  await page.waitForURL(url => !url.pathname.includes('/dang-nhap'), { timeout: 15000 });

  // 2. Navigate to Change Password page
  await page.goto('/Account/ChangePassword');
  await page.waitForLoadState('networkidle');

  // 3. Focus and type password to trigger strength bar & checklist
  const newPwdInput = page.locator('#newPasswordInput');
  await expect(newPwdInput).toBeVisible({ timeout: 10000 });
  await newPwdInput.fill('Novera@2026!');
  await page.waitForTimeout(300);

  // 4. Capture screenshot
  await page.screenshot({ path: path.join(screenshotsDir, '27_change_password_spacing.png'), fullPage: false });
});
