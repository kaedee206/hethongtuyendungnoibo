(function () {
    'use strict';

    document.addEventListener('DOMContentLoaded', function () {
        const form = document.getElementById('jobPositionForm');
        if (!form) return;

        const codeInput = document.getElementById('posCode');
        const titleInput = document.getElementById('posTitle');
        const deptSelect = document.getElementById('posDepartment');
        const levelSelect = document.getElementById('posLevel');
        const minSalaryInput = document.getElementById('posMinSalary');
        const maxSalaryInput = document.getElementById('posMaxSalary');
        const minSalaryPreview = document.getElementById('minSalaryPreview');
        const maxSalaryPreview = document.getElementById('maxSalaryPreview');
        const salaryRangeBar = document.getElementById('salaryRangeBar');
        const salaryComparisonAlert = document.getElementById('salaryComparisonAlert');
        const descTextarea = document.getElementById('posDescription');
        const charCounter = document.getElementById('descCharCount');

        function formatVnd(value) {
            if (value === null || value === undefined || isNaN(value) || value < 0) return '';
            return new Intl.NumberFormat('vi-VN').format(value) + ' VNĐ';
        }

        function formatHumanReadableVnd(value) {
            if (!value || isNaN(value) || value <= 0) return '';
            if (value >= 1000000000) {
                const billions = (value / 1000000000).toFixed(2).replace(/\.00$/, '');
                return `(~ ${billions} tỷ VNĐ)`;
            }
            if (value >= 1000000) {
                const millions = (value / 1000000).toFixed(1).replace(/\.0$/, '');
                return `(~ ${millions} triệu VNĐ)`;
            }
            if (value >= 1000) {
                const thousands = (value / 1000).toFixed(0);
                return `(~ ${thousands} nghìn VNĐ)`;
            }
            return '';
        }

        function setFieldError(inputElement, errorMessage) {
            if (!inputElement) return;
            inputElement.classList.add('is-invalid');
            inputElement.classList.remove('is-valid');

            let feedback = inputElement.parentElement.querySelector('.invalid-feedback.dynamic-error');
            if (!feedback) {
                feedback = document.createElement('div');
                feedback.className = 'invalid-feedback dynamic-error d-block fw-medium mt-1';
                inputElement.parentElement.appendChild(feedback);
            }
            feedback.innerHTML = `<i class="bi bi-exclamation-circle-fill me-1"></i>${errorMessage}`;
            feedback.style.display = 'block';
        }

        function clearFieldError(inputElement) {
            if (!inputElement) return;
            inputElement.classList.remove('is-invalid');
            inputElement.classList.add('is-valid');

            const feedback = inputElement.parentElement.querySelector('.invalid-feedback.dynamic-error');
            if (feedback) {
                feedback.style.display = 'none';
                feedback.innerHTML = '';
            }
        }

        function validateCode() {
            if (!codeInput) return true;
            let val = codeInput.value.trim().toUpperCase();
            codeInput.value = val;

            if (!val) {
                setFieldError(codeInput, 'Mã chức danh không được để trống.');
                return false;
            }
            if (val.length < 3 || val.length > 30) {
                setFieldError(codeInput, 'Mã chức danh phải có độ dài từ 3 đến 30 ký tự.');
                return false;
            }
            const codePattern = /^[A-Z0-9_-]+$/;
            if (!codePattern.test(val)) {
                setFieldError(codeInput, 'Mã chức danh chỉ được chứa chữ cái in hoa, chữ số, gạch nối (-) và gạch dưới (_).');
                return false;
            }
            clearFieldError(codeInput);
            return true;
        }

        function validateTitle() {
            if (!titleInput) return true;
            const val = titleInput.value.trim();
            if (!val) {
                setFieldError(titleInput, 'Tên chức danh không được để trống.');
                return false;
            }
            if (val.length < 3) {
                setFieldError(titleInput, 'Tên chức danh quá ngắn (tối thiểu 3 ký tự).');
                return false;
            }
            if (val.length > 150) {
                setFieldError(titleInput, 'Tên chức danh quá dài (tối đa 150 ký tự).');
                return false;
            }
            clearFieldError(titleInput);
            return true;
        }

        function validateLevel() {
            if (!levelSelect) return true;
            const val = levelSelect.value;
            if (!val || val === '') {
                setFieldError(levelSelect, 'Vui lòng chọn cấp bậc chuyên môn từ danh sách.');
                return false;
            }
            clearFieldError(levelSelect);
            return true;
        }

        function validateDepartment() {
            if (!deptSelect) return true;
            const val = deptSelect.value;
            if (!val || val === '') {
                setFieldError(deptSelect, 'Vui lòng chọn phòng ban trực thuộc.');
                return false;
            }
            clearFieldError(deptSelect);
            return true;
        }

        function validateSalaries() {
            let isValid = true;
            const minValStr = minSalaryInput?.value.trim();
            const maxValStr = maxSalaryInput?.value.trim();

            const minVal = minValStr !== '' ? parseFloat(minValStr) : NaN;
            const maxVal = maxValStr !== '' ? parseFloat(maxValStr) : NaN;

            if (minSalaryPreview) {
                if (!isNaN(minVal) && minVal >= 0) {
                    minSalaryPreview.textContent = `${formatVnd(minVal)} ${formatHumanReadableVnd(minVal)}`;
                    minSalaryPreview.classList.remove('d-none');
                } else {
                    minSalaryPreview.classList.add('d-none');
                }
            }

            if (maxSalaryPreview) {
                if (!isNaN(maxVal) && maxVal >= 0) {
                    maxSalaryPreview.textContent = `${formatVnd(maxVal)} ${formatHumanReadableVnd(maxVal)}`;
                    maxSalaryPreview.classList.remove('d-none');
                } else {
                    maxSalaryPreview.classList.add('d-none');
                }
            }

            if (isNaN(minVal)) {
                setFieldError(minSalaryInput, 'Dải lương tối thiểu không được để trống.');
                isValid = false;
            } else if (minVal < 0) {
                setFieldError(minSalaryInput, 'Dải lương tối thiểu không được là số âm.');
                isValid = false;
            } else if (minVal > 1000000000) {
                setFieldError(minSalaryInput, 'Dải lương tối thiểu không được vượt quá 1.000.000.000 VNĐ.');
                isValid = false;
            } else {
                clearFieldError(minSalaryInput);
            }

            if (isNaN(maxVal)) {
                setFieldError(maxSalaryInput, 'Dải lương tối đa không được để trống.');
                isValid = false;
            } else if (maxVal < 0) {
                setFieldError(maxSalaryInput, 'Dải lương tối đa không được là số âm.');
                isValid = false;
            } else if (maxVal > 2000000000) {
                setFieldError(maxSalaryInput, 'Dải lương tối đa không được vượt quá 2.000.000.000 VNĐ.');
                isValid = false;
            } else {
                clearFieldError(maxSalaryInput);
            }

            if (!isNaN(minVal) && !isNaN(maxVal)) {
                if (maxVal < minVal) {
                    setFieldError(maxSalaryInput, `Dải lương tối đa (${formatVnd(maxVal)}) phải lớn hơn hoặc bằng lương tối thiểu (${formatVnd(minVal)}).`);
                    if (salaryComparisonAlert) {
                        salaryComparisonAlert.classList.remove('d-none');
                        salaryComparisonAlert.innerHTML = `
                            <i class="bi bi-shield-exclamation me-2 fs-5"></i>
                            <div><strong>Xung đột dải lương:</strong> Mức lương tối đa (${formatVnd(maxVal)}) hiện đang nhỏ hơn mức tối thiểu (${formatVnd(minVal)}). Vui lòng điều chỉnh lại.</div>
                        `;
                    }
                    isValid = false;
                } else {
                    if (salaryComparisonAlert) {
                        salaryComparisonAlert.classList.add('d-none');
                    }
                    if (salaryRangeBar) {
                        salaryRangeBar.style.width = '100%';
                    }
                }
            } else if (salaryComparisonAlert) {
                salaryComparisonAlert.classList.add('d-none');
            }

            return isValid;
        }

        if (descTextarea && charCounter) {
            descTextarea.addEventListener('input', function () {
                const len = this.value.length;
                charCounter.textContent = `${len} / 2000 ký tự`;
                if (len > 2000) {
                    charCounter.classList.add('text-danger');
                    setFieldError(descTextarea, 'Mô tả vai trò không được vượt quá 2000 ký tự.');
                } else {
                    charCounter.classList.remove('text-danger');
                    clearFieldError(descTextarea);
                }
            });
        }

        codeInput?.addEventListener('blur', validateCode);
        codeInput?.addEventListener('input', function () {
            this.value = this.value.toUpperCase();
            if (this.classList.contains('is-invalid')) validateCode();
        });

        titleInput?.addEventListener('blur', validateTitle);
        titleInput?.addEventListener('input', function () {
            if (this.classList.contains('is-invalid')) validateTitle();
        });

        levelSelect?.addEventListener('change', validateLevel);
        deptSelect?.addEventListener('change', validateDepartment);

        minSalaryInput?.addEventListener('input', validateSalaries);
        minSalaryInput?.addEventListener('blur', validateSalaries);
        maxSalaryInput?.addEventListener('input', validateSalaries);
        maxSalaryInput?.addEventListener('blur', validateSalaries);

        if (minSalaryInput?.value || maxSalaryInput?.value) {
            validateSalaries();
        }

        form.addEventListener('submit', function (e) {
            const isCodeValid = validateCode();
            const isTitleValid = validateTitle();
            const isLevelValid = validateLevel();
            const isDeptValid = validateDepartment();
            const isSalaryValid = validateSalaries();

            const isFormValid = isCodeValid && isTitleValid && isLevelValid && isDeptValid && isSalaryValid;

            if (!isFormValid) {
                e.preventDefault();
                e.stopPropagation();

                const firstInvalidInput = form.querySelector('.is-invalid');
                if (firstInvalidInput) {
                    firstInvalidInput.scrollIntoView({ behavior: 'smooth', block: 'center' });
                    firstInvalidInput.focus();
                    
                    firstInvalidInput.classList.add('animate-shake');
                    setTimeout(() => firstInvalidInput.classList.remove('animate-shake'), 600);
                }

                const globalAlert = document.getElementById('formValidationAlert');
                if (globalAlert) {
                    globalAlert.classList.remove('d-none');
                    globalAlert.scrollIntoView({ behavior: 'smooth', block: 'start' });
                }
                return false;
            }
        });
    });
})();
