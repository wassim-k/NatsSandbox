namespace NatsSandbox;

/// <summary>
/// Interface for HTTP transport operations.
/// Allows custom HTTP client configuration for scenarios like proxies or firewalls.
/// </summary>
public interface IHttpTransport
{
    /// <summary>
    /// Downloads content from the specified URL.
    /// </summary>
    Task<Stream> DownloadAsync(string url, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default HTTP transport implementation using HttpClient.
/// </summary>
/// <remarks>
/// Creates a new HTTP transport with a custom HttpClient.
/// </remarks>
/// <param name="http">The HttpClient to use for downloads.</param>
/// <param name="disposeHttpClient">Whether to dispose the HttpClient when this instance is disposed.</param>
/// <remarks>
/// Creates a new HTTP transport with a default HttpClient.
/// </remarks>
public sealed class HttpTransport(HttpClient http, bool disposeHttpClient = false) : IHttpTransport, IDisposable
{
    public HttpTransport() : this(new HttpClient(), disposeHttpClient: true)
    {
    }

    public async Task<Stream> DownloadAsync(string url, CancellationToken cancellationToken = default)
    {
        var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (disposeHttpClient)
        {
            http.Dispose();
        }
    }
}
