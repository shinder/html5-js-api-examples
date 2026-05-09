// ───────────────────────────────────────────────────────────────────────
// Html5CsharpBackend：對應原本 Node.js 範例的 ASP.NET Core 後端版本
// ───────────────────────────────────────────────────────────────────────
// 本檔案使用 .NET 的 Minimal API 寫法（不需要 Controller 類別），
// 所有路由、WebSocket、SSE、上傳邏輯都集中在這個 Program.cs 裡。
// 對應前端範例：../public/ 目錄下的 HTML / JS。
// ───────────────────────────────────────────────────────────────────────

using System.Collections.Concurrent;          // ConcurrentDictionary：執行緒安全的字典，用於管理多人 WebSocket 連線
using System.Net.ServerSentEvents;            // .NET 內建的 SSE 工具：SseItem<T> / TypedResults.ServerSentEvents
using System.Net.WebSockets;                  // WebSocket 相關 API（接收/傳送/關閉）
using System.Runtime.CompilerServices;        // [EnumeratorCancellation]：讓 IAsyncEnumerable 能正確接收取消權杖
using System.Text;                            // Encoding.UTF8：處理位元組與字串轉換
using System.Text.Json;                       // JsonSerializer：讀寫 profile.json
using Microsoft.AspNetCore.Mvc;               // [FromForm] 等 binding 屬性
using Microsoft.Extensions.FileProviders;     // PhysicalFileProvider：把實體目錄當成靜態檔案來源

// ─── 建立 Web 應用程式 ────────────────────────────────────────────────
// WebApplication.CreateBuilder：載入設定檔（appsettings.json）、設定 DI 容器、Kestrel 伺服器等
var builder = WebApplication.CreateBuilder(args);

// 註冊「目錄瀏覽」服務：讓使用者連到目錄路徑時可以看到檔案清單
// 對應 Node.js 範例中 serve-index 套件的功能
builder.Services.AddDirectoryBrowser();

// 把 builder 組裝成可執行的 app（之後就以 app 為主來掛 middleware / 路由）
var app = builder.Build();

// ─── 靜態檔案設定 ─────────────────────────────────────────────────────
// 將 ../public/ 目錄當成前端來源，與 Node 範例共用同一份 HTML / JS / 圖片
// 使用 Path.GetFullPath 把相對路徑轉成絕對路徑，避免不同工作目錄下找不到檔案
var publicPath = Path.GetFullPath(
    Path.Combine(builder.Environment.ContentRootPath, "..", "public"));

// PhysicalFileProvider：實體目錄包裝成 IFileProvider，給後續三個 middleware 使用
var fileProvider = new PhysicalFileProvider(publicPath);

// 三個 middleware 必須依下列順序註冊：
//   1) UseDefaultFiles    → 偵測到請求是目錄時，自動改抓 index.html（不會回應，只改寫路徑）
//   2) UseStaticFiles     → 真正回應靜態檔的 middleware
//   3) UseDirectoryBrowser→ 若仍無對應檔案，列出目錄內容
app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
app.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider });
app.UseDirectoryBrowser(new DirectoryBrowserOptions { FileProvider = fileProvider });

// 啟用 WebSocket 支援；之後才能用 ctx.WebSockets.AcceptWebSocketAsync()
app.UseWebSockets();

// ─── §8 Server-Sent Events（SSE）──────────────────────────────────────
// SSE：伺服器主動「單向」推送多筆事件給瀏覽器（瀏覽器以 EventSource 訂閱）。
// 與 WebSocket 不同點：SSE 走 HTTP，僅伺服器→瀏覽器，且瀏覽器會自動斷線重連。
// 對應 Node 的 app.get("/try-sse", ...)
app.MapGet("/try-sse",
    // CancellationToken 由 ASP.NET Core 自動注入，瀏覽器斷線時會被觸發取消
    (CancellationToken token) => TypedResults.ServerSentEvents(GenerateTimeEvents(token)));

// ─── §9 WebSocket：3 條獨立 path、共用一個 port ──────────────────────
// 設計策略：所有 WebSocket 都走同一個 HTTP port，藉由不同 path 來區分用途
// （/ws/echo、/ws/chat、/ws/draw），這樣前端只要連同一台主機即可。

// ─── 9.4 回音（echo）──────────────────────────────────────────────────
// 最簡單的範例：客戶端傳什麼，伺服器就原封不動傳回什麼。
app.Map("/ws/echo", async (HttpContext ctx) =>
{
    // 防呆：若不是 WebSocket 請求（例如普通瀏覽器點開），回應 400
    if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }

    // 接受 WebSocket handshake，升級為持續連線；using 確保結束時自動關閉/釋放
    using var ws = await ctx.WebSockets.AcceptWebSocketAsync();

    // 取得對方 IP（注意：在反向代理後面可能是 Proxy 的 IP）
    var ip = ctx.Connection.RemoteIpAddress?.ToString();
    await SendText(ws, $"連線了, 你來自 {ip}");

    // 4096 bytes 的接收緩衝區，足夠示範用；實務上可視訊息大小調整
    var buffer = new byte[4096];
    while (ws.State == WebSocketState.Open)
    {
        // ReceiveAsync 會「阻塞」直到收到訊息或對方關閉連線
        var result = await ws.ReceiveAsync(buffer, CancellationToken.None);

        // 對方主動關閉時，回覆關閉確認後跳出迴圈
        if (result.MessageType == WebSocketMessageType.Close)
        {
            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
            break;
        }

        // 把收到的位元組轉成 UTF-8 字串，再原訊息送回
        var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
        await SendText(ws, text);
    }
});

// ─── 9.5 簡易聊天室 ───────────────────────────────────────────────────
// 多人連線時，伺服器需要記住每個連線，才能廣播給其他人。
// 使用 ConcurrentDictionary 是因為多個請求各自在不同 thread 上跑，需要執行緒安全的容器。
var chatClients = new ConcurrentDictionary<string, WebSocket>();
app.Map("/ws/chat", async (HttpContext ctx) =>
{
    if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }

    using var ws = await ctx.WebSockets.AcceptWebSocketAsync();

    // 用 GUID 當每個連線的唯一 key，方便離線時從字典中移除
    var id = Guid.NewGuid().ToString();
    chatClients[id] = ws;

    var ip = ctx.Connection.RemoteIpAddress?.ToString();

    // 約定：使用者送出的「第一筆訊息」是暱稱，之後的訊息才是聊天內容
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
                // 第一筆 → 設為暱稱並廣播加入訊息
                userName = text;
                await Broadcast(chatClients,
                    $"{userName}({ip}) 進入,目前 {chatClients.Count} 人");
            }
            else
            {
                // 之後 → 一律當聊天內容廣播
                await Broadcast(chatClients, $"{userName}: {text}");
            }
        }
    }
    finally
    {
        // 不論是正常結束、例外還是斷線，都要把自己從清單移除，避免廣播到死掉的連線
        chatClients.TryRemove(id, out _);

        // 若曾經設定過暱稱，才有意義通知別人「誰離開」
        if (userName is not null)
        {
            await Broadcast(chatClients,
                $"{userName}({ip}) 離開,目前 {chatClients.Count} 人");
        }
    }
});

// ─── 9.6 共用塗鴉板 ───────────────────────────────────────────────────
// 多人協作畫圖：任何人傳來的座標/筆觸資料，都直接廣播給所有連線（包含自己）。
// 與聊天室不同的是：不解析訊息內容，純粹是位元組轉發。
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

            // AsMemory 可避免額外配置：直接把 buffer 的有效片段當成 ReadOnlyMemory<byte> 來轉送
            var payload = buffer.AsMemory(0, result.Count);

            // 廣播給所有人；個別連線傳送失敗就略過，不影響其他人
            foreach (var c in drawClients.Values)
            {
                if (c.State != WebSocketState.Open) continue;
                try { await c.SendAsync(payload, WebSocketMessageType.Text, true, CancellationToken.None); }
                catch { /* 單一連線失敗不該中斷整體廣播 */ }
            }
        }
    }
    finally
    {
        drawClients.TryRemove(id, out _);
    }
});

// ─── §10 AJAX 上傳 ────────────────────────────────────────────────────
// 為了避免使用者上傳偽裝副檔名的檔案（例如把 .exe 改成 .jpg），
// 後端以 Content-Type（MIME type）為準來決定要存成什麼副檔名。
// 不在白名單內的格式（例如 image/gif、application/pdf）一律忽略。
var extMap = new Dictionary<string, string>
{
    ["image/jpeg"] = ".jpg",
    ["image/png"] = ".png",
    ["image/webp"] = ".webp",
};

// ─── 10.2 單檔上傳：個人資料 + 大頭貼 ─────────────────────────────────
// 同一個 POST 同時帶有：表單欄位 user / description（文字）與 avatar（檔案）。
// 設計：每次上傳都「合併」到 profile.json，沒帶到的欄位會保留舊值。
app.MapPost("/uploads/profile", async (
    [FromForm] string? user,           // 從 multipart/form-data 取出文字欄位
    [FromForm] string? description,
    IFormFile? avatar) =>              // 檔案以 IFormFile 接收（沒上傳則為 null）
{
    var profilePath = Path.Combine(publicPath, "profile.json");

    // 先讀取既有資料，作為合併基底；找不到或解析失敗時改用空字典
    var data = new Dictionary<string, string?>();
    if (File.Exists(profilePath))
    {
        try
        {
            data = JsonSerializer.Deserialize<Dictionary<string, string?>>(
                await File.ReadAllTextAsync(profilePath)) ?? new();
        }
        catch { /* 檔案壞了當空，避免整個 API 失敗 */ }
    }

    // 只有「有送」的欄位才覆寫；null 代表前端沒傳，要保留舊值
    if (user is not null) data["user"] = user;
    if (description is not null) data["description"] = description;

    // 處理大頭貼：必須是允許的 MIME，且實際存檔成 GUID 隨機檔名（避免同名覆蓋與 path traversal）
    if (avatar is not null && extMap.TryGetValue(avatar.ContentType, out var ext))
    {
        var imagesDir = Path.Combine(publicPath, "images");
        Directory.CreateDirectory(imagesDir);                  // 不存在才建立；存在則無動作
        var fileName = $"{Guid.NewGuid()}{ext}";
        await using var stream = File.Create(Path.Combine(imagesDir, fileName));
        await avatar.CopyToAsync(stream);                      // 串流寫入，不用整顆塞進記憶體

        // 對應前端能直接拿來顯示的相對 URL（前面已經把 /images 對應到 publicPath/images）
        data["avatar"] = $"/images/{fileName}";
    }

    // 寫回 JSON；寫入失敗仍然回傳 success=false 與當前 data 讓前端能感知
    try
    {
        await File.WriteAllTextAsync(profilePath, JsonSerializer.Serialize(data));
        return Results.Ok(new { success = true, data });
    }
    catch
    {
        return Results.Ok(new { success = false, data });
    }
})
// .NET 對 form 預設會啟用 antiforgery 驗證；本範例是純 AJAX 上傳示範，這裡關閉以簡化程式
.DisableAntiforgery();

// ─── 10.3 多檔上傳：相簿 ──────────────────────────────────────────────
// 一次接收多個檔案；以 IFormFileCollection 接住所有同名（photos）的欄位
app.MapPost("/uploads/photos", async (IFormFileCollection photos) =>
{
    var imagesDir = Path.Combine(publicPath, "images");
    Directory.CreateDirectory(imagesDir);

    var urls = new List<string>();
    foreach (var photo in photos)
    {
        // 同樣以 MIME 過濾；不允許的格式直接跳過，不會中斷其他檔案的處理
        if (!extMap.TryGetValue(photo.ContentType, out var ext)) continue;

        var fileName = $"{Guid.NewGuid()}{ext}";
        await using var stream = File.Create(Path.Combine(imagesDir, fileName));
        await photo.CopyToAsync(stream);
        urls.Add($"/images/{fileName}");
    }
    // 回傳所有已存檔的相對 URL，前端可直接用來建立 <img>
    return Results.Ok(urls);
}).DisableAntiforgery();

// 啟動 Kestrel 伺服器並開始接受請求；此呼叫會一直阻塞到應用程式被停止
app.Run();

// ─── 共用 helper ──────────────────────────────────────────────────────

// 把字串以 UTF-8 編碼後，當成「一筆完整的文字訊息」送出（endOfMessage=true）
static async Task SendText(WebSocket ws, string text)
{
    var bytes = Encoding.UTF8.GetBytes(text);
    await ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
}

// 廣播：把同一段文字傳給字典中所有連線；個別失敗會被吞掉，不影響其他連線。
// 注意：這裡用 ConcurrentDictionary，遍歷時是「快照」式的，不會丟例外。
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

// 產生 SSE 事件來源：每 2 秒推一筆「目前時間」字串。
// 使用 IAsyncEnumerable + yield return：讓 ASP.NET Core 持續地一筆筆把事件寫到回應串流。
// [EnumeratorCancellation]：將外部傳入的取消權杖綁到產生器，使瀏覽器斷線時能正確跳出迴圈。
static async IAsyncEnumerable<SseItem<string>> GenerateTimeEvents(
    [EnumeratorCancellation] CancellationToken token)
{
    // 起始 EventId：對應 SSE 規範的 id 欄位；前端 EventSource 在重連時會帶 Last-Event-ID
    var id = 30;
    while (!token.IsCancellationRequested)
    {
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        yield return new SseItem<string>(now) { EventId = (id++).ToString() };

        // Task.Delay 配合 token：在等待期間若被取消，會丟 TaskCanceledException → 用 yield break 結束序列
        try { await Task.Delay(2000, token); }
        catch (TaskCanceledException) { yield break; }
    }
}
