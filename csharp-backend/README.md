# csharp-backend

對應 [HTML5 API（.NET Core 後端版）講義](../../../Dropbox/講義/markdown-handouts/javascript/html5-api-dotnet-2605.md) 的 ASP.NET Core 範例。

涵蓋原講義的：

- §8 Server-Sent Event（`/try-sse`）
- §9 WebSocket（`/ws/echo`、`/ws/chat`、`/ws/draw`）
- §10 AJAX 上傳（`/uploads/profile`、`/uploads/photos`）

前端 HTML 直接共用上層 `../public/` 的靜態檔，跟 Node 範例專案是同一份。

## 環境

- .NET 10 SDK 或更新
- 預設聽 `http://localhost:3031`（與 Node 同 port，同時間只能跑一個）

## 啟動

```sh
dotnet run            # 啟動
dotnet watch run      # 修改原始碼自動重啟
```

開啟 <http://localhost:3031> 會看到 `../public/` 的檔案列表（對應 Node 的 `serve-index`）。

## 與 Node 後端的差異

| 項目        | Node Express                                     | 本專案                              |
| ----------- | ------------------------------------------------ | ----------------------------------- |
| WebSocket   | 三條獨立 port（3070/3071/3072）                  | 同 port、用 path 區分（`/ws/*`）   |
| 路由形式    | `app.get` / `app.post`                           | `app.MapGet` / `app.MapPost`        |
| 客戶端關閉  | `req.on("close")`                                | `HttpContext.RequestAborted`        |
| 檔案上傳    | `multer`                                         | `IFormFile` + `[FromForm]`          |
| SSE         | 手寫 header + `res.write` + `setInterval`        | `TypedResults.ServerSentEvents` + `IAsyncEnumerable<SseItem<T>>` |

> **WebSocket 前端 URL**：A03/A04/A05 的 HTML 目前還是 Node 的 `ws://...:3070/3071/3072` 寫法。Node 端改成單一 port + path 之後，前端會跟著改成 `ws://.../ws/echo|chat|draw`，那時兩個後端就完全共用同一份 HTML 了。
