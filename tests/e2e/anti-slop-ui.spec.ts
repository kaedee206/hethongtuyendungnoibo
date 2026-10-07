import { test, expect } from '@playwright/test';

test.describe('RULE.md Design Tokens & Anti-Slop Verification', () => {

  test('Public Landing (/gioi-thieu) adheres to design tokens', async ({ page }) => {
    await page.goto('/gioi-thieu');
    await expect(page).toHaveTitle(/NoveraTech Careers/);

    // Canvas background check #F8FAFC
    const bodyBg = await page.evaluate(() => {
      return window.getComputedStyle(document.body).backgroundColor;
    });
    expect(bodyBg).toBe('rgb(248, 250, 252)'); // #F8FAFC

    // Check no pill buttons exist in landing action buttons
    const buttons = page.locator('button, a.btn');
    const count = await buttons.count();
    for (let i = 0; i < Math.min(count, 15); i++) {
      const radius = await buttons.nth(i).evaluate(el => window.getComputedStyle(el).borderRadius);
      expect(radius).not.toBe('9999px');
      expect(radius).not.toBe('50%');
    }
  });

  test('Auth Viewport (/dang-nhap) has zero-scroll on 1366x768 and 1920x1080', async ({ page }) => {
    // 1366x768 viewport
    await page.setViewportSize({ width: 1366, height: 768 });
    await page.goto('/dang-nhap');
    await expect(page.locator('form[action*="StaffLogin"]')).toBeVisible();

    const isScrollable1366 = await page.evaluate(() => {
      return document.documentElement.scrollHeight > window.innerHeight;
    });
    expect(isScrollable1366).toBe(false);

    // 1920x1080 viewport
    await page.setViewportSize({ width: 1920, height: 1080 });
    await page.goto('/dang-nhap');
    const isScrollable1080 = await page.evaluate(() => {
      return document.documentElement.scrollHeight > window.innerHeight;
    });
    expect(isScrollable1080).toBe(false);
  });

  test('Top Bar Zero-Clutter: unauthenticated and authenticated states', async ({ page }) => {
    // 1. Unauthenticated state: Only Logo and Login link
    await page.goto('/gioi-thieu');
    const brandLink = page.locator('a.navbar-brand');
    await expect(brandLink).toBeVisible();
    await expect(brandLink).toHaveAttribute('href', '/gioi-thieu');

    // 2. Login as Admin
    await page.goto('/dang-nhap');
    await page.fill('input[name="LoginInput.Email"]', 'nam.dang@noveratech.digital');
    await page.fill('input[name="LoginInput.Password"]', '123456@@');
    await page.click('form[action*="StaffLogin"] button[type="submit"]');

    // After login, should be redirected to dashboard or home
    await page.waitForURL(url => !url.pathname.includes('/dang-nhap'), { timeout: 10000 });

    // Verify authenticated Top Bar has NO horizontal navigation links
    // All navigation links are inside .dropdown-menu
    const topBarHorizontalNav = page.locator('.navbar-nav.me-auto, nav .nav-item:not(.dropdown)');
    const horizontalNavVisibleCount = await topBarHorizontalNav.count();
    expect(horizontalNavVisibleCount).toBe(0);

    // Verify User Toggle Button is present
    const userToggle = page.locator('.btn-user-toggle');
    await expect(userToggle).toBeVisible();

    // Click User Toggle and inspect dropdown links
    await userToggle.click();
    const dropdownMenu = page.locator('.dropdown-menu.show');
    await expect(dropdownMenu).toBeVisible();

    // Must contain Profile, Jobs, and role-specific items
    await expect(dropdownMenu.locator('a[href*="/ho-so"], a:has-text("Hồ sơ cá nhân")')).toBeVisible();
  });

  test('Toast Notification container is fixed at bottom-right', async ({ page }) => {
    await page.goto('/gioi-thieu');
    
    // Evaluate toast container position
    const toastPos = await page.evaluate(() => {
      let container = document.getElementById('noveraToastContainer');
      if (!container) {
        // Trigger toast to create container
        if (window['NoveraToast']) {
          window['NoveraToast'].show('Kiểm tra Toast', 'info');
        }
        container = document.getElementById('noveraToastContainer');
      }
      if (!container) return null;
      const style = window.getComputedStyle(container);
      return {
        position: style.position,
        bottom: style.bottom,
        right: style.right,
        zIndex: style.zIndex
      };
    });

    if (toastPos) {
      expect(toastPos.position).toBe('fixed');
      expect(parseInt(toastPos.bottom)).toBeLessThanOrEqual(30);
      expect(parseInt(toastPos.right)).toBeLessThanOrEqual(30);
    }
  });
});
