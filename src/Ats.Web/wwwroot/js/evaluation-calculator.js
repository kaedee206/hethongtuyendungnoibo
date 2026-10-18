/**
 * evaluation-calculator.js
 * Xử lý tính điểm tự động theo thời gian thực (Real-time Weighted Average) cho phiếu đánh giá khung năng lực (SCRUM-56).
 * Quy chuẩn: Vanilla JS, No external dependencies, Responsive UX.
 */

document.addEventListener('DOMContentLoaded', function () {
    initEvaluationCalculator();
});

function initEvaluationCalculator() {
    const form = document.getElementById('evaluation-form');
    if (!form) return;

    const criterionItems = document.querySelectorAll('.criterion-card');
    const totalScoreDisplay = document.getElementById('eval-total-score-display');
    const totalScoreProgressBar = document.getElementById('eval-score-progress-bar');
    const scoreRatingBadge = document.getElementById('eval-score-rating-badge');
    const completionCounter = document.getElementById('eval-completion-counter');
    const isDraftInput = document.getElementById('eval-is-draft');

    // Lắng nghe thay đổi trên tất cả radio buttons chọn điểm
    criterionItems.forEach(card => {
        const radios = card.querySelectorAll('input[type="radio"][data-score]');
        radios.forEach(radio => {
            radio.addEventListener('change', function () {
                // Đánh dấu visual active trên nhãn điểm
                updateRadioVisuals(card);
                recalculateTotalScore();
            });
        });
    });

    // Hàm cập nhật trạng thái hiển thị của các nút radio 1-5
    function updateRadioVisuals(card) {
        const labels = card.querySelectorAll('.score-choice-label');
        labels.forEach(lbl => lbl.classList.remove('active', 'border-primary', 'bg-primary-subtle'));

        const checkedRadio = card.querySelector('input[type="radio"]:checked');
        if (checkedRadio) {
            const activeLabel = card.querySelector(`label[for="${checkedRadio.id}"]`);
            if (activeLabel) {
                activeLabel.classList.add('active', 'border-primary', 'bg-primary-subtle');
            }
        }
    }

    // Hàm tính lại điểm tổng bình quân gia quyền theo công thức
    function recalculateTotalScore() {
        let totalWeightedScore = 0;
        let totalWeight = 0;
        let scoredCount = 0;
        const totalCount = criterionItems.length;

        criterionItems.forEach(card => {
            const weightVal = parseFloat(card.getAttribute('data-weight')) || 1;
            totalWeight += weightVal;

            const checkedRadio = card.querySelector('input[type="radio"]:checked');
            if (checkedRadio) {
                const scoreVal = parseInt(checkedRadio.value, 10);
                if (scoreVal >= 1 && scoreVal <= 5) {
                    totalWeightedScore += scoreVal * weightVal;
                    scoredCount++;
                }
            }
        });

        // Điểm tổng bình quân gia quyền
        const finalScore = totalWeight > 0 ? (totalWeightedScore / totalWeight) : 0;
        const formattedScore = finalScore.toFixed(2);

        // 1. Cập nhật số điểm hiển thị
        if (totalScoreDisplay) {
            totalScoreDisplay.textContent = formattedScore;
        }

        // 2. Cập nhật thanh tiến độ %
        if (totalScoreProgressBar) {
            const percentage = Math.min(100, Math.max(0, (finalScore / 5.0) * 100));
            totalScoreProgressBar.style.width = percentage + '%';

            // Đổi màu thanh tiến độ
            totalScoreProgressBar.classList.remove('bg-danger', 'bg-warning', 'bg-success');
            if (finalScore < 3.0) {
                totalScoreProgressBar.classList.add('bg-danger');
            } else if (finalScore < 4.0) {
                totalScoreProgressBar.classList.add('bg-warning');
            } else {
                totalScoreProgressBar.classList.add('bg-success');
            }
        }

        // 3. Cập nhật Score Badge đổi màu linh hoạt
        if (scoreRatingBadge) {
            scoreRatingBadge.className = 'badge px-3 py-2 rounded-2 fw-semibold fs-6 ';
            if (finalScore === 0 && scoredCount === 0) {
                scoreRatingBadge.classList.add('bg-secondary', 'text-white');
                scoreRatingBadge.innerHTML = '<i class="bi bi-clock me-1"></i>Chưa chấm điểm';
            } else if (finalScore < 3.0) {
                scoreRatingBadge.classList.add('bg-danger', 'text-white');
                scoreRatingBadge.innerHTML = '<i class="bi bi-exclamation-triangle-fill me-1"></i>Chưa đạt yêu cầu (< 3.0)';
            } else if (finalScore < 4.0) {
                scoreRatingBadge.classList.add('bg-warning', 'text-dark');
                scoreRatingBadge.innerHTML = '<i class="bi bi-check-circle-fill me-1"></i>Đạt yêu cầu (3.0 - 3.99)';
            } else {
                scoreRatingBadge.classList.add('bg-success', 'text-white');
                scoreRatingBadge.innerHTML = '<i class="bi bi-star-fill me-1"></i>Xuất sắc (4.0 - 5.0)';
            }
        }

        // 4. Cập nhật tiến độ hoàn thành
        if (completionCounter) {
            completionCounter.textContent = `${scoredCount}/${totalCount} tiêu chí`;
            if (scoredCount === totalCount) {
                completionCounter.classList.remove('text-muted');
                completionCounter.classList.add('text-success', 'fw-bold');
            } else {
                completionCounter.classList.remove('text-success', 'fw-bold');
                completionCounter.classList.add('text-muted');
            }
        }
    }

    // Khởi chạy tính toán ban đầu
    criterionItems.forEach(card => updateRadioVisuals(card));
    recalculateTotalScore();

    // ── XỬ LÝ SUBMIT NỘP CHÍNH THỨC & LƯU NHÁP ──
    const btnSaveDraft = document.getElementById('btn-save-draft');
    const btnSubmitFinal = document.getElementById('btn-submit-final');

    if (btnSaveDraft) {
        btnSaveDraft.addEventListener('click', function (e) {
            e.preventDefault();
            if (isDraftInput) isDraftInput.value = 'true';
            submitEvaluationForm(true);
        });
    }

    if (btnSubmitFinal) {
        btnSubmitFinal.addEventListener('click', function (e) {
            e.preventDefault();

            // Kiểm tra xem đã chấm đủ toàn bộ tiêu chí chưa
            let missingCriterion = null;
            criterionItems.forEach(card => {
                const checked = card.querySelector('input[type="radio"]:checked');
                if (!checked && !missingCriterion) {
                    missingCriterion = card;
                }
            });

            if (missingCriterion) {
                const name = missingCriterion.querySelector('.criterion-title')?.textContent?.trim() || 'tiêu chí';
                missingCriterion.scrollIntoView({ behavior: 'smooth', block: 'center' });
                missingCriterion.classList.add('border-danger', 'shadow-sm');
                setTimeout(() => missingCriterion.classList.remove('border-danger'), 3000);
                alert(`Vui lòng chấm điểm cho tiêu chí: "${name}" trước khi nộp chính thức.`);
                return;
            }

            // Cảnh báo xác nhận tính bất biến
            const confirmMsg = "BẠN CÓ CHẮC CHẮN MUỐN NỘP CHÍNH THỨC PHIẾU ĐÁNH GIÁ NÀY?\n\n" +
                "• Điểm số và tiêu chí sẽ được lưu thành bản ghi snapshot bất biến.\n" +
                "• Phiếu sẽ được KHÓA NGAY LẬP TỨC và không thể chỉnh sửa lại sau khi nộp.";

            if (confirm(confirmMsg)) {
                if (isDraftInput) isDraftInput.value = 'false';
                submitEvaluationForm(false);
            }
        });
    }

    // Hàm gửi form qua AJAX
    function submitEvaluationForm(isDraft) {
        const formData = new FormData(form);
        const actionBtn = isDraft ? btnSaveDraft : btnSubmitFinal;
        const originalHtml = actionBtn ? actionBtn.innerHTML : '';

        if (actionBtn) {
            actionBtn.disabled = true;
            actionBtn.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span>Đang xử lý...';
        }

        fetch(form.action, {
            method: 'POST',
            body: formData,
            headers: {
                'X-Requested-With': 'XMLHttpRequest'
            }
        })
        .then(res => res.json())
        .then(data => {
            if (data.success) {
                // Hiển thị thông báo thành công
                showNotificationAlert(data.message || 'Thao tác thành công!', 'success');
                setTimeout(() => {
                    if (data.redirectUrl) {
                        window.location.href = data.redirectUrl;
                    } else {
                        window.location.reload();
                    }
                }, 1000);
            } else {
                showNotificationAlert(data.message || 'Có lỗi xảy ra khi lưu phiếu.', 'danger');
                if (actionBtn) {
                    actionBtn.disabled = false;
                    actionBtn.innerHTML = originalHtml;
                }
            }
        })
        .catch(err => {
            console.error('Submit evaluation error:', err);
            showNotificationAlert('Không thể kết nối đến máy chủ. Vui lòng kiểm tra lại kết nối mạng.', 'danger');
            if (actionBtn) {
                actionBtn.disabled = false;
                actionBtn.innerHTML = originalHtml;
            }
        });
    }

    function showNotificationAlert(msg, type) {
        const alertBox = document.getElementById('eval-alert-container');
        if (alertBox) {
            alertBox.innerHTML = `
                <div class="alert alert-${type} alert-dismissible fade show rounded-2 py-2.5 px-3 mb-3 border-0 shadow-sm" role="alert">
                    <div class="d-flex align-items-center">
                        <i class="bi bi-${type === 'success' ? 'check-circle-fill' : 'exclamation-triangle-fill'} me-2 fs-5"></i>
                        <span class="small fw-semibold">${msg}</span>
                    </div>
                    <button type="button" class="btn-close" data-bs-dismiss="alert" aria-label="Close"></button>
                </div>
            `;
            alertBox.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
        } else {
            alert(msg);
        }
    }
}
