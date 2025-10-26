import express from "express";
import serveIndex from "serve-index";
import fs from "node:fs/promises";
import "./routes/ws-echo.js";
import "./routes/ws-chat.js";
import "./routes/ws-draw.js";
import upload from "./routes/upload-img-module.js";

const web_port = process.env.WEB_PORT || 3031;
const app = express();

app.use(express.urlencoded({ extended: true }));
app.use(express.json());

app.get("/try-qs", (req, res) => {
  res.json(req.query);
});

app.get("/try-sse", (req, res) => {
  let id = 30;
  res.writeHead(200, {
    "Content-Type": "text/event-stream",
    "Cache-Control": "no-cache",
    Connection: "keep-alive",
  });
  setInterval(function () {
    let now = new Date();
    res.write(`id: ${id++}\n`);
    res.write(`data: ${now.toLocaleString()}\n\n`);
  }, 2000);
});

// 處理個人資料的表單
app.post("/uploads/profile", upload.single("avatar"), async (req, res) => {
  console.log(req.body);
  let data = {}; // 要存檔的資料
  try {
    const d = await fs.readFile("./public/profile.json");
    data = JSON.parse(d.toString());
  } catch (ex) {}
  data = { ...data, ...req.body }; // 變更資料

  if (req.file && req.file.originalname) {
    // 若有上傳檔案
    data.avatar = "/images/" + req.file.filename; // 儲存包含路徑
  }
  try {
    await fs.writeFile("./public/profile.json", JSON.stringify(data));
  } catch (ex) {
    return res.json({ success: false, data });
  }
  res.json({ success: true, data });
});

app.post("/uploads/photos", upload.array("photos"), (req, res) => {
  const output = [];
  req.files.forEach((file) => {
    output.push("/images/" + file.filename);
  });
  res.json(output);
});

// 本機需要有 Ollama 伺服器執行中
// https://www.npmjs.com/package/openai#streaming-responses
// https://docs.ollama.com/openai
import { OpenAI } from "openai";
app.post(`/chat`, async (req, res) => {
  // 使用 Ollama 的 Gemma3 模型進行聊天
  if (!req.body?.message) {
    return res.json({ message: "沒有 message !" });
  }

  const client = new OpenAI({
    baseURL: "http://localhost:11434/v1/",
    apiKey: "ollama",
  });

  const stream = await client.chat.completions.create({
    model: "gemma3",
    messages: [
      {
        role: "system",
        content:
          "<角色>您是一個出色的3C產品介紹人員，會使用正體中文、並且貼心精簡回答問題</角色>",
          // "<角色>您是一個出色的3C產品介紹人員，會使用正體中文、並且貼心周詳回答問題</角色>"
      },
      {
        role: "user",
        content: req.body?.message,
      },
    ],
    stream: true,
  });

  res.writeHead(200, {
    "Content-Type": "text/event-stream",
    "Cache-Control": "no-cache",
    Connection: "keep-alive",
  });

  for await (const event of stream) {
    // console.log(event);
    console.log(JSON.stringify(event));
    res.write(`data: ${JSON.stringify(event)}\n\n`);
    // my.choices[0].delta.content
  }
  res.end("");
});

app.use(express.static("public"));
// 要放在所有路由之後
app.use("/", serveIndex("public", { icons: true }));

app.listen(web_port, () => {
  console.log(`伺服器啟動於通訊埠：http://localhost:${web_port}`);
});
