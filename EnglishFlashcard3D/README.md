# EnglishFlashcard3D (C# WPF)

Ứng dụng desktop WPF học từ vựng Anh–Việt, kết nối với `BilingualVocabApi` tại `http://localhost:5046`.

## F2 — Quản lý chủ đề

Đăng nhập bằng tài khoản **student** để:

- Tạo chủ đề với tên tối đa 80 ký tự và chọn trình độ CEFR từ A1 đến C2.
- Dùng Gemini tạo 15 flashcard cho chủ đề.
- Xem danh sách chủ đề cùng trình độ và số lượng thẻ; mở chủ đề để học.
- Sinh thêm tối đa 15 từ mới cho chủ đề đang chọn; từ đã có được loại trừ để tránh lặp.
- Xóa chủ đề sau khi xác nhận; các thẻ thuộc chủ đề đó cũng bị xóa.

API phải đang chạy trước khi đăng nhập hoặc quản lý chủ đề. Các chức năng sinh từ cần cấu hình Gemini API key trong file `.env` ở thư mục gốc; xem hướng dẫn tại [README chính](../README.md).

## Tính năng đã chuyển
- Hiển thị flashcard (mặt trước/mặt sau)
- Lật thẻ bằng nút hoặc phím `Space`
- Điều hướng thẻ bằng `←` `→`
- Phát âm từ bằng TTS (phím `P`)
- Đánh dấu đã thuộc (phím `M`)
- Dữ liệu mẫu từ `Assets/Data/default_cards.json`

## Chạy ứng dụng

Chạy các lệnh sau từ thư mục gốc repository. Mở một terminal để chạy API:

```bash
dotnet run --project BilingualVocabApi/BilingualVocabApi.csproj
```

Sau đó, mở terminal thứ hai để chạy ứng dụng desktop:

```bash
dotnet run --project EnglishFlashcard3D/EnglishFlashcard3D.csproj
```

## Build
```bash
dotnet build EnglishFlashcard3D/EnglishFlashcard3D.csproj
```
