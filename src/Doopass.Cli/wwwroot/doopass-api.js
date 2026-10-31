// Doopass browser HTTP client — thin-client bridge for Blazor Server.
// Called via JS interop from LocalApiService so that HTTP requests
// originate from the browser (visible in the Network tab).

"use strict";

const doopass = window.doopass || (window.doopass = {});

doopass.api = {
    /**
     * Sends a GET request and returns { ok, status, headers, body }.
     */
    async get(url) {
        const res = await fetch(url, { cache: "no-store" });
        return _wrapResponse(res);
    },

    /**
     * Sends a POST request with a JSON body and returns { ok, status, headers, body }.
     */
    async post(url, body) {
        const res = await fetch(url, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: body !== null ? JSON.stringify(body) : undefined,
            cache: "no-store",
        });
        return _wrapResponse(res);
    },

    /**
     * Sends a PUT request with a JSON body and returns { ok, status, headers, body }.
     */
    async put(url, body) {
        const res = await fetch(url, {
            method: "PUT",
            headers: { "Content-Type": "application/json" },
            body: body !== null ? JSON.stringify(body) : undefined,
            cache: "no-store",
        });
        return _wrapResponse(res);
    },

    /**
     * Sends a DELETE request and returns { ok, status, headers, body }.
     */
    async delete(url) {
        const res = await fetch(url, { method: "DELETE", cache: "no-store" });
        return _wrapResponse(res);
    },
};

/**
 * Store and retrieve the vault auth token in localStorage.
 * This is the browser-side counterpart to ApiVaultCoordinator.ActiveToken.
 */
doopass.api.token = {
    get() {
        return localStorage.getItem("doopass_vault_token") ?? null;
    },
    set(token) {
        if (token) {
            localStorage.setItem("doopass_vault_token", token);
        } else {
            localStorage.removeItem("doopass_vault_token");
        }
    },
};

function _wrapResponse(res) {
    const headers = {};
    res.headers.forEach((v, k) => { headers[k] = v; });
    return {
        ok: res.ok,
        status: res.status,
        headers: headers,
        body: res.text(),
    };
}
