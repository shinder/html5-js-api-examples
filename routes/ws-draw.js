import WebSocket from "ws";

export default function setup(wss) {
  wss.on("connection", (ws) => {
    ws.on("message", (message) => {
      wss.clients.forEach((c) => {
        if (c.readyState === WebSocket.OPEN) c.send(message.toString());
      });
    });
  });
}
