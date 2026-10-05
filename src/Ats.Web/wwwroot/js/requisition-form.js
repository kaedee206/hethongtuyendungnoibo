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

    // 5. Khởi tạo và xử lý nút Lưu nháp & Gửi duyệt
    const isDraftInput = form.querySelector('#reqIsDraft');
    const reqIdInput = form.querySelector('#reqId');
    const reqCodeInput = form.querySelector('#reqCode');
    const btnSaveDraft = form.querySelector('#btnSaveDraft');
    const btnSubmitRequisition = form.querySelector('#btnSubmitRequisition');
    const spinnerSubmit = form.querySelector('.btn-spinner');
    const spinnerDraft = form.querySelector('.btn-spinner-draft');

    const autoSaveIndicator = form.querySelector('#autoSaveIndicator');
    const autoSaveSpinner = autoSaveIndicator ? autoSaveIndicator.querySelector('.auto-save-spinner') : null;
    const autoSaveIcon = autoSaveIndicator ? autoSaveIndicator.querySelector('.auto-save-icon') : null;
    const autoSaveText = autoSaveIndicator ? autoSaveIndicator.querySelector('.auto-save-text') : null;

    const jobDescWrapper = form.querySelector('#jobDescEditorWrapper');
    const jobDescContent = form.querySelector('#jobDescEditorContent');
    const jobDescHidden = form.querySelector('#reqJobDescription');
    const jobDescError = form.querySelector('#jobDescError');

    const reqWrapper = form.querySelector('#requirementsEditorWrapper');
    const reqContent = form.querySelector('#requirementsEditorContent');
    const reqHidden = form.querySelector('#reqRequirements');
    const reqError = form.querySelector('#requirementsError');

    let isDirty = false;
    let isAutoSaving = false;

    function markDirty() {
        isDirty = true;
    }

    form.addEventListener('input', markDirty);
    form.addEventListener('change', markDirty);
    if (jobDescContent) jobDescContent.addEventListener('input', markDirty);
    if (reqContent) reqContent.addEventListener('input', markDirty);

    function stripHtmlToText(html) {
        if (!html) return '';
        const temp = document.createElement('div');
        temp.innerHTML = html;
        return (temp.textContent || temp.innerText || '').trim();
    }

    function syncEditorsBeforeSubmit() {
        if (jobDescContent && jobDescHidden) {
            jobDescHidden.value = jobDescContent.innerHTML;
        }
        if (reqContent && reqHidden) {
            reqHidden.value = reqContent.innerHTML;
        }
    }

    function validateEditors(isDraft) {
        syncEditorsBeforeSubmit();
        let valid = true;

        if (isDraft) {
            // Khi lưu nháp: Cho phép để trống cả Mô tả công việc và Yêu cầu ứng viên
            if (jobDescWrapper) jobDescWrapper.classList.remove('is-invalid');
            if (jobDescError) jobDescError.textContent = '';
            if (reqWrapper) reqWrapper.classList.remove('is-invalid');
            if (reqError) reqError.textContent = '';
            return true;
        }

        // Khi gửi duyệt: Bắt buộc cả 2 editor phải có nội dung thực tế
        if (jobDescContent) {
            const descText = stripHtmlToText(jobDescContent.innerHTML);
            if (!descText) {
                if (jobDescWrapper) jobDescWrapper.classList.add('is-invalid');
                if (jobDescError) {
                    jobDescError.textContent = 'Vui lòng nhập mô tả công việc (trách nhiệm, nhiệm vụ chính, KPI...) khi gửi duyệt.';
                    jobDescError.classList.remove('d-none');
                }
                valid = false;
            } else {
                if (jobDescWrapper) jobDescWrapper.classList.remove('is-invalid');
                if (jobDescError) jobDescError.textContent = '';
            }
        }

        if (reqContent) {
            const reqText = stripHtmlToText(reqContent.innerHTML);
            if (!reqText) {
                if (reqWrapper) reqWrapper.classList.add('is-invalid');
                if (reqError) {
                    reqError.textContent = 'Vui lòng nhập yêu cầu ứng viên (trình độ học vấn, kinh nghiệm, kỹ năng, chứng chỉ...) khi gửi duyệt.';
                    reqError.classList.remove('d-none');
                }
                valid = false;
            } else {
                if (reqWrapper) reqWrapper.classList.remove('is-invalid');
                if (reqError) reqError.textContent = '';
            }
        }

        return valid;
    }

    function hasAnyFormData() {
        if (positionSelect && positionSelect.value) return true;
        if (deptSelect && deptSelect.value) return true;
        if (minSalaryInput && minSalaryInput.value) return true;
        if (maxSalaryInput && maxSalaryInput.value) return true;
        const reasonDetail = form.querySelector('[name="ReasonDetail"]');
        if (reasonDetail && reasonDetail.value && reasonDetail.value.trim().length > 0) return true;
        if (jobDescContent && stripHtmlToText(jobDescContent.innerHTML).length > 0) return true;
        if (reqContent && stripHtmlToText(reqContent.innerHTML).length > 0) return true;
        return false;
    }

    // 6. Xử lý Tự động lưu ngầm (Auto-Save) định kỳ mỗi 30 giây
    async function triggerAutoSave() {
        if (!isDirty || isAutoSaving || !hasAnyFormData()) return;

        isAutoSaving = true;
        syncEditorsBeforeSubmit();

        if (autoSaveIndicator) {
            autoSaveIndicator.classList.remove('d-none');
            if (autoSaveSpinner) autoSaveSpinner.classList.remove('d-none');
            if (autoSaveIcon) autoSaveIcon.classList.add('d-none');
            if (autoSaveText) autoSaveText.textContent = 'Đang tự động lưu...';
        }

        try {
            const formData = new FormData(form);
            formData.set('IsDraft', 'true');

            const response = await fetch('/yeu-cau-tuyen-dung/api/auto-save', {
                method: 'POST',
                body: formData
            });

            if (response.ok) {
                const data = await response.json();
                if (data.success) {
                    if (data.requisitionId && reqIdInput) {
                        reqIdInput.value = data.requisitionId;
                    }
                    if (data.code && reqCodeInput) {
                        reqCodeInput.value = data.code;
                    }

                    // Cập nhật URL trình duyệt sang /yeu-cau-tuyen-dung/chinh-sua/{id} nếu đang ở trang tạo mới
                    const currentPath = window.location.pathname.toLowerCase();
                    if (data.requisitionId && (currentPath.includes('/tao-moi') || currentPath.includes('/create'))) {
                        const newUrl = `/yeu-cau-tuyen-dung/chinh-sua/${data.requisitionId}`;
                        window.history.replaceState(null, '', newUrl);
                    }

                    isDirty = false;

                    if (autoSaveIndicator) {
                        if (autoSaveSpinner) autoSaveSpinner.classList.add('d-none');
                        if (autoSaveIcon) {
                            autoSaveIcon.classList.remove('d-none', 'bi-exclamation-triangle', 'text-warning');
                            autoSaveIcon.classList.add('bi-cloud-check', 'text-success');
                        }
                        if (autoSaveText) {
                            autoSaveText.textContent = `Đã tự động lưu lúc ${data.savedAt || new Date().toLocaleTimeString('vi-VN')}`;
                        }
                    }
                }
            } else {
                if (autoSaveIndicator) {
                    if (autoSaveSpinner) autoSaveSpinner.classList.add('d-none');
                    if (autoSaveIcon) {
                        autoSaveIcon.classList.remove('d-none', 'bi-cloud-check', 'text-success');
                        autoSaveIcon.classList.add('bi-exclamation-triangle', 'text-warning');
                    }
                    if (autoSaveText) autoSaveText.textContent = 'Lưu tự động chưa thành công';
                }
            }
        } catch (err) {
            console.warn('[AutoSave] Lỗi kết nối tự động lưu:', err);
            if (autoSaveIndicator) {
                if (autoSaveSpinner) autoSaveSpinner.classList.add('d-none');
                if (autoSaveIcon) {
                    autoSaveIcon.classList.remove('d-none', 'bi-cloud-check', 'text-success');
                    autoSaveIcon.classList.add('bi-exclamation-triangle', 'text-warning');
                }
                if (autoSaveText) autoSaveText.textContent = 'Mất kết nối lưu tự động';
            }
        } finally {
            isAutoSaving = false;
        }
    }

    const autoSaveInterval = setInterval(triggerAutoSave, 30000);

    if (btnSaveDraft) {
        btnSaveDraft.addEventListener('click', function () {
            if (isDraftInput) isDraftInput.value = 'true';
            executeFormSubmit(true);
        });
    }

    if (btnSubmitRequisition) {
        btnSubmitRequisition.addEventListener('click', function () {
            if (isDraftInput) isDraftInput.value = 'false';
            executeFormSubmit(false);
        });
    }

    function executeFormSubmit(isDraft) {
        syncEditorsBeforeSubmit();

        if (isDraft) {
            // Khi lưu nháp: Cho phép lưu ở bất kỳ bước nào, không chặn khi form chưa điền đầy đủ
            form.querySelectorAll('.is-invalid').forEach(el => el.classList.remove('is-invalid'));
            if (salaryErrorMsg) salaryErrorMsg.classList.add('d-none');
            if (dateErrorMsg) dateErrorMsg.classList.add('d-none');
            if (jobDescError) jobDescError.textContent = '';
            if (reqError) reqError.textContent = '';

            // Đảm bảo số lượng có giá trị hợp lệ trước khi submit
            if (quantityInput && (!quantityInput.value || parseInt(quantityInput.value, 10) < 1)) {
                quantityInput.value = '1';
            }

            if (btnSaveDraft) btnSaveDraft.disabled = true;
            if (btnSubmitRequisition) btnSubmitRequisition.disabled = true;
            if (spinnerDraft) spinnerDraft.classList.remove('d-none');

            if (autoSaveInterval) clearInterval(autoSaveInterval);
            form.submit();
            return true;
        }

        // Khi gửi duyệt: Kiểm tra nghiêm ngặt đầy đủ tất cả các trường
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

        const editorsValid = validateEditors(false);
        if (!editorsValid) {
            isFormValid = false;
        }

        if (!isFormValid) {
            const firstInvalid = form.querySelector('.is-invalid');
            if (firstInvalid) {
                firstInvalid.scrollIntoView({ behavior: 'smooth', block: 'center' });
                const editorArea = firstInvalid.querySelector('.rich-editor-content');
                if (editorArea) {
                    editorArea.focus();
                } else {
                    firstInvalid.focus();
                }
            }
            return false;
        }

        if (btnSaveDraft) btnSaveDraft.disabled = true;
        if (btnSubmitRequisition) btnSubmitRequisition.disabled = true;
        if (spinnerSubmit) spinnerSubmit.classList.remove('d-none');

        if (autoSaveInterval) clearInterval(autoSaveInterval);
        form.submit();
        return true;
    }

    // Xử lý submit gốc dự phòng
    form.addEventListener('submit', function (e) {
        e.preventDefault();
        const isDraft = isDraftInput && isDraftInput.value === 'true';
        executeFormSubmit(isDraft);
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
