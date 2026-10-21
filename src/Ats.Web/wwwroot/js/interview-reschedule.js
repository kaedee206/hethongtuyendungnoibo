/**
 * ATS Shared Interview Reschedule & Cancel Modal Controller
 * Provides modal dialogs and AJAX handlers across all ATS routes.
 * NoveraTech ATS v2.0
 */

(function () {
    'use strict';

    function getCsrfToken() {
        var input = document.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : '';
    }

    function showToastMsg(msg, isSuccess) {
        if (typeof window.showToast === 'function') {
            window.showToast(msg, isSuccess);
        } else {
            alert(msg);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 1. DỜI LỊCH PHỎNG VẤN (RESCHEDULE INTERVIEW)
    // ─────────────────────────────────────────────────────────────
    function validateReschedDateTime() {
        var dateInput = document.getElementById('reschedDate');
        var errEl = document.getElementById('reschedPastDateError');
        if (!dateInput || !dateInput.value) return false;

        var selected = new Date(dateInput.value);
        var now = new Date();
        if (selected <= now) {
            errEl?.classList.remove('d-none');
            dateInput.classList.add('is-invalid');
            return false;
        } else {
            errEl?.classList.add('d-none');
            dateInput.classList.remove('is-invalid');
            return true;
        }
    }

    function validateReschedPanelists() {
        var select = document.getElementById('reschedPanelists');
        var errEl = document.getElementById('reschedPanelistError');
        if (!select) return true;
        var selected = Array.from(select.selectedOptions).map(o => o.value).filter(v => v);
        if (selected.length === 0) {
            errEl?.classList.remove('d-none');
            return false;
        } else {
            errEl?.classList.add('d-none');
            return true;
        }
    }

    function onReschedTypeChange(val) {
        var locInput = document.getElementById('reschedLocation');
        if (!locInput) return;
        if (val === 'ONLINE_MEET') {
            if (!locInput.value || !locInput.value.startsWith('http')) {
                locInput.value = 'https://meet.google.com/nov-' + Math.random().toString(36).substring(2, 6) + '-' + Math.random().toString(36).substring(2, 5);
            }
        } else {
            if (!locInput.value || locInput.value.startsWith('http')) {
                locInput.value = 'Trụ sở NoveraTech, Tầng 12 Tòa nhà Innovation, Cầu Giấy, Hà Nội';
            }
        }
    }

    function escapeHtml(str) {
        if (!str) return '';
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    // Render danh sach nguoi phong van dang dropdown multi-select
    function renderPanelistDropdown(prefix, interviewers, selectedIds) {
        var select = document.getElementById(prefix + 'Panelists');
        var listContainer = document.getElementById(prefix + 'PanelistList');
        var searchInput = document.getElementById(prefix + 'PanelistSearchInput');
        if (searchInput) searchInput.value = '';

        selectedIds = selectedIds || [];
        var selectedSet = new Set(selectedIds.map(String));

        if (!interviewers || interviewers.length === 0) {
            if (select) select.innerHTML = '<option value="">(Chưa có người phỏng vấn trong hệ thống)</option>';
            if (listContainer) {
                listContainer.innerHTML = '<div class="text-center py-3 text-muted small"><i class="bi bi-info-circle me-1"></i>Chưa có người phỏng vấn trong hệ thống.</div>';
            }
            updatePanelistDropdownUI(prefix);
            return;
        }

        // Dong bo select an
        if (select) {
            select.innerHTML = interviewers.map(function(u) {
                var uId = String(u.id || u.Id || '');
                var uName = escapeHtml(u.fullName || u.name || u.FullName || 'Nhân sự');
                var uRole = escapeHtml(u.role || u.Role || 'Interviewer');
                var uEmail = escapeHtml(u.email || u.Email || '');
                var isSel = selectedSet.has(uId);
                return '<option value="' + uId + '" ' + (isSel ? 'selected' : '') + '>' + uName + ' (' + uRole + ') - ' + uEmail + '</option>';
            }).join('');
        }

        // Render cac hang lua chon kem checkbox
        if (listContainer) {
            listContainer.innerHTML = interviewers.map(function(u) {
                var uId = String(u.id || u.Id || '');
                var uName = u.fullName || u.name || u.FullName || 'Nhân sự';
                var uRole = u.role || u.Role || 'Interviewer';
                var uEmail = u.email || u.Email || '';
                var isSel = selectedSet.has(uId);

                var initials = uName.trim().split(/\s+/).slice(-2).map(function(p) { return p[0]; }).join('').toUpperCase() || 'U';
                var searchText = (uName + ' ' + uRole + ' ' + uEmail).toLowerCase();

                return [
                    '<div class="custom-panelist-item d-flex align-items-center justify-content-between p-2 ' + (isSel ? 'is-selected' : '') + '" ',
                    '     data-search-text="' + escapeHtml(searchText) + '" ',
                    '     data-user-id="' + escapeHtml(uId) + '" ',
                    '     onclick="togglePanelistSelection(\'' + prefix + '\', \'' + escapeHtml(uId) + '\', event)">',
                    '    <div class="d-flex align-items-center min-w-0 flex-grow-1 me-2">',
                    '        <input type="checkbox" class="form-check-input custom-panelist-checkbox me-2.5 flex-shrink-0" ',
                    '               id="' + prefix + '_chk_' + escapeHtml(uId) + '" ',
                    '               value="' + escapeHtml(uId) + '" ' + (isSel ? 'checked' : '') + ' ',
                    '               onclick="event.stopPropagation(); togglePanelistSelection(\'' + prefix + '\', \'' + escapeHtml(uId) + '\', event);">',
                    '        <div class="custom-panelist-badge me-2 flex-shrink-0">' + escapeHtml(initials) + '</div>',
                    '        <div class="min-w-0 flex-grow-1">',
                    '            <div class="fw-semibold small text-dark text-truncate">' + escapeHtml(uName) + '</div>',
                    '            <div class="text-muted text-truncate" style="font-size: 0.72rem;">',
                    '                ' + escapeHtml(uEmail),
                    '                <span class="badge bg-light text-secondary border ms-1" style="font-size: 0.68rem;">' + escapeHtml(uRole) + '</span>',
                    '            </div>',
                    '        </div>',
                    '    </div>',
                    '    <i class="bi bi-check2 text-success fw-bold custom-panelist-check-icon ' + (isSel ? '' : 'd-none') + '"></i>',
                    '</div>'
                ].join('');
            }).join('');
        }

        updatePanelistDropdownUI(prefix);
    }

    // Xu ly chon hoac bo chon mot nguoi phong van
    function togglePanelistSelection(prefix, id, event) {
        var chk = document.getElementById(prefix + '_chk_' + id);
        if (!chk) return;

        if (event && event.target !== chk) {
            chk.checked = !chk.checked;
        }

        var isChecked = chk.checked;
        var item = chk.closest('.custom-panelist-item');
        if (item) {
            if (isChecked) {
                item.classList.add('is-selected');
                item.querySelector('.custom-panelist-check-icon')?.classList.remove('d-none');
            } else {
                item.classList.remove('is-selected');
                item.querySelector('.custom-panelist-check-icon')?.classList.add('d-none');
            }
        }

        var select = document.getElementById(prefix + 'Panelists');
        if (select) {
            for (var i = 0; i < select.options.length; i++) {
                if (select.options[i].value === String(id)) {
                    select.options[i].selected = isChecked;
                    break;
                }
            }
            select.dispatchEvent(new Event('change', { bubbles: true }));
        }

        updatePanelistDropdownUI(prefix);
    }

    // Cap nhat text tren nut dropdown va badge so luong
    function updatePanelistDropdownUI(prefix) {
        var select = document.getElementById(prefix + 'Panelists');
        var btnText = document.getElementById(prefix + 'PanelistBtnText');
        var countBadge = document.getElementById(prefix + 'PanelistCountBadge');
        if (!select || !btnText) return;

        var selected = Array.from(select.selectedOptions).filter(function(o) { return o.value; });
        var count = selected.length;

        if (countBadge) {
            countBadge.textContent = count + ' người đã chọn';
            if (count > 0) {
                countBadge.classList.remove('bg-light', 'text-secondary');
                countBadge.classList.add('bg-success-subtle', 'text-success', 'border-success-subtle');
            } else {
                countBadge.classList.add('bg-light', 'text-secondary');
                countBadge.classList.remove('bg-success-subtle', 'text-success', 'border-success-subtle');
            }
        }

        if (count === 0) {
            btnText.innerHTML = '<span class="text-muted"><i class="bi bi-people me-1.5 text-secondary"></i>Bấm để chọn người phỏng vấn...</span>';
        } else if (count === 1) {
            var rawText = selected[0].text || '';
            var nameOnly = rawText.split('(')[0].trim() || rawText;
            btnText.innerHTML = '<span class="fw-semibold text-dark"><i class="bi bi-person-check-fill text-success me-1.5"></i>' + escapeHtml(nameOnly) + '</span>';
        } else {
            var firstText = selected[0].text || '';
            var firstName = firstText.split('(')[0].trim() || firstText;
            btnText.innerHTML = '<span class="fw-semibold text-dark"><i class="bi bi-people-fill text-success me-1.5"></i>' + escapeHtml(firstName) + ' <span class="badge bg-success-subtle text-success border border-success-subtle ms-1" style="font-size:0.75rem;">+' + (count - 1) + ' khác</span></span>';
        }

        if (prefix === 'sched' && typeof window.validateSchedPanelists === 'function') {
            window.validateSchedPanelists();
        } else if (prefix === 'resched' && typeof window.validateReschedPanelists === 'function') {
            window.validateReschedPanelists();
        }
    }

    // Loc danh sach nguoi phong van theo ten hoac email
    function filterPanelistDropdown(prefix) {
        var input = document.getElementById(prefix + 'PanelistSearchInput');
        var listContainer = document.getElementById(prefix + 'PanelistList');
        if (!input || !listContainer) return;

        var query = input.value.trim().toLowerCase();
        var items = listContainer.querySelectorAll('.custom-panelist-item');
        var visibleCount = 0;

        items.forEach(function(item) {
            var search = item.getAttribute('data-search-text') || '';
            if (!query || search.includes(query)) {
                item.style.display = 'flex';
                visibleCount++;
            } else {
                item.style.display = 'none';
            }
        });

        var noResultEl = listContainer.querySelector('.no-results-msg');
        if (visibleCount === 0) {
            if (!noResultEl) {
                noResultEl = document.createElement('div');
                noResultEl.className = 'no-results-msg text-center py-3 text-muted small';
                noResultEl.innerHTML = '<i class="bi bi-search me-1"></i>Không tìm thấy nhân sự phù hợp';
                listContainer.appendChild(noResultEl);
            }
            noResultEl.style.display = 'block';
        } else if (noResultEl) {
            noResultEl.style.display = 'none';
        }
    }

    // Chon hoac bo chon tat ca
    function selectAllPanelists(prefix, shouldSelectAll) {
        var listContainer = document.getElementById(prefix + 'PanelistList');
        var select = document.getElementById(prefix + 'Panelists');
        if (!listContainer || !select) return;

        var items = listContainer.querySelectorAll('.custom-panelist-item');
        items.forEach(function(item) {
            if (item.style.display !== 'none') {
                var chk = item.querySelector('.custom-panelist-checkbox');
                var uId = item.getAttribute('data-user-id');
                if (chk) chk.checked = shouldSelectAll;
                if (shouldSelectAll) {
                    item.classList.add('is-selected');
                    item.querySelector('.custom-panelist-check-icon')?.classList.remove('d-none');
                } else {
                    item.classList.remove('is-selected');
                    item.querySelector('.custom-panelist-check-icon')?.classList.add('d-none');
                }
                if (uId) {
                    for (var i = 0; i < select.options.length; i++) {
                        if (select.options[i].value === uId) {
                            select.options[i].selected = shouldSelectAll;
                            break;
                        }
                    }
                }
            }
        });

        select.dispatchEvent(new Event('change', { bubbles: true }));
        updatePanelistDropdownUI(prefix);
    }

    async function openRescheduleInterviewModal(interviewId, candidateName, jobTitle, roundTitle, timeDisplay, dateDisplay) {
        var idInput = document.getElementById('reschedInterviewId');
        if (!idInput) {
            console.error('[InterviewModal] Modal element #reschedInterviewId not found in DOM.');
            return;
        }
        idInput.value = interviewId;

        var candNameEl = document.getElementById('reschedCandidateName');
        if (candNameEl) candNameEl.textContent = candidateName || 'Ứng viên';

        var jobTitleEl = document.getElementById('reschedJobTitle');
        if (jobTitleEl) jobTitleEl.textContent = jobTitle || 'Vị trí';

        var roundBadgeEl = document.getElementById('reschedRoundBadge');
        if (roundBadgeEl) roundBadgeEl.textContent = roundTitle || 'Vòng PV';

        var currentTimeEl = document.getElementById('reschedCurrentTimeDisplay');
        if (currentTimeEl) currentTimeEl.textContent = (timeDisplay || '') + ' - ' + (dateDisplay || '');

        var genError = document.getElementById('reschedGeneralError');
        if (genError) {
            genError.classList.add('d-none');
            genError.textContent = '';
        }
        document.getElementById('reschedPastDateError')?.classList.add('d-none');
        document.getElementById('reschedPanelistError')?.classList.add('d-none');

        var now = new Date();
        var localNowIso = new Date(now.getTime() - now.getTimezoneOffset() * 60000).toISOString().slice(0, 16);
        var dateInput = document.getElementById('reschedDate');
        if (dateInput) {
            dateInput.min = localNowIso;
            dateInput.classList.remove('is-invalid');

            // Mac dinh thoi gian: ngay mai luc 9:00
            var tomorrow = new Date(now);
            tomorrow.setDate(tomorrow.getDate() + 1);
            tomorrow.setHours(9, 0, 0, 0);
            var localIso = new Date(tomorrow.getTime() - tomorrow.getTimezoneOffset() * 60000).toISOString().slice(0, 16);
            dateInput.value = localIso;
        }

        var reschedList = document.getElementById('reschedPanelistList');
        if (reschedList) {
            reschedList.innerHTML = '<div class="text-center py-3 text-muted small"><span class="spinner-border spinner-border-sm me-1 text-success"></span>Đang tải danh sách nhân sự...</div>';
        }

        var modalEl = document.getElementById('rescheduleInterviewModal');
        if (!modalEl) {
            console.error('[InterviewModal] #rescheduleInterviewModal not found in DOM.');
            return;
        }
        var modal = bootstrap.Modal.getOrCreateInstance(modalEl);
        modal.show();

        try {
            // Tai danh sach tat ca interviewers
            var resInts = await fetch('/Workspace/GetInterviewers', {
                headers: { 'X-Requested-With': 'XMLHttpRequest' }
            });
            var dataInts = await resInts.json();
            var allInterviewers = dataInts.interviewers || dataInts.data || [];

            // Tai chi tiet buoi phong van
            var resDetail = await fetch('/Workspace/GetInterviewDetailForEdit?id=' + encodeURIComponent(interviewId), {
                headers: { 'X-Requested-With': 'XMLHttpRequest' }
            });
            var detailData = await resDetail.json();

            if (!detailData.success && detailData.message) {
                showToastMsg(detailData.message, false);
                modal.hide();
                return;
            }

            var interview = detailData.data || {};

            if (interview.hasSubmittedEvaluation) {
                showToastMsg('Không thể dời lịch vì buổi phỏng vấn đã có phiếu đánh giá được nộp.', false);
                modal.hide();
                return;
            }

            if (interview.interviewType && document.getElementById('reschedType')) {
                document.getElementById('reschedType').value = interview.interviewType;
            }
            if (interview.locationOrLink && document.getElementById('reschedLocation')) {
                document.getElementById('reschedLocation').value = interview.locationOrLink;
            }
            if (interview.durationMinutes && document.getElementById('reschedDuration')) {
                document.getElementById('reschedDuration').value = interview.durationMinutes;
            }
            if (interview.startTimeIso && dateInput) {
                var stDate = new Date(interview.startTimeIso);
                var stIso = new Date(stDate.getTime() - stDate.getTimezoneOffset() * 60000).toISOString().slice(0, 16);
                if (stDate > now) {
                    dateInput.value = stIso;
                }
            }

            var assignedPanelistIds = (interview.panelists || []).map(function(p) { return p.interviewerId; });
            renderPanelistDropdown('resched', allInterviewers, assignedPanelistIds);
        } catch (err) {
            console.error('[InterviewModal] Error loading detail:', err);
            renderPanelistDropdown('resched', [], []);
        }
    }

    async function handleRescheduleInterviewSubmit(e) {
        if (e && e.preventDefault) e.preventDefault();

        if (!validateReschedDateTime()) {
            showToastMsg('Thời gian dời lịch phải ở tương lai.', false);
            return;
        }

        var select = document.getElementById('reschedPanelists');
        var selectedPanelists = Array.from(select?.selectedOptions || []).map(o => o.value).filter(v => v);

        if (selectedPanelists.length === 0) {
            document.getElementById('reschedPanelistError')?.classList.remove('d-none');
            showToastMsg('Vui lòng chọn ít nhất một người phỏng vấn.', false);
            return;
        }

        var btn = document.getElementById('btnSubmitResched');
        var spinner = document.getElementById('spinnerResched');
        var genError = document.getElementById('reschedGeneralError');
        if (btn) btn.disabled = true;
        if (spinner) spinner.classList.remove('d-none');
        if (genError) genError.classList.add('d-none');

        var payload = {
            interviewId: document.getElementById('reschedInterviewId')?.value,
            newStartTime: document.getElementById('reschedDate')?.value,
            durationMinutes: parseInt(document.getElementById('reschedDuration')?.value, 10) || 60,
            interviewType: document.getElementById('reschedType')?.value,
            locationOrLink: document.getElementById('reschedLocation')?.value,
            interviewerIds: selectedPanelists,
            panelistIds: selectedPanelists,
            reason: document.getElementById('reschedReason')?.value
        };

        try {
            var res = await fetch('/Workspace/RescheduleInterview', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': getCsrfToken(),
                    'X-Requested-With': 'XMLHttpRequest'
                },
                body: JSON.stringify(payload)
            });
            var data = await res.json();
            if (data.success) {
                var modalEl = document.getElementById('rescheduleInterviewModal');
                if (modalEl) bootstrap.Modal.getInstance(modalEl)?.hide();
                showToastMsg(data.message || 'Dời lịch phỏng vấn thành công! Toàn bộ các bên đã được thông báo.', true);
                setTimeout(() => window.location.reload(), 1200);
            } else {
                if (genError) {
                    genError.textContent = data.message || 'Lỗi khi dời lịch phỏng vấn.';
                    genError.classList.remove('d-none');
                }
                showToastMsg(data.message || 'Không thể dời lịch phỏng vấn', false);
            }
        } catch (err) {
            if (genError) {
                genError.textContent = 'Lỗi kết nối máy chủ: ' + err.message;
                genError.classList.remove('d-none');
            }
            showToastMsg('Lỗi kết nối: ' + err.message, false);
        } finally {
            if (btn) btn.disabled = false;
            if (spinner) spinner.classList.add('d-none');
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 2. HỦY LỊCH PHỎNG VẤN (CANCEL INTERVIEW)
    // ─────────────────────────────────────────────────────────────
    function validateCancelReason() {
        var textarea = document.getElementById('cancelReason');
        var errEl = document.getElementById('cancelReasonError');
        var charCount = document.getElementById('cancelCharCount');
        var val = (textarea?.value || '').trim();

        if (charCount) {
            charCount.textContent = val.length + ' ký tự';
        }

        if (val.length < 5) {
            errEl?.classList.remove('d-none');
            textarea?.classList.add('is-invalid');
            return false;
        } else {
            errEl?.classList.add('d-none');
            textarea?.classList.remove('is-invalid');
            return true;
        }
    }

    function openCancelInterviewModal(interviewId, candidateName, jobTitle, roundTitle, timeDisplay) {
        var idInput = document.getElementById('cancelInterviewId');
        if (!idInput) {
            console.error('[InterviewModal] #cancelInterviewId not found in DOM.');
            return;
        }
        idInput.value = interviewId;

        var candNameEl = document.getElementById('cancelCandidateName');
        if (candNameEl) candNameEl.textContent = candidateName || 'Ứng viên';

        var jobTitleEl = document.getElementById('cancelJobTitle');
        if (jobTitleEl) jobTitleEl.textContent = jobTitle || 'Vị trí';

        var roundBadgeEl = document.getElementById('cancelRoundBadge');
        if (roundBadgeEl) roundBadgeEl.textContent = roundTitle || 'Vòng PV';

        var timeDisplayEl = document.getElementById('cancelTimeDisplay');
        if (timeDisplayEl) timeDisplayEl.textContent = timeDisplay || '-';

        var textarea = document.getElementById('cancelReason');
        if (textarea) {
            textarea.value = '';
            textarea.classList.remove('is-invalid');
        }
        document.getElementById('cancelReasonError')?.classList.add('d-none');
        var charCount = document.getElementById('cancelCharCount');
        if (charCount) charCount.textContent = '0 ký tự';

        var genError = document.getElementById('cancelGeneralError');
        if (genError) {
            genError.classList.add('d-none');
            genError.textContent = '';
        }

        var modalEl = document.getElementById('cancelInterviewModal');
        if (!modalEl) {
            console.error('[InterviewModal] #cancelInterviewModal not found in DOM.');
            return;
        }
        var modal = bootstrap.Modal.getOrCreateInstance(modalEl);
        modal.show();
    }

    async function handleCancelInterviewSubmit(e) {
        if (e && e.preventDefault) e.preventDefault();

        if (!validateCancelReason()) {
            showToastMsg('Vui lòng nhập lý do hủy lịch phỏng vấn (tối thiểu 5 ký tự).', false);
            return;
        }

        var btn = document.getElementById('btnSubmitCancel');
        var spinner = document.getElementById('spinnerCancel');
        var genError = document.getElementById('cancelGeneralError');
        if (btn) btn.disabled = true;
        if (spinner) spinner.classList.remove('d-none');
        if (genError) genError.classList.add('d-none');

        var payload = {
            interviewId: document.getElementById('cancelInterviewId')?.value,
            reason: (document.getElementById('cancelReason')?.value || '').trim()
        };

        try {
            var res = await fetch('/Workspace/CancelInterview', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': getCsrfToken(),
                    'X-Requested-With': 'XMLHttpRequest'
                },
                body: JSON.stringify(payload)
            });
            var data = await res.json();
            if (data.success) {
                var modalEl = document.getElementById('cancelInterviewModal');
                if (modalEl) bootstrap.Modal.getInstance(modalEl)?.hide();
                showToastMsg(data.message || 'Đã hủy lịch phỏng vấn thành công. Các bên đã nhận được thông báo.', true);
                setTimeout(() => window.location.reload(), 1200);
            } else {
                if (genError) {
                    genError.textContent = data.message || 'Lỗi khi hủy lịch phỏng vấn.';
                    genError.classList.remove('d-none');
                }
                showToastMsg(data.message || 'Không thể hủy lịch phỏng vấn', false);
            }
        } catch (err) {
            if (genError) {
                genError.textContent = 'Lỗi kết nối máy chủ: ' + err.message;
                genError.classList.remove('d-none');
            }
            showToastMsg('Lỗi kết nối: ' + err.message, false);
        } finally {
            if (btn) btn.disabled = false;
            if (spinner) spinner.classList.add('d-none');
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 3. EXPORT TO GLOBAL WINDOW OBJECT
    // ─────────────────────────────────────────────────────────────
    window.validateReschedDateTime = validateReschedDateTime;
    window.validateReschedPanelists = validateReschedPanelists;
    window.onReschedTypeChange = onReschedTypeChange;
    window.openRescheduleInterviewModal = openRescheduleInterviewModal;
    window.handleRescheduleInterviewSubmit = handleRescheduleInterviewSubmit;
    window.validateCancelReason = validateCancelReason;
    window.openCancelInterviewModal = openCancelInterviewModal;
    window.handleCancelInterviewSubmit = handleCancelInterviewSubmit;
    window.renderPanelistDropdown = renderPanelistDropdown;
    window.togglePanelistSelection = togglePanelistSelection;
    window.updatePanelistDropdownUI = updatePanelistDropdownUI;
    window.filterPanelistDropdown = filterPanelistDropdown;
    window.selectAllPanelists = selectAllPanelists;
})();
