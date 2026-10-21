/**
 * offer-management.js
 * Quản lý Thư mời nhận việc (Offer), Xử lý từ chối, Thương lượng lại & Kho tiềm năng (User Story SP-7).
 */
window.OfferManager = (function () {
    let rejectionReasonsLoaded = false;

    function getCsrfToken() {
        const tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
        return tokenInput ? tokenInput.value : '';
    }

    function showFeedbackToast(message, isSuccess) {
        if (typeof showToast === 'function') {
            showToast(message, isSuccess);
        } else {
            alert(message);
        }
    }

    async function loadRejectionReasons() {
        if (rejectionReasonsLoaded) return;
        const select = document.getElementById('rejectOfferReasonCategory');
        if (!select) return;

        try {
            const res = await fetch('/offers/rejection-reasons');
            const data = await res.json();
            if (data.success && Array.isArray(data.data)) {
                select.innerHTML = '<option value="">-- Chọn danh mục lý do chuẩn --</option>';
                data.data.forEach(item => {
                    const opt = document.createElement('option');
                    opt.value = item.id;
                    opt.textContent = item.displayName;
                    opt.dataset.requiresSalary = item.requiresExpectedSalary ? 'true' : 'false';
                    select.appendChild(opt);
                });
                rejectionReasonsLoaded = true;
            }
        } catch (err) {
            console.error('Không thể nạp danh mục lý do từ chối:', err);
        }
    }

    return {
        init: function () {
            loadRejectionReasons();
        },

        openReject: function (offerId, candidateName, jobTitle, currentSalary, candidateId, applicationId, currentVersion) {
            const modalEl = document.getElementById('rejectOfferModal');
            if (!modalEl) return;

            loadRejectionReasons();

            document.getElementById('rejectOfferId').value = offerId || '';
            document.getElementById('rejectOfferCandidateId').value = candidateId || '';
            document.getElementById('rejectOfferApplicationId').value = applicationId || '';

            document.getElementById('rejectOfferCandidateName').textContent = candidateName || 'Ứng viên';
            document.getElementById('rejectOfferJobTitle').textContent = 'Vị trí: ' + (jobTitle || '-');
            document.getElementById('rejectOfferCurrentSalaryDisplay').textContent = currentSalary ? ('Mức lương offer hiện tại: ' + currentSalary) : '';
            document.getElementById('rejectOfferVersionBadge').textContent = 'v' + (currentVersion || 1);

            // Reset inputs
            const reasonSelect = document.getElementById('rejectOfferReasonCategory');
            if (reasonSelect) reasonSelect.value = '';

            const expectedSalaryInput = document.getElementById('rejectOfferExpectedSalary');
            if (expectedSalaryInput) expectedSalaryInput.value = '';

            const salaryGroup = document.getElementById('rejectOfferExpectedSalaryGroup');
            if (salaryGroup) salaryGroup.classList.add('d-none');

            const notes = document.getElementById('rejectOfferNotes');
            if (notes) notes.value = '';

            // Talent pool defaults
            const poolCheck = document.getElementById('rejectOfferAddToTalentPool');
            if (poolCheck) poolCheck.checked = true;

            const poolFields = document.getElementById('rejectOfferTalentPoolFields');
            if (poolFields) poolFields.classList.remove('d-none');

            const targetTitle = document.getElementById('rejectOfferTalentPoolTargetTitle');
            if (targetTitle) targetTitle.value = jobTitle || '';

            const poolReason = document.getElementById('rejectOfferTalentPoolReason');
            if (poolReason) poolReason.value = `Ứng viên tốt, từ chối offer v${currentVersion || 1}`;

            const poolNotes = document.getElementById('rejectOfferTalentPoolNotes');
            if (poolNotes) poolNotes.value = '';

            const modal = bootstrap.Modal.getOrCreateInstance(modalEl);
            modal.show();
        },

        onCategoryChange: function (select) {
            const salaryGroup = document.getElementById('rejectOfferExpectedSalaryGroup');
            const salaryInput = document.getElementById('rejectOfferExpectedSalary');
            if (!salaryGroup || !salaryInput) return;

            // Value 1 is Salary (Compensation)
            if (select.value === '1' || select.value === 'Salary') {
                salaryGroup.classList.remove('d-none');
                salaryInput.required = true;
                salaryInput.focus();
            } else {
                salaryGroup.classList.add('d-none');
                salaryInput.required = false;
            }
        },

        toggleTalentPoolExtra: function (checkbox) {
            const fields = document.getElementById('rejectOfferTalentPoolFields');
            if (fields) {
                if (checkbox.checked) {
                    fields.classList.remove('d-none');
                } else {
                    fields.classList.add('d-none');
                }
            }
        },

        submitReject: async function (e) {
            if (e && e.preventDefault) e.preventDefault();

            const btn = document.getElementById('btnSubmitRejectOffer');
            const spinner = document.getElementById('spinnerRejectOffer');
            const form = document.getElementById('formRejectOffer');
            if (!form) return;

            btn.disabled = true;
            if (spinner) spinner.classList.remove('d-none');

            const formData = new FormData(form);

            try {
                const res = await fetch('/offers/reject', {
                    method: 'POST',
                    headers: {
                        'X-Requested-With': 'XMLHttpRequest'
                    },
                    body: formData
                });

                const data = await res.json();
                if (data.success) {
                    const modalEl = document.getElementById('rejectOfferModal');
                    if (modalEl) bootstrap.Modal.getInstance(modalEl)?.hide();

                    showFeedbackToast(data.message || 'Đã ghi nhận ứng viên từ chối Offer thành công.', true);
                    setTimeout(() => window.location.reload(), 900);
                } else {
                    showFeedbackToast(data.message || 'Lỗi khi từ chối Thư mời nhận việc.', false);
                }
            } catch (err) {
                console.error('Lỗi khi từ chối offer:', err);
                showFeedbackToast('Lỗi kết nối máy chủ: ' + err.message, false);
            } finally {
                btn.disabled = false;
                if (spinner) spinner.classList.add('d-none');
            }
        },

        openRevise: function (offerId, candidateName, jobTitle, currentSalary, currentAllowance, joinDate, contractType, currentVersion, probationMonths) {
            const modalEl = document.getElementById('reviseOfferModal');
            if (!modalEl) return;

            const vOld = parseInt(currentVersion, 10) || 1;
            const vNew = vOld + 1;

            document.getElementById('reviseOriginalOfferId').value = offerId || '';
            document.getElementById('reviseCandidateName').textContent = candidateName || 'Ứng viên';
            document.getElementById('reviseJobTitle').textContent = 'Vị trí: ' + (jobTitle || '-');
            document.getElementById('reviseOldVersionBadge').textContent = 'v' + vOld;
            document.getElementById('reviseNewVersionBadge').textContent = 'v' + vNew;

            // Pre-fill fields
            const rawSalary = typeof currentSalary === 'number' ? currentSalary : parseFloat(String(currentSalary).replace(/[^\d.]/g, '')) || 0;
            const rawAllowance = typeof currentAllowance === 'number' ? currentAllowance : parseFloat(String(currentAllowance).replace(/[^\d.]/g, '')) || 0;

            const baseInput = document.getElementById('reviseBaseSalary');
            if (baseInput) baseInput.value = rawSalary > 0 ? rawSalary : '';

            const allowanceInput = document.getElementById('reviseBonusAllowance');
            if (allowanceInput) allowanceInput.value = rawAllowance;

            const joinInput = document.getElementById('reviseProposedJoinDate');
            if (joinInput) {
                if (joinDate && joinDate.length >= 10) {
                    joinInput.value = joinDate.substring(0, 10);
                } else {
                    const d = new Date();
                    d.setDate(d.getDate() + 14);
                    joinInput.value = d.toISOString().split('T')[0];
                }
            }

            const contractInput = document.getElementById('reviseContractType');
            if (contractInput && contractType) {
                contractInput.value = contractType;
            }

            const probInput = document.getElementById('reviseProbationPeriodMonths');
            if (probInput && typeof probationMonths !== 'undefined' && probationMonths !== null) {
                probInput.value = String(probationMonths);
            }

            const notes = document.getElementById('reviseNotes');
            if (notes) notes.value = '';

            OfferManager.calcReviseTotal();

            const modal = bootstrap.Modal.getOrCreateInstance(modalEl);
            modal.show();
        },

        calcReviseTotal: function () {
            const baseInput = document.getElementById('reviseBaseSalary');
            const allowanceInput = document.getElementById('reviseBonusAllowance');
            const display = document.getElementById('reviseTotalPackageDisplay');
            if (!display) return;

            const base = parseFloat(baseInput?.value) || 0;
            const allowance = parseFloat(allowanceInput?.value) || 0;
            const total = base + allowance;

            display.textContent = total.toLocaleString('vi-VN') + ' VNĐ';
        },

        submitRevise: async function (e) {
            if (e && e.preventDefault) e.preventDefault();

            const btn = document.getElementById('btnSubmitReviseOffer');
            const spinner = document.getElementById('spinnerReviseOffer');
            const form = document.getElementById('formReviseOffer');
            if (!form) return;

            const base = parseFloat(document.getElementById('reviseBaseSalary')?.value) || 0;
            if (base <= 0) {
                showFeedbackToast('Mức lương cơ bản thỏa thuận mới phải lớn hơn 0.', false);
                return;
            }

            btn.disabled = true;
            if (spinner) spinner.classList.remove('d-none');

            const formData = new FormData(form);

            try {
                const res = await fetch('/offers/revise', {
                    method: 'POST',
                    headers: {
                        'X-Requested-With': 'XMLHttpRequest'
                    },
                    body: formData
                });

                const data = await res.json();
                if (data.success) {
                    const modalEl = document.getElementById('reviseOfferModal');
                    if (modalEl) bootstrap.Modal.getInstance(modalEl)?.hide();

                    showFeedbackToast(data.message || 'Đã tạo Thư mời sửa đổi thành công!', true);
                    setTimeout(() => window.location.reload(), 900);
                } else {
                    showFeedbackToast(data.message || 'Lỗi khi tạo Thư mời sửa đổi.', false);
                }
            } catch (err) {
                console.error('Lỗi khi sửa đổi offer:', err);
                showFeedbackToast('Lỗi kết nối máy chủ: ' + err.message, false);
            } finally {
                btn.disabled = false;
                if (spinner) spinner.classList.add('d-none');
            }
        }
    };
})();

// Tự động khởi tạo khi tải xong DOM
document.addEventListener('DOMContentLoaded', function () {
    if (window.OfferManager) {
        window.OfferManager.init();
    }
});
