// 由 index.js 建好的 WebSocketServer 注入；同 port、用 path 區分
export default function setup(wss) {
  wss.on("connection", (ws, req) => {
    const ip = req.socket.remoteAddress;
    ws.send(`連線了, 你來自 ${ip}, 目前連線人數: ${wss.clients.size}`);

    ws.on("message", (message) => {
      ws.send(message.toString()); // 原訊息送回
    });
  });
}
