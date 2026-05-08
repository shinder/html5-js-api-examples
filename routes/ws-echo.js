import { WebSocketServer } from "ws";

const wsServer = new WebSocketServer({ port: 3070 });

wsServer.on("connection", (ws, req) => {
  const ip = req.socket.remoteAddress;
  ws.send(`連線了, 你來自 ${ip}, 目前連線人數: ${wsServer.clients.size}`);

  ws.on("message", (message) => {
    ws.send(message.toString()); // 原訊息送回
  });
});

export default wsServer;
