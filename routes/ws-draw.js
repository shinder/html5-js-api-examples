import WebSocket, { WebSocketServer } from "ws";

const wsServer = new WebSocketServer({ port: 3072 });
wsServer.on("connection", (ws, req) => {
  ws.on("message", (message) => {
    wsServer.clients.forEach((c) => {
      if (c.readyState === WebSocket.OPEN) {
        // 確認是有效連線, 再送資料
        c.send(message.toString());
      }
    });
  });
});

export default wsServer;
