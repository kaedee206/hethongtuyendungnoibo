/**
 * requisition-list.js
 * Xử lý tương tác danh sách Yêu cầu tuyển dụng của Trưởng bộ phận:
 * - Điều hướng khi click vào hàng (Nháp -> Chỉnh sửa; Khác -> Xem chi tiết)
 * - Tự động lọc / sắp xếp khi đổi dropdown
 * - Đồng bộ Quick Filter Tabs
 */

document.addEventListener('DOMContentLoaded', function () {
    // 1. Xử lý click vào hàng của bảng (Row Clickable)
    const tableRows = document.querySelectorAll('tr.req-row-clickable');
    tableRows.forEach(function (row) {
        row.addEventListener('click', function (e) {
            // Không chuyển trang nếu người dùng click vào thẻ a, button hoặc dropdown
            if (e.target.closest('a') || e.target.closest('button') || e.target.closest('.dropdown-menu')) {
                return;
            }

            const targetUrl = row.getAttribute('data-href');
            if (targetUrl) {
                window.location.href = targetUrl;
            }
        });
    });

    // 2. Tự động submit form lọc khi thay đổi lựa chọn trong Dropdowns
    const filterForm = document.getElementById('requisitionFilterForm');
    if (filterForm) {
        const autoSubmitSelects = filterForm.querySelectorAll('.auto-submit-select');
        autoSubmitSelects.forEach(function (select) {
            select.addEventListener('change', function () {
                // Đặt lại trang về 1 khi lọc hoặc sắp xếp mới
                const pageInput = filterForm.querySelector('input[name="Page"]');
                if (pageInput) {
                    pageInput.value = '1';
                }
                filterForm.submit();
            });
        });
    }

    // 3. Xử lý Quick Filter Tabs
    const quickTabs = document.querySelectorAll('.req-tab-btn');
    quickTabs.forEach(function (tab) {
        tab.addEventListener('click', function (e) {
            e.preventDefault();
            const statusValue = tab.getAttribute('data-status');
            const statusSelect = document.getElementById('statusFilterSelect');
            const pageInput = document.querySelector('input[name="Page"]');

            if (statusSelect && filterForm) {
                statusSelect.value = statusValue || '';
                if (pageInput) {
                    pageInput.value = '1';
                }
                filterForm.submit();
            }
        });
    });
});
