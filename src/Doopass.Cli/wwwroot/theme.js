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

    function current() {
        return root.getAttribute('data-theme') || storedMode() || systemMode();
    }

    function apply(mode) {
        root.setAttribute('data-theme', mode);
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
    apply(storedMode() || systemMode());

    window.doapassTheme = {
        toggle: function () {
            return set(current() === 'dark' ? 'light' : 'dark');
        },
        isDark: function () {
            return current() === 'dark';
        },
        set: set,
        current: current
    };
})();
