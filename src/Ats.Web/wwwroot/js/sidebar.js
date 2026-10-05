/**
 * ATS Sidebar Controller
 * Handles: mobile open/close, desktop collapse/expand, group toggles, active state, localStorage
 * RULE.MD Compliant – NoveraTech ATS v2.0
 */

(function () {
    'use strict';

    var STORAGE_KEY_COLLAPSED = 'ats_sidebar_collapsed';
    var STORAGE_KEY_GROUPS    = 'ats_sidebar_groups';

    document.addEventListener('DOMContentLoaded', function () {
        var sidebar  = document.getElementById('atsSidebar');
        var backdrop = document.getElementById('atsSidebarBackdrop');
        if (!sidebar) return;

        /* ─────────────────────────────────────────
           1. MOBILE: Open / Close slide-drawer
        ───────────────────────────────────────── */
        var btnToggleMobile = document.getElementById('btnToggleSidebar');
        var btnCloseMobile  = document.getElementById('btnCloseSidebar');

        function openSidebar() {
            sidebar.classList.add('show');
            if (backdrop) backdrop.classList.add('show');
            document.body.style.overflow = 'hidden';
        }

        function closeSidebar() {
            sidebar.classList.remove('show');
            if (backdrop) backdrop.classList.remove('show');
            document.body.style.overflow = '';
        }

        if (btnToggleMobile) btnToggleMobile.addEventListener('click', openSidebar);
        if (btnCloseMobile)  btnCloseMobile.addEventListener('click', closeSidebar);
        if (backdrop)        backdrop.addEventListener('click', closeSidebar);

        window.closeAtsSidebar = closeSidebar;

        /* ─────────────────────────────────────────
           2. DESKTOP: Collapse / Expand sidebar
        ───────────────────────────────────────── */
        var btnCollapse  = document.getElementById('btnToggleSidebarCollapse');
        var collapseIcon = document.getElementById('sidebarCollapseIcon');

        function setSidebarCollapsed(collapsed) {
            if (collapsed) {
                sidebar.classList.add('collapsed');
                if (collapseIcon) {
                    collapseIcon.classList.remove('bi-layout-sidebar-reverse');
                    collapseIcon.classList.add('bi-layout-sidebar');
                }
                localStorage.setItem(STORAGE_KEY_COLLAPSED, '1');
            } else {
                sidebar.classList.remove('collapsed');
                if (collapseIcon) {
                    collapseIcon.classList.remove('bi-layout-sidebar');
                    collapseIcon.classList.add('bi-layout-sidebar-reverse');
                }
                localStorage.setItem(STORAGE_KEY_COLLAPSED, '0');
            }
        }

        // Restore collapse state from localStorage (desktop only)
        if (window.innerWidth >= 992) {
            var stored = localStorage.getItem(STORAGE_KEY_COLLAPSED);
            if (stored === '1') {
                setSidebarCollapsed(true);
            }
        }

        if (btnCollapse) {
            btnCollapse.addEventListener('click', function () {
                var isNowCollapsed = !sidebar.classList.contains('collapsed');
                setSidebarCollapsed(isNowCollapsed);
            });
        }

        /* ─────────────────────────────────────────
           3. GROUP TOGGLE: Collapse/expand sections
        ───────────────────────────────────────── */
        var savedGroups = {};
        try {
            savedGroups = JSON.parse(localStorage.getItem(STORAGE_KEY_GROUPS) || '{}');
        } catch (e) {
            savedGroups = {};
        }

        function saveGroupState(groupId, collapsed) {
            savedGroups[groupId] = collapsed ? '1' : '0';
            try {
                localStorage.setItem(STORAGE_KEY_GROUPS, JSON.stringify(savedGroups));
            } catch (e) {}
        }

        function setGroupCollapsed(label, section, collapsed) {
            if (collapsed) {
                label.classList.add('group-collapsed');
                if (section) {
                    section.style.maxHeight = section.scrollHeight + 'px';
                    // Force reflow then set 0
                    section.getBoundingClientRect();
                    section.style.maxHeight = '0';
                    section.style.overflow = 'hidden';
                }
            } else {
                label.classList.remove('group-collapsed');
                if (section) {
                    section.style.maxHeight = section.scrollHeight + 'px';
                    section.style.overflow = 'hidden';
                    // After transition, remove max-height cap
                    section.addEventListener('transitionend', function onEnd() {
                        if (!label.classList.contains('group-collapsed')) {
                            section.style.maxHeight = '';
                            section.style.overflow = '';
                        }
                        section.removeEventListener('transitionend', onEnd);
                    });
                }
            }
        }

        // Init all group labels
        var categoryLabels = sidebar.querySelectorAll('.sidebar-category-label[data-group]');
        categoryLabels.forEach(function (label) {
            var groupId  = label.getAttribute('data-group');
            var section  = document.getElementById('section-' + groupId);
            var chevron  = label.querySelector('.sidebar-group-chevron');
            if (!section) return;

            // Set initial max-height for open sections
            section.style.maxHeight = section.scrollHeight + 'px';

            // Restore state from localStorage
            if (savedGroups[groupId] === '1') {
                // Collapsed — use instant collapse (no animation on load)
                label.classList.add('group-collapsed');
                section.style.maxHeight = '0';
                section.style.overflow  = 'hidden';
            }

            // Click on chevron or label to toggle
            function toggleGroup() {
                // Don't toggle if sidebar is icon-only (collapsed desktop)
                if (sidebar.classList.contains('collapsed')) return;
                var isCollapsed = label.classList.contains('group-collapsed');
                setGroupCollapsed(label, section, !isCollapsed);
                saveGroupState(groupId, !isCollapsed);
            }

            if (chevron) {
                chevron.addEventListener('click', function (e) {
                    e.stopPropagation();
                    toggleGroup();
                });
            }
            // Clicking the whole label text area (except the chevron) does nothing —
            // only the chevron toggles the group (prevents accidental collapses)
        });

        /* ─────────────────────────────────────────
           4. ACTIVE STATE: Highlight current route
        ───────────────────────────────────────── */
        var currentPath = window.location.pathname.toLowerCase().replace(/\/$/, '');
        var sidebarLinks = sidebar.querySelectorAll('.sidebar-nav-link');

        sidebarLinks.forEach(function (link) {
            var dataPath = (link.getAttribute('data-path') || '').toLowerCase();
            var href     = (link.getAttribute('href') || '').toLowerCase().replace(/\/$/, '');
            var target   = dataPath || href;

            var isActive = false;
            if (target === currentPath) {
                isActive = true;
            } else if (target.length > 2
                && target !== '/dashboard'
                && target !== '/dashboard/tong-quan'
                && currentPath.startsWith(target)) {
                isActive = true;
            } else if ((target === '/dashboard' || target === '/dashboard/tong-quan')
                && (currentPath === '/dashboard' || currentPath === '/dashboard/tong-quan')) {
                isActive = true;
            }

            if (isActive) {
                link.classList.add('active');
                // If the parent group is collapsed, auto-expand it
                var parentSection = link.closest('.sidebar-section');
                if (parentSection) {
                    var sectionId = parentSection.id; // e.g. "section-staff-workspace"
                    var gId       = sectionId.replace('section-', '');
                    var parentLabel = sidebar.querySelector('[data-group="' + gId + '"]');
                    if (parentLabel && parentLabel.classList.contains('group-collapsed')) {
                        setGroupCollapsed(parentLabel, parentSection, false);
                        saveGroupState(gId, false);
                    }
                }
            }
        });

        /* ─────────────────────────────────────────
           5. DASHBOARD TAB SWITCHING
        ───────────────────────────────────────── */
        document.addEventListener('click', function (e) {
            var sidebarLink = e.target.closest('.ats-sidebar .sidebar-nav-link');
            if (!sidebarLink) return;

            var href = (sidebarLink.getAttribute('href') || '').toLowerCase();
            if (href.startsWith('/dashboard') && typeof window.switchDashboardFeature === 'function') {
                e.preventDefault();
                var feature = 'tong-quan';
                if (href.startsWith('/dashboard/')) {
                    feature = href.replace('/dashboard/', '').trim() || 'tong-quan';
                }
                window.switchDashboardFeature(feature);
                if (window.innerWidth < 992) closeSidebar();
            }
        });
    });
})();
