// Module điều khiển đặt lịch phỏng vấn tập trung hàng loạt (Scrum-52)
(function (window, document) {
    'use strict';

    var batchCandidatesCache = [];
    var batchCurrentSlots = [];
    var batchInterviewersLoaded = false;

    function getBatchModal() {
        var el = document.getElementById('batchScheduleInterviewModal');
        return el ? bootstrap.Modal.getOrCreateInstance(el) : null;
    }

    function getCsrfToken() {
        var tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
        return tokenInput ? tokenInput.value : '';
    }

    function initDefaultDateTime() {
        var timeInput = document.getElementById('batchStartTime');
        if (!timeInput || timeInput.value) return;

        var d = new Date();
        d.setDate(d.getDate() + 1);
        d.setHours(9, 0, 0, 0);

        var pad = function (n) { return n < 10 ? '0' + n : n; };
        var yyyy = d.getFullYear();
        var mm = pad(d.getMonth() + 1);
        var dd = pad(d.getDate());
        var hh = pad(d.getHours());
        var min = pad(d.getMinutes());

        timeInput.value = `${yyyy}-${mm}-${dd}T${hh}:${min}`;
    }

    async function loadInterviewers() {
        var select = document.getElementById('batchPanelists');
        if (!select || batchInterviewersLoaded) return;

        try {
            var res = await fetch('/Workspace/GetInterviewers');
            var data = await res.json();
            var list = data.interviewers || data.data || data;

            select.innerHTML = '';
            if (Array.isArray(list) && list.length > 0) {
                list.forEach(function (user) {
                    var opt = document.createElement('option');
                    opt.value = user.id || user.userId;
                    opt.textContent = `${user.fullName || user.name || user.email} (${user.email || ''})`;
                    select.appendChild(opt);
                });
                if (select.options.length > 0) {
                    select.options[0].selected = true;
                }
                batchInterviewersLoaded = true;
            } else {
                select.innerHTML = '<option disabled>Không có dữ liệu người phỏng vấn</option>';
            }
        } catch (e) {
            console.error('Lỗi khi tải danh sách người phỏng vấn:', e);
            select.innerHTML = '<option disabled>Không thể tải người phỏng vấn</option>';
        }
    }

    async function loadBatchCandidateList() {
        var container = document.getElementById('batchCandidateItems');
        var loading = document.getElementById('batchCandidateLoading');
        var error = document.getElementById('batchCandidateError');
        var checkAll = document.getElementById('batchCheckAll');

        if (!container) return;

        if (loading) loading.classList.remove('d-none');
        container.innerHTML = '';
        if (error) error.classList.add('d-none');
        if (checkAll) checkAll.checked = false;
        updateSelectedCount();

        var jobSelect = document.getElementById('batchFilterJob');
        var stageSelect = document.getElementById('batchFilterStage');

        var jobId = jobSelect ? jobSelect.value : '';
        var stageOrder = stageSelect ? stageSelect.value : '';

        var url = '/Workspace/GetBatchSchedulingCandidates?';
        if (jobId) url += 'jobPostingId=' + encodeURIComponent(jobId) + '&';
        if (stageOrder) url += 'stageOrder=' + encodeURIComponent(stageOrder);

        try {
            var res = await fetch(url);
            var json = await res.json();
            var list = json.candidates || json.data || [];
            batchCandidatesCache = Array.isArray(list) ? list : [];

            if (loading) loading.classList.add('d-none');

            if (batchCandidatesCache.length === 0) {
                container.innerHTML = '<div class="text-center py-4 text-muted small"><i class="bi bi-inbox d-block fs-4 text-secondary mb-1"></i>Không có ứng viên nào phù hợp bộ lọc.</div>';
                return;
            }

            renderCandidateList(batchCandidatesCache);
        } catch (e) {
            console.error('Lỗi nạp danh sách ứng viên:', e);
            if (loading) loading.classList.add('d-none');
            container.innerHTML = '<div class="text-center py-4 text-danger small">Lỗi kết nối khi tải danh sách ứng viên.</div>';
        }
    }

    function renderCandidateList(list) {
        var container = document.getElementById('batchCandidateItems');
        if (!container) return;
        container.innerHTML = '';

        list.forEach(function (c) {
            var appId = c.applicationId || c.id;
            var name = c.fullName || c.candidateName || 'Ứng viên';
            var title = c.jobTitle || 'Chưa phân vị trí';
            var email = c.email || '';

            var item = document.createElement('div');
            item.className = 'batch-cand-item d-flex align-items-center justify-content-between p-2 mb-1 border rounded-2 bg-light-subtle';
            item.dataset.name = (name + ' ' + email + ' ' + title).toLowerCase();
            item.dataset.appId = appId;

            item.innerHTML = `
                <div class="form-check d-flex align-items-center gap-2 mb-0" style="min-width: 0;">
                    <input class="form-check-input batch-cand-check flex-shrink-0" type="checkbox" value="${appId}" id="chk_${appId}" onchange="window.updateBatchSelectedCount()" style="cursor: pointer;">
                    <label class="form-check-label text-truncate" for="chk_${appId}" style="cursor: pointer; font-size: 0.82rem;">
                        <span class="fw-semibold text-dark d-block text-truncate">${name}</span>
                        <span class="text-muted small d-block text-truncate">${title} • ${email}</span>
                    </label>
                </div>
            `;
            container.appendChild(item);
        });
    }

    function filterBatchCandidatesInList() {
        var input = document.getElementById('batchCandidateSearch');
        var q = input ? input.value.trim().toLowerCase() : '';
        var items = document.querySelectorAll('#batchCandidateItems .batch-cand-item');

        items.forEach(function (el) {
            var txt = el.dataset.name || '';
            el.style.display = (!q || txt.includes(q)) ? '' : 'none';
        });
    }

    function toggleBatchSelectAll(checked) {
        var checks = document.querySelectorAll('#batchCandidateItems .batch-cand-item:not([style*="display: none"]) .batch-cand-check');
        checks.forEach(function (chk) {
            chk.checked = checked;
        });
        updateSelectedCount();
    }

    function clearBatchSelection() {
        var checks = document.querySelectorAll('#batchCandidateItems .batch-cand-check');
        checks.forEach(function (chk) {
            chk.checked = false;
        });
        var checkAll = document.getElementById('batchCheckAll');
        if (checkAll) checkAll.checked = false;
        updateSelectedCount();
    }

    function updateSelectedCount() {
        var checks = document.querySelectorAll('#batchCandidateItems .batch-cand-check:checked');
        var counter = document.getElementById('batchSelectedCount');
        if (counter) counter.textContent = checks.length;

        var err = document.getElementById('batchCandidateError');
        if (err && checks.length > 0) err.classList.add('d-none');
    }

    function updateBatchRoundTitleDefault() {
        var round = document.getElementById('batchRoundNumber');
        var title = document.getElementById('batchRoundTitle');
        if (round && title) {
            title.value = `Phỏng vấn chuyên môn tập trung - Vòng ${round.value}`;
        }
    }

    function validateBatchStartTime() {
        var input = document.getElementById('batchStartTime');
        var err = document.getElementById('batchPastDateError');
        if (!input) return true;

        var chosen = new Date(input.value);
        var now = new Date();
        if (chosen < now) {
            if (err) err.classList.remove('d-none');
            return false;
        } else {
            if (err) err.classList.add('d-none');
            return true;
        }
    }

    function onBatchInterviewTypeChange() {
        var typeSelect = document.getElementById('batchInterviewType');
        var locInput = document.getElementById('batchLocationOrLink');
        if (!typeSelect || !locInput) return;

        if (typeSelect.value === 'OFFLINE_OFFICE') {
            locInput.value = 'Phòng họp Tầng 4, Trụ sở NoveraTech';
        } else {
            locInput.value = 'https://meet.google.com/nov-batch-interview';
        }
    }

    async function generateBatchSchedulePreview() {
        var selectedChecks = Array.from(document.querySelectorAll('#batchCandidateItems .batch-cand-check:checked'));
        var candErr = document.getElementById('batchCandidateError');
        if (selectedChecks.length === 0) {
            if (candErr) candErr.classList.remove('d-none');
            return;
        }

        if (!validateBatchStartTime()) return;

        var panelistSelect = document.getElementById('batchPanelists');
        var selectedPanelists = Array.from(panelistSelect.selectedOptions).map(function (o) { return o.value; });
        var panelistErr = document.getElementById('batchPanelistError');

        if (selectedPanelists.length === 0) {
            if (panelistErr) panelistErr.classList.remove('d-none');
            return;
        } else if (panelistErr) {
            panelistErr.classList.add('d-none');
        }

        var appIds = selectedChecks.map(function (c) { return c.value; });
        var payload = {
            applicationIds: appIds,
            roundNumber: parseInt(document.getElementById('batchRoundNumber').value, 10) || 1,
            roundTitle: document.getElementById('batchRoundTitle').value.trim(),
            firstSlotStartTime: document.getElementById('batchStartTime').value,
            slotDurationMinutes: parseInt(document.getElementById('batchSlotDuration').value, 10) || 30,
            breakDurationMinutes: parseInt(document.getElementById('batchBreakDuration').value, 10) || 5,
            interviewType: document.getElementById('batchInterviewType').value,
            locationOrLink: document.getElementById('batchLocationOrLink').value.trim(),
            interviewerIds: selectedPanelists,
            notes: document.getElementById('batchNotes').value.trim()
        };

        var nextBtn = document.getElementById('btnBatchStep1Next');
        if (nextBtn) {
            nextBtn.disabled = true;
            nextBtn.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span> Đang tạo khe giờ...';
        }

        try {
            var res = await fetch('/Workspace/PreviewBatchSchedule', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': getCsrfToken()
                },
                body: JSON.stringify(payload)
            });

            var result = await res.json();
            if (!result.success) {
                alert(result.message || 'Không thể tính toán khung giờ.');
                return;
            }

            batchCurrentSlots = result.slots || [];
            document.getElementById('previewTotalCandidates').textContent = result.totalCandidates || batchCurrentSlots.length;
            document.getElementById('previewOverallTime').textContent = result.overallTimeDisplay || '--:--';

            renderPreviewSlotsTable();

            document.getElementById('batchSchedStep1').classList.add('d-none');
            document.getElementById('batchSchedStep2').classList.remove('d-none');

            document.getElementById('btnBatchStep1Next').classList.add('d-none');
            document.getElementById('btnBatchStep2Back').classList.remove('d-none');
            document.getElementById('btnBatchConfirmSubmit').classList.remove('d-none');

            var badge1 = document.getElementById('badgeStep1');
            var badge2 = document.getElementById('badgeStep2');
            if (badge1 && badge2) {
                badge1.className = 'badge px-3 py-2 fw-semibold text-muted bg-white border';
                badge2.className = 'badge px-3 py-2 fw-semibold text-white bg-success';
            }
        } catch (e) {
            console.error('Lỗi khi xem trước ca phỏng vấn:', e);
            alert('Lỗi kết nối máy chủ khi tính toán khung giờ.');
        } finally {
            if (nextBtn) {
                nextBtn.disabled = false;
                nextBtn.innerHTML = 'Xem trước toàn bộ khe giờ <i class="bi bi-arrow-right ms-1"></i>';
            }
        }
    }

    function renderPreviewSlotsTable() {
        var tbody = document.getElementById('batchPreviewSlotsTableBody');
        if (!tbody) return;
        tbody.innerHTML = '';

        batchCurrentSlots.forEach(function (slot, idx) {
            var tr = document.createElement('tr');
            tr.innerHTML = `
                <td class="text-center font-monospace fw-bold text-muted">${idx + 1}</td>
                <td>
                    <span class="badge bg-light text-dark border font-monospace px-2 py-1" style="border-radius: 6px;">
                        ${slot.timeRangeDisplay || (slot.startTime + ' - ' + slot.endTime)}
                    </span>
                </td>
                <td>
                    <div class="fw-semibold text-dark">${slot.candidateName || 'Ứng viên'}</div>
                </td>
                <td>
                    <span class="text-muted small">${slot.jobTitle || 'Vị trí'}</span>
                </td>
                <td>
                    <span class="text-muted small font-monospace">${slot.candidateEmail || ''}</span>
                </td>
                <td class="text-center">
                    <div class="btn-group btn-group-sm">
                        <button type="button" class="btn btn-outline-secondary px-2 py-0.5" title="Chuyển lên" ${idx === 0 ? 'disabled' : ''} onclick="window.moveBatchSlot(${idx}, -1)">
                            <i class="bi bi-arrow-up"></i>
                        </button>
                        <button type="button" class="btn btn-outline-secondary px-2 py-0.5" title="Chuyển xuống" ${idx === batchCurrentSlots.length - 1 ? 'disabled' : ''} onclick="window.moveBatchSlot(${idx}, 1)">
                            <i class="bi bi-arrow-down"></i>
                        </button>
                    </div>
                </td>
            `;
            tbody.appendChild(tr);
        });
    }

    function moveBatchSlot(index, direction) {
        var targetIndex = index + direction;
        if (targetIndex < 0 || targetIndex >= batchCurrentSlots.length) return;

        var tempCandidate = {
            candidateName: batchCurrentSlots[index].candidateName,
            candidateEmail: batchCurrentSlots[index].candidateEmail,
            candidatePhone: batchCurrentSlots[index].candidatePhone,
            applicationId: batchCurrentSlots[index].applicationId,
            jobTitle: batchCurrentSlots[index].jobTitle
        };

        batchCurrentSlots[index].candidateName = batchCurrentSlots[targetIndex].candidateName;
        batchCurrentSlots[index].candidateEmail = batchCurrentSlots[targetIndex].candidateEmail;
        batchCurrentSlots[index].candidatePhone = batchCurrentSlots[targetIndex].candidatePhone;
        batchCurrentSlots[index].applicationId = batchCurrentSlots[targetIndex].applicationId;
        batchCurrentSlots[index].jobTitle = batchCurrentSlots[targetIndex].jobTitle;

        batchCurrentSlots[targetIndex].candidateName = tempCandidate.candidateName;
        batchCurrentSlots[targetIndex].candidateEmail = tempCandidate.candidateEmail;
        batchCurrentSlots[targetIndex].candidatePhone = tempCandidate.candidatePhone;
        batchCurrentSlots[targetIndex].applicationId = tempCandidate.applicationId;
        batchCurrentSlots[targetIndex].jobTitle = tempCandidate.jobTitle;

        renderPreviewSlotsTable();
    }

    function backToBatchStep1() {
        document.getElementById('batchSchedStep2').classList.add('d-none');
        document.getElementById('batchSchedStep1').classList.remove('d-none');

        document.getElementById('btnBatchStep1Next').classList.remove('d-none');
        document.getElementById('btnBatchStep2Back').classList.add('d-none');
        document.getElementById('btnBatchConfirmSubmit').classList.add('d-none');

        var badge1 = document.getElementById('badgeStep1');
        var badge2 = document.getElementById('badgeStep2');
        if (badge1 && badge2) {
            badge1.className = 'badge px-3 py-2 fw-semibold text-white bg-success';
            badge2.className = 'badge px-3 py-2 fw-semibold text-muted bg-white border';
        }
    }

    async function submitConfirmBatchSchedule() {
        if (!batchCurrentSlots || batchCurrentSlots.length === 0) {
            alert('Chưa có danh sách ca phỏng vấn nào để xác nhận.');
            return;
        }

        var panelistSelect = document.getElementById('batchPanelists');
        var selectedPanelists = Array.from(panelistSelect.selectedOptions).map(function (o) { return o.value; });

        var payload = {
            roundNumber: parseInt(document.getElementById('batchRoundNumber').value, 10) || 1,
            roundTitle: document.getElementById('batchRoundTitle').value.trim(),
            interviewType: document.getElementById('batchInterviewType').value,
            locationOrLink: document.getElementById('batchLocationOrLink').value.trim(),
            interviewerIds: selectedPanelists,
            notes: document.getElementById('batchNotes').value.trim(),
            confirmedSlots: batchCurrentSlots.map(function (s, idx) {
                return {
                    slotOrder: idx + 1,
                    applicationId: s.applicationId,
                    startTime: s.startTime,
                    endTime: s.endTime
                };
            })
        };

        var btn = document.getElementById('btnBatchConfirmSubmit');
        var spinner = document.getElementById('spinnerBatchConfirm');
        var icon = document.getElementById('iconBatchConfirm');

        if (btn) btn.disabled = true;
        if (spinner) spinner.classList.remove('d-none');
        if (icon) icon.classList.add('d-none');

        try {
            var res = await fetch('/Workspace/ConfirmBatchSchedule', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': getCsrfToken()
                },
                body: JSON.stringify(payload)
            });

            var result = await res.json();
            if (!result.success) {
                alert(result.message || 'Lỗi khi xác nhận đặt lịch.');
                return;
            }

            var modal = getBatchModal();
            if (modal) modal.hide();

            if (window.NoveraToast) {
                window.NoveraToast.show(result.message || 'Đặt lịch phỏng vấn tập trung thành công!', 'success');
            } else {
                alert(result.message || 'Đặt lịch phỏng vấn tập trung thành công!');
            }

            setTimeout(function () {
                window.location.reload();
            }, 800);
        } catch (e) {
            console.error('Lỗi xác nhận lịch hàng loạt:', e);
            alert('Lỗi kết nối máy chủ.');
        } finally {
            if (btn) btn.disabled = false;
            if (spinner) spinner.classList.add('d-none');
            if (icon) icon.classList.remove('d-none');
        }
    }

    function openBatchScheduleModal() {
        var modal = getBatchModal();
        if (!modal) return;

        initDefaultDateTime();
        loadInterviewers();
        loadBatchCandidateList();
        backToBatchStep1();

        modal.show();
    }

    // Expose global functions
    window.openBatchScheduleModal = openBatchScheduleModal;
    window.loadBatchCandidateList = loadBatchCandidateList;
    window.filterBatchCandidatesInList = filterBatchCandidatesInList;
    window.toggleBatchSelectAll = toggleBatchSelectAll;
    window.clearBatchSelection = clearBatchSelection;
    window.updateBatchSelectedCount = updateSelectedCount;
    window.updateBatchRoundTitleDefault = updateBatchRoundTitleDefault;
    window.validateBatchStartTime = validateBatchStartTime;
    window.onBatchInterviewTypeChange = onBatchInterviewTypeChange;
    window.generateBatchSchedulePreview = generateBatchSchedulePreview;
    window.backToBatchStep1 = backToBatchStep1;
    window.submitConfirmBatchSchedule = submitConfirmBatchSchedule;
    window.moveBatchSlot = moveBatchSlot;

})(window, document);
