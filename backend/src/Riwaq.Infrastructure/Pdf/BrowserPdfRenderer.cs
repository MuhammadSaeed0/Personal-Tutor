using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PuppeteerSharp;
using PuppeteerSharp.Media;
using Riwaq.Application.Abstractions;

namespace Riwaq.Infrastructure.Pdf;

public sealed class PdfRenderOptions
{
    public const string Section = "Pdf";

    /// <summary>Path to a Chromium-based browser (Edge or Chrome). Empty = look in the usual install locations.</summary>
    public string? BrowserPath { get; set; }
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Start the browser without its sandbox. Only for Linux hosts that block the sandbox (e.g. Ubuntu 24 CI runners);
    /// the browser only ever renders the app's own sanitised HTML. Leave off everywhere else.
    /// </summary>
    public bool NoSandbox { get; set; }

    /// <summary>The browser is closed after this long without exports, so it only runs while the student exports.</summary>
    public int IdleSeconds { get; set; } = 120;
}

/// <summary>
/// Prints HTML to PDF with the installed Edge or Chrome (headless, through PuppeteerSharp): real text with correct
/// Arabic shaping, and math typeset with the bundled KaTeX. One browser is started on first use and reused. Pages are
/// fully offline — every network request is blocked and KaTeX's scripts, styles and fonts are embedded.
/// </summary>
public sealed partial class BrowserPdfRenderer(IOptions<PdfRenderOptions> options, ILogger<BrowserPdfRenderer> logger) : IPdfRenderer, IAsyncDisposable
{
    private readonly PdfRenderOptions _options = options.Value;
    private readonly Lazy<string?> _browserPath = new(() => FindBrowser(options.Value.BrowserPath));
    private readonly Lazy<string> _katex = new(LoadKatex);
    private readonly SemaphoreSlim _launch = new(1, 1);
    private readonly SemaphoreSlim _renders = new(2, 2);
    private IBrowser? _browser;
    private Process? _process;
    private string? _profile;
    private Timer? _idleTimer;
    private const string ProfilePrefix = "riwaq-pdf-browser-";

    public bool IsAvailable => _browserPath.Value is not null;

    public async Task<byte[]> RenderAsync(string html, string footerHtml, CancellationToken cancellationToken)
    {
        await _renders.WaitAsync(cancellationToken);
        _idleTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        IPage? page = null;
        try
        {
            var browser = await GetBrowserAsync();
            page = await browser.NewPageAsync();
            await page.SetJavaScriptEnabledAsync(true);
            await page.SetRequestInterceptionAsync(true);
            page.Request += async (_, e) =>
            {
                if (e.Request.Url.StartsWith("data:", StringComparison.Ordinal) || e.Request.Url == "about:blank") await e.Request.ContinueAsync();
                else await e.Request.AbortAsync();
            };

            var timeout = _options.TimeoutSeconds * 1000;
            await page.SetContentAsync(WithKatex(html), new NavigationOptions { Timeout = timeout, WaitUntil = [WaitUntilNavigation.Load] });
            await page.WaitForFunctionAsync("() => document.body.dataset.ready === '1'", new WaitForFunctionOptions { Timeout = timeout });

            return await page.PdfDataAsync(new PdfOptions
            {
                Format = PaperFormat.A4,
                PrintBackground = true,
                DisplayHeaderFooter = true,
                HeaderTemplate = "<span></span>",
                FooterTemplate = footerHtml,
                MarginOptions = new MarginOptions { Top = "16mm", Bottom = "18mm", Left = "15mm", Right = "15mm" }
            });
        }
        catch (Exception ex) when (ex is PuppeteerException or ProcessException or TimeoutException or System.Net.WebSockets.WebSocketException)
        {
            logger.LogWarning(ex, "PDF rendering failed");
            throw new Application.Common.ServiceUnavailableException("pdf_failed", "The PDF could not be created. Please try again.");
        }
        finally
        {
            if (page is not null)
            {
                try { await page.CloseAsync(); } catch (PuppeteerException) { }
            }
            _idleTimer ??= new Timer(_ => _ = CloseWhenIdleAsync());
            _idleTimer.Change(TimeSpan.FromSeconds(Math.Max(10, _options.IdleSeconds)), Timeout.InfiniteTimeSpan);
            _renders.Release();
        }
    }

    private async Task<IBrowser> GetBrowserAsync()
    {
        if (_browser is { IsConnected: true }) return _browser;
        await _launch.WaitAsync();
        try
        {
            if (_browser is { IsConnected: true }) return _browser;
            var path = _browserPath.Value ?? throw new Application.Common.ServiceUnavailableException("pdf_unavailable", "No supported browser was found on the server.");
            await StopBrowserAsync();
            await CloseOrphansAsync();
            logger.LogInformation("Starting headless browser for PDF export: {Path}", path);

            // Edge on Windows does not print its DevTools address on stderr, so the browser is started here and the
            // address is read from the DevToolsActivePort file it writes into its (private, temporary) profile.
            _profile = Path.Combine(Path.GetTempPath(), $"{ProfilePrefix}{Guid.NewGuid():N}");
            Directory.CreateDirectory(_profile);
            var start = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
            foreach (var arg in new[]
                     {
                         "--headless=new", "--remote-debugging-port=0", "--remote-debugging-address=127.0.0.1", $"--user-data-dir={_profile}",
                         "--no-first-run", "--no-default-browser-check", "--disable-extensions", "--disable-gpu", "--mute-audio",
                         "--disable-background-networking", "--disable-sync", "about:blank"
                     })
                start.ArgumentList.Add(arg);
            if (_options.NoSandbox) start.ArgumentList.Insert(0, "--no-sandbox");
            _process = Process.Start(start) ?? throw new InvalidOperationException("The browser did not start.");
            // Kept only to explain a failed start (the last lines are logged); nothing from it reaches the student.
            var errors = new System.Collections.Concurrent.ConcurrentQueue<string>();
            _process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data is null) return;
                errors.Enqueue(e.Data);
                while (errors.Count > 40) errors.TryDequeue(out _);
            };
            _process.BeginErrorReadLine();

            // msedge.exe may hand over to child processes and exit at once, so only the port file is awaited.
            var portFile = Path.Combine(_profile, "DevToolsActivePort");
            var deadline = DateTime.UtcNow.AddSeconds(30);
            string[]? lines = null;
            while (DateTime.UtcNow < deadline)
            {
                if (File.Exists(portFile))
                {
                    try { lines = File.ReadAllLines(portFile); } catch (IOException) { }
                    if (lines is { Length: >= 2 }) break;
                }
                await Task.Delay(100);
            }
            if (lines is not { Length: >= 2 })
            {
                logger.LogWarning("The PDF browser did not open its debugging port (exited: {Exited}). Its output:\n{Output}",
                    _process.HasExited, string.Join('\n', errors));
                throw new Application.Common.ServiceUnavailableException("pdf_unavailable", "The browser for PDF export could not be started.");
            }

            _browser = await Puppeteer.ConnectAsync(new ConnectOptions { BrowserWSEndpoint = $"ws://127.0.0.1:{lines[0].Trim()}{lines[1].Trim()}" });
            return _browser;
        }
        finally
        {
            _launch.Release();
        }
    }

    /// <summary>Adds KaTeX (styles with embedded fonts, scripts) and marks the page ready once math and fonts are done.</summary>
    private string WithKatex(string html)
    {
        var assets = _katex.Value;
        var index = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        return index < 0 ? html + assets : html[..index] + assets + html[index..];
    }

    private static string LoadKatex()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Pdf", "assets", "katex");
        var css = File.ReadAllText(Path.Combine(root, "katex.min.css"));
        css = FontUrl().Replace(css, m =>
        {
            var font = Path.Combine(root, "fonts", m.Groups[1].Value + ".woff2");
            return File.Exists(font) ? $"url(data:font/woff2;base64,{Convert.ToBase64String(File.ReadAllBytes(font))})" : m.Value;
        });

        var sb = new StringBuilder();
        sb.Append("<style>").Append(css).Append("</style>");
        foreach (var script in new[] { "katex.min.js", "contrib/mhchem.min.js", "contrib/auto-render.min.js" })
            sb.Append("<script>").Append(File.ReadAllText(Path.Combine(root, script))).Append("</script>");
        sb.Append("""
            <script>
              try {
                renderMathInElement(document.body, {
                  delimiters: [{ left: '\\[', right: '\\]', display: true }, { left: '\\(', right: '\\)', display: false }],
                  throwOnError: false, strict: 'ignore', trust: false, maxExpand: 500
                });
              } finally {
                document.fonts.ready.then(() => { document.body.dataset.ready = '1'; });
              }
            </script>
            """);
        return sb.ToString();
    }

    internal static string? FindBrowser(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return File.Exists(configured) ? configured : null;
        var candidates = OperatingSystem.IsWindows()
            ? new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft\Edge\Application\msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\Application\chrome.exe")
            }
            : ["/usr/bin/microsoft-edge", "/usr/bin/google-chrome", "/usr/bin/chromium", "/usr/bin/chromium-browser",
               "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge", "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"];
        return candidates.FirstOrDefault(File.Exists);
    }

    public async ValueTask DisposeAsync()
    {
        if (_idleTimer is not null) await _idleTimer.DisposeAsync();
        await StopBrowserAsync();
    }

    private async Task CloseWhenIdleAsync()
    {
        if (!await _launch.WaitAsync(0)) return;
        try
        {
            // Only when no export is running (all render slots free).
            if (_renders.CurrentCount == 2 && _browser is not null)
            {
                logger.LogInformation("Closing idle PDF browser");
                await StopBrowserAsync();
            }
        }
        finally
        {
            _launch.Release();
        }
    }

    /// <summary>
    /// Browsers left behind when the server was killed (no shutdown hook ran) still listen on the port saved in their
    /// profile: close them over DevTools and remove their profiles.
    /// </summary>
    private async Task CloseOrphansAsync()
    {
        foreach (var dir in Directory.EnumerateDirectories(Path.GetTempPath(), ProfilePrefix + "*"))
        {
            try
            {
                var portFile = Path.Combine(dir, "DevToolsActivePort");
                if (File.Exists(portFile) && File.ReadAllLines(portFile) is { Length: >= 2 } lines)
                {
                    using var connect = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    var orphan = await Puppeteer.ConnectAsync(new ConnectOptions { BrowserWSEndpoint = $"ws://127.0.0.1:{lines[0].Trim()}{lines[1].Trim()}" })
                        .WaitAsync(connect.Token);
                    var page = await orphan.NewPageAsync();
                    try { await page.Client.SendAsync("Browser.close"); } catch (PuppeteerException) { }
                    orphan.Dispose();
                    await Task.Delay(500);
                }
            }
            catch (Exception ex)
            {
                // Best effort: a dead orphan refuses the connection; nothing else to do than remove its profile.
                logger.LogDebug(ex, "Could not close an orphaned PDF browser in {Profile}", dir);
            }

            try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private async Task StopBrowserAsync()
    {
        if (_browser is not null)
        {
            // A browser we connected to (rather than launched through Puppeteer) is only disconnected by CloseAsync,
            // so ask it to quit over the DevTools protocol; msedge.exe has already handed over to child processes.
            try
            {
                if (_browser.IsConnected)
                {
                    var page = await _browser.NewPageAsync();
                    await page.Client.SendAsync("Browser.close");
                }
            }
            catch (Exception ex) when (ex is PuppeteerException or ObjectDisposedException or TimeoutException) { }
            try { await _browser.CloseAsync(); } catch (Exception ex) when (ex is PuppeteerException or ObjectDisposedException) { }
            _browser.Dispose();
            _browser = null;
        }
        if (_process is not null)
        {
            try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            _process.Dispose();
            _process = null;
        }
        if (_profile is not null)
        {
            try { Directory.Delete(_profile, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            _profile = null;
        }
    }

    [GeneratedRegex(@"url\(fonts/([A-Za-z0-9_-]+)\.woff2\)")]
    private static partial Regex FontUrl();
}
