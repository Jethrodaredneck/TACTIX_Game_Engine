using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TACTIX.Engine.Core.Logging;

namespace TACTIX.Engine.AI;

/// <summary>
/// Local, provider-neutral bridge for AI/editor tooling. It binds only to loopback,
/// requires a per-run bearer token, and never permits writes unless the user enables them.
/// </summary>
public sealed class AIBridgeServer : IDisposable
{
    private readonly string _projectRoot;
    private readonly Func<object> _contextProvider;
    private readonly HttpListener _listener = new();
    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private readonly string _token;
    private readonly string _manifestPath;

    public int Port { get; }
    public string BaseUrl => $"http://127.0.0.1:{Port}/";
    public bool AllowEdits { get; set; }
    public string ManifestPath => _manifestPath;

    public event Action<bool>? EditPermissionChanged;

    public AIBridgeServer(string projectRoot, Func<object> contextProvider, int port = 0)
    {
        _projectRoot = Path.GetFullPath(projectRoot);
        _contextProvider = contextProvider;
        Port = port > 0 ? port : FindFreeLoopbackPort();
        _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        _manifestPath = Path.Combine(_projectRoot, ".tactix", "ai-bridge.json");
        _listener.Prefixes.Add(BaseUrl);
    }

    private static int FindFreeLoopbackPort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        try { return ((IPEndPoint)probe.LocalEndpoint).Port; }
        finally { probe.Stop(); }
    }

    public void Start()
    {
        if (_listener.IsListening)
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(_manifestPath)!);
        File.WriteAllText(_manifestPath, JsonSerializer.Serialize(new
        {
            version = 1,
            url = BaseUrl,
            token = _token,
            transport = "http",
            permissions = new { read = true, write = AllowEdits }
        }, new JsonSerializerOptions { WriteIndented = true }));

        _cts = new CancellationTokenSource();
        _listener.Start();
        _loopTask = Task.Run(() => ListenLoopAsync(_cts.Token));
        Log.Info($"AI Bridge listening on {BaseUrl} (loopback only)");
    }

    public void SetAllowEdits(bool enabled)
    {
        AllowEdits = enabled;
        RefreshManifest();
        EditPermissionChanged?.Invoke(enabled);
        Log.Info($"AI Bridge write permission: {(enabled ? "ENABLED" : "disabled")}");
    }

    private void RefreshManifest()
    {
        try
        {
            if (!File.Exists(_manifestPath)) return;
            File.WriteAllText(_manifestPath, JsonSerializer.Serialize(new
            {
                version = 1,
                url = BaseUrl,
                token = _token,
                transport = "http",
                permissions = new { read = true, write = AllowEdits }
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener.IsListening)
        {
            try
            {
                var ctx = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleAsync(ctx), ct);
            }
            catch (HttpListenerException) when (ct.IsCancellationRequested) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex) { Log.Warn($"AI Bridge listener error: {ex.Message}"); }
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        try
        {
            AddCors(ctx.Response);
            if (ctx.Request.HttpMethod == "OPTIONS")
            {
                ctx.Response.StatusCode = 204;
                ctx.Response.Close();
                return;
            }

            if (!Authorized(ctx.Request))
            {
                await JsonAsync(ctx.Response, 401, new { error = "unauthorized" });
                return;
            }

            var path = ctx.Request.Url?.AbsolutePath.TrimEnd('/') ?? "";
            if (path.Length == 0) path = "/";

            if (ctx.Request.HttpMethod == "GET" && path == "/health")
                await JsonAsync(ctx.Response, 200, new { ok = true, name = "TACTIX AI Bridge", version = 1, allowEdits = AllowEdits });
            else if (ctx.Request.HttpMethod == "GET" && path == "/context")
                await JsonAsync(ctx.Response, 200, _contextProvider());
            else if (ctx.Request.HttpMethod == "GET" && path == "/project/list")
                await HandleListAsync(ctx);
            else if (ctx.Request.HttpMethod == "GET" && path == "/project/read")
                await HandleReadAsync(ctx);
            else if (ctx.Request.HttpMethod == "POST" && path == "/project/write")
                await HandleWriteAsync(ctx);
            else if (ctx.Request.HttpMethod == "GET" && path == "/console")
                await HandleConsoleAsync(ctx);
            else if (ctx.Request.HttpMethod == "GET" && path == "/tools")
                await JsonAsync(ctx.Response, 200, ToolDescription());
            else
                await JsonAsync(ctx.Response, 404, new { error = "not_found" });
        }
        catch (Exception ex)
        {
            try { await JsonAsync(ctx.Response, 500, new { error = "bridge_error", message = ex.Message }); } catch { }
        }
    }

    private bool Authorized(HttpListenerRequest request)
    {
        var auth = request.Headers["Authorization"];
        return string.Equals(auth, $"Bearer {_token}", StringComparison.Ordinal);
    }

    private async Task HandleListAsync(HttpListenerContext ctx)
    {
        var requested = ctx.Request.QueryString["path"] ?? "";
        var dir = ResolveSafePath(requested);
        if (!Directory.Exists(dir))
        {
            await JsonAsync(ctx.Response, 404, new { error = "directory_not_found" });
            return;
        }

        var entries = Directory.EnumerateFileSystemEntries(dir)
            .Where(p => !IsIgnoredPath(p))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .Take(1000)
            .Select(p => new
            {
                name = Path.GetFileName(p),
                path = Path.GetRelativePath(_projectRoot, p).Replace('\\', '/'),
                kind = Directory.Exists(p) ? "directory" : "file",
                size = File.Exists(p) ? new FileInfo(p).Length : 0
            })
            .ToArray();
        await JsonAsync(ctx.Response, 200, new { path = requested, entries });
    }

    private async Task HandleReadAsync(HttpListenerContext ctx)
    {
        var requested = ctx.Request.QueryString["path"];
        if (string.IsNullOrWhiteSpace(requested))
        {
            await JsonAsync(ctx.Response, 400, new { error = "path_required" });
            return;
        }
        var file = ResolveSafePath(requested);
        if (!File.Exists(file))
        {
            await JsonAsync(ctx.Response, 404, new { error = "file_not_found" });
            return;
        }
        var info = new FileInfo(file);
        if (info.Length > 2 * 1024 * 1024)
        {
            await JsonAsync(ctx.Response, 413, new { error = "file_too_large", maxBytes = 2 * 1024 * 1024 });
            return;
        }
        var content = await File.ReadAllTextAsync(file);
        await JsonAsync(ctx.Response, 200, new { path = requested.Replace('\\', '/'), content });
    }

    private async Task HandleWriteAsync(HttpListenerContext ctx)
    {
        if (!AllowEdits)
        {
            await JsonAsync(ctx.Response, 403, new { error = "edits_disabled", message = "Enable Allow edits in the TACTIX AI Bridge panel." });
            return;
        }

        using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding ?? Encoding.UTF8);
        var body = await reader.ReadToEndAsync();
        var req = JsonSerializer.Deserialize<WriteRequest>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (req == null || string.IsNullOrWhiteSpace(req.Path))
        {
            await JsonAsync(ctx.Response, 400, new { error = "invalid_request" });
            return;
        }

        var file = ResolveSafePath(req.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        if (File.Exists(file))
            File.Copy(file, file + ".tactix-ai.bak", overwrite: true);

        await File.WriteAllTextAsync(file, req.Content ?? string.Empty, Encoding.UTF8);
        Log.Info($"AI Bridge wrote {Path.GetRelativePath(_projectRoot, file)}");
        await JsonAsync(ctx.Response, 200, new { ok = true, path = Path.GetRelativePath(_projectRoot, file).Replace('\\', '/') });
    }

    private static async Task HandleConsoleAsync(HttpListenerContext ctx)
    {
        const string path = "/tmp/tactix_boottrace.txt";
        var content = File.Exists(path) ? await File.ReadAllTextAsync(path) : "";
        await JsonAsync(ctx.Response, 200, new { path, content });
    }

    private string ResolveSafePath(string requested)
    {
        requested = (requested ?? "").Replace('/', Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(_projectRoot, requested));
        if (!full.Equals(_projectRoot, StringComparison.Ordinal) &&
            !full.StartsWith(_projectRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Path escapes the TACTIX project root.");
        if (IsIgnoredPath(full))
            throw new UnauthorizedAccessException("That generated/private path is not exposed by the AI bridge.");
        return full;
    }

    private bool IsIgnoredPath(string path)
    {
        var rel = Path.GetRelativePath(_projectRoot, path).Replace('\\', '/');
        return rel.Equals(".git", StringComparison.OrdinalIgnoreCase) || rel.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) ||
               rel.Equals("bin", StringComparison.OrdinalIgnoreCase) || rel.StartsWith("bin/", StringComparison.OrdinalIgnoreCase) ||
               rel.Equals("obj", StringComparison.OrdinalIgnoreCase) || rel.StartsWith("obj/", StringComparison.OrdinalIgnoreCase) ||
               rel.EndsWith(".tactix-ai.bak", StringComparison.OrdinalIgnoreCase);
    }

    private static object ToolDescription() => new
    {
        tools = new object[]
        {
            new { name = "tactix_context", method = "GET", path = "/context" },
            new { name = "tactix_list_files", method = "GET", path = "/project/list?path=" },
            new { name = "tactix_read_file", method = "GET", path = "/project/read?path=" },
            new { name = "tactix_write_file", method = "POST", path = "/project/write", requiresEditPermission = true },
            new { name = "tactix_console", method = "GET", path = "/console" }
        }
    };

    private static void AddCors(HttpListenerResponse r)
    {
        r.Headers["Access-Control-Allow-Origin"] = "http://127.0.0.1";
        r.Headers["Access-Control-Allow-Headers"] = "Authorization, Content-Type";
        r.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
    }

    private static async Task JsonAsync(HttpListenerResponse response, int status, object value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, new JsonSerializerOptions { WriteIndented = true });
        response.StatusCode = status;
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { }
        try { _listener.Stop(); } catch { }
        try { _listener.Close(); } catch { }
        try { if (File.Exists(_manifestPath)) File.Delete(_manifestPath); } catch { }
        _cts?.Dispose();
    }

    private sealed class WriteRequest
    {
        public string Path { get; set; } = "";
        public string? Content { get; set; }
    }
}
