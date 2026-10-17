/**
 * NoveraTech ATS — CV AutoFill & Information Extraction Module (Scrum - Số 34)
 * Tuân thủ quy tắc:
 * 1. Bóc tách họ tên, email, SĐT từ CV PDF.
 * 2. Hiển thị để Recruiter xác nhận hoặc sửa, KHÔNG tự ý ghi đè nếu đã có dữ liệu.
 * 3. Bóc tách thất bại tuyệt đối không làm gián đoạn việc nộp hồ sơ.
 */
(function (window) {
    'use strict';

    function initCvAutoFill(config) {
        const fileInput = typeof config.fileInput === 'string' ? document.querySelector(config.fileInput) : config.fileInput;
        const nameInput = typeof config.nameInput === 'string' ? document.querySelector(config.nameInput) : config.nameInput;
        const emailInput = typeof config.emailInput === 'string' ? document.querySelector(config.emailInput) : config.emailInput;
        const phoneInput = typeof config.phoneInput === 'string' ? document.querySelector(config.phoneInput) : config.phoneInput;
        const linkedInInput = config.linkedInInput ? (typeof config.linkedInInput === 'string' ? document.querySelector(config.linkedInInput) : config.linkedInInput) : null;
        const container = config.container ? (typeof config.container === 'string' ? document.querySelector(config.container) : config.container) : (fileInput ? fileInput.closest('.mb-3') || fileInput.parentElement : null);

        if (!fileInput || !container) {
            console.warn('[CvAutoFill] Không tìm thấy input file hoặc container phù hợp.');
            return;
        }

        // Tạo container riêng cho thông báo và preview
        let feedbackContainer = container.querySelector('.cv-autofill-feedback');
        if (!feedbackContainer) {
            feedbackContainer = document.createElement('div');
            feedbackContainer.className = 'cv-autofill-feedback mt-2.5';
            container.appendChild(feedbackContainer);
        }

        fileInput.addEventListener('change', async function () {
            feedbackContainer.innerHTML = '';

            const files = fileInput.files;
            if (!files || files.length === 0) return;

            const file = files[0];
            const fileName = file.name.toLowerCase();

            // Chỉ xử lý file PDF
            if (!fileName.endsWith('.pdf')) {
                return;
            }

            // Hiển thị trạng thái đang xử lý nhẹ nhàng
            feedbackContainer.innerHTML = `
                <div class="d-flex align-items-center gap-2 p-2.5 rounded-2 bg-light border text-muted small" style="border-radius: 8px;">
                    <div class="spinner-border spinner-border-sm text-success" role="status" style="width: 1rem; height: 1rem;"></div>
                    <span>Đang tự động bóc tách thông tin ứng viên từ CV (PDF)...</span>
                </div>
            `;

            try {
                const formData = new FormData();
                formData.append('file', file);

                const response = await fetch('/api/cv/parse', {
                    method: 'POST',
                    body: formData
                });

                if (!response.ok) {
                    throw new Error('Không thể kết nối dịch vụ bóc tách.');
                }

                const result = await response.json();

                if (!result.success || !result.data || !result.data.hasDetectedData) {
                    // Graceful Degradation: Báo nhẹ nhàng, không chặn form
                    feedbackContainer.innerHTML = `
                        <div class="alert alert-warning py-2 px-3 small d-flex align-items-center justify-content-between mb-0" style="border-radius: 8px;" role="alert">
                            <div class="d-flex align-items-center gap-2">
                                <i class="bi bi-info-circle-fill text-warning"></i>
                                <span>${result.message || 'Không phát hiện đủ thông tin từ CV (ảnh scan/bảo mật). Bạn vui lòng nhập thông tin thủ công.'}</span>
                            </div>
                            <button type="button" class="btn-close" style="font-size: 0.65rem;" onclick="this.closest('.alert').remove()"></button>
                        </div>
                    `;
                    return;
                }

                // Có dữ liệu -> Hiển thị Card Xác Nhận (Không tự ghi đè)
                renderConfirmationCard(result.data, {
                    nameInput,
                    emailInput,
                    phoneInput,
                    linkedInInput,
                    feedbackContainer
                });

            } catch (err) {
                console.warn('[CvAutoFill] Bóc tách CV không thành công:', err);
                // Bóc tách thất bại không làm hỏng việc nộp hồ sơ
                feedbackContainer.innerHTML = `
                    <div class="alert alert-light border py-2 px-3 small d-flex align-items-center justify-content-between mb-0 text-muted" style="border-radius: 8px;" role="alert">
                        <div class="d-flex align-items-center gap-2">
                            <i class="bi bi-exclamation-circle text-secondary"></i>
                            <span>Không thể tự động đọc CV này. Vui lòng điền thông tin thủ công vào các ô bên dưới.</span>
                        </div>
                        <button type="button" class="btn-close" style="font-size: 0.65rem;" onclick="this.closest('.alert').remove()"></button>
                    </div>
                `;
            }
        });
    }

    function renderConfirmationCard(data, elements) {
        const { nameInput, emailInput, phoneInput, linkedInInput, feedbackContainer } = elements;

        const hasExistingName = nameInput && nameInput.value.trim().length > 0;
        const hasExistingEmail = emailInput && emailInput.value.trim().length > 0;
        const hasExistingPhone = phoneInput && phoneInput.value.trim().length > 0;

        let overwriteWarnings = [];
        if (hasExistingName && data.fullName) overwriteWarnings.push(`Họ tên hiện tại: "${nameInput.value.trim()}"`);
        if (hasExistingEmail && data.email) overwriteWarnings.push(`Email hiện tại: "${emailInput.value.trim()}"`);
        if (hasExistingPhone && data.phoneNumber) overwriteWarnings.push(`SĐT hiện tại: "${phoneInput.value.trim()}"`);

        const warningHtml = overwriteWarnings.length > 0 ? `
            <div class="text-warning small mb-2 d-flex align-items-center gap-1.5" style="font-size: 0.76rem;">
                <i class="bi bi-exclamation-triangle-fill"></i>
                <span>Chú ý: Một số ô đã có dữ liệu. Bấm "Áp dụng" sẽ thay thế bằng thông tin trích xuất từ CV.</span>
            </div>
        ` : '';

        feedbackContainer.innerHTML = `
            <div class="card border-0 shadow-sm p-3 mt-2" style="background: #F0FDF4; border: 1px solid #BBF7D0 !important; border-radius: 10px;">
                <div class="d-flex align-items-center justify-content-between mb-2">
                    <div class="d-flex align-items-center gap-1.5 text-success fw-bold small">
                        <i class="bi bi-check2-circle fs-6"></i>
                        <span>Thông tin trích xuất từ CV (Scrum-34)</span>
                    </div>
                    <span class="badge bg-white text-success border border-success-subtle" style="font-size: 0.7rem; font-weight: 600;">
                        Chờ bạn xác nhận
                    </span>
                </div>

                <div class="row g-2 mb-2">
                    ${data.fullName ? `
                    <div class="col-12 col-sm-4">
                        <div class="p-2 bg-white rounded border small" style="border-radius: 6px !important;">
                            <span class="text-muted text-xs d-block">Họ và tên:</span>
                            <span class="fw-semibold text-dark text-truncate d-block" id="extractedFullName">${escapeHtml(data.fullName)}</span>
                        </div>
                    </div>` : ''}

                    ${data.email ? `
                    <div class="col-12 col-sm-4">
                        <div class="p-2 bg-white rounded border small" style="border-radius: 6px !important;">
                            <span class="text-muted text-xs d-block">Email liên hệ:</span>
                            <span class="fw-semibold text-dark text-truncate d-block font-mono" id="extractedEmail">${escapeHtml(data.email)}</span>
                        </div>
                    </div>` : ''}

                    ${data.phoneNumber ? `
                    <div class="col-12 col-sm-4">
                        <div class="p-2 bg-white rounded border small" style="border-radius: 6px !important;">
                            <span class="text-muted text-xs d-block">Số điện thoại:</span>
                            <span class="fw-semibold text-dark text-truncate d-block font-mono" id="extractedPhone">${escapeHtml(data.phoneNumber)}</span>
                        </div>
                    </div>` : ''}
                </div>

                ${warningHtml}

                <div class="d-flex align-items-center justify-content-end gap-2 pt-1 border-top" style="border-color: #DCFCE7 !important;">
                    <button type="button" class="btn btn-sm btn-light border py-1 px-2.5 text-muted" id="btnDismissCvAutofill" style="border-radius: 6px; font-size: 0.78rem;">
                        <i class="bi bi-x me-1"></i>Bỏ qua (Giữ nguyên form)
                    </button>
                    <button type="button" class="btn btn-sm btn-success py-1 px-3 fw-semibold text-white d-inline-flex align-items-center gap-1" id="btnApplyCvAutofill" style="border-radius: 6px; font-size: 0.78rem; background: #059669; border-color: #059669;">
                        <i class="bi bi-check-lg"></i>Xác nhận &amp; Điền vào form
                    </button>
                </div>
            </div>
        `;

        // Bắt sự kiện xác nhận điền
        const btnApply = feedbackContainer.querySelector('#btnApplyCvAutofill');
        if (btnApply) {
            btnApply.addEventListener('click', function () {
                if (data.fullName && nameInput) {
                    nameInput.value = data.fullName;
                    highlightInput(nameInput);
                }
                if (data.email && emailInput) {
                    emailInput.value = data.email;
                    highlightInput(emailInput);
                }
                if (data.phoneNumber && phoneInput) {
                    phoneInput.value = data.phoneNumber;
                    highlightInput(phoneInput);
                }
                if (data.linkedInUrl && linkedInInput && !linkedInInput.value.trim()) {
                    linkedInInput.value = data.linkedInUrl;
                    highlightInput(linkedInInput);
                }

                // Hiển thị thông báo hoàn tất nhẹ nhàng
                feedbackContainer.innerHTML = `
                    <div class="text-success small py-1.5 px-2.5 bg-success-subtle rounded d-flex align-items-center gap-1.5" style="border-radius: 6px; font-size: 0.8rem;">
                        <i class="bi bi-check-circle-fill"></i>
                        <span>Đã điền thông tin từ CV vào form. Bạn có thể kiểm tra và chỉnh sửa lại nếu cần.</span>
                    </div>
                `;
                setTimeout(() => {
                    const msg = feedbackContainer.querySelector('.text-success');
                    if (msg) msg.remove();
                }, 4000);
            });
        }

        // Bắt sự kiện bỏ qua
        const btnDismiss = feedbackContainer.querySelector('#btnDismissCvAutofill');
        if (btnDismiss) {
            btnDismiss.addEventListener('click', function () {
                feedbackContainer.innerHTML = '';
            });
        }
    }

    function highlightInput(inputEl) {
        inputEl.dispatchEvent(new Event('input', { bubbles: true }));
        inputEl.dispatchEvent(new Event('change', { bubbles: true }));
        inputEl.style.transition = 'box-shadow 0.3s ease, border-color 0.3s ease';
        inputEl.style.boxShadow = '0 0 0 3px rgba(5, 150, 105, 0.25)';
        inputEl.style.borderColor = '#059669';
        setTimeout(() => {
            inputEl.style.boxShadow = '';
            inputEl.style.borderColor = '';
        }, 1500);
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
        return text.replace(/[&<>"']/g, m => map[m]);
    }

    window.CvAutoFill = {
        init: initCvAutoFill
    };

})(window);
