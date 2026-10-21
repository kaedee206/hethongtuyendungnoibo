/**
 * Hệ thống Thông báo Nội bộ (In-App Notification) - SP-8
 * Xử lý tải danh sách, polling nhẹ, đánh dấu đã đọc và deep linking điều hướng.
 */
(function () {
    'use strict';

    var POLL_INTERVAL_MS = 45000; // 45 giây
    var pollTimer = null;
    var isFetching = false;

    function escapeHtml(str) {
        if (!str) return '';
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    function getTypeMeta(type) {
        switch (type) {
            case 'ApprovalPending':
            case 0:
                return {
                    icon: 'bi-file-earmark-check-fill',
                    colorClass: 'text-warning',
                    bgClass: 'bg-warning-subtle',
                    label: 'Chờ duyệt'
                };
            case 'JobAssigned':
            case 1:
                return {
                    icon: 'bi-person-badge-fill',
                    colorClass: 'text-info',
                    bgClass: 'bg-info-subtle',
                    label: 'Phân công'
                };
            case 'InterviewInvitation':
            case 2:
                return {
                    icon: 'bi-calendar-event-fill',
                    colorClass: 'text-primary',
                    bgClass: 'bg-primary-subtle',
                    label: 'Phỏng vấn'
                };
            case 'OfferResponse':
            case 3:
                return {
                    icon: 'bi-gift-fill',
                    colorClass: 'text-success',
                    bgClass: 'bg-success-subtle',
                    label: 'Phản hồi Offer'
                };
            default:
                return {
                    icon: 'bi-bell-fill',
                    colorClass: 'text-secondary',
                    bgClass: 'bg-light',
                    label: 'Hệ thống'
                };
        }
    }

    function updateBadge(unreadCount) {
        var badge = document.getElementById('staffNotifBadge');
        var countText = document.getElementById('staffNotifCountText');
        var badgeText = document.getElementById('staffNotifBadgeText');

        if (countText) {
            countText.textContent = unreadCount + ' chưa đọc';
        }

        if (badge) {
            if (unreadCount > 0) {
                badge.classList.remove('d-none');
                if (badgeText) {
                    badgeText.textContent = unreadCount > 99 ? '99+' : unreadCount;
                }
            } else {
                badge.classList.add('d-none');
                if (badgeText) {
                    badgeText.textContent = '0';
                }
            }
        }
    }

    function renderNotificationsList(items, unreadCount) {
        var list = document.getElementById('staffNotifList');
        if (!list) return;

        updateBadge(unreadCount);

        if (!items || items.length === 0) {
            list.innerHTML = [
                '<div class="text-center py-4 px-3 text-muted">',
                '  <i class="bi bi-bell-slash fs-2 d-block mb-2 text-secondary opacity-50"></i>',
                '  <div class="fw-semibold small text-dark">Bạn không có thông báo mới nào</div>',
                '  <div class="text-muted" style="font-size: 0.72rem; margin-top: 2px;">Các thông báo phê duyệt, lịch phỏng vấn và phân công sẽ hiển thị ở đây.</div>',
                '</div>'
            ].join('');
            return;
        }

        var html = items.map(function (n) {
            var meta = getTypeMeta(n.type || n.typeName);
            var isUnread = !n.isRead;
            var itemBg = isUnread ? '#F8FAFC' : '#FFFFFF';
            var borderLeft = isUnread ? 'border-left: 3px solid #10B981;' : 'border-left: 3px solid transparent;';
            var titleWeight = isUnread ? 'fw-bold text-dark' : 'fw-semibold text-secondary';
            var target = n.targetUrl || n.link || '#';

            return [
                '<div class="notif-item p-2.5 px-3 border-bottom position-relative transition-all" ',
                '     data-notif-id="' + escapeHtml(n.id) + '" ',
                '     data-target-url="' + escapeHtml(target) + '" ',
                '     style="background-color: ' + itemBg + '; cursor: pointer; ' + borderLeft + ' font-size: 0.8rem; transition: background-color 0.15s ease;" ',
                '     onclick="window.StaffNotifications.handleItemClick(this)" ',
                '     onmouseover="this.style.backgroundColor=\'#F1F5F9\'" ',
                '     onmouseout="this.style.backgroundColor=\'' + itemBg + '\'">',
                '  <div class="d-flex align-items-start gap-2">',
                '    <div class="rounded-circle d-flex align-items-center justify-content-center flex-shrink-0 mt-0.5 ' + meta.bgClass + '" style="width: 28px; height: 28px;">',
                '      <i class="bi ' + meta.icon + ' ' + meta.colorClass + '" style="font-size: 0.85rem;"></i>',
                '    </div>',
                '    <div class="flex-grow-1 overflow-hidden">',
                '      <div class="d-flex justify-content-between align-items-center gap-1 mb-1">',
                '        <span class="' + titleWeight + ' text-truncate" style="max-width: 200px;" title="' + escapeHtml(n.title) + '">' + escapeHtml(n.title) + '</span>',
                '        <span class="text-muted flex-shrink-0" style="font-size: 0.68rem;">' + escapeHtml(n.timeAgo || n.timeDisplay) + '</span>',
                '      </div>',
                '      <div class="text-secondary small text-truncate-2" style="font-size: 0.74rem; line-height: 1.35; display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden;">',
                '        ' + escapeHtml(n.content),
                '      </div>',
                '    </div>',
                (isUnread ? '    <span class="unread-dot position-absolute top-2 end-2 p-1 bg-success border border-light rounded-circle" style="width: 7px; height: 7px; margin-top: 10px; margin-right: 8px;"></span>' : ''),
                '  </div>',
                '</div>'
            ].join('');
        }).join('');

        list.innerHTML = html;
    }

    function loadStaffNotifications() {
        if (isFetching) return;
        isFetching = true;

        var list = document.getElementById('staffNotifList');
        if (list && list.children.length === 0) {
            list.innerHTML = [
                '<div class="text-center py-4 text-muted small">',
                '  <div class="spinner-border spinner-border-sm text-success mb-2" role="status"></div>',
                '  <div>Đang tải thông báo...</div>',
                '</div>'
            ].join('');
        }

        fetch('/api/notifications/recent?count=10', { credentials: 'same-origin' })
            .then(function (r) {
                if (!r.ok) throw new Error('Network error');
                return r.json();
            })
            .then(function (res) {
                if (res && res.success) {
                    renderNotificationsList(res.items || [], res.unreadCount || 0);
                }
            })
            .catch(function () {
                // Im lặng khi lỗi mạng tạm thời
            })
            .finally(function () {
                isFetching = false;
            });
    }

    function pollUnreadCount() {
        if (document.hidden) return; // Tiết kiệm tài nguyên khi tab ẩn

        fetch('/api/notifications/unread-count', { credentials: 'same-origin' })
            .then(function (r) {
                if (!r.ok) throw new Error('Network error');
                return r.json();
            })
            .then(function (res) {
                if (res && res.success && typeof res.unreadCount === 'number') {
                    updateBadge(res.unreadCount);
                }
            })
            .catch(function () {});
    }

    function markAsReadAndNavigate(id, targetUrl) {
        // Optimistic UI: Đổi giao diện mục này thành đã đọc ngay lập tức
        var itemEl = document.querySelector('[data-notif-id="' + id + '"]');
        if (itemEl) {
            itemEl.style.backgroundColor = '#FFFFFF';
            itemEl.style.borderLeft = '3px solid transparent';
            var dot = itemEl.querySelector('.unread-dot');
            if (dot) dot.remove();

            var badge = document.getElementById('staffNotifBadge');
            var badgeText = document.getElementById('staffNotifBadgeText');
            var countText = document.getElementById('staffNotifCountText');
            if (badgeText) {
                var current = parseInt(badgeText.textContent || '0', 10);
                if (current > 0) {
                    var next = current - 1;
                    badgeText.textContent = next > 99 ? '99+' : next;
                    if (countText) countText.textContent = next + ' chưa đọc';
                    if (next === 0 && badge) badge.classList.add('d-none');
                }
            }
        }

        // Gọi API gửi cờ read
        fetch('/api/notifications/' + id + '/read', {
            method: 'POST',
            credentials: 'same-origin',
            headers: {
                'X-Requested-With': 'XMLHttpRequest'
            }
        })
            .then(function (r) { return r.json(); })
            .then(function (res) {
                var dest = (res && res.targetUrl) ? res.targetUrl : targetUrl;
                if (dest && dest !== '#' && dest !== '') {
                    window.location.href = dest;
                }
            })
            .catch(function () {
                if (targetUrl && targetUrl !== '#' && targetUrl !== '') {
                    window.location.href = targetUrl;
                }
            });
    }

    function handleItemClick(element) {
        if (!element) return;
        var id = element.getAttribute('data-notif-id');
        var targetUrl = element.getAttribute('data-target-url');
        if (id) {
            markAsReadAndNavigate(id, targetUrl || '#');
        }
    }

    function markAllAsRead(event) {
        if (event) {
            event.preventDefault();
            event.stopPropagation();
        }

        // Optimistic UI: Biến tất cả mục thành đã đọc
        var list = document.getElementById('staffNotifList');
        if (list) {
            var items = list.querySelectorAll('.notif-item');
            items.forEach(function (el) {
                el.style.backgroundColor = '#FFFFFF';
                el.style.borderLeft = '3px solid transparent';
                var dot = el.querySelector('.unread-dot');
                if (dot) dot.remove();
            });
        }
        updateBadge(0);

        var btn = document.getElementById('markAllReadBtn');
        if (btn) {
            var originalHtml = btn.innerHTML;
            btn.innerHTML = '<span class="text-success"><i class="bi bi-check-lg me-1"></i>Đã đọc tất cả</span>';
            setTimeout(function () {
                btn.innerHTML = originalHtml;
            }, 2500);
        }

        fetch('/api/notifications/mark-all-read', {
            method: 'POST',
            credentials: 'same-origin',
            headers: {
                'X-Requested-With': 'XMLHttpRequest'
            }
        })
            .then(function (r) { return r.json(); })
            .catch(function () {});
    }

    // Expose ra toàn cục
    window.StaffNotifications = {
        load: loadStaffNotifications,
        poll: pollUnreadCount,
        handleItemClick: handleItemClick,
        markAsReadAndNavigate: markAsReadAndNavigate,
        markAllAsRead: markAllAsRead
    };

    // Khởi tạo khi DOM sẵn sàng
    document.addEventListener('DOMContentLoaded', function () {
        var dropdownEl = document.getElementById('staffNotificationDropdown');
        if (!dropdownEl) return;

        // Poll nhanh số lượng chưa đọc ban đầu
        pollUnreadCount();

        // Lắng nghe sự kiện mở Dropdown để nạp danh sách mới nhất
        dropdownEl.addEventListener('show.bs.dropdown', function () {
            loadStaffNotifications();
        });

        // Định kỳ poll nhẹ mỗi 45 giây
        pollTimer = setInterval(pollUnreadCount, POLL_INTERVAL_MS);

        // Khi chuyển tab quay lại, lập tức cập nhật
        document.addEventListener('visibilitychange', function () {
            if (!document.hidden) {
                pollUnreadCount();
            }
        });
    });
})();
