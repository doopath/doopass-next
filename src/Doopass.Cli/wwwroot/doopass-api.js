// Doopass browser HTTP client — thin-client bridge for Blazor Server.
// Registered on window.doopass by BrowserHttpClient.RegisterAsync().
// Called via IJSRuntime.InvokeAsync from LocalApiService so that HTTP
// requests originate from the browser (visible in the Network tab).

"use strict";

window.doopass = window.doopass || {};

window.doopass.fetchGet = async function (url) {
    const res = await fetch(url, { cache: "no-store" });
    return _wrapResponse(res);
};

window.doopass.fetchPost = async function (url, body) {
    const res = await fetch(url, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: body !== null ? JSON.stringify(body) : undefined,
        cache: "no-store",
    });
    return _wrapResponse(res);
};

window.doopass.fetchPut = async function (url, body) {
    const res = await fetch(url, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: body !== null ? JSON.stringify(body) : undefined,
        cache: "no-store",
    });
    return _wrapResponse(res);
};

window.doopass.fetchDelete = async function (url) {
    const res = await fetch(url, { method: "DELETE", cache: "no-store" });
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
