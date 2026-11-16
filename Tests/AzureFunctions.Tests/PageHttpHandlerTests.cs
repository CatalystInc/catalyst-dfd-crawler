using System.Net;
using System.Net.Sockets;
using System.Text;
using AzureFunctions.Indexer;

namespace AzureFunctions.Tests;

/// <summary>
/// Replays DFDMVP-2279 against a local server: a legacy URL redirects to a /fr-ca/ page
/// that sets 'locale=fr-ca', and the next page reports which Cookie header it received.
/// </summary>
public sealed class PageHttpHandlerTests : IAsyncLifetime
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private Task? _serverLoop;
    private string _baseUrl = "";

    public Task InitializeAsync()
    {
        _listener.Start();
        _baseUrl = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";
        _serverLoop = Task.Run(ServeAsync);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _stop.Cancel();
        _listener.Stop();
        if (_serverLoop != null)
        {
            try { await _serverLoop; } catch { /* listener stopped */ }
        }
    }

    [Fact]
    public async Task The_crawler_handler_follows_redirects_without_replaying_cookies()
    {
        using var client = new HttpClient(PageCrawlerBase.CreatePageHandler());

        using var legacy = await client.GetAsync($"{_baseUrl}/fr/legacy");
        var cookieSeen = await client.GetStringAsync($"{_baseUrl}/pages/terms-of-use");

        Assert.Equal(HttpStatusCode.OK, legacy.StatusCode);
        Assert.Equal("/fr-ca/page", legacy.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Equal("cookie: none", cookieSeen);
    }

    [Fact]
    public async Task A_default_client_replays_the_locale_cookie()
    {
        // Control: proves this test would catch the bug.
        using var client = new HttpClient();

        await client.GetAsync($"{_baseUrl}/fr/legacy");
        var cookieSeen = await client.GetStringAsync($"{_baseUrl}/pages/terms-of-use");

        Assert.Equal("cookie: locale=fr-ca", cookieSeen);
    }

    private async Task ServeAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient connection;
            try
            {
                connection = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch
            {
                return;
            }

            using (connection)
            {
                var stream = connection.GetStream();
                var (path, cookie) = await ReadRequestAsync(stream);
                var (status, headers, body) = path switch
                {
                    // Like the site: a legacy URL redirects to a /fr-ca/ page, which sets the cookie.
                    "/fr/legacy" => ("301 Moved Permanently", "Location: /fr-ca/page\r\n", ""),
                    "/fr-ca/page" => ("200 OK", "Set-Cookie: locale=fr-ca; Path=/\r\n", "fr-ca page"),
                    "/pages/terms-of-use" => ("200 OK", "", $"cookie: {cookie ?? "none"}"),
                    _ => ("404 Not Found", "", ""),
                };
                var bodyBytes = Encoding.ASCII.GetBytes(body);
                var head = $"HTTP/1.1 {status}\r\nContent-Type: text/plain\r\nContent-Length: {bodyBytes.Length}\r\n{headers}Connection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
                await stream.WriteAsync(bodyBytes);
            }
        }
    }

    private static async Task<(string Path, string? Cookie)> ReadRequestAsync(NetworkStream stream)
    {
        var buffer = new byte[8192];
        var received = new StringBuilder();
        while (!received.ToString().Contains("\r\n\r\n"))
        {
            var read = await stream.ReadAsync(buffer);
            if (read == 0) break;
            received.Append(Encoding.ASCII.GetString(buffer, 0, read));
        }

        var lines = received.ToString().Split("\r\n");
        var path = lines[0].Split(' ')[1];
        var query = path.IndexOf('?');
        if (query >= 0) path = path[..query];
        var cookie = lines.Skip(1)
            .FirstOrDefault(line => line.StartsWith("Cookie:", StringComparison.OrdinalIgnoreCase))?["Cookie:".Length..].Trim();
        return (path, cookie);
    }
}
