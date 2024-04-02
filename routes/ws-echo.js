import { WebSocketServer } from "ws";

const wsServer = new WebSocketServer({ port: 3070 });
wsServer.on("connection", (ws, req) => {
  console.log(req.constructor.name);
  console.log(ws.constructor.name);
  console.log(req.url);
  const ip = req.socket.remoteAddress; // 用戶的 IP
  ws.on("message", (message) => {
    ws.send(message.toString());
  });

  ws.send(`連線了, 你來自 ${ip}, 連線人數: ${wsServer.clients.size}`);
});

export default wsServer;
