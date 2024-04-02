import express from "express";
import serveIndex from "serve-index";
import fs from "node:fs/promises";
import "./routes/ws-echo.js";
import "./routes/ws-chat.js";
import "./routes/ws-draw.js";
import upload from "./routes/upload-img-module.js";

const web_port = 3031;
const app = express();

app.use(express.static("public"));
app.use("/", serveIndex("public", { icons: true }));

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

app.listen(web_port, () => {
  console.log(`伺服器啟動於通訊埠：${web_port}`);
});
