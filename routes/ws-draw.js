import WebSocket, { WebSocketServer } from "ws";

const wsServer = new WebSocketServer({ port: 3072 });

wsServer.on("connection", (ws) => {
  ws.on("message", (message) => {
    wsServer.clients.forEach((c) => {
      if (c.readyState === WebSocket.OPEN) c.send(message.toString());
    });
  });
});

export default wsServer;
