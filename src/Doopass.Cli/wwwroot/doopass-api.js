// Doopass browser HTTP client — thin-client bridge for Blazor Server.
// Called via IJSRuntime.InvokeAsync from LocalApiService so that HTTP
// requests originate from the browser (visible in the Network tab).

"use strict";

window.doopass = window.doopass || {};

window.doopass.fetchGet = async function (url, token) {
    const headers = {};
    if (token && typeof token === "string") {
        // Blazor serializes C# strings as JSON values — a .NET string value
        // arrives here as a plain JS string ("abc123"), not escaped.
        headers["Authorization"] = `Bearer ${token}`;
    }
    const res = await fetch(url, {
        method: "GET",
        headers: headers,
        cache: "no-store",
    });
    return _wrapResponse(res);
};

window.doopass.fetchPost = async function (url, body, token) {
    const headers = { "Content-Type": "application/json" };
    if (token && typeof token === "string") {
        const actualToken = token.startsWith('"') && token.endsWith('"')
            ? JSON.parse(token)
            : token;
        headers["Authorization"] = `Bearer ${actualToken}`;
    }
    const res = await fetch(url, {
        method: "POST",
        headers: headers,
        body: body !== null && body !== undefined
            ? (typeof body === "string" ? body : JSON.stringify(body))
            : undefined,
        cache: "no-store",
    });
    return _wrapResponse(res);
};

window.doopass.fetchPut = async function (url, body, token) {
    const headers = { "Content-Type": "application/json" };
    if (token && typeof token === "string") {
        const actualToken = token.startsWith('"') && token.endsWith('"')
            ? JSON.parse(token)
            : token;
        headers["Authorization"] = `Bearer ${actualToken}`;
    }
    const res = await fetch(url, {
        method: "PUT",
        headers: headers,
        body: body !== null && body !== undefined
            ? (typeof body === "string" ? body : JSON.stringify(body))
            : undefined,
        cache: "no-store",
    });
    return _wrapResponse(res);
};

window.doopass.fetchDelete = async function (url, token) {
    const headers = {};
    if (token && typeof token === "string") {
        const actualToken = token.startsWith('"') && token.endsWith('"')
            ? JSON.parse(token)
            : token;
        headers["Authorization"] = `Bearer ${actualToken}`;
    }
    const res = await fetch(url, {
        method: "DELETE",
        headers: headers,
        cache: "no-store",
    });
    return _wrapResponse(res);
};

window.doopass.getToken = function () {
    return localStorage.getItem("doopass_vault_token") ?? null;
};

window.doopass.setToken = function (token) {
    if (token) {
        localStorage.setItem("doopass_vault_token", token);
    } else {
        localStorage.removeItem("doopass_vault_token");
    }
};

async function _wrapResponse(res) {
    const headers = {};
    res.headers.forEach((v, k) => { headers[k] = v; });
    return {
        ok: res.ok,
        status: res.status,
        headers: headers,
        body: await res.text(),
    };
}
