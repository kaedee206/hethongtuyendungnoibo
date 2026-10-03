document.addEventListener('DOMContentLoaded', function () {
    const configureModalEl = document.getElementById('configureRubricModal');
    const interviewSheetModalEl = document.getElementById('interviewSheetModal');
    const rubricModalLoading = document.getElementById('rubricModalLoading');
    const rubricModalContent = document.getElementById('rubricModalContent');
    const rubricCriteriaIdInput = document.getElementById('rubricCriteriaId');
    const rubricModalTitle = document.getElementById('rubricModalTitle');
    const rubricModalBadge = document.getElementById('rubricModalBadge');
    const rubricLevelsBody = document.getElementById('rubricLevelsBody');
    const rubricLevelsCountBadge = document.getElementById('rubricLevelsCountBadge');
    const rubricValidationSummary = document.getElementById('rubricValidationSummary');
    const rubricValidationSummaryText = document.getElementById('rubricValidationSummaryText');
    const btnSaveRubric = document.getElementById('btnSaveRubric');
    const saveRubricSpinner = document.getElementById('saveRubricSpinner');
    const saveRubricIcon = document.getElementById('saveRubricIcon');
    const btnFillSmartTemplate = document.getElementById('btnFillSmartTemplate');
    const interviewSheetContent = document.getElementById('interviewSheetContent');

    let currentRubricData = null;
    let configureModalInstance = null;

    if (configureModalEl && window.bootstrap) {
        configureModalInstance = new bootstrap.Modal(configureModalEl);
    }

    const defaultNamesMap = {
        1: 'Chưa đạt yêu cầu',
        2: 'Dưới kỳ vọng',
        3: 'Đạt yêu cầu (Chuẩn)',
        4: 'Tốt / Vượt kỳ vọng',
        5: 'Xuất sắc / Chuyên gia'
    };

    document.querySelectorAll('.configure-rubric-btn').forEach(function (button) {
        button.addEventListener('click', function () {
            const criteriaId = this.getAttribute('data-criteria-id');
            const criteriaName = this.getAttribute('data-criteria-name') || 'Tiêu chí';

            if (!criteriaId) return;

            openRubricModal(criteriaId, criteriaName);
        });
    });

    document.querySelectorAll('input[name="scaleMaxRadio"]').forEach(function (radio) {
        radio.addEventListener('change', function () {
            const newScaleMax = parseInt(this.value, 10);
            updateVisibleLevels(newScaleMax);
        });
    });

    if (btnFillSmartTemplate) {
        btnFillSmartTemplate.addEventListener('click', function () {
            fillSmartTemplate();
        });
    }

    if (btnSaveRubric) {
        btnSaveRubric.addEventListener('click', function () {
            saveRubricConfig();
        });
    }

    if (interviewSheetModalEl) {
        interviewSheetModalEl.addEventListener('show.bs.modal', function () {
            loadInterviewSheetData();
        });
    }

    function openRubricModal(criteriaId, criteriaName) {
        if (rubricValidationSummary) rubricValidationSummary.classList.add('d-none');
        if (rubricModalTitle) rubricModalTitle.textContent = `Cấu hình thang điểm & Rubric: ${criteriaName}`;
        if (rubricModalBadge) rubricModalBadge.textContent = 'ĐANG TẢI...';
        if (rubricModalLoading) rubricModalLoading.classList.remove('d-none');
        if (rubricModalContent) rubricModalContent.classList.add('d-none');

        if (configureModalInstance) {
            configureModalInstance.show();
        }

        fetch(`/api/evaluation-criteria/${criteriaId}/rubric`)
            .then(function (res) {
                if (!res.ok) throw new Error('Không thể tải dữ liệu tiêu chí.');
                return res.json();
            })
            .then(function (res) {
                if (!res.success || !res.data) throw new Error(res.message || 'Lỗi dữ liệu');
                currentRubricData = res.data;
                populateRubricForm(res.data);
            })
            .catch(function (err) {
                alert(err.message || 'Đã xảy ra lỗi khi kết nối máy chủ.');
                if (configureModalInstance) configureModalInstance.hide();
            });
    }

    function populateRubricForm(data) {
        if (rubricCriteriaIdInput) rubricCriteriaIdInput.value = data.criteriaId;
        if (rubricModalBadge) rubricModalBadge.textContent = `${data.criteriaType || 'CRITERIA'} · HỆ SỐ ${data.weight || 1.0}x`;

        const scaleMax = data.scaleMax || 5;
        const targetRadio = document.querySelector(`input[name="scaleMaxRadio"][value="${scaleMax}"]`);
        if (targetRadio) {
            targetRadio.checked = true;
        }

        renderLevelRows(data.levels || [], scaleMax);

        if (rubricModalLoading) rubricModalLoading.classList.add('d-none');
        if (rubricModalContent) rubricModalContent.classList.remove('d-none');
    }

    function renderLevelRows(levels, scaleMax) {
        if (!rubricLevelsBody) return;
        rubricLevelsBody.innerHTML = '';

        if (rubricLevelsCountBadge) {
            rubricLevelsCountBadge.textContent = `${scaleMax} mức điểm (1–${scaleMax})`;
        }

        const existingLevelsMap = {};
        levels.forEach(function (l) {
            existingLevelsMap[l.score || l.Score] = l;
        });

        for (let score = 1; score <= scaleMax; score++) {
            const levelData = existingLevelsMap[score] || {};
            const levelName = levelData.levelName || levelData.LevelName || defaultNamesMap[score] || `Mức ${score}`;
            const desc = levelData.behavioralDescription || levelData.BehavioralDescription || '';
            const isRequired = levelData.isRequired !== undefined ? levelData.isRequired : true;

            const tr = document.createElement('tr');
            tr.setAttribute('data-score', score.toString());
            tr.className = 'level-row';

            tr.innerHTML = `
                <td class="text-center ps-3">
                    <span class="rubric-score-badge score-badge-${score}">${score}</span>
                </td>
                <td>
                    <input type="text" 
                           class="form-control form-control-sm level-name-input fw-semibold text-dark" 
                           style="border-radius: 8px;" 
                           value="${escapeHtml(levelName)}" 
                           placeholder="Tên mức ${score}" 
                           required />
                    <div class="form-text text-muted small mt-1">Mức điểm ${score}</div>
                </td>
                <td>
                    <textarea class="form-control form-control-sm behavioral-desc-input" 
                              rows="3" 
                              style="border-radius: 8px; resize: vertical;" 
                              placeholder="Mô tả cụ thể hành vi, kiến thức hoặc kỹ năng ứng viên thể hiện ở mức điểm ${score}..." 
                              required>${escapeHtml(desc)}</textarea>
                    <div class="d-flex justify-content-between align-items-center mt-1">
                        <span class="text-danger small desc-error-msg d-none">
                            <i class="bi bi-x-circle me-1"></i>Bắt buộc nhập mô tả tối thiểu 10 ký tự.
                        </span>
                        <span class="text-muted small ms-auto char-counter font-monospace">${desc.length} ký tự</span>
                    </div>
                </td>
                <td class="text-center">
                    <span class="badge bg-danger-subtle text-danger border border-danger-subtle px-2 py-1" style="border-radius: 6px;">
                        Bắt buộc
                    </span>
                    <input type="hidden" class="level-required-input" value="true" />
                </td>
            `;

            rubricLevelsBody.appendChild(tr);
        }

        attachDescEventListeners();
    }

    function updateVisibleLevels(newScaleMax) {
        if (!currentRubricData) return;

        const currentRowsData = collectCurrentRowsData();
        renderLevelRows(currentRowsData, newScaleMax);
    }

    function collectCurrentRowsData() {
        const results = [];
        const rows = document.querySelectorAll('#rubricLevelsBody tr.level-row');

        rows.forEach(function (tr) {
            const score = parseInt(tr.getAttribute('data-score'), 10);
            const nameInput = tr.querySelector('.level-name-input');
            const descInput = tr.querySelector('.behavioral-desc-input');
            const reqInput = tr.querySelector('.level-required-input');

            results.push({
                score: score,
                levelName: nameInput ? nameInput.value.trim() : defaultNamesMap[score],
                behavioralDescription: descInput ? descInput.value.trim() : '',
                isRequired: reqInput ? reqInput.value === 'true' : true
            });
        });

        return results;
    }

    function attachDescEventListeners() {
        document.querySelectorAll('.behavioral-desc-input').forEach(function (textarea) {
            textarea.addEventListener('input', function () {
                const tr = this.closest('tr');
                const counter = tr.querySelector('.char-counter');
                const errorMsg = tr.querySelector('.desc-error-msg');
                const length = this.value.trim().length;

                if (counter) counter.textContent = `${length} ký tự`;

                if (length >= 10) {
                    this.classList.remove('is-invalid');
                    this.classList.add('is-valid');
                    if (errorMsg) errorMsg.classList.add('d-none');
                } else {
                    this.classList.remove('is-valid');
                }
            });

            textarea.addEventListener('blur', function () {
                const tr = this.closest('tr');
                const errorMsg = tr.querySelector('.desc-error-msg');
                const length = this.value.trim().length;

                if (length < 10) {
                    this.classList.add('is-invalid');
                    if (errorMsg) errorMsg.classList.remove('d-none');
                }
            });
        });
    }

    function fillSmartTemplate() {
        if (!currentRubricData) return;

        const criteriaType = currentRubricData.criteriaType || 'HARD_SKILL';
        const rows = document.querySelectorAll('#rubricLevelsBody tr.level-row');

        rows.forEach(function (tr) {
            const score = parseInt(tr.getAttribute('data-score'), 10);
            const nameInput = tr.querySelector('.level-name-input');
            const descInput = tr.querySelector('.behavioral-desc-input');
            const counter = tr.querySelector('.char-counter');
            const errorMsg = tr.querySelector('.desc-error-msg');

            if (nameInput) nameInput.value = defaultNamesMap[score] || `Mức ${score}`;

            let templateText = '';
            if (criteriaType === 'HARD_SKILL') {
                if (score === 1) templateText = 'Chưa nắm vững khái niệm kỹ thuật cốt lõi; không giải thích được luồng thực thi hoặc vi phạm các nguyên lý thiết kế cơ bản.';
                else if (score === 2) templateText = 'Nắm kiến thức ở mức cơ bản nhưng còn lúng túng khi gặp bài toán thực tế; thiếu nhận thức về tối ưu tài nguyên và bẫy lỗi thường gặp.';
                else if (score === 3) templateText = 'Nắm vững lý thuyết và thực hành, viết mã nguồn sạch sẽ, tuân thủ đúng kiến trúc chuẩn và xử lý an toàn các ngoại lệ.';
                else if (score === 4) templateText = 'Chủ động đề xuất giải pháp tối ưu hiệu năng, am hiểu sâu cơ chế bên dưới của runtime, kiến trúc chịu tải và bảo mật.';
                else if (score === 5) templateText = 'Trình độ chuyên gia, tư duy thiết kế giải pháp cấp Enterprise, có khả năng dẫn dắt kỹ thuật và phản biện xuất sắc các trade-off.';
            } else if (criteriaType === 'SOFT_SKILL') {
                if (score === 1) templateText = 'Trình bày lan man, khó diễn đạt ý tưởng kỹ thuật; phản ứng phòng thủ hoặc né tránh khi bị chất vấn phản biện.';
                else if (score === 2) templateText = 'Giao tiếp được nhưng thụ động; kỹ năng lắng nghe còn hạn chế và cần người khác hướng dẫn chi tiết để làm rõ vấn đề.';
                else if (score === 3) templateText = 'Trình bày mạch lạc, logic, biết lắng nghe tích cực; phối hợp tốt với các thành viên khác trong nhóm.';
                else if (score === 4) templateText = 'Giao tiếp thuyết phục, truyền đạt vấn đề phức tạp thành đơn giản, có khả năng điều phối và kết nối nhóm hiệu quả.';
                else if (score === 5) templateText = 'Kỹ năng truyền cảm hứng, dẫn dắt thảo luận, giải quyết xung đột xuất sắc và xây dựng sự đồng thuận cao trong đội ngũ.';
            } else {
                if (score === 1) templateText = 'Thiếu tinh thần trách nhiệm, đổ lỗi cho hoàn cảnh hoặc không quan tâm đến tiêu chuẩn chất lượng của công việc bàn giao.';
                else if (score === 2) templateText = 'Hoàn thành công việc khi được phân công nhưng thiếu tính chủ động; tinh thần cam kết chỉ ở mức trung bình.';
                else if (score === 3) templateText = 'Thể hiện tinh thần trách nhiệm rõ ràng với sản phẩm, tôn trọng văn hóa công ty và giữ cam kết trong công việc.';
                else if (score === 4) templateText = 'Chủ động nhận việc khó, tinh thần sở hữu cao (Ownership), luôn hướng đến chuẩn mực kỹ thuật cao nhất.';
                else if (score === 5) templateText = 'Là tấm gương về văn hóa công nghệ "Engineering-First, Zero Politics", chủ động lan tỏa năng lượng tích cực và bảo vệ uy tín tập thể.';
            }

            if (descInput) {
                descInput.value = templateText;
                descInput.classList.remove('is-invalid');
                descInput.classList.add('is-valid');
            }
            if (counter) counter.textContent = `${templateText.length} ký tự`;
            if (errorMsg) errorMsg.classList.add('d-none');
        });

        if (rubricValidationSummary) rubricValidationSummary.classList.add('d-none');
    }

    function saveRubricConfig() {
        const criteriaId = rubricCriteriaIdInput ? rubricCriteriaIdInput.value : '';
        if (!criteriaId) return;

        const selectedRadio = document.querySelector('input[name="scaleMaxRadio"]:checked');
        const scaleMax = selectedRadio ? parseInt(selectedRadio.value, 10) : 5;

        const levels = [];
        let firstInvalidInput = null;
        let validationError = null;

        const rows = document.querySelectorAll('#rubricLevelsBody tr.level-row');
        rows.forEach(function (tr) {
            const score = parseInt(tr.getAttribute('data-score'), 10);
            if (score > scaleMax) return;

            const nameInput = tr.querySelector('.level-name-input');
            const descInput = tr.querySelector('.behavioral-desc-input');
            const errorMsg = tr.querySelector('.desc-error-msg');

            const name = nameInput ? nameInput.value.trim() : `Mức ${score}`;
            const desc = descInput ? descInput.value.trim() : '';

            if (!desc || desc.length < 10) {
                if (descInput) descInput.classList.add('is-invalid');
                if (errorMsg) errorMsg.classList.remove('d-none');

                if (!firstInvalidInput) {
                    firstInvalidInput = descInput;
                    validationError = `Mức ${score} (${name}): Bắt buộc nhập mô tả hành vi chi tiết ít nhất 10 ký tự.`;
                }
            } else {
                if (descInput) {
                    descInput.classList.remove('is-invalid');
                    descInput.classList.add('is-valid');
                }
                if (errorMsg) errorMsg.classList.add('d-none');
            }

            levels.push({
                score: score,
                levelName: name,
                behavioralDescription: desc,
                isRequired: true
            });
        });

        if (validationError) {
            if (rubricValidationSummary && rubricValidationSummaryText) {
                rubricValidationSummaryText.textContent = validationError;
                rubricValidationSummary.classList.remove('d-none');
            }
            if (firstInvalidInput) {
                firstInvalidInput.focus();
            }
            return;
        }

        if (rubricValidationSummary) rubricValidationSummary.classList.add('d-none');

        const payload = {
            criteriaId: criteriaId,
            scaleMin: 1,
            scaleMax: scaleMax,
            levels: levels
        };

        if (btnSaveRubric) btnSaveRubric.disabled = true;
        if (saveRubricSpinner) saveRubricSpinner.classList.remove('d-none');
        if (saveRubricIcon) saveRubricIcon.classList.add('d-none');

        fetch('/evaluation-criteria/save-rubric', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify(payload)
        })
            .then(function (res) {
                return res.json().then(function (data) {
                    return { ok: res.ok, data: data };
                });
            })
            .then(function (result) {
                if (!result.ok || !result.data.success) {
                    throw new Error(result.data.message || 'Lỗi khi lưu cấu hình.');
                }

                if (configureModalInstance) configureModalInstance.hide();
                window.location.reload();
            })
            .catch(function (err) {
                if (rubricValidationSummary && rubricValidationSummaryText) {
                    rubricValidationSummaryText.textContent = err.message || 'Không thể lưu rubric.';
                    rubricValidationSummary.classList.remove('d-none');
                }
            })
            .finally(function () {
                if (btnSaveRubric) btnSaveRubric.disabled = false;
                if (saveRubricSpinner) saveRubricSpinner.classList.add('d-none');
                if (saveRubricIcon) saveRubricIcon.classList.remove('d-none');
            });
    }

    function loadInterviewSheetData() {
        if (!interviewSheetContent) return;
        interviewSheetContent.innerHTML = `
            <div class="text-center py-5">
                <div class="spinner-border text-success" role="status">
                    <span class="visually-hidden">Đang tải...</span>
                </div>
                <div class="mt-2 text-muted small">Đang tải phiếu phỏng vấn và dữ liệu rubric...</div>
            </div>
        `;

        fetch('/evaluation-criteria/api/interview-sheet')
            .then(function (res) {
                if (!res.ok) throw new Error('Không thể tải phiếu phỏng vấn');
                return res.json();
            })
            .then(function (res) {
                if (!res.success || !res.data) throw new Error(res.message || 'Lỗi dữ liệu');
                renderInterviewSheet(res.data.criteriaRubrics || []);
            })
            .catch(function (err) {
                interviewSheetContent.innerHTML = `
                    <div class="alert alert-danger py-3 px-4">
                        <i class="bi bi-exclamation-triangle-fill me-2"></i>${err.message || 'Lỗi tải phiếu phỏng vấn'}
                    </div>
                `;
            });
    }

    function renderInterviewSheet(criteriaRubrics) {
        if (!interviewSheetContent) return;
        interviewSheetContent.innerHTML = '';

        if (!criteriaRubrics || criteriaRubrics.length === 0) {
            interviewSheetContent.innerHTML = '<div class="text-center py-4 text-muted">Chưa có tiêu chí phỏng vấn nào.</div>';
            return;
        }

        criteriaRubrics.forEach(function (rubric, idx) {
            const card = document.createElement('div');
            card.className = 'card border p-3 shadow-sm';
            card.style.borderRadius = '10px';

            const defaultScore = 3;
            const defaultLevel = (rubric.levels || []).find(function (l) { return l.score === defaultScore; }) || (rubric.levels && rubric.levels[0]) || {};

            let buttonsHtml = '';
            (rubric.levels || []).forEach(function (l) {
                const isActive = l.score === defaultScore;
                buttonsHtml += `
                    <button type="button" 
                            class="btn btn-outline-secondary btn-sm score-btn-selector ${isActive ? 'active' : ''}" 
                            data-criteria-index="${idx}" 
                            data-score="${l.score}">
                        ${l.score}
                    </button>
                `;
            });

            card.innerHTML = `
                <div class="d-flex justify-content-between align-items-start flex-wrap gap-2 mb-2">
                    <div>
                        <span class="badge bg-light text-secondary border font-monospace me-1">${rubric.criteriaType || 'SKILL'}</span>
                        <span class="fw-bold text-dark fs-6">${rubric.criteriaName}</span>
                        <span class="badge bg-light text-dark border font-monospace ms-2">Hệ số: ${rubric.weight}x</span>
                    </div>
                    <div class="d-flex align-items-center gap-1">
                        <span class="small text-secondary me-2">Chấm điểm:</span>
                        <div class="btn-group" role="group">
                            ${buttonsHtml}
                        </div>
                    </div>
                </div>

                <div class="p-3 rounded-2 border behavioral-card-active mt-2" id="behavioralCard_${idx}">
                    <div class="d-flex align-items-center gap-2 mb-1">
                        <span class="rubric-score-badge score-badge-${defaultLevel.score || 3}" id="behavioralScoreBadge_${idx}" style="width: 26px; height: 26px; font-size: 0.8rem;">
                            ${defaultLevel.score || 3}
                        </span>
                        <strong class="text-dark" id="behavioralLevelName_${idx}">
                            ${escapeHtml(defaultLevel.levelName || 'Đạt yêu cầu')}
                        </strong>
                        <span class="badge bg-light text-muted border font-monospace ms-auto" style="font-size: 0.75rem;">Mô tả hành vi quan sát</span>
                    </div>
                    <div class="text-secondary small mt-1" id="behavioralText_${idx}">
                        ${escapeHtml(defaultLevel.behavioralDescription || 'Chưa có mô tả cụ thể')}
                    </div>
                </div>
            `;

            interviewSheetContent.appendChild(card);
        });

        document.querySelectorAll('.score-btn-selector').forEach(function (btn) {
            btn.addEventListener('click', function () {
                const critIdx = parseInt(this.getAttribute('data-criteria-index'), 10);
                const score = parseInt(this.getAttribute('data-score'), 10);
                const rubric = criteriaRubrics[critIdx];
                if (!rubric) return;

                const parentGroup = this.closest('.btn-group');
                if (parentGroup) {
                    parentGroup.querySelectorAll('.score-btn-selector').forEach(function (b) {
                        b.classList.remove('active');
                    });
                }
                this.classList.add('active');

                const targetLevel = (rubric.levels || []).find(function (l) { return l.score === score; });
                if (!targetLevel) return;

                const badgeEl = document.getElementById(`behavioralScoreBadge_${critIdx}`);
                const nameEl = document.getElementById(`behavioralLevelName_${critIdx}`);
                const textEl = document.getElementById(`behavioralText_${critIdx}`);

                if (badgeEl) {
                    badgeEl.className = `rubric-score-badge score-badge-${score}`;
                    badgeEl.textContent = score;
                }
                if (nameEl) nameEl.textContent = targetLevel.levelName || `Mức ${score}`;
                if (textEl) textEl.textContent = targetLevel.behavioralDescription || 'Chưa có mô tả cụ thể';
            });
        });
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
        return text.toString().replace(/[&<>"']/g, function (m) { return map[m]; });
    }
});
