using System.Collections.Concurrent;
using System.Net.ServerSentEvents;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDirectoryBrowser(); // 對應 Node 的 serve-index
var app = builder.Build();

// 把 ../public/ 當成靜態檔來源（與 Node 範例共用同一份前端）
var publicPath = Path.GetFullPath(
    Path.Combine(builder.Environment.ContentRootPath, "..", "public"));
var fileProvider = new PhysicalFileProvider(publicPath);
app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
app.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider });
app.UseDirectoryBrowser(new DirectoryBrowserOptions { FileProvider = fileProvider });

app.UseWebSockets();

// ─── §8 Server-Sent Event ─────────────────────────────────────────────
// 對應 Node 的 app.get("/try-sse", ...)
app.MapGet("/try-sse",
    (CancellationToken token) => TypedResults.ServerSentEvents(GenerateTimeEvents(token)));

// ─── §9 WebSocket：3 條獨立 path、共用一個 port ──────────────────────
// 9.4 回音
app.Map("/ws/echo", async (HttpContext ctx) =>
{
    if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }

    using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
    var ip = ctx.Connection.RemoteIpAddress?.ToString();
    await SendText(ws, $"連線了, 你來自 {ip}");

    var buffer = new byte[4096];
    while (ws.State == WebSocketState.Open)
    {
        var result = await ws.ReceiveAsync(buffer, CancellationToken.None);
        if (result.MessageType == WebSocketMessageType.Close)
        {
            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
            break;
        }
        var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
        await SendText(ws, text); // 原訊息送回
    }
});

// 9.5 簡易聊天室
var chatClients = new ConcurrentDictionary<string, WebSocket>();
app.Map("/ws/chat", async (HttpContext ctx) =>
{
    if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }

    using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
    var id = Guid.NewGuid().ToString();
    chatClients[id] = ws;

    var ip = ctx.Connection.RemoteIpAddress?.ToString();
    string? userName = null;
    var buffer = new byte[4096];

    try
    {
        while (ws.State == WebSocketState.Open)
        {
            var result = await ws.ReceiveAsync(buffer, CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close) break;

            var text = Encoding.UTF8.GetString(buffer, 0, result.Count);

            if (userName is null)
            {
                userName = text;
                await Broadcast(chatClients,
                    $"{userName}({ip}) 進入,目前 {chatClients.Count} 人");
            }
            else
            {
                await Broadcast(chatClients, $"{userName}: {text}");
            }
        }
    }
    finally
    {
        chatClients.TryRemove(id, out _);
        if (userName is not null)
        {
            await Broadcast(chatClients,
                $"{userName}({ip}) 離開,目前 {chatClients.Count} 人");
        }
    }
});

// 9.6 共用塗鴉板
var drawClients = new ConcurrentDictionary<string, WebSocket>();
app.Map("/ws/draw", async (HttpContext ctx) =>
{
    if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }

    using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
    var id = Guid.NewGuid().ToString();
    drawClients[id] = ws;

    var buffer = new byte[4096];
    try
    {
        while (ws.State == WebSocketState.Open)
        {
            var result = await ws.ReceiveAsync(buffer, CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close) break;

            var payload = buffer.AsMemory(0, result.Count);
            foreach (var c in drawClients.Values)
            {
                if (c.State != WebSocketState.Open) continue;
                try { await c.SendAsync(payload, WebSocketMessageType.Text, true, CancellationToken.None); }
                catch { }
            }
        }
    }
    finally
    {
        drawClients.TryRemove(id, out _);
    }
});

// ─── §10 AJAX 上傳 ────────────────────────────────────────────────────
var extMap = new Dictionary<string, string>
{
    ["image/jpeg"] = ".jpg",
    ["image/png"] = ".png",
    ["image/webp"] = ".webp",
};

// 10.2 單檔上傳：個人資料 + 大頭貼
app.MapPost("/uploads/profile", async (
    [FromForm] string? user,
    [FromForm] string? description,
    IFormFile? avatar) =>
{
    var profilePath = Path.Combine(publicPath, "profile.json");

    // 讀取既有資料
    var data = new Dictionary<string, string?>();
    if (File.Exists(profilePath))
    {
        try
        {
            data = JsonSerializer.Deserialize<Dictionary<string, string?>>(
                await File.ReadAllTextAsync(profilePath)) ?? new();
        }
        catch { /* 檔案壞了當空 */ }
    }

    if (user is not null) data["user"] = user;
    if (description is not null) data["description"] = description;

    if (avatar is not null && extMap.TryGetValue(avatar.ContentType, out var ext))
    {
        var imagesDir = Path.Combine(publicPath, "images");
        Directory.CreateDirectory(imagesDir);
        var fileName = $"{Guid.NewGuid()}{ext}";
        await using var stream = File.Create(Path.Combine(imagesDir, fileName));
        await avatar.CopyToAsync(stream);
        data["avatar"] = $"/images/{fileName}";
    }

    try
    {
        await File.WriteAllTextAsync(profilePath, JsonSerializer.Serialize(data));
        return Results.Ok(new { success = true, data });
    }
    catch
    {
        return Results.Ok(new { success = false, data });
    }
}).DisableAntiforgery();

// 10.3 多檔上傳：相簿
app.MapPost("/uploads/photos", async (IFormFileCollection photos) =>
{
    var imagesDir = Path.Combine(publicPath, "images");
    Directory.CreateDirectory(imagesDir);

    var urls = new List<string>();
    foreach (var photo in photos)
    {
        if (!extMap.TryGetValue(photo.ContentType, out var ext)) continue;
        var fileName = $"{Guid.NewGuid()}{ext}";
        await using var stream = File.Create(Path.Combine(imagesDir, fileName));
        await photo.CopyToAsync(stream);
        urls.Add($"/images/{fileName}");
    }
    return Results.Ok(urls);
}).DisableAntiforgery();

app.Run();

// ─── 共用 helper ──────────────────────────────────────────────────────

static async Task SendText(WebSocket ws, string text)
{
    var bytes = Encoding.UTF8.GetBytes(text);
    await ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
}

static async Task Broadcast(ConcurrentDictionary<string, WebSocket> clients, string message)
{
    var bytes = Encoding.UTF8.GetBytes(message);
    foreach (var ws in clients.Values)
    {
        if (ws.State != WebSocketState.Open) continue;
        try
        {
            await ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch { /* 連線失敗就略過，下次廣播會排除 */ }
    }
}

static async IAsyncEnumerable<SseItem<string>> GenerateTimeEvents(
    [EnumeratorCancellation] CancellationToken token)
{
    var id = 30;
    while (!token.IsCancellationRequested)
    {
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        yield return new SseItem<string>(now) { EventId = (id++).ToString() };
        try { await Task.Delay(2000, token); }
        catch (TaskCanceledException) { yield break; }
    }
}
