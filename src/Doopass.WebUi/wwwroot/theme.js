// Doopass theme bootstrap: applies the persisted (or system) theme before paint,
// and exposes a small API used by the ThemeToggle component.
(function () {
    'use strict';

    var KEY = 'doopass-theme';
    var root = document.documentElement;

    function systemMode() {
        try {
            return window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches
                ? 'dark'
                : 'light';
        } catch (e) {
            return 'light';
        }
    }

    function storedMode() {
        try {
            return localStorage.getItem(KEY);
        } catch (e) {
            return null;
        }
    }

    // The mode that should be on screen: the last explicit choice, else the system
    // preference. Anything other than 'dark' / 'light' is ignored so a corrupted
    // value in localStorage cannot leave the page without a theme.
    function preferred() {
        var stored = storedMode();
        return stored === 'dark' || stored === 'light' ? stored : systemMode();
    }

    function current() {
        var applied = root.getAttribute('data-theme');
        return applied === 'dark' || applied === 'light' ? applied : preferred();
    }

    function apply(mode) {
        if (root.getAttribute('data-theme') !== mode) {
            root.setAttribute('data-theme', mode);
        }

        // Keep the Android/Chrome address-bar color in sync with the theme.
        // (Passing null is safe: querySelector simply finds nothing.)
        var meta = document.querySelector('meta[name="theme-color"]');
        if (meta) {
            meta.setAttribute('content', mode === 'dark' ? '#0d0f15' : '#f6f7fb');
        }

        return mode;
    }

    function set(mode) {
        apply(mode);
        try {
            localStorage.setItem(KEY, mode);
        } catch (e) {
            /* ignore */
        }
        return mode;
    }

    // Apply as early as possible (avoids a flash of the wrong theme).
    apply(preferred());

    // Blazor rewrites the attributes of <html> from server-rendered markup that never
    // carries data-theme (for example the redirect to the login page when a page is
    // reloaded without a live circuit), and theme.js is not re-run for that
    // navigation -- so the page dropped back to the light theme while the toggle
    // still reported the saved one. Re-assert the theme whenever it is removed or
    // overwritten by someone else.
    function restore() {
        apply(preferred());
    }

    if (window.MutationObserver) {
        new MutationObserver(restore).observe(root, {
            attributes: true,
            attributeFilter: ['data-theme']
        });
    }

    document.addEventListener('enhancedload', restore);
    document.addEventListener('visibilitychange', function () {
        if (!document.hidden) {
            restore();
        }
    });
    window.addEventListener('pageshow', restore);

    window.doopassTheme = {
        toggle: function () {
            return set(current() === 'dark' ? 'light' : 'dark');
        },
        isDark: function () {
            return current() === 'dark';
        },
        set: set,
        current: current,
        restore: restore
    };
})();
