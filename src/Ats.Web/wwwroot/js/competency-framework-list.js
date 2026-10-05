document.addEventListener('DOMContentLoaded', function () {
    const modalElement = document.getElementById('frameworkPositionsModal');
    const modalCodeEl = document.getElementById('modalFrameworkCode');
    const modalNameEl = document.getElementById('modalFrameworkName');
    const modalCountEl = document.getElementById('modalPositionsCount');
    const modalTableEl = document.getElementById('modalPositionsTable');
    const modalBodyEl = document.getElementById('modalPositionsBody');
    const modalEmptyStateEl = document.getElementById('modalPositionsEmptyState');
    const searchForm = document.getElementById('frameworkSearchForm');
    const loadingState = document.getElementById('tableLoadingState');

    if (searchForm && loadingState) {
        searchForm.addEventListener('submit', function () {
            loadingState.classList.remove('d-none');
        });
    }

    const paginationLinks = document.querySelectorAll('.pagination-ats .page-link:not(.disabled)');
    paginationLinks.forEach(function (link) {
        link.addEventListener('click', function () {
            if (loadingState && !this.closest('.disabled')) {
                loadingState.classList.remove('d-none');
            }
        });
    });

    document.querySelectorAll('.view-positions-btn').forEach(function (button) {
        button.addEventListener('click', function () {
            const code = this.getAttribute('data-framework-code') || '';
            const name = this.getAttribute('data-framework-name') || '';
            const rawPositions = this.getAttribute('data-positions');

            if (modalCodeEl) modalCodeEl.textContent = code;
            if (modalNameEl) modalNameEl.textContent = name;

            let positions = [];
            try {
                if (rawPositions) {
                    positions = JSON.parse(rawPositions);
                }
            } catch (err) {
                positions = [];
            }

            renderPositions(positions);
        });
    });

    function renderPositions(positions) {
        if (!modalBodyEl || !modalTableEl || !modalEmptyStateEl || !modalCountEl) {
            return;
        }

        modalBodyEl.innerHTML = '';
        modalCountEl.textContent = `${positions.length} chức danh`;

        if (!positions || positions.length === 0) {
            modalTableEl.classList.add('d-none');
            modalEmptyStateEl.classList.remove('d-none');
            return;
        }

        modalTableEl.classList.remove('d-none');
        modalEmptyStateEl.classList.add('d-none');

        const rows = positions.map(function (pos) {
            const code = escapeHtml(pos.JobCode || pos.jobCode || '');
            const title = escapeHtml(pos.Title || pos.title || '');
            const department = escapeHtml(pos.DepartmentName || pos.departmentName || 'Chung');
            const level = escapeHtml(pos.Level || pos.level || '-');
            const isActive = pos.IsActive !== undefined ? pos.IsActive : (pos.isActive !== undefined ? pos.isActive : true);

            const statusBadge = isActive
                ? '<span class="badge bg-success-subtle text-success border border-success-subtle px-2 py-1" style="border-radius: 6px;"><i class="bi bi-check-circle me-1"></i>Đang áp dụng</span>'
                : '<span class="badge bg-secondary-subtle text-secondary border border-secondary-subtle px-2 py-1" style="border-radius: 6px;">Ngừng</span>';

            return `
                <tr>
                    <td class="ps-3">
                        <span class="badge bg-light text-dark border font-monospace px-2 py-1" style="border-radius: 6px; font-size: 0.82rem;">
                            ${code}
                        </span>
                    </td>
                    <td>
                        <div class="fw-semibold text-dark">${title}</div>
                    </td>
                    <td>
                        <span class="text-secondary small">${department}</span>
                    </td>
                    <td>
                        <span class="badge bg-info-subtle text-info-emphasis border border-info-subtle px-2 py-1" style="border-radius: 6px;">
                            ${level}
                        </span>
                    </td>
                    <td class="text-end pe-3">
                        ${statusBadge}
                    </td>
                </tr>
            `;
        }).join('');

        modalBodyEl.innerHTML = rows;
    }

    function escapeHtml(text) {
        if (!text) return '';
        const map = {
            '&': '&amp;',
            '<': '&lt;',
            '>': '&gt;',
            '"': '&quot;',
            "'": '&#039;'
        };
        return text.toString().replace(/[&<>"']/g, function (m) { return map[m]; });
    }
});
