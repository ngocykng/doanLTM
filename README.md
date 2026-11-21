# LTM – Ứng dụng học từ vựng tiếng Anh

Project gồm hai thành phần C#:

- **BilingualVocabApi**: REST API dùng ASP.NET Core, lưu dữ liệu mặc định trong `BilingualVocabApi/vocab_database.json`.
- **EnglishFlashcard3D**: ứng dụng desktop WPF để học từ vựng bằng flashcard; gọi API tại `http://localhost:5046`.

## Chức năng tài khoản

Ứng dụng hỗ trợ đăng nhập và đăng ký tài khoản sinh viên. API cung cấp các endpoint:

- `POST /api/auth/login`: đăng nhập bằng email và mật khẩu.
- `POST /api/auth/register`: đăng ký bằng họ tên, email và mật khẩu; tài khoản mới có vai trò `student`.

Trong ứng dụng desktop, chọn **Đăng nhập** hoặc chuyển sang chế độ tạo tài khoản mới ở màn hình tài khoản. API phải đang chạy trước khi sử dụng các chức năng này.

## Quản lý chủ đề

Đăng nhập bằng tài khoản **student** để:

- Tạo chủ đề (tên tối đa 80 ký tự), chọn cấp CEFR A1–C2 và yêu cầu Gemini sinh bộ flashcard.
- Tải lại danh sách chủ đề, xem cấp độ và số thẻ, rồi mở chủ đề để học bằng flashcard.
- Sinh thêm tối đa 15 từ mới cho chủ đề đang chọn; từ đã có sẽ bị loại trừ.
- Xóa chủ đề sau khi xác nhận; các flashcard thuộc chủ đề đó cũng bị xóa.

Tên chủ đề không được trùng trong cùng tài khoản (không phân biệt chữ hoa/thường). Cần cấu hình `GEMINI_API_KEY` trước khi yêu cầu sinh từ.

## Yêu cầu

- Windows 10/11.
- Git.
- .NET SDK 10 để chạy API.
- .NET SDK 8 để chạy ứng dụng WPF.

## Clone project

Mở PowerShell hoặc Terminal:

```powershell
git clone https://github.com/ngocykng/doanLTM.git
cd doanLTM
```

## Chạy project

### 1. Chạy API

Mở terminal thứ nhất tại thư mục gốc project:

```powershell
dotnet run --project BilingualVocabApi/BilingualVocabApi.csproj
```

API mặc định chạy tại `http://localhost:5046`. Mở `http://localhost:5046/api/health` để xem trạng thái.

### 2. Chạy ứng dụng desktop

Giữ API đang chạy. Mở terminal thứ hai tại thư mục gốc:

```powershell
dotnet run --project EnglishFlashcard3D/EnglishFlashcard3D.csproj
```

Ứng dụng WPF hiện chỉ chạy trên Windows.

Có thể mở solution `LTM.slnx` bằng Visual Studio 2022 có hỗ trợ .NET để chạy hoặc debug từng project.

### Cấu hình Gemini API key (tùy chọn)

Một số chức năng tạo từ vựng cần Gemini API key:

1. Tại thư mục gốc project, sao chép file mẫu `.env.example` thành `.env`:

   ```powershell
   Copy-Item .env.example .env
   ```

2. Mở file `.env`, thay `your_gemini_api_key` bằng Gemini API key của bạn:

   ```dotenv
   GEMINI_API_KEY=your_gemini_api_key
   ```

3. Lưu file và khởi động lại API.

Chỉ sửa giá trị giữ chỗ trong `.env.example` nếu muốn thay đổi nội dung mẫu cho mọi người. Không điền API key thật vào `.env.example`; file `.env` đã được Git ignore và không được commit lên GitHub.

## Làm việc nhóm bằng branch

Mỗi thành viên làm trên branch riêng. Không push trực tiếp lên `main`. Ví dụ tạo branch cho module đăng nhập:

```powershell
git switch main
git pull origin main
git switch -c feature/login
```

Thay `feature/login` bằng tên phù hợp, chẳng hạn `feature/flashcards` hoặc `feature/api-vocab`.

Sau khi sửa code, commit và push branch lần đầu:

```powershell
git add .
git commit -m "Add login module"
git push -u origin feature/login
```

Ở những lần sau trên cùng branch:

```powershell
git add .
git commit -m "Update login module"
git push
```

`git add .` thêm các thay đổi trong thư mục hiện tại. Trước khi commit, có thể xem các file sẽ được ghi nhận bằng `git status`.

## Cập nhật code khi thành viên khác đã push

### Trước khi bắt đầu một công việc mới

Cập nhật `main`, rồi tạo branch mới từ phiên bản mới nhất:

```powershell
git switch main
git pull origin main
git switch -c feature/ten-module
```

### Khi đang làm trên branch riêng

Commit công việc hiện tại trước, sau đó lấy thay đổi mới nhất từ `main` về branch của mình:

```powershell
git add .
git commit -m "Save current work"
git fetch origin
git rebase origin/main
```

Nếu Git báo conflict, mở các file được báo, sửa những đoạn có dấu `<<<<<<<`, `=======`, `>>>>>>>`, lưu lại, rồi chạy:

```powershell
git add .
git rebase --continue
git push
```

Nếu muốn hủy rebase để quay lại trạng thái trước đó:

```powershell
git rebase --abort
```

## Gộp code vào main

Sau khi push branch, thành viên mở repository trên GitHub và tạo **Pull Request**:

1. Chọn branch của mình làm nhánh nguồn (**compare**) và `main` làm nhánh đích (**base**).
2. Mô tả ngắn phần đã làm và kiểm tra danh sách file thay đổi.
3. Trưởng nhóm hoặc thành viên được giao review Pull Request.
4. Khi thống nhất, chọn **Merge pull request** để gộp vào `main`.

Sau khi merge, mọi người cập nhật bản chính trên máy bằng:

```powershell
git switch main
git pull origin main
```

Nếu chưa có quyền push, nhờ chủ repository thêm tài khoản GitHub của bạn vào **Settings → Collaborators**.
