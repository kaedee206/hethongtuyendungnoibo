// Global standard HTML Escaper to prevent XSS in DOM manipulation
window.escapeHtml = function(text) {
    if (text === null || text === undefined) return '';
    return String(text)
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
};

// ─── Centralized Stacked Toast Notification Engine (RULE.md Section 4.3.B) ───
(function() {
    'use strict';

    var ICONS = {
        success: 'bi-check-circle-fill',
        reject: 'bi-x-circle-fill',
        error: 'bi-exclamation-octagon-fill',
        warning: 'bi-exclamation-triangle-fill',
        info: 'bi-info-circle-fill'
    };

    var DEFAULT_TITLES = {
        success: 'Thành công',
        reject: 'Từ chối',
        error: 'Có lỗi xảy ra',
        warning: 'Cảnh báo',
        info: 'Thông báo'
    };

    var DEFAULT_DURATIONS = {
        success: 5000,
        reject: 6000,
        error: 7000,
        warning: 6000,
        info: 5000
    };

    var MAX_TOASTS = 5;

    function getOrCreateContainer() {
        var container = document.getElementById('novera-toast-container');
        if (!container) {
            container = document.createElement('div');
            container.id = 'novera-toast-container';
            container.setAttribute('aria-live', 'polite');
            container.setAttribute('aria-atomic', 'false');
            document.body.appendChild(container);
        }
        return container;
    }

    function createToast(options) {
        var type = options.type || 'info';
        if (!ICONS[type]) type = 'info';

        var title = options.title !== undefined ? options.title : DEFAULT_TITLES[type];
        var message = options.message || '';
        var duration = options.duration !== undefined ? options.duration : DEFAULT_DURATIONS[type];

        var container = getOrCreateContainer();

        // Limit stack to MAX_TOASTS
        var existingToasts = container.querySelectorAll('.novera-toast:not(.is-hiding)');
        if (existingToasts.length >= MAX_TOASTS) {
            var oldest = existingToasts[0];
            if (oldest && oldest._dismiss) {
                oldest._dismiss();
            }
        }

        var toast = document.createElement('div');
        toast.className = 'novera-toast novera-toast-' + type;
        toast.setAttribute('role', type === 'error' ? 'alert' : 'status');

        var iconClass = ICONS[type];
        var safeTitle = window.escapeHtml(title);
        var safeMessage = window.escapeHtml(message);

        var titleHtml = safeTitle ? '<div class="novera-toast-title">' + safeTitle + '</div>' : '';

        toast.innerHTML = 
            '<div class="novera-toast-content">' +
                '<div class="novera-toast-icon-wrap">' +
                    '<i class="bi ' + iconClass + '"></i>' +
                '</div>' +
                '<div class="novera-toast-body">' +
                    titleHtml +
                    '<div class="novera-toast-message">' + safeMessage + '</div>' +
                '</div>' +
                '<button type="button" class="novera-toast-close" title="Đóng" aria-label="Đóng">' +
                    '<i class="bi bi-x"></i>' +
                '</button>' +
            '</div>' +
            '<div class="novera-toast-progress-track">' +
                '<div class="novera-toast-progress-bar" style="width: 100%;"></div>' +
            '</div>';

        container.appendChild(toast);

        var progressBar = toast.querySelector('.novera-toast-progress-bar');
        var closeBtn = toast.querySelector('.novera-toast-close');

        var startTime = Date.now();
        var remainingTime = duration;
        var timerId = null;
        var animFrameId = null;
        var isPaused = false;
        var isDismissed = false;

        function dismiss() {
            if (isDismissed) return;
            isDismissed = true;
            clearTimeout(timerId);
            cancelAnimationFrame(animFrameId);

            toast.classList.add('is-hiding');
            setTimeout(function() {
                if (toast.parentNode) {
                    toast.parentNode.removeChild(toast);
                }
            }, 260);
        }
        toast._dismiss = dismiss;

        closeBtn.addEventListener('click', function(e) {
            e.stopPropagation();
            dismiss();
        });

        function updateProgress() {
            if (isDismissed) return;
            if (!isPaused && duration > 0) {
                var elapsed = Date.now() - startTime;
                var percent = Math.max(0, 100 - (elapsed / duration) * 100);
                if (progressBar) {
                    progressBar.style.width = percent + '%';
                }
            }
            if (!isDismissed && remainingTime > 0) {
                animFrameId = requestAnimationFrame(updateProgress);
            }
        }

        function startTimer() {
            if (duration <= 0) return;
            startTime = Date.now() - (duration - remainingTime);
            timerId = setTimeout(dismiss, remainingTime);
            animFrameId = requestAnimationFrame(updateProgress);
        }

        function pauseTimer() {
            if (duration <= 0 || isDismissed) return;
            isPaused = true;
            clearTimeout(timerId);
            cancelAnimationFrame(animFrameId);
            var elapsed = Date.now() - startTime;
            remainingTime = Math.max(0, duration - elapsed);
        }

        function resumeTimer() {
            if (duration <= 0 || isDismissed || remainingTime <= 0) return;
            isPaused = false;
            startTimer();
        }

        toast.addEventListener('mouseenter', pauseTimer);
        toast.addEventListener('mouseleave', resumeTimer);

        // Start countdown
        startTimer();

        return toast;
    }

    window.NoveraToast = {
        show: function(options) {
            return createToast(options || {});
        },
        success: function(message, title, options) {
            var opts = Object.assign({}, options, { type: 'success', message: message });
            if (title !== undefined) opts.title = title;
            return createToast(opts);
        },
        reject: function(message, title, options) {
            var opts = Object.assign({}, options, { type: 'reject', message: message });
            if (title !== undefined) opts.title = title;
            return createToast(opts);
        },
        error: function(message, title, options) {
            var opts = Object.assign({}, options, { type: 'error', message: message });
            if (title !== undefined) opts.title = title;
            return createToast(opts);
        },
        warning: function(message, title, options) {
            var opts = Object.assign({}, options, { type: 'warning', message: message });
            if (title !== undefined) opts.title = title;
            return createToast(opts);
        },
        info: function(message, title, options) {
            var opts = Object.assign({}, options, { type: 'info', message: message });
            if (title !== undefined) opts.title = title;
            return createToast(opts);
        }
    };

    // Backward compatibility with legacy showToast(message, isSuccess)
    window.showToast = function(message, isSuccess) {
        if (isSuccess === false || isSuccess === 'error') {
            return window.NoveraToast.error(message);
        } else if (isSuccess === 'warning') {
            return window.NoveraToast.warning(message);
        } else if (isSuccess === 'reject') {
            return window.NoveraToast.reject(message);
        } else if (isSuccess === 'info') {
            return window.NoveraToast.info(message);
        }
        return window.NoveraToast.success(message);
    };
})();

