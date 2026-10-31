// Doopass browser HTTP client — thin-client bridge for Blazor Server.
// Called via JS interop from LocalApiService so that HTTP requests
// originate from the browser (visible in the Network tab).

"use strict";

export async function get(url) {
    const res = await fetch(url, { cache: "no-store" });
    return _wrapResponse(res);
}

export async function post(url, body) {
    const res = await fetch(url, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: body !== null ? JSON.stringify(body) : undefined,
        cache: "no-store",
    });
    return _wrapResponse(res);
}

export async function put(url, body) {
    const res = await fetch(url, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: body !== null ? JSON.stringify(body) : undefined,
        cache: "no-store",
    });
    return _wrapResponse(res);
}

export async function deleteReq(url) {
    const res = await fetch(url, { method: "DELETE", cache: "no-store" });
    return _wrapResponse(res);
}

/**
 * Store and retrieve the vault auth token in localStorage.
 * This is the browser-side counterpart to ApiVaultCoordinator.ActiveToken.
 */
export function getToken() {
    return localStorage.getItem("doopass_vault_token") ?? null;
}

export function setToken(token) {
    if (token) {
        localStorage.setItem("doopass_vault_token", token);
    } else {
        localStorage.removeItem("doopass_vault_token");
    }
}

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
