# Bilingual Vocab AI

Hệ thống học và khảo thí tiếng Anh chuyên ngành DAU, hỗ trợ học từ vựng
song ngữ Anh - Trung (có thêm trường dữ liệu tiếng Nhật), trích xuất từ
vựng từ video YouTube và phân quyền theo ba vai trò: **Sinh viên**,
**Chuyên gia** và **Quản trị viên**.

## 1. Kiến trúc tổng quan

Ứng dụng gồm ba phần chạy cùng nhau:

| Thành phần | Công nghệ | Vai trò | Cổng mặc định |
| --- | --- | --- | --- |
| Frontend client | React 19, Vite, TypeScript, Tailwind CSS | Giao diện học tập, notebook, bài thi, chuyên gia và admin | `5173` |
| Backend API | Node.js, Express, TypeScript/TSX | Xử lý nghiệp vụ, gọi Gemini, trích xuất transcript YouTube và cung cấp REST API | `5000` |
| Desktop shell | Electron hoặc Edge/Chrome App Mode | Mở frontend trong một cửa sổ desktop riêng | Không có cổng riêng |

Luồng kết nối chính:

```text
Người dùng
   ↓
React/Vite (http://localhost:5173)
   ↓ fetch /api/*
Express API (http://localhost:5000)
   ├─ Gemini API: sinh, dịch và bổ sung dữ liệu từ vựng
   ├─ YouTube transcript: lấy phụ đề và metadata video
   └─ MySQL (ưu tiên) hoặc vocab_database.json (fallback local)
```

Frontend lấy URL API từ `VITE_API_URL`. Nếu biến này không được đặt,
frontend gọi API cùng origin; khi chạy Vite riêng, backend nên được truy cập
qua proxy/cấu hình môi trường tương ứng.

## 2. Chuẩn bị môi trường

- Windows 10/11 (script `start-desktop.bat` dùng lệnh Windows).
- Node.js và npm.
- MySQL nếu muốn dùng dữ liệu bền vững qua database. Mặc định hệ thống
  dùng `localhost:3307`, database `bilingual_vocab_dau`.
- Gemini API key để dùng các chức năng AI: sinh từ vựng, dịch/tra cứu,
  tự động điền thẻ và phân tích từ vựng video.

Cài dependency:

```bash
npm install
```

Tạo `.env` từ `.env.example`, sau đó cập nhật các giá trị phù hợp với máy
local. Không commit API key hoặc mật khẩu database vào Git:

```env
PORT=5000
GEMINI_API_KEY=your-gemini-api-key
DB_HOST=localhost
DB_PORT=3307
DB_USER=root
DB_PASSWORD=your-mysql-password
DB_NAME=bilingual_vocab_dau
# VITE_API_URL=http://localhost:5000
```

Nếu MySQL không kết nối được, server vẫn khởi động và dùng
`vocab_database.json` làm bộ nhớ cục bộ. Dữ liệu ở chế độ fallback được ghi
tại thư mục gốc dự án; khi MySQL sẵn sàng, có thể dùng chức năng đồng bộ
trong Admin.

## 3. Cách chạy hệ thống

### Cách 1: Mở desktop một lệnh (khuyến nghị trên Windows)

Chạy một trong hai lệnh sau:

```bash
npm run app
# hoặc
npm run desktop
```

Script `scripts/launch-desktop.mjs` sẽ:

1. Kiểm tra `http://localhost:5000/api/health`; nếu chưa có backend thì tự
   khởi động `server.ts`.
2. Kiểm tra `http://localhost:5173`; nếu chưa có frontend thì tự khởi động
   Vite.
3. Chờ frontend sẵn sàng rồi mở Electron. Nếu Electron chưa được cài, script
   thử Edge App Mode, Chrome App Mode, sau đó mở trình duyệt thông thường.

### Cách 2: Chạy tự động bằng file Windows

Nhấp đúp `start-desktop.bat`, hoặc chạy:

```bat
npm run start:all
```

File này khởi động backend, frontend, chờ 3 giây rồi gọi launcher desktop.

### Cách 3: Chạy từng tiến trình để debug

Mở ba terminal tại thư mục dự án:

```bash
# Terminal 1 - backend
npm run server

# Terminal 2 - frontend
npm run client

# Terminal 3 - cửa sổ desktop
npm run app
```

Hoặc chỉ chạy frontend trên trình duyệt:

```bash
npm run dev
```

Kiểm tra backend:

```text
http://localhost:5000/
http://localhost:5000/api/health
```

## 4. Các module và luồng hoạt động

### 4.1. Khởi tạo ứng dụng và xác thực

1. `src/main.tsx` mount React vào phần tử `#root`.
2. `src/App.tsx` bọc giao diện trong `AuthProvider`.
3. `AuthContext` đọc user từ `localStorage`. Nếu chưa có user, hệ thống
   đăng nhập mặc định tài khoản sinh viên qua `POST /api/auth/login`.
4. Sau khi đăng nhập, token và thông tin user được lưu ở local storage; việc
   chuyển vai trò trên giao diện thực hiện đăng nhập lại bằng tài khoản demo
   tương ứng.
5. Tab đang mở được lưu ở `vocab_active_tab`, nên lần mở sau có thể tiếp tục
   tại khu vực trước đó.

### 4.2. Học từ vựng theo chủ đề

```text
Chọn/tạo chủ đề
   ↓
POST /api/vocab/generate
   ↓
Backend gọi Gemini với CEFR và danh sách từ cần loại trừ
   ↓
Lưu topic + cards vào MySQL hoặc JSON
   ↓
FlashcardDeck / QuizMode hiển thị và cập nhật trạng thái learned
```

Các thao tác chính gồm lấy danh sách chủ đề, lấy thẻ theo topic, sinh thêm
thẻ, tạo/sửa/xóa thẻ và đánh dấu đã học. Dữ liệu notebook tổng hợp qua
`GET /api/vocab/notebook/all`.

### 4.3. Trích xuất từ vựng từ video YouTube

1. Người học dán URL YouTube tại `VideoVocabPage`.
2. Backend kiểm tra video ID, lấy transcript/metadata và tạo một
   `video_session`.
3. Gemini phân tích transcript để tạo các `video_words` kèm nghĩa, ngôn ngữ,
   cấp độ và timestamp.
4. Frontend tải session, phát video và đồng bộ từ/phụ đề theo thời gian.
5. Phụ đề có thể được hiệu chỉnh rồi lưu lại bằng
   `PUT /api/video/sessions/:sessionId/subtitles`.

Endpoint chính: `POST /api/video/extract`, `GET /api/video/sessions` và
`GET /api/video/:sessionId`.

### 4.4. Tra cứu và tự động điền thẻ

- `POST /api/translate/lookup`: tra cứu nhanh một từ/cụm từ theo ngôn ngữ
  nguồn.
- `POST /api/vocab/autofill`: Gemini bổ sung cách đọc, nghĩa, ví dụ và các
  trường còn thiếu cho thẻ.
- Người học xác nhận nội dung rồi lưu bằng
  `POST /api/vocab/cards`; thẻ có thể chỉnh sửa hoặc xóa sau đó.

### 4.5. Bài thi và cổng Chuyên gia

```text
Chuyên gia tạo assignment
   ↓
Sinh viên xem assignment và nộp Writing/Speaking/Listening
   ↓
Chuyên gia xem submissions
   ↓
Chấm điểm + feedback + CEFR
   ↓
Sinh viên xem kết quả và consultation/study plan
```

Nhóm API tương ứng:

- Chuyên gia: `/api/expert/assignments`, `/api/expert/submissions`,
  `/api/expert/consultations`, `/api/expert/students-progress`.
- Sinh viên: `/api/student/assignments`, `/api/student/assignments/:id/submit`,
  `/api/student/submissions`, `/api/student/consultations`.

### 4.6. Trang Quản trị

Admin theo dõi thống kê, người dùng, chương trình DAU, cấu hình Gemini và
API logs. Các endpoint chính:

- `/api/admin/stats`
- `/api/admin/ai-config`
- `/api/admin/api-logs`
- `/api/admin/dau-curriculum`
- `POST /api/admin/sync-mysql`
- `/api/auth/users`

Khi server khởi động, `server/db/store.ts` thử kết nối MySQL và tự tạo các
bảng cần thiết nếu database đã truy cập được. Nếu thất bại, server chuyển
sang JSON local và ghi cảnh báo trong terminal.

## 5. Tài khoản demo

Các tài khoản được khởi tạo trong database local:

| Vai trò | Email | Mật khẩu |
| --- | --- | --- |
| Sinh viên | `student@dau.edu.vn` | `1234` |
| Chuyên gia | `expert@dau.edu.vn` | `1234` |
| Quản trị viên | `admin@dau.edu.vn` | `1234` |

Đây là tài khoản phục vụ demo local. Khi triển khai thật, cần thay đổi mật
khẩu và bổ sung cơ chế xác thực/phân quyền phù hợp.

## 6. Build và chạy bản production

Kiểm tra TypeScript:

```bash
npm run lint
```

Build frontend và bundle backend:

```bash
npm run build
```

Chạy backend đã build:

```bash
npm run start
```

Để backend phục vụ thêm frontend đã build, đặt:

```env
SERVE_STATIC=true
```

Sau đó truy cập `http://localhost:5000`. `npm run preview` chỉ phục vụ kiểm
tra bản build frontend qua Vite preview; nó không thay thế backend API.

## 7. Xử lý lỗi thường gặp

| Hiện tượng | Kiểm tra |
| --- | --- |
| Không mở được ứng dụng desktop | Kiểm tra `npm install`, Electron/Edge/Chrome và chạy `npm run client` thủ công |
| API không phản hồi | Mở `http://localhost:5000/api/health`, xem log terminal backend và kiểm tra cổng `PORT` |
| Không sinh/tra cứu được từ | Kiểm tra `GEMINI_API_KEY` và quota Gemini |
| Không có dữ liệu MySQL | Kiểm tra MySQL đang chạy, `DB_HOST`, `DB_PORT`, user/password và database |
| Dữ liệu vẫn chạy nhưng không vào MySQL | Đây là chế độ fallback JSON; sửa kết nối rồi dùng `POST /api/admin/sync-mysql` |
| Video không có transcript | Kiểm tra URL YouTube, quyền truy cập phụ đề và thử nhập transcript thủ công nếu giao diện hỗ trợ |

## 8. Cấu trúc thư mục chính

```text
.
├─ src/
│  ├─ components/       # Thành phần giao diện dùng lại
│  ├─ context/          # AuthContext và trạng thái dùng chung
│  ├─ pages/            # Các màn hình theo module
│  ├─ services/api.ts   # Client gọi REST API
│  └─ types/            # Kiểu dữ liệu dùng chung
├─ server/
│  ├─ modules/          # auth, vocab, video, translation, student, expert, admin
│  ├─ config/           # Gemini client
│  └─ db/               # MySQL và fallback JSON
├─ electron/main.cjs    # Cửa sổ Electron
├─ scripts/             # Launcher desktop và kiểm tra service
├─ schema.sql           # Schema MySQL tham khảo
└─ server.ts            # Entry point Express API
```
#   L T M  
 #   L T M  
 