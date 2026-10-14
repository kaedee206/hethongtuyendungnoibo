/**
 * ATS Candidate Stage Transition & Rejection Manager (SCRUM-42)
 * Quản lý chuyển giai đoạn ứng viên, ghi nhận lý do loại và rào chắn kiểm soát duyệt đặc cách Offer.
 */
const CandidateStageManager = (function () {
    let cachedRejectionReasons = null;

    /**
     * Lấy token Anti-Forgery từ form hoặc container toàn cục
     */
    function getAntiForgeryToken(form) {
        if (form) {
            const tokenInput = form.querySelector('input[name="__RequestVerificationToken"]');
            if (tokenInput && tokenInput.value) {
                return tokenInput.value;
            }
        }
        const globalTokenInput = document.querySelector('#globalAntiforgeryContainer input[name="__RequestVerificationToken"]');
        return globalTokenInput ? globalTokenInput.value : '';
    }

    /**
     * Map mã giai đoạn sang class màu CSS và tên hiển thị
     */
    function getStageBadgeInfo(stage) {
        const stageNum = typeof stage === 'number' ? stage : parseInt(stage, 10);
        switch (stageNum) {
            case 1:
                return { name: 'Ứng tuyển mới', dotClass: 'timeline-dot-applied', badgeClass: 'bg-primary' };
            case 2:
                return { name: 'Sàng lọc hồ sơ', dotClass: 'timeline-dot-screening', badgeClass: 'bg-secondary' };
            case 3:
                return { name: 'Phỏng vấn', dotClass: 'timeline-dot-interview', badgeClass: 'bg-info text-dark' };
            case 4:
                return { name: 'Đề xuất tuyển dụng (Offer)', dotClass: 'timeline-dot-offer', badgeClass: 'bg-warning text-dark' };
            case 5:
                return { name: 'Tuyển dụng thành công', dotClass: 'timeline-dot-hired', badgeClass: 'bg-success' };
            case 6:
                return { name: 'Loại hồ sơ', dotClass: 'timeline-dot-rejected', badgeClass: 'bg-danger' };
            default:
                return { name: stage || 'Chưa xác định', dotClass: 'timeline-dot-screening', badgeClass: 'bg-secondary' };
        }
    }

    /**
     * Định dạng thời gian theo múi giờ Việt Nam vi-VN (GMT+7)
     */
    function formatVietnamDateTime(isoString) {
        if (!isoString) return '---';
        try {
            const d = new Date(isoString);
            return new Intl.DateTimeFormat('vi-VN', {
                year: 'numeric',
                month: '2-digit',
                day: '2-digit',
                hour: '2-digit',
                minute: '2-digit',
                second: '2-digit',
                hour12: false
            }).format(d);
        } catch {
            return isoString;
        }
    }

    /**
     * Tải danh mục lý do loại hồ sơ từ API Controller
     */
    async function loadRejectionReasons() {
        const selectEl = document.getElementById('stageRejectReasonSelect');
        const loadingEl = document.getElementById('stageRejectReasonLoading');
        if (!selectEl) return;

        if (cachedRejectionReasons && cachedRejectionReasons.length > 0) {
            renderRejectionReasons(cachedRejectionReasons);
            return;
        }

        if (loadingEl) loadingEl.classList.remove('d-none');

        try {
            const res = await fetch('/candidates/rejection-reasons', {
                headers: { 'X-Requested-With': 'XMLHttpRequest' }
            });

            if (res.ok) {
                const data = await res.json();
                if (data.success && Array.isArray(data.data) && data.data.length > 0) {
                    cachedRejectionReasons = data.data;
                    renderRejectionReasons(data.data);
                    return;
                }
            }
        } catch (err) {
            console.warn('Lỗi khi nạp danh mục lý do loại từ CandidatesController, thử fallback:', err);
        } finally {
            if (loadingEl) loadingEl.classList.add('d-none');
        }

        // Fallback sang catalog dùng chung nếu có
        try {
            const resFallback = await fetch('/recruitment-catalogs/api/by-type?type=REJECTION_REASON');
            if (resFallback.ok) {
                const items = await resFallback.json();
                if (Array.isArray(items) && items.length > 0) {
                    cachedRejectionReasons = items;
                    renderRejectionReasons(items);
                    return;
                }
            }
        } catch (errFallback) {
            console.error('Không thể nạp danh mục lý do loại hồ sơ:', errFallback);
        }

        // Fallback mặc định an toàn
        selectEl.innerHTML = '<option value="">-- Vui lòng chọn lý do loại (* Bắt buộc) --</option>' +
            '<option value="11111111-1111-1111-1111-111111111111">Chuyên môn chưa đạt yêu cầu</option>' +
            '<option value="22222222-2222-2222-2222-222222222222">Chưa đủ số năm kinh nghiệm</option>' +
            '<option value="33333333-3333-3333-3333-333333333333">Không thỏa thuận được mức lương</option>' +
            '<option value="44444444-4444-4444-4444-444444444444">Chưa phù hợp văn hóa doanh nghiệp</option>' +
            '<option value="55555555-5555-5555-5555-555555555555">Ứng viên chủ động rút lui</option>';
    }

    function renderRejectionReasons(reasons) {
        const selectEl = document.getElementById('stageRejectReasonSelect');
        if (!selectEl) return;

        let html = '<option value="">-- Chọn lý do loại hồ sơ (* Bắt buộc) --</option>';
        reasons.forEach(r => {
            const desc = r.description ? ` (${r.description})` : '';
            html += `<option value="${r.id}">${r.name}${desc}</option>`;
        });
        selectEl.innerHTML = html;
    }

    /**
     * Mở Modal loại ứng viên (REJECT)
     */
    function openRejectModal(applicationId, candidateName, jobTitle) {
        document.getElementById('stageRejectAppId').value = applicationId || '';
        document.getElementById('stageRejectCandidateName').textContent = candidateName || 'Ứng viên';
        document.getElementById('stageRejectJobTitle').textContent = jobTitle || 'Chưa có thông tin vị trí';

        const noteEl = document.getElementById('stageRejectNote');
        if (noteEl) noteEl.value = '';
        updateRejectCharCount(noteEl);

        const errorAlert = document.getElementById('stageRejectErrorAlert');
        if (errorAlert) errorAlert.classList.add('d-none');

        const btn = document.getElementById('btnConfirmRejectCandidate');
        if (btn) btn.disabled = false;
        const spinner = document.getElementById('spinnerRejectCandidate');
        if (spinner) spinner.classList.add('d-none');

        loadRejectionReasons();

        const modalEl = document.getElementById('candidateRejectModal');
        if (modalEl) {
            const modal = bootstrap.Modal.getOrCreateInstance(modalEl);
            modal.show();
        }
    }

    /**
     * Cập nhật số ký tự ghi chú lý do loại
     */
    function updateRejectCharCount(el) {
        const counter = document.getElementById('stageRejectCharCounter');
        if (counter && el) {
            counter.textContent = `${el.value.length}/${el.maxLength || 500} ký tự`;
        }
    }

    /**
     * Xử lý gửi Form Loại ứng viên
     */
    async function submitReject(e) {
        if (e) e.preventDefault();

        const form = document.getElementById('formRejectCandidate');
        const reasonSelect = document.getElementById('stageRejectReasonSelect');
        const errorAlert = document.getElementById('stageRejectErrorAlert');
        const errorMsg = document.getElementById('stageRejectErrorMessage');
        const btn = document.getElementById('btnConfirmRejectCandidate');
        const spinner = document.getElementById('spinnerRejectCandidate');

        if (!reasonSelect.value) {
            errorMsg.textContent = 'Vui lòng chọn một lý do hợp lệ từ danh mục lý do loại.';
            errorAlert.classList.remove('d-none');
            reasonSelect.focus();
            return;
        }

        errorAlert.classList.add('d-none');
        btn.disabled = true;
        spinner.classList.remove('d-none');

        try {
            const formData = new FormData(form);
            const token = getAntiForgeryToken(form);

            const res = await fetch('/candidates/transition-stage', {
                method: 'POST',
                headers: {
                    'RequestVerificationToken': token,
                    'X-Requested-With': 'XMLHttpRequest'
                },
                body: formData
            });

            const result = await res.json();
            if (result.success) {
                const modalEl = document.getElementById('candidateRejectModal');
                const modal = bootstrap.Modal.getInstance(modalEl);
                if (modal) modal.hide();

                // Bắn event toàn cục để UI cập nhật
                window.dispatchEvent(new CustomEvent('candidateStageChanged', {
                    detail: {
                        applicationId: formData.get('ApplicationId'),
                        toStage: 6,
                        result: result.data
                    }
                }));

                // Hiển thị thông báo thành công và reload nhẹ nhàng
                alert('✅ ' + result.message);
                window.location.reload();
            } else {
                errorMsg.textContent = result.message || 'Không thể loại hồ sơ. Vui lòng thử lại.';
                errorAlert.classList.remove('d-none');
                btn.disabled = false;
                spinner.classList.add('d-none');
            }
        } catch (err) {
            console.error('Lỗi khi gửi yêu cầu loại hồ sơ:', err);
            errorMsg.textContent = 'Đã có lỗi kết nối máy chủ. Vui lòng thử lại sau.';
            errorAlert.classList.remove('d-none');
            btn.disabled = false;
            spinner.classList.add('d-none');
        }
    }

    /**
     * Mở Modal cảnh báo nhảy cóc sang OFFER (Offer Barrier)
     */
    function openSkipInterviewOfferModal(applicationId, candidateName, fromStageName) {
        document.getElementById('skipInterviewAppId').value = applicationId || '';
        document.getElementById('skipInterviewCandidateName').textContent = candidateName || 'Ứng viên';
        document.getElementById('skipInterviewFromStageBadge').textContent = fromStageName || 'Chưa phỏng vấn';

        const chk = document.getElementById('chkSkipInterviewConfirmedByHrManager');
        if (chk) chk.checked = false;

        const noteEl = document.getElementById('skipInterviewNote');
        if (noteEl) noteEl.value = '';

        const errorAlert = document.getElementById('skipInterviewErrorAlert');
        if (errorAlert) errorAlert.classList.add('d-none');

        const btn = document.getElementById('btnConfirmSkipInterviewOffer');
        if (btn) btn.disabled = true;

        const spinner = document.getElementById('spinnerSkipInterviewOffer');
        if (spinner) spinner.classList.add('d-none');

        const modalEl = document.getElementById('skipInterviewOfferModal');
        if (modalEl) {
            const modal = bootstrap.Modal.getOrCreateInstance(modalEl);
            modal.show();
        }
    }

    /**
     * Bật/tắt nút xác nhận đặc cách Offer khi HR Manager tick checkbox
     */
    function toggleSkipInterviewSubmitBtn(isChecked) {
        const btn = document.getElementById('btnConfirmSkipInterviewOffer');
        if (btn) {
            btn.disabled = !isChecked;
        }
    }

    /**
     * Xử lý gửi Form Duyệt đặc cách Offer
     */
    async function submitSkipInterviewOffer(e) {
        if (e) e.preventDefault();

        const form = document.getElementById('formSkipInterviewOffer');
        const chk = document.getElementById('chkSkipInterviewConfirmedByHrManager');
        const noteEl = document.getElementById('skipInterviewNote');
        const errorAlert = document.getElementById('skipInterviewErrorAlert');
        const errorMsg = document.getElementById('skipInterviewErrorMessage');
        const btn = document.getElementById('btnConfirmSkipInterviewOffer');
        const spinner = document.getElementById('spinnerSkipInterviewOffer');

        if (chk && !chk.checked) {
            errorMsg.textContent = 'Trưởng phòng Nhân sự bắt buộc phải tick xác nhận cờ đặc cách bỏ qua vòng phỏng vấn.';
            errorAlert.classList.remove('d-none');
            return;
        }

        if (noteEl && !noteEl.value.trim()) {
            errorMsg.textContent = 'Vui lòng nhập lý do/căn cứ giải trình cho việc duyệt đặc cách thẳng lên Offer.';
            errorAlert.classList.remove('d-none');
            noteEl.focus();
            return;
        }

        errorAlert.classList.add('d-none');
        btn.disabled = true;
        spinner.classList.remove('d-none');

        try {
            const formData = new FormData(form);
            const token = getAntiForgeryToken(form);

            const res = await fetch('/candidates/transition-stage', {
                method: 'POST',
                headers: {
                    'RequestVerificationToken': token,
                    'X-Requested-With': 'XMLHttpRequest'
                },
                body: formData
            });

            const result = await res.json();
            if (result.success) {
                const modalEl = document.getElementById('skipInterviewOfferModal');
                const modal = bootstrap.Modal.getInstance(modalEl);
                if (modal) modal.hide();

                window.dispatchEvent(new CustomEvent('candidateStageChanged', {
                    detail: {
                        applicationId: formData.get('ApplicationId'),
                        toStage: 4,
                        result: result.data
                    }
                }));

                alert('🎉 ' + result.message);
                window.location.reload();
            } else {
                errorMsg.textContent = result.message || 'Không thể duyệt đặc cách lên Offer.';
                errorAlert.classList.remove('d-none');
                btn.disabled = false;
                spinner.classList.add('d-none');
            }
        } catch (err) {
            console.error('Lỗi khi duyệt đặc cách lên Offer:', err);
            errorMsg.textContent = 'Lỗi kết nối khi gửi yêu cầu. Vui lòng thử lại sau.';
            errorAlert.classList.remove('d-none');
            btn.disabled = false;
            spinner.classList.add('d-none');
        }
    }

    /**
     * Mở Modal Dòng thời gian lịch sử chuyển giai đoạn (Stage History Timeline)
     */
    async function openHistoryModal(applicationId, candidateName) {
        document.getElementById('historyCandidateNameHeader').textContent = candidateName || 'Ứng viên';
        document.getElementById('historyAppIdHeader').textContent = applicationId ? applicationId.substring(0, 8) + '...' : '---';

        const loadingEl = document.getElementById('stageHistoryLoading');
        const emptyEl = document.getElementById('stageHistoryEmpty');
        const containerEl = document.getElementById('stageHistoryTimelineContainer');
        const itemsEl = document.getElementById('stageHistoryItems');

        loadingEl.classList.remove('d-none');
        emptyEl.classList.add('d-none');
        containerEl.classList.add('d-none');
        itemsEl.innerHTML = '';

        const modalEl = document.getElementById('candidateStageHistoryModal');
        if (modalEl) {
            const modal = bootstrap.Modal.getOrCreateInstance(modalEl);
            modal.show();
        }

        try {
            const res = await fetch(`/candidates/${applicationId}/history`, {
                headers: { 'X-Requested-With': 'XMLHttpRequest' }
            });

            loadingEl.classList.add('d-none');

            if (res.ok) {
                const result = await res.json();
                if (result.success && Array.isArray(result.data) && result.data.length > 0) {
                    renderTimelineItems(result.data, itemsEl);
                    containerEl.classList.remove('d-none');
                    return;
                }
            }
            emptyEl.classList.remove('d-none');
        } catch (err) {
            console.error('Lỗi khi nạp lịch sử giai đoạn ứng viên:', err);
            loadingEl.classList.add('d-none');
            emptyEl.classList.remove('d-none');
        }
    }

    /**
     * Render các bước dòng thời gian
     */
    function renderTimelineItems(items, container) {
        let html = '';
        items.forEach((item, index) => {
            const fromInfo = item.fromStage ? getStageBadgeInfo(item.fromStage) : { name: 'Bắt đầu nộp hồ sơ', badgeClass: 'bg-light text-dark border' };
            const toInfo = getStageBadgeInfo(item.toStage);
            const timeStr = formatVietnamDateTime(item.createdAt);

            html += `
                <div class="timeline-step-item">
                    <div class="timeline-step-dot ${toInfo.dotClass}"></div>
                    <div class="timeline-card shadow-sm">
                        <div class="d-flex flex-wrap justify-content-between align-items-center gap-2 mb-2">
                            <div class="d-flex align-items-center gap-2">
                                <span class="badge ${fromInfo.badgeClass} px-2 py-1">${item.fromStageDisplayName || fromInfo.name}</span>
                                <i class="bi bi-arrow-right text-muted"></i>
                                <span class="badge ${toInfo.badgeClass} px-2 py-1 fw-bold">${item.toStageDisplayName || toInfo.name}</span>
                            </div>
                            <div class="small text-muted d-flex align-items-center gap-1">
                                <i class="bi bi-clock"></i>
                                <span>${timeStr} (GMT+7)</span>
                            </div>
                        </div>

                        <div class="d-flex align-items-center gap-2 small text-secondary mb-1">
                            <i class="bi bi-person-circle"></i>
                            <span>Thực hiện bởi: <strong>${item.changedByUserName || 'Người dùng hệ thống'}</strong></span>
                            ${item.changedByUserEmail ? `<span class="text-muted">(${item.changedByUserEmail})</span>` : ''}
                        </div>

                        ${item.rejectionReasonName ? `
                            <div class="mt-2 p-2 rounded bg-danger bg-opacity-10 border border-danger border-opacity-25 small text-danger">
                                <i class="bi bi-x-circle-fill me-1"></i>
                                <strong>Lý do loại:</strong> ${item.rejectionReasonName}
                            </div>
                        ` : ''}

                        ${item.note ? `
                            <div class="mt-2 p-2 rounded bg-light border small text-dark">
                                <i class="bi bi-chat-quote-fill me-1 text-primary"></i>
                                <em>"${item.note}"</em>
                            </div>
                        ` : ''}
                    </div>
                </div>
            `;
        });

        container.innerHTML = html;
    }

    /**
     * Bộ điều phối chuyển giai đoạn thông minh
     * Kiểm tra rào chắn phỏng vấn trước khi chuyển sang Offer
     */
    function routeStageTransition(applicationId, candidateName, fromStageNum, toStageNum, jobTitle) {
        const fromStage = parseInt(fromStageNum, 10);
        const toStage = parseInt(toStageNum, 10);

        if (toStage === 6) {
            // Chuyển sang REJECTED -> Mở Modal loại hồ sơ
            openRejectModal(applicationId, candidateName, jobTitle);
            return;
        }

        if (toStage === 4) {
            // Chuyển sang OFFER: Kiểm tra xem đã qua INTERVIEW (Stage 3) chưa
            if (fromStage !== 3) {
                // Nhảy cóc từ Stage 1 (APPLIED) hoặc Stage 2 (SCREENING) lên OFFER -> Mở modal cảnh báo
                const fromBadge = getStageBadgeInfo(fromStage);
                openSkipInterviewOfferModal(applicationId, candidateName, fromBadge.name);
                return;
            }
        }

        // Luồng thông thường: Xác nhận nhanh và chuyển giai đoạn
        const toBadge = getStageBadgeInfo(toStage);
        if (confirm(`Bạn có chắc chắn muốn chuyển hồ sơ ứng viên "${candidateName}" sang giai đoạn "${toBadge.name}"?`)) {
            executeDirectTransition(applicationId, toStage);
        }
    }

    /**
     * Chuyển giai đoạn trực tiếp cho các vòng bình thường (tiến/lùi)
     */
    async function executeDirectTransition(applicationId, toStage) {
        const globalTokenInput = document.querySelector('#globalAntiforgeryContainer input[name="__RequestVerificationToken"]');
        const token = globalTokenInput ? globalTokenInput.value : '';

        const formData = new FormData();
        formData.append('ApplicationId', applicationId);
        formData.append('ToStage', toStage);

        try {
            const res = await fetch('/candidates/transition-stage', {
                method: 'POST',
                headers: {
                    'RequestVerificationToken': token,
                    'X-Requested-With': 'XMLHttpRequest'
                },
                body: formData
            });

            const result = await res.json();
            if (result.success) {
                alert('✅ ' + result.message);
                window.location.reload();
            } else {
                alert('❌ ' + (result.message || 'Chuyển giai đoạn thất bại.'));
            }
        } catch (err) {
            console.error('Lỗi khi chuyển giai đoạn:', err);
            alert('❌ Đã xảy ra lỗi kết nối khi chuyển giai đoạn.');
        }
    }

    // Public API
    return {
        loadRejectionReasons,
        openRejectModal,
        updateRejectCharCount,
        submitReject,
        openSkipInterviewOfferModal,
        toggleSkipInterviewSubmitBtn,
        submitSkipInterviewOffer,
        openHistoryModal,
        routeStageTransition,
        executeDirectTransition
    };
})();

// Đăng ký toàn cục cho window
window.CandidateStageManager = CandidateStageManager;
