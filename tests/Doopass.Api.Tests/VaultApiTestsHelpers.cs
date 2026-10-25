using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;

namespace Doopass.Api.Tests;

/// <summary>
/// Shared helpers: host fixtures + a Send(auth) wrapper for the requests that
/// carry the bearer token.
/// </summary>
internal static class VaultApiTestsHelpers
{
    public static Task<HttpResponseMessage> AuthSend(
        this HttpClient client, HttpMethod method, string url, string auth, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth);
        return client.SendAsync(request);
    }

    public static JsonContent Json<T>(T payload) => JsonContent.Create(payload);
}
