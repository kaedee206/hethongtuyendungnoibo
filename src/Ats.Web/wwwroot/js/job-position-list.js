document.addEventListener('DOMContentLoaded', function () {
    const filterForm = document.getElementById('filterForm');
    const filterBtn = document.getElementById('filterSubmitBtn');
    const loadingOverlay = document.getElementById('tableLoadingState');

    if (filterForm) {
        filterForm.addEventListener('submit', function () {
            if (loadingOverlay) {
                loadingOverlay.classList.remove('d-none');
                loadingOverlay.classList.add('d-flex');
            }

            if (filterBtn) {
                filterBtn.disabled = true;
                filterBtn.innerHTML = '<span class="spinner-border spinner-border-sm me-1" role="status" aria-hidden="true"></span> Đang lọc...';
            }
        });
    }

    const resetBtn = document.getElementById('filterResetBtn');
    if (resetBtn) {
        resetBtn.addEventListener('click', function () {
            if (loadingOverlay) {
                loadingOverlay.classList.remove('d-none');
                loadingOverlay.classList.add('d-flex');
            }
        });
    }
});
