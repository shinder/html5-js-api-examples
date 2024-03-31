# 基本環境建立方式

1. 在欲放置專案資料夾的路徑，建立專案資料夾。

```sh
mkdir html5-js-api-base
```

2. 切換工作目錄到專案資料夾內。

```sh
cd html5-js-api-base
```

3. 使用 npm 初始化專案。

```sh
npm init -y
```

4. 利用 npm 安裝所需套件。

```sh
npm i express
npm i serve-index
npm i --save-dev nodemon
```

5. 編輯 package.json，使用 ESM。

```
  "type": "module",
```

6. 在專案目錄建立 public 資料夾，並放入一支 html 檔。

```sh
mkdir public
```

7. 在專案目錄新增檔案 index.js，並輸入以下內容。

```js
import express from "express";
import serveIndex from "serve-index";

const web_port = 3031;
const app = express();

app.use(express.static("public"));
app.use("/", serveIndex("public", { icons: true }));

app.listen(web_port, () => {
  console.log(`伺服器啟動於通訊埠：${web_port}`);
});
```

8. 修改 package.json，加入啟動的 scripts。

```json
  "scripts": {
    "dev": "nodemon index.js"
  }
```

9. 在專案目錄的命令列，輸入下式啟動伺服器，並於瀏覽器上查看 `http://localhost:3031` 。

```sh
npm run dev
```
