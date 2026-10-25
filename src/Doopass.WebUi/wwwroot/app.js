// Doopass web helpers.
// Served from an embedded resource and mapped at /app.js (see Program.cs).
// Contents: clipboard helper used by the store page and support for the
// custom Blazor Server reconnection dialog (#components-reconnect-modal).
(function () {
    'use strict';

    window.doopass = window.doopass || {};

    // Legacy fallback for non-secure contexts (plain HTTP on a LAN address,
    // older browsers) that have no navigator.clipboard at all. Must stay in
    // one synchronous block; execCommand('copy') is deprecated but still the
    // only way to write the clipboard outside a secure context.
    function legacyCopy(text) {
        var ta = document.createElement('textarea');
        ta.value = text;
        ta.setAttribute('readonly', '');
        ta.style.position = 'fixed';
        ta.style.top = '0';
        ta.style.left = '-9999px';
        ta.style.opacity = '0';
        document.body.appendChild(ta);

        var ok = false;
        var selection = document.getSelection();
        var savedRange = selection && selection.rangeCount > 0 ? selection.getRangeAt(0) : null;

        try {
            ta.select();
            ta.setSelectionRange(0, text.length);
            ok = document.execCommand('copy');

            if (savedRange) {
                selection.removeAllRanges();
                selection.addRange(savedRange);
            }
        } catch (e) {
            ok = false;
        } finally {
            document.body.removeChild(ta);
        }

        return ok;
    }

    // Returns true on success, false when the platform blocked the copy.
    // Never throws: a failed copy must not tear down the Blazor circuit.
    window.doopass.copyText = async function (text) {
        if (typeof text !== 'string') {
            return false;
        }

        var api = navigator.clipboard && navigator.clipboard.writeText;

        if (api) {
            try {
                await navigator.clipboard.writeText(text);
                return true;
            } catch (e) {
                // Permission denied or missing user activation: fall through
                // to the legacy path before giving up.
            }
        }

        return legacyCopy(text);
    };

    // blazor.web.js merely toggles classes on the custom reconnect dialog and
    // never wires up its buttons, so handle the reload action here.
    document.addEventListener('click', function (ev) {
        var target = ev.target instanceof Element ? ev.target : null;
        var button = target && target.closest ? target.closest('[data-reconnect-action]') : null;

        if (button) {
            ev.preventDefault();

            if (button.getAttribute('data-reconnect-action') === 'reload') {
                location.reload();
            }
        }
    });

    // ---- PWA ---------------------------------------------------------------
    // Installable web app: manifest + service worker are served by the server
    // from embedded resources (see Program.cs MapGet("/pwa/...")). The service
    // worker only registers in secure contexts (http://localhost or HTTPS);
    // on a plain-HTTP LAN address it silently stays absent and the page works
    // as an ordinary web page.

    var installEvent = null;

    window.addEventListener('beforeinstallprompt', function (e) {
        // Chrome/Edge desktop + Android: keep the event so the Settings page
        // can show its own install button instead of the browser mini-infobar.
        e.preventDefault();
        installEvent = e;
    });

    window.addEventListener('appinstalled', function () {
        installEvent = null;
    });

    window.doopass.pwa = {
        isStandalone: function () {
            if (navigator.standalone) {
                return true; // iOS "Add to Home Screen" web app
            }
            return !!(window.matchMedia &&
                (window.matchMedia('(display-mode: standalone)').matches ||
                 window.matchMedia('(display-mode: fullscreen)').matches));
        },
        canInstall: function () {
            return installEvent !== null;
        },
        promptInstall: async function () {
            var e = installEvent;
            if (!e) {
                return 'unavailable';
            }
            var choice;
            try {
                choice = await e.prompt();
            } catch (err) {
                return 'error';
            }
            return choice && choice.outcome === 'accepted' ? 'accepted' : 'dismissed';
        },
        // Single probe for the Settings card. Returns one of:
        // 'standalone' | 'installable' | 'ios' | 'manual' | 'no-service-worker'.
        probe: function () {
            if (!('serviceWorker' in navigator)) {
                return 'no-service-worker';
            }
            if (this.isStandalone()) {
                return 'standalone';
            }
            if (this.canInstall()) {
                return 'installable';
            }
            var ua = navigator.userAgent;
            var iPadOs = /(iPad|Macintosh)/.test(ua) && 'ontouchend' in document;
            if (/iP(hone|ad|od)/.test(ua) || iPadOs) {
                return 'ios';
            }
            return 'manual';
        }
    };

    // Offline shell registration. Failures are silent by design: without the
    // worker the interface still works as a normal web page.
    if ('serviceWorker' in navigator) {
        navigator.serviceWorker
            .register('/pwa/sw.js', { scope: '/' })
            .catch(function () { /* no offline shell on this origin */ });
    }
})();
