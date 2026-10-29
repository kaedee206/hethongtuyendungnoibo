/**
 * offer-approval.js
 * Quản trị luồng Phê duyệt Đề xuất Thư mời nhận việc (Offer) theo hạn mức lương (SCRUM-341)
 * Tuân thủ RULE.md và tiêu chuẩn NoveraTech UI/UX
 */

(function () {
    'use strict';

    let currentOfferDetail = null;

    /**
     * Mở modal xem xét và phê duyệt Offer
     * @param {string} offerId 
     */
    window.openOfferApprovalModal = async function (offerId) {
        if (!offerId) {
            if (window.NoveraToast) {
                window.NoveraToast.error('Không tìm thấy mã định danh đề xuất Offer.');
            } else {
                alert('Không tìm thấy mã định danh đề xuất Offer.');
            }
            return;
        }

        const modalEl = document.getElementById('offerApprovalModal');
        if (!modalEl) {
            console.error('Không tìm thấy phần tử modal offerApprovalModal');
            return;
        }

        const modal = bootstrap.Modal.getOrCreateInstance(modalEl);
        modal.show();

        const loadingEl = document.getElementById('offerApprovalLoading');
        const contentEl = document.getElementById('offerApprovalContent');
        const commentErrorEl = document.getElementById('commentValidationError');
        const commentInput = document.getElementById('approvalDecisionComment');
        const asteriskEl = document.getElementById('commentRequiredAsterisk');

        // Reset trạng thái
        if (loadingEl) loadingEl.classList.remove('d-none');
        if (contentEl) contentEl.classList.add('d-none');
        if (commentErrorEl) commentErrorEl.classList.add('d-none');
        if (asteriskEl) asteriskEl.classList.add('d-none');
        if (commentInput) {
            commentInput.value = '';
            commentInput.style.borderColor = '';
        }

        document.getElementById('decisionOfferId').value = offerId;
        document.getElementById('decisionAction').value = '';

        try {
            const response = await fetch(`/Workspace/GetOfferApprovalDetail?id=${encodeURIComponent(offerId)}`, {
                method: 'GET',
                headers: {
                    'Accept': 'application/json',
                    'X-Requested-With': 'XMLHttpRequest'
                }
            });

            if (!response.ok) {
                throw new Error(`Lỗi kết nối máy chủ (${response.status})`);
            }

            const resData = await response.json();
            if (!resData.success || !resData.data) {
                throw new Error(resData.message || 'Không thể tải chi tiết đề xuất Offer.');
            }

            currentOfferDetail = resData.data;
            renderOfferApprovalDetail(currentOfferDetail);

            if (loadingEl) loadingEl.classList.add('d-none');
            if (contentEl) contentEl.classList.remove('d-none');

        } catch (error) {
            console.error('Lỗi khi mở modal phê duyệt Offer:', error);
            if (loadingEl) loadingEl.classList.add('d-none');
            if (window.NoveraToast) {
                window.NoveraToast.error(error.message || 'Lỗi khi tải chi tiết đề xuất Offer.');
            } else {
                alert(error.message || 'Lỗi khi tải chi tiết đề xuất Offer.');
            }
            modal.hide();
        }
    };

    /**
     * Điền dữ liệu chi tiết vào giao diện modal
     * @param {Object} data 
     */
    function renderOfferApprovalDetail(data) {
        // Thông tin ứng viên & vị trí
        setText('approvalCandidateName', data.candidateName || 'Ứng viên');
        setText('approvalJobTitle', data.jobTitle || 'Vị trí tuyển dụng');
        setText('approvalDepartment', data.departmentName || 'Phòng ban');
        setText('approvalCandidateEmail', data.candidateEmail || '-');
        setText('approvalRecruiterName', data.recruiterName || 'Chuyên viên tuyển dụng');
        setText('approvalCreatedDate', `Tạo ngày: ${data.createdAtFormatted || '-'}`);

        // Trạng thái Offer badge
        const statusBadge = document.getElementById('approvalOfferStatusBadge');
        if (statusBadge) {
            statusBadge.textContent = data.statusDisplay || 'Chờ duyệt';
            statusBadge.className = 'badge border';
            if (data.status === 2 || data.status === 'APPROVED') {
                statusBadge.classList.add('bg-success-subtle', 'text-success', 'border-success-subtle');
            } else if (data.status === 3 || data.status === 'REJECTED') {
                statusBadge.classList.add('bg-danger-subtle', 'text-danger', 'border-danger-subtle');
            } else if (data.status === 0 || data.status === 'DRAFT') {
                statusBadge.classList.add('bg-secondary-subtle', 'text-secondary', 'border-secondary-subtle');
            } else {
                statusBadge.classList.add('bg-warning-subtle', 'text-warning-emphasis', 'border-warning-subtle');
            }
        }

        // Cấp phê duyệt badge
        const levelBadge = document.getElementById('approvalLevelBadge');
        if (levelBadge) {
            levelBadge.textContent = data.currentLevelDisplay || `Cấp ${data.currentLevel}`;
            if (data.isAboveBudget) {
                levelBadge.className = 'badge px-3 py-2 bg-amber-100 text-amber-900 border border-amber-300';
                levelBadge.style.background = '#FEF3C7';
                levelBadge.style.color = '#78350F';
            } else {
                levelBadge.className = 'badge px-3 py-2 bg-emerald-100 text-emerald-800 border border-emerald-300';
                levelBadge.style.background = '#D1FAE5';
                levelBadge.style.color = '#065F46';
            }
        }

        // Ma trận lương
        setText('approvalProposedSalary', data.salaryDisplay || '-');
        setText('approvalStandardBand', data.salaryBandDisplay || 'Chưa thiết lập');
        setText('approvalTotalPackage', data.totalPackageDisplay || '-');
        setText('approvalJoinDate', data.proposedJoinDateFormatted || 'Thỏa thuận');
        setText('approvalProbationMonths', `${data.probationPeriodMonths || 2} tháng`);
        setText('approvalExpirationDate', data.expirationDateFormatted || '7 ngày');

        // Banner cảnh báo dải lương
        const alertContainer = document.getElementById('salaryBandAlertContainer');
        const justificationBox = document.getElementById('salaryJustificationBox');
        if (alertContainer) {
            if (data.isAboveBudget) {
                const percentText = data.percentOverBand ? `+${data.percentOverBand}%` : 'Vượt mức';
                alertContainer.innerHTML = `
                    <div class="alert py-2.5 px-3 mb-0 d-flex align-items-center" style="background: #FFFBEB; border: 1px solid #FCD34D; border-radius: 8px; color: #92400E;">
                        <i class="bi bi-exclamation-triangle-fill text-warning fs-4 me-3"></i>
                        <div style="font-size: 0.85rem; line-height: 1.4;">
                            <div class="fw-bold text-dark" style="color: #78350F !important;">
                                ⚠️ CẢNH BÁO: ĐỀ XUẤT LƯƠNG VƯỢT DẢI CHUẨN (${percentText})
                            </div>
                            <div class="text-amber-900 mt-0.5">
                                Mức lương cơ bản đề xuất cao hơn trần ngân sách cho phép. Theo quy định kiểm soát quỹ lương, đề xuất này <strong>tự động yêu cầu 2 cấp phê duyệt tuần tự</strong> (Trưởng phòng Nhân sự và Ban Giám Đốc).
                            </div>
                        </div>
                    </div>
                `;
                if (justificationBox) {
                    justificationBox.classList.remove('d-none');
                    setText('approvalSalaryJustification', data.salaryJustification || 'Chưa có bản giải trình chi tiết từ chuyên viên tuyển dụng.');
                }
            } else {
                alertContainer.innerHTML = `
                    <div class="alert py-2.5 px-3 mb-0 d-flex align-items-center" style="background: #ECFDF5; border: 1px solid #A7F3D0; border-radius: 8px; color: #065F46;">
                        <i class="bi bi-check-circle-fill text-emerald-600 fs-4 me-3" style="color: #059669;"></i>
                        <div style="font-size: 0.85rem; line-height: 1.4;">
                            <div class="fw-bold" style="color: #064E3B;">
                                ✅ ĐỀ XUẤT NẰM TRONG DẢI LƯƠNG CHUẨN CỦA VỊ TRÍ
                            </div>
                            <div style="color: #047857; margin-top: 2px;">
                                Mức lương đề xuất phù hợp với khung quỹ lương quy định. Chỉ yêu cầu <strong>1 cấp phê duyệt</strong> từ Trưởng phòng Nhân sự.
                            </div>
                        </div>
                    </div>
                `;
                if (justificationBox) {
                    justificationBox.classList.add('d-none');
                }
            }
        }

        // Render Lịch sử phê duyệt bất biến
        renderApprovalHistory(data.approvalHistory || []);

        // Phân quyền tương tác của người xem
        const formContainer = document.getElementById('approvalDecisionFormContainer');
        const readOnlyNotice = document.getElementById('approvalReadOnlyNotice');
        const readOnlyMessage = document.getElementById('approvalReadOnlyMessage');

        const canAct = data.canApprove || data.canReject || data.canRequestChanges;
        if (canAct) {
            if (formContainer) formContainer.classList.remove('d-none');
            if (readOnlyNotice) readOnlyNotice.classList.add('d-none');
        } else {
            if (formContainer) formContainer.classList.add('d-none');
            if (readOnlyNotice) {
                readOnlyNotice.classList.remove('d-none');
                if (readOnlyMessage) {
                    readOnlyMessage.textContent = data.userActionRestrictionReason 
                        || 'Đề xuất này hiện không trong thẩm quyền duyệt của bạn hoặc đang chờ cấp duyệt trước xử lý.';
                }
            }
        }
    }

    /**
     * Render danh sách lịch sử phê duyệt phân cấp
     * @param {Array} history 
     */
    function renderApprovalHistory(history) {
        const container = document.getElementById('approvalHistoryTimeline');
        if (!container) return;

        if (!history || history.length === 0) {
            container.innerHTML = `
                <div class="text-center py-3 text-muted small">
                    <i class="bi bi-clock-history me-1"></i>Chưa có lịch sử phê duyệt nào được ghi nhận.
                </div>
            `;
            return;
        }

        let html = '<div class="d-flex flex-column gap-2.5">';
        history.forEach((item, index) => {
            const isLast = index === history.length - 1;
            let statusBadge = '';
            let borderColor = '#E2E8F0';
            let bgStep = '#FFFFFF';

            // 0: PENDING, 1: APPROVED, 2: REJECTED, 3: CHANGES_REQUESTED
            if (item.status === 1 || item.status === 'APPROVED') {
                statusBadge = '<span class="badge bg-emerald-100 text-emerald-800 border border-emerald-300" style="background:#D1FAE5; color:#065F46; border-radius:6px;"><i class="bi bi-check-circle-fill me-1"></i>Đã phê duyệt</span>';
                borderColor = '#10B981';
            } else if (item.status === 2 || item.status === 'REJECTED') {
                statusBadge = '<span class="badge bg-rose-100 text-rose-800 border border-rose-300" style="background:#FFE4E6; color:#9F1239; border-radius:6px;"><i class="bi bi-x-circle-fill me-1"></i>Đã từ chối</span>';
                borderColor = '#E11D48';
            } else if (item.status === 3 || item.status === 'CHANGES_REQUESTED') {
                statusBadge = '<span class="badge bg-amber-100 text-amber-800 border border-amber-300" style="background:#FEF3C7; color:#92400E; border-radius:6px;"><i class="bi bi-arrow-counterclockwise me-1"></i>Yêu cầu sửa đổi</span>';
                borderColor = '#F59E0B';
            } else {
                statusBadge = '<span class="badge bg-slate-100 text-slate-700 border border-slate-300" style="background:#F1F5F9; color:#475569; border-radius:6px;"><i class="bi bi-hourglass-split me-1"></i>Chờ xem xét</span>';
                borderColor = '#CBD5E1';
                bgStep = '#FAFAFA';
            }

            html += `
                <div class="p-3 border rounded-3 position-relative" style="background: ${bgStep}; border-left: 4px solid ${borderColor} !important; border-radius: 8px;">
                    <div class="d-flex flex-wrap justify-content-between align-items-center mb-1 gap-2">
                        <div class="d-flex align-items-center gap-2">
                            <span class="badge bg-slate-200 text-slate-800 font-monospace" style="border-radius: 6px; font-size: 0.75rem;">
                                ${escapeHtml(item.levelDisplay || `Cấp ${item.level}`)}
                            </span>
                            <span class="fw-bold text-dark" style="font-size: 0.88rem;">
                                ${escapeHtml(item.approverName || 'Người duyệt')}
                            </span>
                            <span class="text-muted small" style="font-size: 0.78rem;">
                                (${escapeHtml(item.approverRoleDisplay || item.approverRole || 'Người phê duyệt')})
                            </span>
                        </div>
                        <div>
                            ${statusBadge}
                        </div>
                    </div>

                    ${item.decidedAtFormatted ? `
                        <div class="text-muted small mb-1.5" style="font-size: 0.75rem;">
                            <i class="bi bi-calendar-check me-1"></i>Thời điểm xử lý: <span class="fw-medium text-dark">${escapeHtml(item.decidedAtFormatted)}</span>
                        </div>
                    ` : `
                        <div class="text-muted small mb-1.5 fst-italic" style="font-size: 0.75rem;">
                            <i class="bi bi-clock me-1"></i>Đang trong danh sách chờ phê duyệt
                        </div>
                    `}

                    ${item.comment ? `
                        <div class="p-2 border rounded mt-2" style="background: #F8FAFC; border-color: #E2E8F0 !important; font-size: 0.82rem;">
                            <span class="fw-semibold text-slate-700 me-1"><i class="bi bi-chat-left-quote me-1 text-primary"></i>Ý kiến:</span>
                            <span class="text-dark">${escapeHtml(item.comment)}</span>
                        </div>
                    ` : ''}
                </div>
            `;
        });
        html += '</div>';

        container.innerHTML = html;
    }

    /**
     * Gắn nhanh nội dung ý kiến gợi ý
     * @param {string} text 
     */
    window.setQuickApprovalComment = function (text) {
        const commentInput = document.getElementById('approvalDecisionComment');
        const commentErrorEl = document.getElementById('commentValidationError');
        if (commentInput) {
            commentInput.value = text;
            commentInput.style.borderColor = '';
            commentInput.focus();
        }
        if (commentErrorEl) {
            commentErrorEl.classList.add('d-none');
        }
    };

    /**
     * Kích hoạt xử lý 1 trong 3 hành động: APPROVE, REJECT, CHANGES_REQUESTED
     * @param {string} action 
     */
    window.triggerDecisionAction = function (action) {
        const commentInput = document.getElementById('approvalDecisionComment');
        const commentErrorEl = document.getElementById('commentValidationError');
        const asteriskEl = document.getElementById('commentRequiredAsterisk');
        const comment = (commentInput ? commentInput.value : '').trim();

        // Kiểm tra ràng buộc bắt buộc nhập ý kiến đối với Từ chối và Yêu cầu sửa
        if (action === 'REJECT' || action === 'CHANGES_REQUESTED') {
            if (asteriskEl) asteriskEl.classList.remove('d-none');

            if (!comment) {
                if (commentErrorEl) {
                    commentErrorEl.textContent = action === 'REJECT'
                        ? 'Bắt buộc phải nhập ý kiến/lý do giải thích rõ khi Từ chối đề xuất Offer.'
                        : 'Bắt buộc phải nhập ý kiến/chỉ dẫn điều chỉnh cụ thể khi Yêu cầu sửa Offer.';
                    commentErrorEl.classList.remove('d-none');
                }
                if (commentInput) {
                    commentInput.style.borderColor = '#E11D48';
                    commentInput.focus();
                }
                return;
            }
        }

        // Nếu hợp lệ, gán action và submit form
        if (commentErrorEl) commentErrorEl.classList.add('d-none');
        if (commentInput) commentInput.style.borderColor = '';
        document.getElementById('decisionAction').value = action;

        // Xác nhận thao tác với người dùng
        let confirmMsg = 'Bạn có chắc chắn muốn thực hiện hành động này?';
        if (action === 'APPROVE') {
            confirmMsg = 'Xác nhận PHÊ DUYỆT đề xuất Thư mời nhận việc này?';
        } else if (action === 'REJECT') {
            confirmMsg = 'Xác nhận TỪ CHỐI đề xuất Offer này? Trạng thái offer sẽ chuyển thành Đã từ chối.';
        } else if (action === 'CHANGES_REQUESTED') {
            confirmMsg = 'Xác nhận YÊU CẦU SỬA ĐỔI? Đề xuất sẽ được trả về trạng thái Bản nháp để Recruiter chỉnh sửa.';
        }

        if (!confirm(confirmMsg)) {
            return;
        }

        executeOfferDecisionSubmit();
    };

    /**
     * Thực thi gửi quyết định duyệt lên máy chủ qua AJAX
     */
    async function executeOfferDecisionSubmit() {
        const form = document.getElementById('offerApprovalDecisionForm');
        if (!form) return;

        const offerId = document.getElementById('decisionOfferId').value;
        const action = document.getElementById('decisionAction').value;
        const comment = document.getElementById('approvalDecisionComment').value;
        const tokenInput = form.querySelector('input[name="__RequestVerificationToken"]');
        const token = tokenInput ? tokenInput.value : '';

        const actionButtons = document.querySelectorAll('#approvalActionButtonsGroup button');
        actionButtons.forEach(btn => btn.disabled = true);

        // Hiển thị trạng thái đang gửi
        let activeBtn = null;
        if (action === 'APPROVE') activeBtn = document.getElementById('btnApproveOffer');
        else if (action === 'REJECT') activeBtn = document.getElementById('btnRejectOffer');
        else if (action === 'CHANGES_REQUESTED') activeBtn = document.getElementById('btnRequestChangesOffer');

        let originalBtnHtml = '';
        if (activeBtn) {
            originalBtnHtml = activeBtn.innerHTML;
            activeBtn.innerHTML = '<span class="spinner-border spinner-border-sm me-1" role="status" aria-hidden="true"></span>Đang xử lý...';
        }

        try {
            const formData = new FormData();
            formData.append('OfferId', offerId);
            formData.append('Decision', action);
            formData.append('Comment', comment || '');
            if (token) {
                formData.append('__RequestVerificationToken', token);
            }

            const response = await fetch('/Workspace/SubmitOfferDecision', {
                method: 'POST',
                headers: {
                    'X-Requested-With': 'XMLHttpRequest',
                    'RequestVerificationToken': token
                },
                body: formData
            });

            const result = await response.json();

            if (!response.ok || !result.success) {
                throw new Error(result.message || 'Lỗi xử lý quyết định phê duyệt.');
            }

            // Đóng modal
            const modalEl = document.getElementById('offerApprovalModal');
            if (modalEl) {
                const modal = bootstrap.Modal.getInstance(modalEl);
                if (modal) modal.hide();
            }

            // Hiển thị Toast thông báo theo RULE.md
            if (window.NoveraToast) {
                if (action === 'APPROVE') {
                    window.NoveraToast.success(result.message || 'Phê duyệt Offer thành công.');
                } else if (action === 'REJECT') {
                    window.NoveraToast.error(result.message || 'Đã từ chối đề xuất Offer.');
                } else {
                    window.NoveraToast.warning(result.message || 'Đã yêu cầu điều chỉnh đề xuất Offer.');
                }
            } else {
                alert(result.message || 'Xử lý thành công.');
            }

            // Cập nhật giao diện: Reload trang hoặc tải lại tab Phê duyệt
            setTimeout(() => {
                window.location.reload();
            }, 600);

        } catch (error) {
            console.error('Lỗi khi gửi quyết định duyệt Offer:', error);
            if (window.NoveraToast) {
                window.NoveraToast.error(error.message || 'Lỗi kết nối khi gửi phê duyệt Offer.');
            } else {
                alert(error.message || 'Lỗi kết nối khi gửi phê duyệt Offer.');
            }
            actionButtons.forEach(btn => btn.disabled = false);
            if (activeBtn && originalBtnHtml) {
                activeBtn.innerHTML = originalBtnHtml;
            }
        }
    }

    // Helper gán text an toàn
    function setText(id, text) {
        const el = document.getElementById(id);
        if (el) el.textContent = text || '-';
    }

    // Helper escape html
    function escapeHtml(str) {
        if (!str) return '';
        return str
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#039;');
    }

})();
