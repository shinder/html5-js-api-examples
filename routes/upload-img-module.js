import multer from "multer";
import { v4 as uuid } from "uuid";

// MIME type 對應到副檔名（同時也是白名單）
const extMap = {
  "image/jpeg": ".jpg",
  "image/png": ".png",
  "image/webp": ".webp",
};

const fileFilter = (req, file, cb) => {
  // 不在白名單的 MIME type 直接捨棄（cb 第二個參數 false = 不收）
  cb(null, !!extMap[file.mimetype]);
};

const storage = multer.diskStorage({
  destination: (req, file, cb) => cb(null, "public/images"),
  filename: (req, file, cb) => cb(null, uuid() + extMap[file.mimetype]),
});

export default multer({ fileFilter, storage });
