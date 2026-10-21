/**
 * ATS Instant Navigation & L1 Client-Side Page Cache Engine
 * Provides single-page-app (SPA) speed with 0ms-50ms transitions across all ATS routes.
 * Author: NoveraTech Engineering / ATS Core Team
 */

(function () {
    'use strict';

    // ─────────────────────────────────────────────────────────────
    // 1. CONFIGURATION & STATE
    // ─────────────────────────────────────────────────────────────
    var CACHE_TTL_MS = 5 * 60 * 1000; // 5 minutes TTL
    var MAX_CACHE_SIZE = 25;           // Max pages in memory
    var HOVER_DELAY_MS = 65;           // 65ms intent threshold
    var pageCache = new Map();         // url -> { html, title, timestamp }
    var prefetchTimers = new Map();    // element -> timerId
    var inFlightFetches = new Map();   // url -> Promise<string>
    var isNavigating = false;

    // Progress Bar element
    var progressBar = null;
    var progressTimer = null;

    function getOrCreateProgressBar() {
        if (!progressBar) {
            progressBar = document.getElementById('atsProgressBar');
            if (!progressBar) {
                progressBar = document.createElement('div');
                progressBar.id = 'atsProgressBar';
                progressBar.style.cssText = 'position:fixed;top:0;left:0;height:3px;width:0;background:linear-gradient(90deg, #059669 0%, #10B981 50%, #34D399 100%);z-index:999999;transition:width 0.2s cubic-bezier(0.4,0,0.2,1), opacity 0.3s ease;opacity:0;pointer-events:none;box-shadow:0 0 10px rgba(16,185,129,0.5);';
                document.body.appendChild(progressBar);
            }
        }
        return progressBar;
    }

    function startProgressBar() {
        var bar = getOrCreateProgressBar();
        if (progressTimer) clearInterval(progressTimer);
        bar.style.transition = 'width 0.15s ease, opacity 0.1s ease';
        bar.style.opacity = '1';
        bar.style.width = '20%';

        var currentWidth = 20;
        progressTimer = setInterval(function () {
            if (currentWidth < 80) {
                currentWidth += (80 - currentWidth) * 0.15;
                bar.style.width = currentWidth + '%';
            }
        }, 80);
    }

    function finishProgressBar() {
        if (progressTimer) {
            clearInterval(progressTimer);
            progressTimer = null;
        }
        var bar = getOrCreateProgressBar();
        bar.style.transition = 'width 0.15s ease, opacity 0.25s ease 0.1s';
        bar.style.width = '100%';
        setTimeout(function () {
            bar.style.opacity = '0';
            setTimeout(function () {
                bar.style.width = '0%';
            }, 250);
        }, 150);
    }

    function decodeHtmlEntities(str) {
        if (!str) return '';
        var txt = document.createElement('textarea');
        txt.innerHTML = str;
        return txt.value;
    }

    // ─────────────────────────────────────────────────────────────
    // 2. QUALIFICATION & URL NORMALIZATION
    // ─────────────────────────────────────────────────────────────
    function normalizeUrl(rawUrl) {
        try {
            var u = new URL(rawUrl, window.location.origin);
            return u.pathname + u.search;
        } catch (e) {
            return rawUrl;
        }
    }

    function isQualifyingLink(link) {
        if (!link || !link.href) return false;
        if (link.getAttribute('target') === '_blank') return false;
        if (link.getAttribute('download') !== null) return false;
        if (link.getAttribute('data-no-instant') === 'true') return false;

        var href = link.getAttribute('href');
        if (!href) return false;
        if (href.startsWith('#') || href.startsWith('javascript:') || href.startsWith('mailto:') || href.startsWith('tel:')) {
            return false;
        }

        // Check same origin
        try {
            var url = new URL(link.href, window.location.origin);
            if (url.origin !== window.location.origin) return false;

            var path = url.pathname.toLowerCase();

            // Exclude auth, signout, api, uploads, and binary files
            if (path.startsWith('/account/stafflogin') ||
                path.startsWith('/account/stafflogout') ||
                path.startsWith('/account/candidateauth') ||
                path.startsWith('/account/candidatelogout') ||
                path.startsWith('/signin-') ||
                path.startsWith('/signout-') ||
                path.startsWith('/api/') ||
                path.startsWith('/uploads/') ||
                path.startsWith('/templates/')) {
                return false;
            }

            // Exclude static extensions
            var exts = ['.xlsx', '.xls', '.pdf', '.docx', '.doc', '.zip', '.csv', '.png', '.jpg', '.jpeg', '.gif', '.svg'];
            for (var i = 0; i < exts.length; i++) {
                if (path.endsWith(exts[i])) return false;
            }

            return true;
        } catch (e) {
            return false;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 3. INTENT-DRIVEN PRE-FETCHING
    // ─────────────────────────────────────────────────────────────
    function prefetchUrl(urlStr) {
        var normalized = normalizeUrl(urlStr);
        var cached = pageCache.get(normalized);
        if (cached && (Date.now() - cached.timestamp < CACHE_TTL_MS)) {
            return Promise.resolve(cached);
        }

        if (inFlightFetches.has(normalized)) {
            return inFlightFetches.get(normalized);
        }

        var fetchPromise = fetch(urlStr, {
            method: 'GET',
            headers: {
                'X-Requested-With': 'Ats-Instant-Nav',
                'Accept': 'text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8'
            },
            credentials: 'same-origin'
        })
        .then(function (res) {
            inFlightFetches.delete(normalized);
            if (!res.ok) throw new Error('HTTP ' + res.status);
            var ct = res.headers.get('content-type') || '';
            if (!ct.includes('text/html')) throw new Error('Non-HTML response');

            // If redirected to login, do not cache
            if (res.redirected && (res.url.includes('/StaffLogin') || res.url.includes('/CandidateAuth'))) {
                return null;
            }

            return res.text();
        })
        .then(function (html) {
            // Lấy tiêu đề trang và giải mã thực thể HTML
            var titleMatch = html.match(/<title[^>]*>([^<]+)<\/title>/i);
            var title = titleMatch ? decodeHtmlEntities(titleMatch[1]) : document.title;

            var cacheEntry = {
                html: html,
                title: title,
                timestamp: Date.now()
            };

            // Manage cache size limit
            if (pageCache.size >= MAX_CACHE_SIZE) {
                var firstKey = pageCache.keys().next().value;
                pageCache.delete(firstKey);
            }

            pageCache.set(normalized, cacheEntry);
            return cacheEntry;
        })
        .catch(function (err) {
            inFlightFetches.delete(normalized);
            return null;
        });

        inFlightFetches.set(normalized, fetchPromise);
        return fetchPromise;
    }

    function getClosestLink(target) {
        if (!target) return null;
        var el = (target.nodeType === 1) ? target : (target.parentElement || null);
        if (el && typeof el.closest === 'function') {
            return el.closest('a');
        }
        return null;
    }

    function onPointerEnter(e) {
        var link = getClosestLink(e.target);
        if (!isQualifyingLink(link)) return;

        var url = link.href;
        var timer = setTimeout(function () {
            prefetchUrl(url);
            prefetchTimers.delete(link);
        }, HOVER_DELAY_MS);

        prefetchTimers.set(link, timer);
    }

    function onPointerLeave(e) {
        var link = getClosestLink(e.target);
        if (!link) return;
        if (prefetchTimers.has(link)) {
            clearTimeout(prefetchTimers.get(link));
            prefetchTimers.delete(link);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 4. SOFT NAVIGATION & DOM SWAPPING (0ms - 50ms)
    // ─────────────────────────────────────────────────────────────
    function getMainContainer(doc) {
        return doc.getElementById('atsMainContent') || doc.querySelector('main[role="main"]') || doc.querySelector('main');
    }

    function syncHeadStyles(newDoc) {
        if (!newDoc || !newDoc.head) return;
        var newStyles = Array.from(newDoc.head.querySelectorAll('link[rel="stylesheet"]'));
        var currentHrefs = new Set(Array.from(document.head.querySelectorAll('link[rel="stylesheet"]')).map(function (l) { return l.getAttribute('href'); }));
        newStyles.forEach(function (node) {
            var href = node.getAttribute('href');
            if (href && !currentHrefs.has(href)) {
                var newLink = document.createElement('link');
                Array.from(node.attributes).forEach(function (attr) {
                    newLink.setAttribute(attr.name, attr.value);
                });
                document.head.appendChild(newLink);
                currentHrefs.add(href);
            }
        });
    }

    function decodeCloudflareEmails(container) {
        var scope = container || document;
        var cfEls = scope.querySelectorAll('.__cf_email__');
        cfEls.forEach(function (el) {
            var enc = el.getAttribute('data-cfemail');
            if (!enc) return;
            try {
                var email = '';
                var r = parseInt(enc.substr(0, 2), 16);
                for (var n = 2; enc.length - n; n += 2) {
                    email += String.fromCharCode(parseInt(enc.substr(n, 2), 16) ^ r);
                }
                var textNode = document.createTextNode(email);
                el.parentNode.replaceChild(textNode, el);
            } catch (e) { }
        });
    }

    function executePageScripts(container) {
        if (!container) return;
        var scripts = Array.from(container.querySelectorAll('script'));
        scripts.forEach(function (oldScript) {
            try {
                var newScript = document.createElement('script');
                Array.from(oldScript.attributes).forEach(function (attr) {
                    newScript.setAttribute(attr.name, attr.value);
                });
                newScript.textContent = oldScript.textContent;
                oldScript.parentNode.replaceChild(newScript, oldScript);
            } catch (err) {
                console.warn('[AtsInstantNav] Script execution warning:', err);
            }
        });
    }

    function reinitializeBootstrapComponents(container) {
        if (!window.bootstrap) return;
        var scope = container || document;

        // Khởi tạo tooltips an toàn tránh trùng instance
        if (bootstrap.Tooltip) {
            var tooltipTriggerList = [].slice.call(scope.querySelectorAll('[data-bs-toggle="tooltip"], [data-bs-toggle-second="tooltip"]'));
            tooltipTriggerList.forEach(function (tooltipTriggerEl) {
                try {
                    bootstrap.Tooltip.getOrCreateInstance(tooltipTriggerEl);
                } catch (e) { }
            });
        }

        // Dropdowns
        if (bootstrap.Dropdown) {
            var dropdownTriggerList = [].slice.call(scope.querySelectorAll('[data-bs-toggle="dropdown"]'));
            dropdownTriggerList.forEach(function (dropdownToggleEl) {
                try {
                    bootstrap.Dropdown.getOrCreateInstance(dropdownToggleEl);
                } catch (e) { }
            });
        }
    }

    function navigateTo(targetUrl, pushState, bypassCache) {
        if (isNavigating) return;
        isNavigating = true;

        var normalized = normalizeUrl(targetUrl);
        var currentNormalized = normalizeUrl(window.location.href);

        // Same URL hash jump check
        if (normalized === currentNormalized && targetUrl.includes('#')) {
            isNavigating = false;
            var hash = new URL(targetUrl, window.location.origin).hash;
            if (hash) {
                var el = document.querySelector(hash);
                if (el) el.scrollIntoView({ behavior: 'smooth' });
            }
            return;
        }

        var cached = !bypassCache ? pageCache.get(normalized) : null;
        var isInstant = cached && (Date.now() - cached.timestamp < CACHE_TTL_MS);

        if (!isInstant) {
            startProgressBar();
        }

        var loadPromise = isInstant ? Promise.resolve(cached) : prefetchUrl(targetUrl);

        loadPromise.then(function (entry) {
            if (!entry || !entry.html) {
                finishProgressBar();
                window.location.href = targetUrl;
                isNavigating = false;
                return;
            }

            try {
                var parser = new DOMParser();
                var newDoc = parser.parseFromString(entry.html, 'text/html');

                var newMain = getMainContainer(newDoc);
                var currentMain = getMainContainer(document);

                if (!newMain || !currentMain) {
                    finishProgressBar();
                    window.location.href = targetUrl;
                    isNavigating = false;
                    return;
                }

                // Đồng bộ stylesheet từ trang mới vào head
                syncHeadStyles(newDoc);

                // 1. Thay thế nội dung chính
                currentMain.innerHTML = newMain.innerHTML;
                Array.from(newMain.attributes).forEach(function (attr) {
                    currentMain.setAttribute(attr.name, attr.value);
                });

                // 1.1 Đồng bộ AntiForgeryToken từ trang mới sang container toàn cục
                var newCsrf = newDoc.querySelector('input[name="__RequestVerificationToken"]');
                if (newCsrf) {
                    var currentGlobalCsrf = document.querySelector('#globalAntiforgeryContainer input[name="__RequestVerificationToken"]');
                    if (currentGlobalCsrf) {
                        currentGlobalCsrf.value = newCsrf.value;
                    }
                }

                // 1.2 Thay thế script riêng của trang nếu có (#atsPageScripts)
                var newPageScripts = newDoc.getElementById('atsPageScripts');
                var currentPageScripts = document.getElementById('atsPageScripts');
                if (newPageScripts && currentPageScripts) {
                    currentPageScripts.innerHTML = newPageScripts.innerHTML;
                    executePageScripts(currentPageScripts);
                }

                // 2. Cập nhật tiêu đề trang
                if (newDoc && newDoc.title) {
                    document.title = newDoc.title;
                } else if (entry.title) {
                    document.title = decodeHtmlEntities(entry.title);
                }

                // 3. Update history state
                if (pushState) {
                    window.history.pushState({ atsInstant: true, url: targetUrl }, '', targetUrl);
                }

                // 4. Scroll position to top
                window.scrollTo(0, 0);

                // 5. Update Sidebar Active Item
                if (typeof window.updateAtsSidebarActive === 'function') {
                    window.updateAtsSidebarActive();
                }

                // 6. Close mobile sidebar if open
                if (typeof window.closeAtsSidebar === 'function' && window.innerWidth < 992) {
                    window.closeAtsSidebar();
                }

                // 7. Re-execute inline scripts within main
                executePageScripts(currentMain);

                // 8. Tự động giải mã email nếu Cloudflare đã che
                decodeCloudflareEmails(currentMain);

                // 9. Re-initialize Tooltips & Bootstrap widgets
                reinitializeBootstrapComponents(currentMain);

                // 10. Kích hoạt lại sự kiện DOMContentLoaded cho các script trên trang mới
                try {
                    document.dispatchEvent(new Event('DOMContentLoaded'));
                    window.dispatchEvent(new Event('load'));
                } catch (evtErr) { }

                // 11. Dispatch custom event for page listeners
                window.dispatchEvent(new CustomEvent('ats:navigated', {
                    detail: { url: targetUrl, isCached: isInstant }
                }));

                finishProgressBar();
            } catch (err) {
                console.error('[AtsInstantNav] Error during DOM swap:', err);
                window.location.href = targetUrl;
            } finally {
                isNavigating = false;
            }
        }).catch(function (err) {
            console.error('[AtsInstantNav] Failed to load:', err);
            finishProgressBar();
            window.location.href = targetUrl;
            isNavigating = false;
        });
    }

    // ─────────────────────────────────────────────────────────────
    // 5. EVENT LISTENERS: CLICK, POPSTATE, MUTATION INVALIDATION
    // ─────────────────────────────────────────────────────────────
    function onDocumentClick(e) {
        // Only primary mouse button and no modifier keys
        if (e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;

        var link = getClosestLink(e.target);
        if (!isQualifyingLink(link)) return;

        var href = link.href;

        // If clicking on Dashboard sub-tab handled by switchDashboardFeature
        var urlObj = new URL(href, window.location.origin);
        if (urlObj.pathname.startsWith('/dashboard') && typeof window.switchDashboardFeature === 'function' && window.location.pathname.startsWith('/dashboard')) {
            // Let sidebar.js handle dashboard sub-features
            return;
        }

        e.preventDefault();
        var bypassCache = link.getAttribute('data-no-cache') === 'true';
        navigateTo(href, true, bypassCache);
    }

    function onPopState(e) {
        navigateTo(window.location.href, false, false);
    }

    // Auto-Invalidate cache on any data mutations (POST, PUT, DELETE)
    function invalidateCache() {
        pageCache.clear();
        inFlightFetches.clear();
    }

    function interceptMutations() {
        // 1. Form submissions
        document.addEventListener('submit', function (e) {
            var form = e.target;
            var method = (form.getAttribute('method') || 'get').toLowerCase();
            if (method === 'post') {
                invalidateCache();
            }
        }, true);

        // 2. Fetch API mutations
        var originalFetch = window.fetch;
        window.fetch = function (resource, init) {
            var method = (init && init.method ? init.method : 'GET').toUpperCase();
            if (method === 'POST' || method === 'PUT' || method === 'DELETE' || method === 'PATCH') {
                invalidateCache();
            }
            return originalFetch.apply(this, arguments);
        };

        // 3. Custom event listener
        window.addEventListener('ats:invalidate-cache', invalidateCache);
    }

    // ─────────────────────────────────────────────────────────────
    // 6. INITIALIZATION
    // ─────────────────────────────────────────────────────────────
    function init() {
        // Warm up current page into cache
        var currentUrl = normalizeUrl(window.location.href);
        var currentMain = getMainContainer(document);
        if (currentMain) {
            pageCache.set(currentUrl, {
                html: document.documentElement.outerHTML,
                title: document.title,
                timestamp: Date.now()
            });
        }

        // Mouse hover prefetch listeners (PointerEvents)
        document.addEventListener('pointerenter', onPointerEnter, true);
        document.addEventListener('pointerleave', onPointerLeave, true);
        document.addEventListener('touchstart', onPointerEnter, { passive: true, capture: true });

        // Navigation click listener
        document.addEventListener('click', onDocumentClick, true);

        // Browser Back/Forward buttons
        window.addEventListener('popstate', onPopState);

        // Cache Invalidation on data changes
        interceptMutations();

        // Export global controller
        window.atsInstant = {
            cache: pageCache,
            navigateTo: navigateTo,
            clearCache: invalidateCache,
            prefetch: prefetchUrl
        };
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
