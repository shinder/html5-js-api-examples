import express from "express";
import serveIndex from "serve-index";

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

app.listen(web_port, () => {
  console.log(`伺服器啟動於通訊埠：${web_port}`);
});
