/**
 * NoveraTech ATS - Instant Search & URL Query Synchronization Engine
 * Latency target: < 100ms (typical in-memory execution: 1 - 8ms)
 */
(function (window) {
    'use strict';

    /**
     * Chuyển đổi chuỗi tiếng Việt có dấu thành không dấu, viết thường, chuẩn hóa dấu cách
     * Giúp tìm kiếm "hieu" khớp "Lường Minh Hiếu", "ha noi" khớp "Hà Nội".
     */
    function removeVietnameseTones(str) {
        if (!str) return '';
        str = String(str).toLowerCase().trim();
        str = str.replace(/à|á|ạ|ả|ã|â|ầ|ấ|ậ|ẩ|ẫ|ă|ằ|ắ|ặ|ẳ|ẵ/g, 'a');
        str = str.replace(/è|é|ẹ|ẻ|ẽ|ê|ề|ế|ệ|ể|ễ/g, 'e');
        str = str.replace(/ì|í|ị|ỉ|ĩ/g, 'i');
        str = str.replace(/ò|ó|ọ|ỏ|õ|ô|ồ|ố|ộ|ổ|ỗ|ơ|ờ|ớ|ợ|ở|ỡ/g, 'o');
        str = str.replace(/ù|ú|ụ|ủ|ũ|ư|ừ|ứ|ự|ử|ữ/g, 'u');
        str = str.replace(/ỳ|ý|ỵ|ỷ|ỹ/g, 'y');
        str = str.replace(/đ/g, 'd');
        // Ký tự dấu kết hợp Unicode
        str = str.replace(/\u0300|\u0301|\u0303|\u0309|\u0323/g, '');
        str = str.replace(/\u02C6|\u0306|\u031B/g, '');
        // Thay thế ký tự đặc biệt thừa
        str = str.replace(/[\s\-_]+/g, ' ');
        return str;
    }

    /**
     * Debounce hàm thực thi
     */
    function debounce(func, wait) {
        let timeout;
        return function (...args) {
            clearTimeout(timeout);
            timeout = setTimeout(() => func.apply(this, args), wait);
        };
    }

    /**
     * Lấy toàn bộ tham số từ URL hiện tại thành Object
     */
    function getUrlParams() {
        const params = new URLSearchParams(window.location.search);
        const result = {};
        for (const [key, value] of params.entries()) {
            result[key] = value;
        }
        return result;
    }

    /**
     * Kiểm tra xem chuỗi tìm kiếm có đủ điều kiện hợp lệ không (tối thiểu 3 ký tự)
     */
    function isValidSearchQuery(query, minLength = 3) {
        if (!query) return false;
        return String(query).trim().length >= minLength;
    }

    /**
     * Đồng bộ tham số vào URL query mà không tải lại trang (history.replaceState)
     * Tự động loại bỏ các tham số rỗng hoặc giá trị mặc định ('all', 'ALL', '')
     * Bỏ qua từ khóa tìm kiếm nếu chưa đạt tối thiểu minSearchLength (mặc định 3 ký tự)
     */
    const _debouncedReplaceState = debounce(function (url) {
        window.history.replaceState(null, '', url);
    }, 50);

    function syncUrlParams(paramsObj, immediate = false, minSearchLength = 3) {
        try {
            const url = new URL(window.location.href);
            const searchParams = url.searchParams;

            for (const [key, rawVal] of Object.entries(paramsObj)) {
                const val = (rawVal === null || rawVal === undefined) ? '' : String(rawVal).trim();
                const isSearchParam = (key === 'search' || key === 'keyword' || key === 'q');
                if (!val || val === 'all' || val === 'ALL' || (isSearchParam && val.length < minSearchLength)) {
                    searchParams.delete(key);
                } else {
                    searchParams.set(key, val);
                }
            }

            const newUrl = url.pathname + (searchParams.toString() ? '?' + searchParams.toString() : '') + url.hash;
            if (immediate) {
                window.history.replaceState(null, '', newUrl);
            } else {
                _debouncedReplaceState(newUrl);
            }
        } catch (e) {
            console.warn('Lỗi đồng bộ URL params:', e);
        }
    }

    /**
     * Kiểm tra xem văn bản mục tiêu có chứa tất cả các từ trong chuỗi tìm kiếm không (Multi-token match)
     * Quy tắc: Chỉ tìm kiếm khi từ khóa có từ 3 ký tự trở lên (>= 3 chars). Nếu < 3 ký tự, coi như khớp tất cả (không lọc).
     */
    function matchSearchTokens(targetText, queryText, minLength = 3) {
        if (!queryText) return true;
        const trimmed = String(queryText).trim();
        if (trimmed.length === 0) return true;
        // Bắt buộc từ 3 ký tự trở lên mới kích hoạt lọc
        if (trimmed.length < minLength) return true;

        const normalizedTarget = removeVietnameseTones(targetText);
        const normalizedQuery = removeVietnameseTones(trimmed);
        const tokens = normalizedQuery.split(' ').filter(Boolean);
        if (tokens.length === 0) return true;
        return tokens.every(token => normalizedTarget.includes(token));
    }

    /**
     * Hiển thị hoặc ẩn thông báo gợi ý số ký tự tối thiểu
     */
    function updateSearchHint(inputEl, hintEl, minLength = 3) {
        if (!inputEl || !hintEl) return;
        const val = (inputEl.value || '').trim();
        if (val.length > 0 && val.length < minLength) {
            hintEl.classList.remove('d-none');
        } else {
            hintEl.classList.add('d-none');
        }
    }

    // Export ra window toàn cục
    window.NoveraInstantSearch = {
        removeVietnameseTones,
        debounce,
        getUrlParams,
        syncUrlParams,
        matchSearchTokens,
        isValidSearchQuery,
        updateSearchHint
    };

})(window);
