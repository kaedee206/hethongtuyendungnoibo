/**
 * Quản lý tương tác và kiểm thực dữ liệu cho Form Khai báo Yêu cầu Tuyển dụng (EP-03_Requisition).
 * Tuân thủ RULE.md & Coding_Convention.md: Không dùng icon viễn tưởng, hỗ trợ định dạng tiền tệ thời gian thực,
 * tự động gợi ý dải lương theo chức danh và kiểm tra biên độ lương, ngày có mặt.
 */
document.addEventListener('DOMContentLoaded', function () {
    initRequisitionForms();
});

function initRequisitionForms() {
    const forms = document.querySelectorAll('.requisition-entry-form');
    forms.forEach(function (form) {
        setupRequisitionForm(form);
    });
}

function setupRequisitionForm(form) {
    const positionSelect = form.querySelector('.req-position-select');
    const deptSelect = form.querySelector('.req-dept-select');
    const minSalaryInput = form.querySelector('.req-min-salary');
    const maxSalaryInput = form.querySelector('.req-max-salary');
    const minSalaryHint = form.querySelector('.req-min-salary-hint');
    const maxSalaryHint = form.querySelector('.req-max-salary-hint');
    const benchmarkContainer = form.querySelector('.req-benchmark-container');
    const benchmarkText = form.querySelector('.req-benchmark-text');
    const btnApplyBenchmark = form.querySelector('.btn-apply-benchmark');
    const quantityInput = form.querySelector('.req-quantity-input');
    const dateInput = form.querySelector('.req-date-input');
    const salaryErrorMsg = form.querySelector('.req-salary-error');
    const dateErrorMsg = form.querySelector('.req-date-error');

    // Thiết lập ngày tối thiểu cho DatePicker là ngày hôm nay
    if (dateInput) {
        const todayStr = new Date().toISOString().split('T')[0];
        dateInput.min = todayStr;
        if (!dateInput.value) {
            // Mặc định sau 30 ngày
            const defaultDate = new Date();
            defaultDate.setDate(defaultDate.getDate() + 30);
            dateInput.value = defaultDate.toISOString().split('T')[0];
        }
    }

    // 1. Xử lý khi thay đổi Chức danh
    if (positionSelect) {
        positionSelect.addEventListener('change', function () {
            const selectedOpt = positionSelect.options[positionSelect.selectedIndex];
            if (!selectedOpt || !selectedOpt.value) {
                if (benchmarkContainer) benchmarkContainer.classList.add('d-none');
                return;
            }

            const minSalary = selectedOpt.getAttribute('data-min-salary');
            const maxSalary = selectedOpt.getAttribute('data-max-salary');
            const deptId = selectedOpt.getAttribute('data-dept-id');

            // Gợi ý dải lương theo chức danh
            if (minSalary && maxSalary && benchmarkContainer && benchmarkText) {
                const minNum = parseFloat(minSalary);
                const maxNum = parseFloat(maxSalary);
                if (minNum > 0 && maxNum > 0) {
                    benchmarkText.textContent = `${formatVndCurrency(minNum)} – ${formatVndCurrency(maxNum)}`;
                    benchmarkContainer.classList.remove('d-none');

                    if (btnApplyBenchmark) {
                        btnApplyBenchmark.onclick = function () {
                            if (minSalaryInput) {
                                minSalaryInput.value = minNum;
                                updateSalaryHint(minSalaryInput, minSalaryHint);
                            }
                            if (maxSalaryInput) {
                                maxSalaryInput.value = maxNum;
                                updateSalaryHint(maxSalaryInput, maxSalaryHint);
                            }
                            validateSalaryRange();
                        };
                    }
                } else {
                    benchmarkContainer.classList.add('d-none');
                }
            } else if (benchmarkContainer) {
                benchmarkContainer.classList.add('d-none');
            }

            // Tự động đồng bộ phòng ban nếu phòng ban chưa chọn và người dùng được phép đổi
            if (deptSelect && (!deptSelect.value || deptSelect.value === '') && deptId) {
                for (let i = 0; i < deptSelect.options.length; i++) {
                    if (deptSelect.options[i].value.toLowerCase() === deptId.toLowerCase()) {
                        deptSelect.selectedIndex = i;
                        break;
                    }
                }
            }
        });
    }

    // 2. Định dạng thời gian thực cho Lương tối thiểu & tối đa
    if (minSalaryInput) {
        minSalaryInput.addEventListener('input', function () {
            updateSalaryHint(minSalaryInput, minSalaryHint);
            validateSalaryRange();
        });
        updateSalaryHint(minSalaryInput, minSalaryHint);
    }

    if (maxSalaryInput) {
        maxSalaryInput.addEventListener('input', function () {
            updateSalaryHint(maxSalaryInput, maxSalaryHint);
            validateSalaryRange();
        });
        updateSalaryHint(maxSalaryInput, maxSalaryHint);
    }

    // 3. Kiểm tra số lượng
    if (quantityInput) {
        quantityInput.addEventListener('input', function () {
            const val = parseInt(this.value, 10);
            if (isNaN(val) || val < 1) {
                this.classList.add('is-invalid');
                this.classList.remove('is-valid');
            } else {
                this.classList.remove('is-invalid');
                this.classList.add('is-valid');
            }
        });
    }

    // 4. Kiểm tra ngày cần người
    if (dateInput) {
        dateInput.addEventListener('change', function () {
            validateDate();
        });
    }

    function updateSalaryHint(input, hintEl) {
        if (!hintEl) return;
        const val = parseFloat(input.value);
        if (!isNaN(val) && val > 0) {
            hintEl.textContent = `= ${formatVndCurrency(val)}`;
            hintEl.classList.remove('d-none');
        } else {
            hintEl.textContent = '';
            hintEl.classList.add('d-none');
        }
    }

    function validateSalaryRange() {
        if (!minSalaryInput || !maxSalaryInput) return true;

        const minVal = parseFloat(minSalaryInput.value);
        const maxVal = parseFloat(maxSalaryInput.value);

        if (isNaN(minVal) || minVal <= 0) {
            minSalaryInput.classList.add('is-invalid');
            minSalaryInput.classList.remove('is-valid');
        } else {
            minSalaryInput.classList.remove('is-invalid');
            minSalaryInput.classList.add('is-valid');
        }

        if (isNaN(maxVal) || maxVal <= 0) {
            maxSalaryInput.classList.add('is-invalid');
            maxSalaryInput.classList.remove('is-valid');
        } else {
            maxSalaryInput.classList.remove('is-invalid');
            maxSalaryInput.classList.add('is-valid');
        }

        if (!isNaN(minVal) && !isNaN(maxVal)) {
            if (maxVal < minVal) {
                maxSalaryInput.classList.add('is-invalid');
                maxSalaryInput.classList.remove('is-valid');
                if (salaryErrorMsg) {
                    salaryErrorMsg.textContent = 'Mức lương tối đa không được nhỏ hơn mức lương tối thiểu.';
                    salaryErrorMsg.classList.remove('d-none');
                }
                return false;
            } else {
                if (salaryErrorMsg) {
                    salaryErrorMsg.classList.add('d-none');
                }
                return true;
            }
        }

        return true;
    }

    function validateDate() {
        if (!dateInput) return true;
        const val = dateInput.value;
        if (!val) {
            dateInput.classList.add('is-invalid');
            if (dateErrorMsg) {
                dateErrorMsg.textContent = 'Vui lòng chọn ngày cần nhân sự có mặt.';
                dateErrorMsg.classList.remove('d-none');
            }
            return false;
        }

        const selectedDate = new Date(val);
        const today = new Date();
        today.setHours(0, 0, 0, 0);

        if (selectedDate < today) {
            dateInput.classList.add('is-invalid');
            if (dateErrorMsg) {
                dateErrorMsg.textContent = 'Ngày cần nhân sự có mặt phải từ ngày hôm nay trở đi.';
                dateErrorMsg.classList.remove('d-none');
            }
            return false;
        } else {
            dateInput.classList.remove('is-invalid');
            dateInput.classList.add('is-valid');
            if (dateErrorMsg) dateErrorMsg.classList.add('d-none');
            return true;
        }
    }

    // 5. Kiểm tra toàn bộ form trước khi Submit
    form.addEventListener('submit', function (e) {
        let isFormValid = true;

        if (positionSelect && (!positionSelect.value || positionSelect.value === '')) {
            positionSelect.classList.add('is-invalid');
            isFormValid = false;
        } else if (positionSelect) {
            positionSelect.classList.remove('is-invalid');
        }

        if (deptSelect && (!deptSelect.value || deptSelect.value === '')) {
            deptSelect.classList.add('is-invalid');
            isFormValid = false;
        } else if (deptSelect) {
            deptSelect.classList.remove('is-invalid');
        }

        if (!validateSalaryRange()) {
            isFormValid = false;
        }

        if (!validateDate()) {
            isFormValid = false;
        }

        if (quantityInput) {
            const qty = parseInt(quantityInput.value, 10);
            if (isNaN(qty) || qty < 1) {
                quantityInput.classList.add('is-invalid');
                isFormValid = false;
            }
        }

        if (!isFormValid) {
            e.preventDefault();
            e.stopPropagation();

            const firstInvalid = form.querySelector('.is-invalid');
            if (firstInvalid) {
                firstInvalid.focus();
            }
            return false;
        }

        const submitBtn = form.querySelector('button[type="submit"]');
        const spinner = form.querySelector('.btn-spinner');
        if (submitBtn) {
            submitBtn.disabled = true;
        }
        if (spinner) {
            spinner.classList.remove('d-none');
        }

        return true;
    });
}

function formatVndCurrency(amount) {
    if (isNaN(amount)) return '0 VNĐ';
    return new Intl.NumberFormat('vi-VN', {
        style: 'currency',
        currency: 'VND',
        maximumFractionDigits: 0
    }).format(amount);
}
