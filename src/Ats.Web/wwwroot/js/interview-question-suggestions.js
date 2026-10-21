/**
 * interview-question-suggestions.js
 * User Story SP-7: Gợi ý bộ câu hỏi phỏng vấn theo tiêu chí và chức danh.
 * Tích hợp trực tiếp danh sách câu hỏi gợi ý, gợi ý câu trả lời chuẩn (rubric) và cơ chế đánh dấu tránh trùng lặp vào màn hình phỏng vấn.
 * Quy chuẩn: Vanilla JS, No external dependencies, Toast/Alert notifications, Responsive Offcanvas UX.
 */

document.addEventListener('DOMContentLoaded', function () {
    initInterviewQuestionSuggestions();
});

let _allInterviewQuestions = [];
let _activeCriteriaFilter = '';
let _activeStatusFilter = 'all'; // all, unasked, current, previous
let _searchKeyword = '';
let _interviewQuestionsLoaded = false;

function initInterviewQuestionSuggestions() {
    const container = document.getElementById('offcanvasInterviewQuestions');
    if (!container) return;

    const candidateId = container.getAttribute('data-candidate-id');
    const interviewId = container.getAttribute('data-interviewId') || container.getAttribute('data-interview-id');
    const positionId = container.getAttribute('data-position-id') || '';

    if (!candidateId || !interviewId) {
        return;
    }

    // Gắn sự kiện khi mở Offcanvas để tải dữ liệu lần đầu
    container.addEventListener('show.bs.offcanvas', function () {
        if (!_interviewQuestionsLoaded) {
            loadSuggestedQuestions(candidateId, interviewId, positionId);
        }
    });

    // Tự động tải trước dữ liệu ngầm để cập nhật số lượng câu hỏi trên nút bấm
    loadSuggestedQuestions(candidateId, interviewId, positionId);

    // Gắn sự kiện cho các bộ lọc
    const criteriaSelect = document.getElementById('filter-question-criteria');
    if (criteriaSelect) {
        criteriaSelect.addEventListener('change', function () {
            _activeCriteriaFilter = this.value;
            applyQuestionFilters();
        });
    }

    const searchInput = document.getElementById('search-question-keyword');
    if (searchInput) {
        searchInput.addEventListener('input', function () {
            _searchKeyword = this.value.trim().toLowerCase();
            applyQuestionFilters();
        });
    }

    // Các nút lọc trạng thái (Pills)
    const statusPills = document.querySelectorAll('.filter-question-status-pill');
    statusPills.forEach(pill => {
        pill.addEventListener('click', function () {
            statusPills.forEach(p => p.classList.remove('active', 'btn-primary', 'text-white'));
            statusPills.forEach(p => p.classList.add('btn-light', 'text-secondary'));

            this.classList.remove('btn-light', 'text-secondary');
            this.classList.add('active', 'btn-primary', 'text-white');

            _activeStatusFilter = this.getAttribute('data-status') || 'all';
            applyQuestionFilters();
        });
    });
}

/**
 * Tải danh sách câu hỏi gợi ý từ Backend API
 */
async function loadSuggestedQuestions(candidateId, interviewId, positionId) {
    const listContainer = document.getElementById('suggested-questions-list');
    const loadingSpinner = document.getElementById('questions-loading-spinner');

    if (loadingSpinner) loadingSpinner.classList.remove('d-none');

    try {
        let url = `/api/interview-questions/suggestions?candidateId=${encodeURIComponent(candidateId)}&interviewId=${encodeURIComponent(interviewId)}`;
        if (positionId) {
            url += `&positionId=${encodeURIComponent(positionId)}`;
        }

        const response = await fetch(url, {
            method: 'GET',
            headers: {
                'Accept': 'application/json',
                'X-Requested-With': 'XMLHttpRequest'
            }
        });

        if (!response.ok) {
            throw new Error(`HTTP error ${response.status}`);
        }

        const json = await response.json();
        if (json.success && json.data) {
            _allInterviewQuestions = json.data.questions || [];
            _interviewQuestionsLoaded = true;

            // Cập nhật các bộ lọc và thống kê
            updateQuestionsHeaderStats(json.data);
            populateCriteriaFilterOptions(json.data.criteriaOptions || []);
            applyQuestionFilters();
        } else {
            showQuestionsErrorMessage('Không thể tải danh sách câu hỏi gợi ý.');
        }
    } catch (err) {
        console.error('[SP-7] Lỗi khi tải câu hỏi gợi ý:', err);
        showQuestionsErrorMessage('Đã xảy ra lỗi kết nối khi tải danh sách câu hỏi.');
    } finally {
        if (loadingSpinner) loadingSpinner.classList.add('d-none');
    }
}

/**
 * Cập nhật số liệu thống kê trên header và nút bấm
 */
function updateQuestionsHeaderStats(data) {
    const totalCount = data.totalQuestions || 0;
    const unaskedCount = data.unaskedCount || 0;
    const askedCurrentCount = data.askedInCurrentRoundCount || 0;
    const askedPrevCount = data.askedInPreviousRoundsCount || 0;

    // Badges trên nút trigger
    const headerBadge = document.getElementById('header-question-count-badge');
    if (headerBadge) headerBadge.textContent = totalCount;

    const floatingBadge = document.getElementById('floating-question-count-badge');
    if (floatingBadge) floatingBadge.textContent = totalCount;

    // Badges trong Offcanvas
    const statTotal = document.getElementById('stat-total-questions');
    if (statTotal) statTotal.textContent = totalCount;

    const statUnasked = document.getElementById('stat-unasked-questions');
    if (statUnasked) statUnasked.textContent = unaskedCount;

    const statCurrent = document.getElementById('stat-current-round-questions');
    if (statCurrent) statCurrent.textContent = askedCurrentCount;

    const statPrev = document.getElementById('stat-previous-rounds-questions');
    if (statPrev) statPrev.textContent = askedPrevCount;

    // Badges trên pills
    const pillAllBadge = document.getElementById('pill-count-all');
    if (pillAllBadge) pillAllBadge.textContent = totalCount;

    const pillUnaskedBadge = document.getElementById('pill-count-unasked');
    if (pillUnaskedBadge) pillUnaskedBadge.textContent = unaskedCount;

    const pillCurrentBadge = document.getElementById('pill-count-current');
    if (pillCurrentBadge) pillCurrentBadge.textContent = askedCurrentCount;

    const pillPrevBadge = document.getElementById('pill-count-previous');
    if (pillPrevBadge) pillPrevBadge.textContent = askedPrevCount;
}

/**
 * Đổ dữ liệu tiêu chí vào dropdown bộ lọc
 */
function populateCriteriaFilterOptions(criteriaOptions) {
    const select = document.getElementById('filter-question-criteria');
    if (!select) return;

    const currentValue = select.value;
    select.innerHTML = '<option value="">-- Tất cả tiêu chí đánh giá --</option>';

    criteriaOptions.forEach(crit => {
        const opt = document.createElement('option');
        opt.value = crit.criteriaName;
        opt.textContent = `${crit.criteriaName} (${crit.questionCount || 0})`;
        select.appendChild(opt);
    });

    if (currentValue) {
        select.value = currentValue;
    }
}

/**
 * Lọc danh sách câu hỏi theo tiêu chí, trạng thái và từ khóa
 */
function applyQuestionFilters() {
    let filtered = _allInterviewQuestions;

    // Lọc theo Tiêu chí
    if (_activeCriteriaFilter) {
        const filterLower = _activeCriteriaFilter.toLowerCase();
        filtered = filtered.filter(q =>
            (q.criteriaName && q.criteriaName.toLowerCase() === filterLower) ||
            (q.competency && q.competency.toLowerCase() === filterLower)
        );
    }

    // Lọc theo Trạng thái (AC2)
    if (_activeStatusFilter === 'unasked') {
        filtered = filtered.filter(q => !q.isAskedInPreviousRounds && !q.isAskedInCurrentRound);
    } else if (_activeStatusFilter === 'current') {
        filtered = filtered.filter(q => q.isAskedInCurrentRound);
    } else if (_activeStatusFilter === 'previous') {
        filtered = filtered.filter(q => q.isAskedInPreviousRounds);
    }

    // Lọc theo Từ khóa tìm kiếm
    if (_searchKeyword) {
        filtered = filtered.filter(q =>
            (q.content && q.content.toLowerCase().includes(_searchKeyword)) ||
            (q.expectedAnswer && q.expectedAnswer.toLowerCase().includes(_searchKeyword)) ||
            (q.keyPoints && q.keyPoints.toLowerCase().includes(_searchKeyword)) ||
            (q.competency && q.competency.toLowerCase().includes(_searchKeyword))
        );
    }

    renderQuestionsList(filtered);
}

/**
 * Hiển thị danh sách câu hỏi ra giao diện Offcanvas (AC1, AC2, AC3, AC4)
 */
function renderQuestionsList(questions) {
    const container = document.getElementById('suggested-questions-list');
    if (!container) return;

    if (!questions || questions.length === 0) {
        container.innerHTML = `
            <div class="text-center py-5 px-3">
                <i class="bi bi-search text-muted fs-1 mb-2 d-block"></i>
                <div class="fw-semibold text-secondary">Không tìm thấy câu hỏi phù hợp</div>
                <div class="small text-muted mt-1">Thử đổi bộ lọc tiêu chí hoặc từ khóa tìm kiếm khác.</div>
            </div>
        `;
        return;
    }

    let html = '';
    questions.forEach((q, idx) => {
        const isChecked = q.isAskedInCurrentRound ? 'checked' : '';
        const diffBadgeClass = q.difficulty === 'Nâng cao' ? 'bg-danger-subtle text-danger border-danger-subtle' :
            (q.difficulty === 'Trung bình' ? 'bg-warning-subtle text-warning-emphasis border-warning-subtle' : 'bg-primary-subtle text-primary border-primary-subtle');

        // Status Badge (AC2)
        let statusBadge = '';
        if (q.isAskedInPreviousRounds) {
            statusBadge = `
                <span class="badge bg-danger-subtle text-danger border border-danger-subtle px-2 py-1 rounded-2" title="${escapeHtml(q.previousRoundBadge || '')}">
                    <i class="bi bi-exclamation-triangle-fill me-1"></i>${escapeHtml(q.previousRoundBadge || 'Đã hỏi vòng trước')}
                </span>
            `;
        } else if (q.isAskedInCurrentRound) {
            statusBadge = `
                <span class="badge bg-success-subtle text-success border border-success-subtle px-2 py-1 rounded-2" id="badge_status_${q.questionId}">
                    <i class="bi bi-check2-circle me-1"></i>Đã hỏi trong vòng này
                </span>
            `;
        } else {
            statusBadge = `
                <span class="badge bg-light text-secondary border px-2 py-1 rounded-2" id="badge_status_${q.questionId}">
                    <i class="bi bi-circle me-1"></i>Chưa hỏi
                </span>
            `;
        }

        // Good Answer Rubric HTML (AC3)
        let rubricHtml = '';
        if (q.expectedAnswer || q.keyPoints || q.goodAnswerRubric) {
            rubricHtml = `
                <div class="mt-2.5 pt-2 border-top">
                    <button class="btn btn-sm btn-link p-0 text-decoration-none d-flex align-items-center gap-1.5 text-primary fw-semibold"
                            type="button" data-bs-toggle="collapse" data-bs-target="#rubric_collapse_${q.questionId}" aria-expanded="false">
                        <i class="bi bi-journal-bookmark-fill"></i>
                        <span>Xem gợi ý câu trả lời & Rubric chuẩn</span>
                        <i class="bi bi-chevron-down small ms-auto"></i>
                    </button>
                    
                    <div class="collapse mt-2" id="rubric_collapse_${q.questionId}">
                        <div class="p-3 bg-light rounded-3 border" style="font-size: 0.8rem; line-height: 1.5;">
                            ${q.expectedAnswer ? `
                                <div class="mb-2">
                                    <span class="fw-bold text-dark d-block mb-1">
                                        <i class="bi bi-bullseye text-danger me-1"></i>Gợi ý câu trả lời mong đợi:
                                    </span>
                                    <div class="text-secondary">${escapeHtml(q.expectedAnswer)}</div>
                                </div>
                            ` : ''}

                            ${q.keyPoints ? `
                                <div class="mb-2">
                                    <span class="fw-bold text-dark d-block mb-1">
                                        <i class="bi bi-key-fill text-warning me-1"></i>Các ý chính cần đạt (Key Points):
                                    </span>
                                    <div class="text-secondary" style="white-space: pre-line;">${escapeHtml(q.keyPoints)}</div>
                                </div>
                            ` : ''}

                            ${q.goodAnswerRubric ? `
                                <div>
                                    <span class="fw-bold text-dark d-block mb-1">
                                        <i class="bi bi-bar-chart-steps text-success me-1"></i>Thước đo đánh giá (Rubric):
                                    </span>
                                    <div class="text-secondary" style="white-space: pre-line;">${escapeHtml(q.goodAnswerRubric)}</div>
                                </div>
                            ` : ''}
                        </div>
                    </div>
                </div>
            `;
        }

        html += `
            <div class="card border-0 shadow-sm rounded-3 mb-3 question-card-item ${q.isAskedInPreviousRounds ? 'border-start border-4 border-danger' : (q.isAskedInCurrentRound ? 'border-start border-4 border-success' : 'border-start border-4 border-primary-subtle')}"
                 id="card_question_${q.questionId}" style="border: 1px solid #E2E8F0 !important;">
                <div class="card-body p-3">
                    <div class="d-flex justify-content-between align-items-start gap-2 mb-2">
                        <div class="d-flex flex-wrap align-items-center gap-1.5">
                            <span class="badge ${diffBadgeClass} border px-2 py-0.5 rounded-2" style="font-size: 0.72rem;">
                                ${escapeHtml(q.difficulty)}
                            </span>
                            <span class="badge bg-light text-dark border px-2 py-0.5 rounded-2 fw-medium" style="font-size: 0.72rem;">
                                <i class="bi bi-tag me-1"></i>${escapeHtml(q.criteriaName || q.competency || 'Tiêu chuẩn')}
                            </span>
                            ${statusBadge}
                        </div>

                        <!-- Toggle Switch: Đã hỏi (AC2) -->
                        <div class="form-check form-switch m-0 d-flex align-items-center gap-1" title="Đánh dấu đã hỏi câu này trong vòng hiện tại">
                            <input class="form-check-input question-toggle-switch cursor-pointer" type="checkbox" role="switch"
                                   id="toggle_q_${q.questionId}" data-question-id="${q.questionId}" ${isChecked}
                                   onchange="handleToggleQuestionAsked('${q.questionId}', this.checked)">
                            <label class="form-check-label small fw-semibold text-muted cursor-pointer" for="toggle_q_${q.questionId}" style="font-size: 0.76rem;">
                                Đã hỏi
                            </label>
                        </div>
                    </div>

                    <!-- Question Content -->
                    <div class="fw-semibold text-dark mb-1" style="font-size: 0.88rem; line-height: 1.45;">
                        ${escapeHtml(q.content)}
                    </div>

                    <!-- Good Answer Rubric Accordion (AC3) -->
                    ${rubricHtml}

                    <!-- Footer: Nút cuộn tới tiêu chí trên phiếu chấm điểm -->
                    <div class="d-flex justify-content-between align-items-center mt-2.5 pt-2 border-top">
                        <button type="button" class="btn btn-sm btn-outline-secondary py-1 px-2.5 rounded-2 d-inline-flex align-items-center gap-1"
                                style="font-size: 0.74rem;" onclick="focusCriterionCard('${escapeHtml(q.criteriaName || q.competency)}')">
                            <i class="bi bi-arrow-down-left-square text-primary"></i>
                            <span>Chấm điểm tiêu chí này</span>
                        </button>
                        
                        <span class="text-muted small" style="font-size: 0.7rem;">
                            #${idx + 1}
                        </span>
                    </div>
                </div>
            </div>
        `;
    });

    container.innerHTML = html;
}

/**
 * Xử lý đánh dấu/bỏ đánh dấu câu hỏi đã hỏi qua API (AC2)
 */
async function handleToggleQuestionAsked(questionId, isAsked) {
    const container = document.getElementById('offcanvasInterviewQuestions');
    if (!container) return;

    const candidateId = container.getAttribute('data-candidate-id');
    const interviewId = container.getAttribute('data-interviewId') || container.getAttribute('data-interview-id');

    // Cập nhật trạng thái trong local cache
    const qIndex = _allInterviewQuestions.findIndex(q => q.questionId === questionId);
    if (qIndex >= 0) {
        _allInterviewQuestions[qIndex].isAskedInCurrentRound = isAsked;
    }

    // Cập nhật badge visual tức thì
    const badgeEl = document.getElementById(`badge_status_${questionId}`);
    if (badgeEl) {
        if (isAsked) {
            badgeEl.className = 'badge bg-success-subtle text-success border border-success-subtle px-2 py-1 rounded-2';
            badgeEl.innerHTML = '<i class="bi bi-check2-circle me-1"></i>Đã hỏi trong vòng này';
        } else {
            badgeEl.className = 'badge bg-light text-secondary border px-2 py-1 rounded-2';
            badgeEl.innerHTML = '<i class="bi bi-circle me-1"></i>Chưa hỏi';
        }
    }

    // Lấy CSRF token nếu có
    const csrfTokenEl = document.querySelector('input[name="__RequestVerificationToken"]');
    const csrfToken = csrfTokenEl ? csrfTokenEl.value : '';

    try {
        const response = await fetch('/api/interview-questions/toggle-asked', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'Accept': 'application/json',
                'X-Requested-With': 'XMLHttpRequest',
                'RequestVerificationToken': csrfToken
            },
            body: JSON.stringify({
                candidateId: candidateId,
                interviewId: interviewId,
                questionId: questionId,
                isAsked: isAsked
            })
        });

        const json = await response.json();
        if (!response.ok || !json.success) {
            throw new Error(json.message || 'Lỗi khi cập nhật câu hỏi');
        }

        // Cập nhật lại số liệu thống kê
        recalculateLocalCounters();
        showToastNotification(json.message || (isAsked ? 'Đã ghi nhận câu hỏi đã hỏi.' : 'Đã bỏ đánh dấu câu hỏi.'), 'success');
    } catch (err) {
        console.error('[SP-7] Lỗi toggle câu hỏi:', err);
        // Rollback switch
        const toggleSwitch = document.getElementById(`toggle_q_${questionId}`);
        if (toggleSwitch) toggleSwitch.checked = !isAsked;
        if (qIndex >= 0) _allInterviewQuestions[qIndex].isAskedInCurrentRound = !isAsked;

        showToastNotification('Không thể lưu trạng thái câu hỏi. Vui lòng thử lại.', 'danger');
    }
}

/**
 * Tính lại số liệu thống kê sau khi toggle
 */
function recalculateLocalCounters() {
    const total = _allInterviewQuestions.length;
    const current = _allInterviewQuestions.filter(q => q.isAskedInCurrentRound).length;
    const prev = _allInterviewQuestions.filter(q => q.isAskedInPreviousRounds).length;
    const unasked = _allInterviewQuestions.filter(q => !q.isAskedInPreviousRounds && !q.isAskedInCurrentRound).length;

    updateQuestionsHeaderStats({
        totalQuestions: total,
        unaskedCount: unasked,
        askedInCurrentRoundCount: current,
        askedInPreviousRoundsCount: prev
    });
}

/**
 * Cuộn mượt tới thẻ tiêu chí tương ứng trên phiếu chấm điểm
 */
function focusCriterionCard(criterionName) {
    if (!criterionName) return;

    const cards = document.querySelectorAll('.criterion-card');
    let matchedCard = null;

    cards.forEach(card => {
        const titleEl = card.querySelector('.criterion-title');
        if (titleEl && titleEl.textContent.toLowerCase().includes(criterionName.toLowerCase())) {
            matchedCard = card;
        }
    });

    if (matchedCard) {
        // Thu gọn offcanvas trên màn hình nhỏ để xem phiếu
        if (window.innerWidth < 992) {
            const offcanvasEl = document.getElementById('offcanvasInterviewQuestions');
            if (offcanvasEl && window.bootstrap && window.bootstrap.Offcanvas) {
                const bsOffcanvas = window.bootstrap.Offcanvas.getInstance(offcanvasEl);
                if (bsOffcanvas) bsOffcanvas.hide();
            }
        }

        matchedCard.scrollIntoView({ behavior: 'smooth', block: 'center' });
        matchedCard.style.transition = 'box-shadow 0.3s ease, border-color 0.3s ease';
        matchedCard.style.borderColor = '#3B82F6';
        matchedCard.style.boxShadow = '0 0 0 3px rgba(59, 130, 246, 0.3)';

        setTimeout(() => {
            matchedCard.style.borderColor = '#E2E8F0';
            matchedCard.style.boxShadow = '';
        }, 2000);
    }
}

/**
 * Hiển thị thông báo Toast nhanh
 */
function showToastNotification(message, type = 'info') {
    let container = document.getElementById('questions-toast-container');
    if (!container) {
        container = document.createElement('div');
        container.id = 'questions-toast-container';
        container.className = 'toast-container position-fixed bottom-0 end-0 p-3';
        container.style.zIndex = '1090';
        document.body.appendChild(container);
    }

    const toastId = `toast_${Date.now()}`;
    const bgClass = type === 'success' ? 'bg-success text-white' : (type === 'danger' ? 'bg-danger text-white' : 'bg-dark text-white');
    const iconClass = type === 'success' ? 'bi-check-circle-fill' : (type === 'danger' ? 'bi-exclamation-triangle-fill' : 'bi-info-circle-fill');

    const toastHtml = `
        <div id="${toastId}" class="toast align-items-center ${bgClass} border-0 rounded-3 shadow-sm mb-2" role="alert" aria-live="assertive" aria-atomic="true">
            <div class="d-flex">
                <div class="toast-body d-flex align-items-center gap-2 py-2 px-3 small">
                    <i class="bi ${iconClass} fs-6"></i>
                    <span>${escapeHtml(message)}</span>
                </div>
                <button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast" aria-label="Close"></button>
            </div>
        </div>
    `;

    container.insertAdjacentHTML('beforeend', toastHtml);
    const toastEl = document.getElementById(toastId);
    if (toastEl && window.bootstrap && window.bootstrap.Toast) {
        const bsToast = new window.bootstrap.Toast(toastEl, { delay: 3000 });
        bsToast.show();
        toastEl.addEventListener('hidden.bs.toast', () => toastEl.remove());
    } else {
        setTimeout(() => { if (toastEl) toastEl.remove(); }, 3000);
    }
}

function showQuestionsErrorMessage(message) {
    const listContainer = document.getElementById('suggested-questions-list');
    if (listContainer) {
        listContainer.innerHTML = `
            <div class="alert alert-danger m-3 rounded-2 small" role="alert">
                <i class="bi bi-exclamation-octagon-fill me-1"></i>${escapeHtml(message)}
            </div>
        `;
    }
}

function escapeHtml(str) {
    if (!str) return '';
    return String(str)
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#039;');
}
