import express from "express";
import serveIndex from "serve-index";
import fs from "node:fs/promises";
import { WebSocketServer } from "ws";
import setupEcho from "./routes/ws-echo.js";
import setupChat from "./routes/ws-chat.js";
import setupDraw from "./routes/ws-draw.js";
import upload from "./routes/upload-img-module.js";

const web_port = process.env.WEB_PORT || 3031;
const app = express();

app.use(express.urlencoded({ extended: true }));
app.use(express.json());

app.get("/try-qs", (req, res) => {
  res.json(req.query);
});

app.get("/try-sse", (req, res) => {
  res.writeHead(200, {
    "Content-Type": "text/event-stream",
    "Cache-Control": "no-cache",
    Connection: "keep-alive",
  });

  let id = 30;
  const timer = setInterval(() => {
    res.write(`id: ${id++}\n`);
    res.write(`data: ${new Date().toLocaleString()}\n\n`);
  }, 2000);

  // 客戶端關閉分頁/離開時清掉 timer
  req.on("close", () => clearInterval(timer));
});

// 處理個人資料的表單（單檔上傳）
app.post("/uploads/profile", upload.single("avatar"), async (req, res) => {
  // 讀取既有資料（不存在就用空物件）
  let data = {};
  try {
    data = JSON.parse(await fs.readFile("./public/profile.json", "utf8"));
  } catch {} // 檔案不存在或內容錯誤都算空

  // 合併新資料
  data = { ...data, ...req.body };
  if (req.file) {
    data.avatar = "/images/" + req.file.filename;
  }

  try {
    await fs.writeFile("./public/profile.json", JSON.stringify(data));
    res.json({ success: true, data });
  } catch (ex) {
    res.json({ success: false, data });
  }
});

// 多檔上傳
app.post("/uploads/photos", upload.array("photos"), (req, res) => {
  const urls = req.files.map((f) => "/images/" + f.filename);
  res.json(urls);
});

// 本機需要有 Ollama 伺服器執行中
// https://www.npmjs.com/package/openai#streaming-responses
// https://docs.ollama.com/openai
import { OpenAI } from "openai";

// Ollama 提供 OpenAI 相容 API；apiKey 不檢查，給任何字串都可以
const ollama = new OpenAI({
  baseURL: "http://localhost:11434/v1/",
  apiKey: "ollama",
});

app.post("/chat", async (req, res) => {
  const message = req.body?.message;
  if (!message) return res.status(400).json({ error: "沒有 message" });

  res.writeHead(200, {
    "Content-Type": "text/event-stream",
    "Cache-Control": "no-cache",
    Connection: "keep-alive",
  });

  const stream = await ollama.chat.completions.create({
    model: "gemma3",
    messages: [
      {
        role: "system",
        content: "你是一個出色的 3C 產品介紹員，會使用正體中文、貼心精簡地回答",
      },
      { role: "user", content: message },
    ],
    stream: true,
  });

  // 客戶端關閉連線時中止 OpenAI 串流，避免繼續燒運算
  req.on("close", () => stream.controller.abort());

  for await (const event of stream) {
    const delta = event.choices[0]?.delta?.content;
    // JSON.stringify 處理可能含換行的字串（SSE data 不能有原始換行）
    if (delta) res.write(`data: ${JSON.stringify(delta)}\n\n`);
  }
  res.write("data: [DONE]\n\n");
  res.end();
});

app.use(express.static("public"));
// 要放在所有路由之後
app.use("/", serveIndex("public", { icons: true }));

const server = app.listen(web_port, () => {
  console.log(`伺服器啟動於通訊埠：http://localhost:${web_port}`);
});

// WebSocket：三條 path 共用同一 port（取代原本 3070/3071/3072 三個獨立 port）
const wss = {
  echo: new WebSocketServer({ noServer: true }),
  chat: new WebSocketServer({ noServer: true }),
  draw: new WebSocketServer({ noServer: true }),
};

setupEcho(wss.echo);
setupChat(wss.chat);
setupDraw(wss.draw);

// HTTP upgrade → 依 path 分流到對應的 WebSocketServer
server.on("upgrade", (req, socket, head) => {
  const { pathname } = new URL(req.url, `http://${req.headers.host}`);
  const target = wss[pathname.replace(/^\/ws\//, "")];
  if (!target) {
    socket.destroy();
    return;
  }
  target.handleUpgrade(req, socket, head, (ws) => {
    target.emit("connection", ws, req);
  });
});
