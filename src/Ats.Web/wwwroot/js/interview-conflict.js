/**
 * interview-conflict.js
 * Quản lý kiểm tra xung đột lịch phỏng vấn theo thời gian thực (SCRUM-53).
 * Cơ chế: Debounce 400ms, Hard Block with Override (Lý do >= 10 ký tự).
 * Tuân thủ quy chuẩn: kebab-case, camelCase cho biến cục bộ & tham số.
 */

(function () {
    'use strict';

    let conflictDebounceTimer = null;
    let isConflictActive = false;
    let activeSubmitButtonId = null;
    let originalSubmitButtonHtml = null;
    let originalSubmitButtonClass = null;
    let originalSubmitButtonStyle = null;

    /**
     * Lấy CSRF Anti-Forgery Token từ trang
     */
    function getCsrfToken() {
        const input = document.querySelector('input[name="__RequestVerificationToken"]');
        if (input && input.value) return input.value;
        const meta = document.querySelector('meta[name="csrf-token"]');
        return meta ? meta.getAttribute('content') : '';
    }

    /**
     * Escape chuỗi HTML an toàn chống XSS
     */
    function escapeHtml(str) {
        if (!str) return '';
        const div = document.createElement('div');
        div.textContent = str;
        return div.innerHTML;
    }

    /**
     * Kích hoạt kiểm tra xung đột với cơ chế Debounce 400ms
     */
    function scheduleConflictCheck(params) {
        clearTimeout(conflictDebounceTimer);
        conflictDebounceTimer = setTimeout(() => {
            executeConflictCheck(params);
        }, 400);
    }

    /**
     * Thực thi gọi API kiểm tra xung đột qua AJAX
     */
    async function executeConflictCheck(params) {
        const {
            startTime,
            durationMinutes,
            interviewerIds,
            meetingRoomId,
            locationOrLink,
            excludeInterviewId,
            submitBtnId
        } = params;

        if (submitBtnId) {
            activeSubmitButtonId = submitBtnId;
            const submitBtn = document.getElementById(submitBtnId);
            if (submitBtn && originalSubmitButtonHtml === null) {
                originalSubmitButtonHtml = submitBtn.innerHTML;
                originalSubmitButtonClass = submitBtn.className;
                originalSubmitButtonStyle = submitBtn.getAttribute('style') || '';
            }
        }

        // Nếu chưa nhập thời gian bắt đầu thì xóa sạch cảnh báo và kết thúc
        if (!startTime) {
            resetConflictAlert();
            return;
        }

        // Hiển thị indicator loading nhẹ nhàng cạnh khung giờ / phòng họp
        const indicators = document.querySelectorAll('#interviewConflictCheckingIndicator, .conflict-loading-spinner');
        indicators.forEach(el => el.classList.remove('d-none'));

        // Chuyển đổi định dạng thời gian phù hợp ISO 8601
        let formattedStartTime = startTime;
        if (startTime && !startTime.includes('Z') && !startTime.includes('+')) {
            try {
                formattedStartTime = new Date(startTime).toISOString();
            } catch (e) {
                formattedStartTime = startTime;
            }
        }

        const payload = {
            startTime: formattedStartTime,
            durationMinutes: parseInt(durationMinutes, 10) || 60,
            interviewerIds: Array.isArray(interviewerIds) ? interviewerIds : [],
            meetingRoomId: (meetingRoomId && meetingRoomId !== '') ? meetingRoomId : null,
            locationOrLink: locationOrLink ? locationOrLink.trim() : null,
            excludeInterviewId: excludeInterviewId || null
        };

        try {
            const response = await fetch('/interviews/kiem-tra-trung-lich', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': getCsrfToken(),
                    'X-Requested-With': 'XMLHttpRequest'
                },
                body: JSON.stringify(payload)
            });

            if (!response.ok) {
                console.warn('Lỗi khi gọi API kiểm tra xung đột lịch:', response.status);
                return;
            }

            const resJson = await response.json();
            const result = resJson.data || resJson;

            renderConflictCheckResult(result);
        } catch (error) {
            console.error('Không thể kết nối dịch vụ kiểm tra trùng lịch:', error);
        } finally {
            indicators.forEach(el => el.classList.add('d-none'));
        }
    }

    /**
     * Hiển thị kết quả kiểm tra xung đột lên giao diện
     */
    function renderConflictCheckResult(result) {
        const alertBox = document.getElementById('interviewConflictAlert');
        const conflictList = document.getElementById('interviewConflictList');
        const overrideSection = document.getElementById('interviewOverrideSection');
        const alertMessage = document.getElementById('interviewConflictAlertMessage');

        if (!alertBox || !conflictList || !overrideSection) return;

        if (result && result.hasConflict && Array.isArray(result.conflicts) && result.conflicts.length > 0) {
            isConflictActive = true;

            if (alertMessage) {
                alertMessage.textContent = result.message || 'Phát hiện xung đột lịch. Vui lòng kiểm tra các ca trùng dưới đây.';
            }

            // Xóa sạch danh sách cũ và render danh sách mới
            conflictList.innerHTML = '';

            result.conflicts.forEach(item => {
                const card = document.createElement('div');
                card.className = 'p-2.5 bg-white border border-danger-subtle rounded-2 shadow-xs';

                const isInterviewer = item.conflictType === 'INTERVIEWER';
                const typeIcon = isInterviewer
                    ? '<i class="bi bi-person-fill-exclamation text-danger fs-5 me-1.5"></i>'
                    : '<i class="bi bi-door-closed-fill text-danger fs-5 me-1.5"></i>';
                const typeBadge = isInterviewer
                    ? '<span class="badge bg-danger-subtle text-danger border border-danger-subtle px-2 py-0.5" style="font-size:0.72rem;">Trùng người phỏng vấn</span>'
                    : '<span class="badge bg-danger-subtle text-danger border border-danger-subtle px-2 py-0.5" style="font-size:0.72rem;">Trùng phòng họp</span>';

                card.innerHTML = `
                    <div class="d-flex justify-content-between align-items-center mb-1">
                        <div class="d-flex align-items-center">
                            ${typeIcon}
                            <span class="fw-bold text-danger" style="font-size:0.86rem;">
                                ${escapeHtml(item.conflictingEntityName)}
                            </span>
                        </div>
                        ${typeBadge}
                    </div>
                    <div class="small text-dark mt-1" style="font-size:0.8rem;">
                        <span class="text-secondary">Ứng viên trùng:</span> <b>${escapeHtml(item.conflictingCandidateName)}</b>
                        <span class="text-muted mx-1">•</span>
                        <span class="text-secondary">Vị trí:</span> <span class="text-dark">${escapeHtml(item.jobTitle)}</span>
                    </div>
                    <div class="small text-danger fw-semibold mt-1 d-flex align-items-center gap-1" style="font-size:0.78rem;">
                        <i class="bi bi-clock-history"></i>
                        <span>Khung giờ trùng: <b>${escapeHtml(item.timeRangeDisplay || (item.conflictingStartTime + ' - ' + item.conflictingEndTime))}</b></span>
                    </div>
                `;
                conflictList.appendChild(card);
            });

            alertBox.classList.remove('d-none');
            overrideSection.classList.remove('d-none');

            // Cập nhật trạng thái nút lưu (Hard Block)
            validateOverrideState();
        } else {
            resetConflictAlert();
        }
    }

    /**
     * Xóa sạch cảnh báo xung đột và khôi phục nút Lưu
     */
    function resetConflictAlert() {
        isConflictActive = false;
        const alertBox = document.getElementById('interviewConflictAlert');
        const conflictList = document.getElementById('interviewConflictList');
        const overrideSection = document.getElementById('interviewOverrideSection');
        const chkOverride = document.getElementById('chkIsOverridden');
        const txtReason = document.getElementById('txtOverrideReason');

        if (alertBox) alertBox.classList.add('d-none');
        if (conflictList) conflictList.innerHTML = '';
        if (overrideSection) overrideSection.classList.add('d-none');

        if (chkOverride) chkOverride.checked = false;
        if (txtReason) txtReason.value = '';

        const reasonContainer = document.getElementById('interviewOverrideReasonContainer');
        if (reasonContainer) reasonContainer.classList.add('d-none');

        updateSubmitButtonState(true, false);
    }

    /**
     * Xác thực trạng thái Ghi đè (Hard Block with Override)
     */
    function validateOverrideState() {
        if (!isConflictActive) {
            updateSubmitButtonState(true, false);
            return;
        }

        const chkOverride = document.getElementById('chkIsOverridden');
        const txtReason = document.getElementById('txtOverrideReason');
        const reasonContainer = document.getElementById('interviewOverrideReasonContainer');
        const counter = document.getElementById('overrideReasonCounter');
        const validationMsg = document.getElementById('overrideReasonValidationMsg');
        const statusBadge = document.getElementById('interviewOverrideStatusBadge');

        const isChecked = chkOverride && chkOverride.checked;
        const reason = txtReason ? txtReason.value.trim() : '';

        if (reasonContainer) {
            if (isChecked) {
                reasonContainer.classList.remove('d-none');
            } else {
                reasonContainer.classList.add('d-none');
            }
        }

        if (counter) {
            counter.textContent = `${reason.length}/10 ký tự`;
            if (reason.length >= 10) {
                counter.className = 'text-success fw-semibold';
            } else {
                counter.className = 'text-danger fw-semibold';
            }
        }

        const isValidOverride = isChecked && reason.length >= 10;

        if (validationMsg) {
            if (isChecked && reason.length < 10) {
                validationMsg.classList.remove('d-none');
            } else {
                validationMsg.classList.add('d-none');
            }
        }

        if (statusBadge) {
            if (isValidOverride) {
                statusBadge.className = 'badge rounded-2 bg-warning text-dark';
                statusBadge.innerHTML = '<i class="bi bi-unlock-fill me-1"></i>Đã mở khóa ghi đè';
            } else if (isChecked) {
                statusBadge.className = 'badge rounded-2 bg-danger text-white';
                statusBadge.innerHTML = '<i class="bi bi-pencil-fill me-1"></i>Cần nhập lý do >= 10 ký tự';
            } else {
                statusBadge.className = 'badge rounded-2 bg-secondary text-white';
                statusBadge.innerHTML = '<i class="bi bi-lock-fill me-1"></i>Chưa mở khóa';
            }
        }

        updateSubmitButtonState(isValidOverride, true);
    }

    /**
     * Cập nhật giao diện và trạng thái vô hiệu hóa của nút Submit
     */
    function updateSubmitButtonState(canSubmit, isOverrideMode) {
        if (!activeSubmitButtonId) return;
        const btn = document.getElementById(activeSubmitButtonId);
        if (!btn) return;

        if (canSubmit) {
            btn.disabled = false;
            if (isOverrideMode) {
                btn.className = 'btn btn-sm btn-warning rounded-2 px-4 fw-semibold text-dark shadow-sm';
                btn.style.removeProperty('background');
                btn.style.removeProperty('border-color');
                btn.innerHTML = '<i class="bi bi-exclamation-diamond-fill me-1"></i>Lưu kèm Ghi đè (Override)';
            } else {
                if (originalSubmitButtonClass) {
                    btn.className = originalSubmitButtonClass;
                } else {
                    btn.className = 'btn btn-sm btn-primary rounded-2 px-4 fw-semibold text-white shadow-sm';
                }
                if (originalSubmitButtonStyle) {
                    btn.setAttribute('style', originalSubmitButtonStyle);
                }
                if (originalSubmitButtonHtml) {
                    btn.innerHTML = originalSubmitButtonHtml;
                } else {
                    btn.innerHTML = '<i class="bi bi-calendar-check me-1"></i>Xác nhận &amp; Đặt lịch phỏng vấn';
                }
            }
        } else {
            btn.disabled = true;
            btn.className = 'btn btn-sm btn-danger rounded-2 px-4 fw-semibold text-white opacity-75';
            btn.style.removeProperty('background');
            btn.style.removeProperty('border-color');
            btn.innerHTML = '<i class="bi bi-slash-circle-fill me-1"></i>Bị chặn do trùng lịch';
        }
    }

    /**
     * Tự động gắn bộ theo dõi thay đổi thời gian thực cho một Form đặt lịch
     */
    function watchInterviewForm(config) {
        const {
            startTimeInputId,
            durationInputId,
            panelistCheckboxesSelector,
            panelistSelectId,
            locationInputId,
            meetingRoomSelectId,
            submitBtnId,
            getExcludeInterviewId
        } = config;

        if (submitBtnId) {
            activeSubmitButtonId = submitBtnId;
            const submitBtn = document.getElementById(submitBtnId);
            if (submitBtn && originalSubmitButtonHtml === null) {
                originalSubmitButtonHtml = submitBtn.innerHTML;
                originalSubmitButtonClass = submitBtn.className;
                originalSubmitButtonStyle = submitBtn.getAttribute('style') || '';
            }
        }

        function trigger() {
            const startTimeEl = document.getElementById(startTimeInputId);
            const durationEl = document.getElementById(durationInputId);
            const locationEl = document.getElementById(locationInputId);
            const roomEl = document.getElementById(meetingRoomSelectId);

            const startTime = startTimeEl ? startTimeEl.value : null;
            const durationMinutes = durationEl ? durationEl.value : 60;
            const locationOrLink = locationEl ? locationEl.value : null;
            const meetingRoomId = roomEl ? roomEl.value : null;

            let interviewerIds = [];
            if (panelistCheckboxesSelector) {
                const checked = document.querySelectorAll(panelistCheckboxesSelector + ':checked');
                interviewerIds = Array.from(checked).map(c => c.value);
            } else if (panelistSelectId) {
                const select = document.getElementById(panelistSelectId);
                if (select) {
                    interviewerIds = Array.from(select.selectedOptions).map(o => o.value).filter(v => v);
                }
            }

            const excludeInterviewId = typeof getExcludeInterviewId === 'function' ? getExcludeInterviewId() : null;

            scheduleConflictCheck({
                startTime,
                durationMinutes,
                interviewerIds,
                meetingRoomId,
                locationOrLink,
                excludeInterviewId,
                submitBtnId
            });
        }

        // Gắn lắng nghe sự kiện
        [startTimeInputId, durationInputId, locationInputId, meetingRoomSelectId].forEach(id => {
            if (!id) return;
            const el = document.getElementById(id);
            if (el) {
                el.addEventListener('change', trigger);
                el.addEventListener('input', trigger);
            }
        });

        if (panelistSelectId) {
            const sel = document.getElementById(panelistSelectId);
            if (sel) {
                sel.addEventListener('change', trigger);
            }
        }

        if (panelistCheckboxesSelector) {
            document.addEventListener('change', (e) => {
                if (e.target && e.target.matches(panelistCheckboxesSelector)) {
                    trigger();
                }
            });
        }

        return trigger;
    }

    // Expose global methods cho các file Razor View sử dụng
    window.interviewConflict = {
        scheduleCheck: scheduleConflictCheck,
        executeCheck: executeConflictCheck,
        resetAlert: resetConflictAlert,
        validateOverride: validateOverrideState,
        renderResult: renderConflictCheckResult,
        watchForm: watchInterviewForm
    };

    window.onInterviewOverrideChange = function () {
        validateOverrideState();
    };

    window.onInterviewOverrideReasonInput = function () {
        validateOverrideState();
    };

})();
