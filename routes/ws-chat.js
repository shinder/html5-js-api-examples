import WebSocket, { WebSocketServer } from "ws";

const wsServer = new WebSocketServer({ port: 3071 });

const broadcast = (msg) => {
  wsServer.clients.forEach((c) => {
    if (c.readyState === WebSocket.OPEN) c.send(msg);
  });
};

wsServer.on("connection", (ws, req) => {
  const user = { name: "", ip: req.socket.remoteAddress };

  ws.on("message", (message) => {
    const m = message.toString();
    if (!user.name) {
      // 第一次進來：當作報上名字
      user.name = m;
      broadcast(`${user.name}(${user.ip}) 進入，目前 ${wsServer.clients.size} 人`);
    } else {
      broadcast(`${user.name}: ${m}`);
    }
  });

  ws.on("close", () => {
    if (user.name) {
      broadcast(`${user.name}(${user.ip}) 離開，目前 ${wsServer.clients.size} 人`);
    }
  });
});

export default wsServer;
