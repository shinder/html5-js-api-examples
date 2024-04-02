import WebSocket, { WebSocketServer } from "ws";

const wsServer = new WebSocketServer({ port: 3071 });

const broadcastMsg = (msg) => {
  wsServer.clients.forEach((c) => {
    if (c.readyState === WebSocket.OPEN) {
      // 確認是有效連線, 再送資料
      c.send(msg);
    }
  });
};

wsServer.on("connection", (ws, req) => {
  const dataObj = { name: "", ip: req.socket.remoteAddress }; // 記錄用戶資料
  ws.on("message", (message) => {
    const m = message.toString();
    let msg = ""; // 要回應的訊息
    if (dataObj.name) {
      msg = `${dataObj.name}: ${m}`;
    } else {
      // 沒有值, 表示都還沒有傳任何訊息進來
      dataObj.name = m; // 第一次傳入的就是他的名字
      msg = `${dataObj.name}(${dataObj.ip}) 進入聊天室, 目前人數: ${wsServer.clients.size}`;
    }
    broadcastMsg(msg);
  });

  ws.on("close", () => {
    const msg = `${dataObj.name}(${dataObj.ip}) 離開聊天室, 目前人數: ${wsServer.clients.size}`;
    broadcastMsg(msg);
  });
});

export default wsServer;
