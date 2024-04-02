import multer from "multer";
import { v4 } from "uuid";

// 篩選及副檔名對應物件
const extMap = {
  "image/jpeg": ".jpg",
  "image/png": ".png",
  "image/webp": ".webp",
};

const fileFilter = (req, file, cb) => {
  // 對不到副檔名的檔案捨棄
  cb(null, !!extMap[file.mimetype]);
};

const storage = multer.diskStorage({
  destination: (req, file, cb) => {
    // 儲存檔案的資料夾位置
    cb(null, "public/images");
  },
  filename: (req, file, cb) => {
    // 隨機決定檔名
    cb(null, v4() + extMap[file.mimetype]);
  },
});

export default multer({fileFilter, storage});
