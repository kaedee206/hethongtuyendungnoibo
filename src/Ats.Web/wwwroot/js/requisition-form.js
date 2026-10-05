/**
 * Quản lý tương tác và kiểm thực dữ liệu cho Form Khai báo Yêu cầu Tuyển dụng (EP-03_Requisition).
 * Hỗ trợ Multi-Step Form (3 bước: Thông tin cơ bản -> Mô tả & Tiêu chuẩn -> Xem lại & Gửi duyệt).
 * Tuân thủ RULE.md & Coding_Convention.md:
 * - Bảo toàn dữ liệu 100% khi chuyển qua lại giữa các bước.
 * - Cho phép lưu nháp ở bất kỳ bước nào.
 * - Tự động lưu ngầm định kỳ 30 giây (Auto-Save).
 * - Render bản xem lại (Review summary) trực quan trước khi gửi duyệt.
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

    const isDraftInput = form.querySelector('#reqIsDraft');
    const reqIdInput = form.querySelector('#reqId');
    const reqCodeInput = form.querySelector('#reqCode');

    // Các phần tử Rich Text Editor
    const jobDescWrapper = form.querySelector('#jobDescEditorWrapper');
    const jobDescContent = form.querySelector('#jobDescEditorContent');
    const jobDescHidden = form.querySelector('#reqJobDescription');
    const jobDescError = form.querySelector('#jobDescError');

    const reqWrapper = form.querySelector('#requirementsEditorWrapper');
    const reqContent = form.querySelector('#requirementsEditorContent');
    const reqHidden = form.querySelector('#reqRequirements');
    const reqError = form.querySelector('#requirementsError');

    // Các phần tử Wizard Multi-Step
    let currentStep = 1;
    const stepItems = form.querySelectorAll('.wizard-step-item');
    const stepPanes = form.querySelectorAll('.wizard-step-pane');
    const nextButtons = form.querySelectorAll('.btn-step-next');
    const prevButtons = form.querySelectorAll('.btn-step-prev');
    const jumpButtons = form.querySelectorAll('.btn-jump-step');
    const draftButtons = form.querySelectorAll('.btn-save-draft');
    const submitButton = form.querySelector('#btnSubmitRequisition');

    // Các phần tử Review ở Bước 3
    const reviewPositionTitle = form.querySelector('#reviewPositionTitle');
    const reviewDepartmentName = form.querySelector('#reviewDepartmentName');
    const reviewQuantity = form.querySelector('#reviewQuantity');
    const reviewHireDate = form.querySelector('#reviewHireDate');
    const reviewSalaryRange = form.querySelector('#reviewSalaryRange');
    const reviewHeadcountType = form.querySelector('#reviewHeadcountType');
    const reviewReasonDetail = form.querySelector('#reviewReasonDetail');
    const reviewJobDescContent = form.querySelector('#reviewJobDescContent');
    const reviewRequirementsContent = form.querySelector('#reviewRequirementsContent');

    // Thiết lập ngày tối thiểu cho DatePicker là ngày hôm nay
    if (dateInput) {
        const todayStr = new Date().toISOString().split('T')[0];
        dateInput.min = todayStr;
        if (!dateInput.value) {
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

    // 5. Quản lý Rich Text Editor
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
            if (jobDescWrapper) jobDescWrapper.classList.remove('is-invalid');
            if (jobDescError) jobDescError.textContent = '';
            if (reqWrapper) reqWrapper.classList.remove('is-invalid');
            if (reqError) reqError.textContent = '';
            return true;
        }

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

    // 6. Điều hướng Multi-Step Form (Wizard Navigation)
    function goToStep(targetStep) {
        if (targetStep < 1 || targetStep > 3) return;

        // Đồng bộ dữ liệu Rich Text trước khi chuyển bước
        syncEditorsBeforeSubmit();

        // Nếu chuyển sang Bước 3: Đổ dữ liệu vào bảng xem lại (Review)
        if (targetStep === 3) {
            populateReviewSummary();
        }

        // Cập nhật ẩn/hiện các step pane
        stepPanes.forEach(function (pane) {
            const paneStep = parseInt(pane.getAttribute('data-step'), 10);
            if (paneStep === targetStep) {
                pane.classList.add('is-visible');
            } else {
                pane.classList.remove('is-visible');
            }
        });

        // Cập nhật trạng thái Step Nav Header
        stepItems.forEach(function (item) {
            const itemStep = parseInt(item.getAttribute('data-step'), 10);
            const numSpan = item.querySelector('.step-num');
            const checkIcon = item.querySelector('.step-check');

            if (itemStep < targetStep) {
                item.classList.add('is-completed');
                item.classList.remove('is-active', 'is-pending');
                if (numSpan) numSpan.classList.add('d-none');
                if (checkIcon) checkIcon.classList.remove('d-none');
            } else if (itemStep === targetStep) {
                item.classList.add('is-active');
                item.classList.remove('is-completed', 'is-pending');
                if (numSpan) numSpan.classList.remove('d-none');
                if (checkIcon) checkIcon.classList.add('d-none');
            } else {
                item.classList.add('is-pending');
                item.classList.remove('is-active', 'is-completed');
                if (numSpan) numSpan.classList.remove('d-none');
                if (checkIcon) checkIcon.classList.add('d-none');
            }
        });

        // Cập nhật đường connector
        const conn1 = form.querySelector('#connector1');
        const conn2 = form.querySelector('#connector2');
        if (conn1) {
            if (targetStep >= 2) conn1.classList.add('is-completed');
            else conn1.classList.remove('is-completed');
        }
        if (conn2) {
            if (targetStep >= 3) conn2.classList.add('is-completed');
            else conn2.classList.remove('is-completed');
        }

        currentStep = targetStep;

        // Cuộn mượt lên đầu card wizard để người dùng theo dõi
        const wizardHeader = form.querySelector('.requisition-wizard-header');
        if (wizardHeader) {
            wizardHeader.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
        }
    }

    // Đổ dữ liệu vào màn hình xem lại (Bước 3)
    function populateReviewSummary() {
        // Chức danh
        if (reviewPositionTitle) {
            if (positionSelect && positionSelect.selectedIndex > 0) {
                reviewPositionTitle.textContent = positionSelect.options[positionSelect.selectedIndex].text;
                reviewPositionTitle.classList.remove('text-muted', 'fst-italic');
            } else {
                reviewPositionTitle.textContent = '(Chưa chọn chức danh)';
                reviewPositionTitle.classList.add('text-muted', 'fst-italic');
            }
        }

        // Phòng ban
        if (reviewDepartmentName) {
            if (deptSelect && deptSelect.selectedIndex > 0) {
                reviewDepartmentName.textContent = deptSelect.options[deptSelect.selectedIndex].text;
                reviewDepartmentName.classList.remove('text-muted', 'fst-italic');
            } else {
                reviewDepartmentName.textContent = '(Chưa chọn phòng ban)';
                reviewDepartmentName.classList.add('text-muted', 'fst-italic');
            }
        }

        // Số lượng
        if (reviewQuantity) {
            const qty = quantityInput && quantityInput.value ? quantityInput.value : '1';
            reviewQuantity.textContent = `${qty} người`;
        }

        // Ngày cần người
        if (reviewHireDate) {
            if (dateInput && dateInput.value) {
                const parts = dateInput.value.split('-');
                if (parts.length === 3) {
                    reviewHireDate.textContent = `${parts[2]}/${parts[1]}/${parts[0]}`;
                } else {
                    reviewHireDate.textContent = dateInput.value;
                }
                reviewHireDate.classList.remove('text-muted', 'fst-italic');
            } else {
                reviewHireDate.textContent = '(Chưa chọn ngày)';
                reviewHireDate.classList.add('text-muted', 'fst-italic');
            }
        }

        // Dải lương
        if (reviewSalaryRange) {
            const minVal = minSalaryInput ? parseFloat(minSalaryInput.value) : 0;
            const maxVal = maxSalaryInput ? parseFloat(maxSalaryInput.value) : 0;
            if (!isNaN(minVal) && !isNaN(maxVal) && minVal > 0 && maxVal > 0) {
                reviewSalaryRange.textContent = `${formatVndCurrency(minVal)} – ${formatVndCurrency(maxVal)}`;
                reviewSalaryRange.classList.remove('text-muted', 'fst-italic');
            } else {
                reviewSalaryRange.textContent = '(Chưa nhập đầy đủ dải lương)';
                reviewSalaryRange.classList.add('text-muted', 'fst-italic');
            }
        }

        // Loại headcount
        if (reviewHeadcountType) {
            const checkedRadio = form.querySelector('input[name="HeadcountType"]:checked');
            if (checkedRadio && checkedRadio.value === 'REPLACEMENT') {
                reviewHeadcountType.innerHTML = '<span class="badge bg-warning-subtle text-warning-emphasis border border-warning-subtle font-monospace"><i class="bi bi-arrow-repeat me-1"></i>Thay thế nhân sự nghỉ việc</span>';
            } else {
                reviewHeadcountType.innerHTML = '<span class="badge bg-success-subtle text-success border border-success-subtle font-monospace"><i class="bi bi-person-plus me-1"></i>Tăng mới headcount</span>';
            }
        }

        // Ghi chú lý do
        if (reviewReasonDetail) {
            const reasonInput = form.querySelector('[name="ReasonDetail"]');
            const reasonText = reasonInput ? reasonInput.value.trim() : '';
            if (reasonText) {
                reviewReasonDetail.textContent = reasonText;
                reviewReasonDetail.classList.remove('fst-italic', 'text-secondary');
            } else {
                reviewReasonDetail.textContent = '(Không có ghi chú mục tiêu bổ sung)';
                reviewReasonDetail.classList.add('fst-italic', 'text-secondary');
            }
        }

        // Mô tả công việc HTML
        if (reviewJobDescContent) {
            const descText = stripHtmlToText(jobDescContent ? jobDescContent.innerHTML : '');
            if (descText && jobDescContent) {
                reviewJobDescContent.innerHTML = jobDescContent.innerHTML;
            } else {
                reviewJobDescContent.innerHTML = '<span class="text-muted fst-italic">(Chưa có nội dung mô tả công việc)</span>';
            }
        }

        // Yêu cầu ứng viên HTML
        if (reviewRequirementsContent) {
            const reqText = stripHtmlToText(reqContent ? reqContent.innerHTML : '');
            if (reqText && reqContent) {
                reviewRequirementsContent.innerHTML = reqContent.innerHTML;
            } else {
                reviewRequirementsContent.innerHTML = '<span class="text-muted fst-italic">(Chưa có nội dung tiêu chuẩn ứng viên)</span>';
            }
        }
    }

    // Gán sự kiện cho các nút chuyển bước
    nextButtons.forEach(function (btn) {
        btn.addEventListener('click', function () {
            const target = parseInt(this.getAttribute('data-next-step'), 10);
            goToStep(target);
        });
    });

    prevButtons.forEach(function (btn) {
        btn.addEventListener('click', function () {
            const target = parseInt(this.getAttribute('data-prev-step'), 10);
            goToStep(target);
        });
    });

    jumpButtons.forEach(function (btn) {
        btn.addEventListener('click', function () {
            const target = parseInt(this.getAttribute('data-target-step'), 10);
            goToStep(target);
        });
    });

    // Cho phép click trực tiếp vào Step Header để chuyển bước
    stepItems.forEach(function (item) {
        item.addEventListener('click', function () {
            const target = parseInt(this.getAttribute('data-step'), 10);
            goToStep(target);
        });
    });

    // 7. Auto-Save ngầm định kỳ mỗi 30 giây
    let isDirty = false;
    let isAutoSaving = false;

    function markDirty() {
        isDirty = true;
    }

    form.addEventListener('input', markDirty);
    form.addEventListener('change', markDirty);
    if (jobDescContent) jobDescContent.addEventListener('input', markDirty);
    if (reqContent) reqContent.addEventListener('input', markDirty);

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

    function updateAllAutoSaveIndicators(state, text) {
        const indicators = form.querySelectorAll('.auto-save-indicator');
        indicators.forEach(function (indicator) {
            indicator.classList.remove('d-none');
            const spinner = indicator.querySelector('.auto-save-spinner');
            const icon = indicator.querySelector('.auto-save-icon');
            const textEl = indicator.querySelector('.auto-save-text');

            if (state === 'saving') {
                if (spinner) spinner.classList.remove('d-none');
                if (icon) icon.classList.add('d-none');
                if (textEl) textEl.textContent = 'Đang tự động lưu...';
            } else if (state === 'saved') {
                if (spinner) spinner.classList.add('d-none');
                if (icon) {
                    icon.classList.remove('d-none', 'bi-exclamation-triangle', 'text-warning');
                    icon.classList.add('bi-cloud-check', 'text-success');
                }
                if (textEl) textEl.textContent = text || 'Đã lưu tự động';
            } else if (state === 'error') {
                if (spinner) spinner.classList.add('d-none');
                if (icon) {
                    icon.classList.remove('d-none', 'bi-cloud-check', 'text-success');
                    icon.classList.add('bi-exclamation-triangle', 'text-warning');
                }
                if (textEl) textEl.textContent = text || 'Lưu tự động chưa thành công';
            }
        });
    }

    async function triggerAutoSave() {
        if (!isDirty || isAutoSaving || !hasAnyFormData()) return;

        isAutoSaving = true;
        syncEditorsBeforeSubmit();
        updateAllAutoSaveIndicators('saving');

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

                    // Cập nhật URL sang /yeu-cau-tuyen-dung/chinh-sua/{id} nếu đang ở trang tạo mới
                    const currentPath = window.location.pathname.toLowerCase();
                    if (data.requisitionId && (currentPath.includes('/tao-moi') || currentPath.includes('/create'))) {
                        const newUrl = `/yeu-cau-tuyen-dung/chinh-sua/${data.requisitionId}`;
                        window.history.replaceState(null, '', newUrl);
                    }

                    isDirty = false;
                    const savedTime = data.savedAt || new Date().toLocaleTimeString('vi-VN');
                    updateAllAutoSaveIndicators('saved', `Đã tự động lưu lúc ${savedTime}`);
                }
            } else {
                updateAllAutoSaveIndicators('error', 'Lưu tự động chưa thành công');
            }
        } catch (err) {
            console.warn('[AutoSave] Lỗi kết nối tự động lưu:', err);
            updateAllAutoSaveIndicators('error', 'Mất kết nối lưu tự động');
        } finally {
            isAutoSaving = false;
        }
    }

    const autoSaveInterval = setInterval(triggerAutoSave, 30000);

    // 8. Xử lý Submit Form (Lưu nháp hoặc Gửi đề xuất tuyển dụng)
    draftButtons.forEach(function (btn) {
        btn.addEventListener('click', function () {
            if (isDraftInput) isDraftInput.value = 'true';
            executeFormSubmit(true);
        });
    });

    if (submitButton) {
        submitButton.addEventListener('click', function () {
            if (isDraftInput) isDraftInput.value = 'false';
            executeFormSubmit(false);
        });
    }

    function executeFormSubmit(isDraft) {
        syncEditorsBeforeSubmit();

        if (isDraft) {
            // Khi lưu nháp: Cho phép lưu ở bất kỳ bước nào, không chặn form khi dữ liệu chưa đủ
            form.querySelectorAll('.is-invalid').forEach(el => el.classList.remove('is-invalid'));
            if (salaryErrorMsg) salaryErrorMsg.classList.add('d-none');
            if (dateErrorMsg) dateErrorMsg.classList.add('d-none');
            if (jobDescError) jobDescError.textContent = '';
            if (reqError) reqError.textContent = '';

            // Đảm bảo số lượng có giá trị hợp lệ trước khi submit
            if (quantityInput && (!quantityInput.value || parseInt(quantityInput.value, 10) < 1)) {
                quantityInput.value = '1';
            }

            // Vô hiệu hóa nút và bật spinner
            draftButtons.forEach(b => {
                b.disabled = true;
                const sp = b.querySelector('.btn-spinner-draft');
                if (sp) sp.classList.remove('d-none');
            });
            if (submitButton) submitButton.disabled = true;

            if (autoSaveInterval) clearInterval(autoSaveInterval);
            form.submit();
            return true;
        }

        // Khi gửi duyệt: Kiểm tra nghiêm ngặt đầy đủ tất cả các trường
        let isStep1Valid = true;
        let isStep2Valid = true;

        // Kiểm tra các trường Bước 1
        if (positionSelect && (!positionSelect.value || positionSelect.value === '')) {
            positionSelect.classList.add('is-invalid');
            isStep1Valid = false;
        } else if (positionSelect) {
            positionSelect.classList.remove('is-invalid');
        }

        if (deptSelect && (!deptSelect.value || deptSelect.value === '')) {
            deptSelect.classList.add('is-invalid');
            isStep1Valid = false;
        } else if (deptSelect) {
            deptSelect.classList.remove('is-invalid');
        }

        if (!validateSalaryRange()) {
            isStep1Valid = false;
        }

        if (!validateDate()) {
            isStep1Valid = false;
        }

        if (quantityInput) {
            const qty = parseInt(quantityInput.value, 10);
            if (isNaN(qty) || qty < 1) {
                quantityInput.classList.add('is-invalid');
                isStep1Valid = false;
            }
        }

        // Kiểm tra các trường Bước 2
        const editorsValid = validateEditors(false);
        if (!editorsValid) {
            isStep2Valid = false;
        }

        // Nếu Bước 1 có lỗi, tự động chuyển về Bước 1 và focus
        if (!isStep1Valid) {
            goToStep(1);
            const firstInvalid = form.querySelector('#wizardStepPane1 .is-invalid');
            if (firstInvalid) {
                firstInvalid.scrollIntoView({ behavior: 'smooth', block: 'center' });
                firstInvalid.focus();
            }
            return false;
        }

        // Nếu Bước 2 có lỗi, tự động chuyển về Bước 2 và focus
        if (!isStep2Valid) {
            goToStep(2);
            const firstInvalid = form.querySelector('#wizardStepPane2 .is-invalid');
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

        // Tất cả hợp lệ -> Submit form
        draftButtons.forEach(b => b.disabled = true);
        if (submitButton) {
            submitButton.disabled = true;
            const spinner = submitButton.querySelector('.btn-spinner');
            if (spinner) spinner.classList.remove('d-none');
        }

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
