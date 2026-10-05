import { test, expect } from '@playwright/test';

test.describe('Sprint 2 End-to-End Features Suite', () => {

  test.beforeEach(async ({ page }) => {
    // Authenticate as Admin before each test
    await page.goto('/dang-nhap');
    await page.fill('input[name="LoginInput.Email"]', 'nam.dang@noveratech.digital');
    await page.fill('input[name="LoginInput.Password"]', '123456@@');
    await page.click('form[action*="StaffLogin"] button[type="submit"]');
    await page.waitForURL(url => !url.pathname.includes('/dang-nhap'), { timeout: 15000 });
  });

  test('S2-02 & S2-03: Profile view & edit with phone validation', async ({ page }) => {
    await page.goto('/ho-so');
    await expect(page.locator('h1').filter({ hasText: /Hồ sơ cá nhân/i })).toBeVisible();

    // Verify Read-Only indicators inside main card body
    await expect(page.locator('.card-body').getByText('nam.dang@noveratech.digital').first()).toBeVisible();

    // Navigate to Edit Profile
    await page.goto('/ho-so/chinh-sua');
    await expect(page.locator('input#inputFullName')).toBeVisible();

    // Verify Read-Only inputs are disabled
    const emailInput = page.locator('input[value*="nam.dang@noveratech.digital"]').first();
    await expect(emailInput).toBeDisabled();

    // Try entering an invalid phone number
    const phoneInput = page.locator('input#inputPhone');
    await phoneInput.fill('12345');
    await page.click('button#btnSaveProfile');

    // Check validation error class is applied
    await expect(phoneInput).toHaveClass(/is-invalid/);
  });

  test('S2-04: Department Multi-Level Tree Org Chart', async ({ page }) => {
    await page.goto('/phong-ban');
    await expect(page.locator('h1').filter({ hasText: /Sơ đồ Cơ cấu Tổ chức & Phòng ban/i })).toBeVisible();

    // Check tree container and search input
    await expect(page.locator('#deptSearchInput')).toBeVisible();
    await expect(page.locator('button:has-text("Thêm phòng ban mới")')).toBeVisible();

    // Wait for AJAX tree load to render items
    await expect(page.locator('.dept-card-item, #deptTreeRoot li').first()).toBeVisible({ timeout: 15000 });
  });

  test('S2-05 & S2-10: Job Positions & Salary Bands', async ({ page }) => {
    await page.goto('/job-positions');
    await expect(page.locator('h1').filter({ hasText: /Danh mục Chức danh/i })).toBeVisible();

    // Check salary column exists (take first to satisfy strict mode)
    const salaryHeader = page.locator('th').filter({ hasText: /Dải lương/i }).first();
    await expect(salaryHeader).toBeVisible();
  });

  test('S2-07: Interview Question Bank', async ({ page }) => {
    await page.goto('/question-banks');
    await expect(page.locator('h1').filter({ hasText: /Quản lý ngân hàng câu hỏi/i })).toBeVisible();

    // Check filter or search
    await expect(page.locator('#filterKeyword')).toBeVisible();
    await expect(page.locator('button:has-text("Thêm câu hỏi mới")')).toBeVisible();
  });

  test('S2-08: Recruitment Catalogs Management', async ({ page }) => {
    await page.goto('/recruitment-catalogs');
    await expect(page.locator('h1').filter({ hasText: /Danh mục tuyển dụng/i })).toBeVisible();

    // Check category tabs
    await expect(page.locator('.catalog-tab-btn[data-type="SOURCE"]')).toBeVisible();
    await expect(page.locator('.catalog-tab-btn[data-type="REJECTION_REASON"]')).toBeVisible();
    await expect(page.locator('.catalog-tab-btn[data-type="LOCATION"]')).toBeVisible();
    await expect(page.locator('.catalog-tab-btn[data-type="WORK_TYPE"]')).toBeVisible();

    // Click "Thêm mục danh mục" to open modal
    await page.click('#btnOpenCreateCatalogModal');
    const modal = page.locator('#catalogModal');
    await expect(modal).toBeVisible();
    await expect(modal.locator('#modalCatalogName')).toBeVisible();
  });

  test('S2-09: Company Profile & Landing Configuration', async ({ page }) => {
    await page.goto('/company-profile');
    await expect(page.locator('h1').filter({ hasText: /Hồ sơ Doanh nghiệp/i })).toBeVisible();

    // Check inputs for company info
    await expect(page.locator('input[name="CompanyName"]')).toBeVisible();
    await expect(page.locator('input[name="Headline"]')).toBeVisible();
    await expect(page.locator('textarea[name="TechStackJson"]')).toBeVisible();

    // Verify link to view public page
    const publicLink = page.locator('a[href="/gioi-thieu"]').first();
    await expect(publicLink).toBeVisible();
  });

  test('S2-01: Bulk Excel User Import modal and template download', async ({ page }) => {
    await page.goto('/users');
    await expect(page.locator('h1').filter({ hasText: /Quản trị người dùng/i })).toBeVisible();

    // Check "Nhập Excel" button
    const importBtn = page.locator('button:has-text("Nhập Excel")');
    await expect(importBtn).toBeVisible();

    // Trigger opening modal via Bootstrap Modal API to ensure full display
    await page.evaluate(() => {
      const modalEl = document.getElementById('importUserModal');
      if (modalEl && window['bootstrap']) {
        const modal = window['bootstrap'].Modal.getOrCreateInstance(modalEl);
        modal.show();
      }
    });

    // Check modal shows
    await expect(page.locator('#importUserModal')).toHaveClass(/show/, { timeout: 10000 });

    // Check template download link inside modal
    const templateLink = page.locator('#importUserModal a[href="/users/import-template"]');
    await expect(templateLink).toBeAttached();

    // Check file input exists
    await expect(page.locator('#importUserModal input[type="file"]')).toBeAttached();
  });
});
