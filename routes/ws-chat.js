import WebSocket from "ws";

export default function setup(wss) {
  const broadcast = (msg) => {
    wss.clients.forEach((c) => {
      if (c.readyState === WebSocket.OPEN) c.send(msg);
    });
  };

  wss.on("connection", (ws, req) => {
    const user = { name: "", ip: req.socket.remoteAddress };

    ws.on("message", (message) => {
      const m = message.toString();
      if (!user.name) {
        // 第一次進來：當作報上名字
        user.name = m;
        broadcast(`${user.name}(${user.ip}) 進入，目前 ${wss.clients.size} 人`);
      } else {
        broadcast(`${user.name}: ${m}`);
      }
    });

    ws.on("close", () => {
      if (user.name) {
        broadcast(`${user.name}(${user.ip}) 離開，目前 ${wss.clients.size} 人`);
      }
    });
  });
}
