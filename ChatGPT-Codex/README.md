<div align="center">

[← Về Remielle the Widget](../README.md)

# 🪟 ChatGPT/Codex — Windows Desktop App

**ChatGPT/Codex** là phiên bản Windows của
**[Remielle the Widget](../README.md)**. Widget hiển thị 5 trạng thái hoạt động
của ChatGPT/Codex, đi theo cửa sổ ứng dụng và tự ẩn khi bạn thu nhỏ hoặc chuyển
sang ứng dụng khác.

[![Platform](https://img.shields.io/badge/Platform-Windows-0078D4?style=for-the-badge&logo=windows&logoColor=white)](https://www.microsoft.com/windows)
[![.NET](https://img.shields.io/badge/.NET-8-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow?style=for-the-badge)](../LICENSE)

</div>

---

## ✨ Tính năng nổi bật

- Giữ nguyên 5 trạng thái `WAITING`, `USER_TYPING`, `AI_THINKING`, `AI_TYPING`
  và `AI_COMPLETE`.
- Overlay WPF trong suốt, luôn nằm trên cùng, kéo thả và đổi kích thước từ
  60–220 pixel bằng con lăn.
- Tự hiện khi ChatGPT/Codex foreground; tự ẩn khi ứng dụng bị thu nhỏ, đóng
  hoặc khi bạn chuyển sang ứng dụng khác.
- Kiểm tra foreground bằng Win32 mỗi 25 ms, độc lập với lần quét UI Automation
  500 ms. Khi ứng dụng chưa mở, observer thử kết nối lại mỗi 250 ms.
- Lưu vị trí, kích thước, auto-open và logging tại
  `%LocalAppData%\Remielle Widget\settings.json`.
- Không có telemetry và không dùng thư viện runtime bên thứ ba.

> 💡 Phiên bản khác:
> **[Claude — Chrome Extension](../Claude/README.md)** ·
> **[ChatGPT — Chrome Extension](../ChatGPT/README.md)**

---

## 🚀 Yêu cầu và cách chạy

- Windows 10 hoặc Windows 11.
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) trở lên.

> ℹ️ Hiện chưa có installer; các lệnh bên dưới build và chạy ứng dụng từ source.

Chạy từ thư mục gốc của repository:

```powershell
dotnet restore ChatGPT-Codex/Remielle.Desktop.sln
dotnet build ChatGPT-Codex/Remielle.Desktop.sln
dotnet test ChatGPT-Codex/Remielle.Desktop.sln
dotnet run --project ChatGPT-Codex/Remielle.Overlay
```

Lần chạy thủ công đầu tiên đăng ký widget khởi động cùng Windows. Ở các lần
đăng nhập sau, widget chạy ẩn và tự hiện khi phát hiện cửa sổ ChatGPT/Codex.
Auto-open dùng mục `Run` của người dùng hiện tại, không cần quyền quản trị và
không sửa shortcut hay inject vào ứng dụng.

---

## 🖱️ Sử dụng

- Kéo widget bằng chuột trái.
- Cuộn chuột để đổi kích thước.
- Nhấp chuột phải để bật/tắt **Auto-open with ChatGPT/Codex**, bật/tắt
  diagnostic logging, reset vị trí hoặc Exit.
- Trong build Debug, dùng **Debug state simulator** để thử đủ 5 GIF hoặc chọn
  **Resume observer** để quay lại quan sát ứng dụng.

Logging mặc định tắt. Khi bật, log nằm tại
`%LocalAppData%\Remielle Widget\remielle.log`.

---

## 🔍 Windows UI Automation và quyền riêng tư

Observer tìm process/cửa sổ ChatGPT hoặc Codex, sau đó dùng selector trong
[`UiSelectors.json`](./Remielle.ChatGPTObserver/UiSelectors.json) để nhận diện
composer, Send, Stop và vùng assistant. Nếu giao diện desktop thay đổi, dùng
Inspect.exe hoặc Accessibility Insights để cập nhật matcher `automationId`,
`nameContains`, `controlType`, `className` hoặc `classNameContains`, rồi build
và khởi động lại.

Ứng dụng chỉ kiểm tra process, trạng thái foreground/thu nhỏ, empty/non-empty
và thay đổi cấu trúc UI. Nội dung prompt hoặc phản hồi không được lưu, ghi log
hay gửi đi. Ứng dụng không đọc cookie/token/clipboard, không hook bàn phím,
không inject DLL và không đọc bộ nhớ process.

> ⚠️ **Giới hạn đã biết:** trên Codex desktop `26.721.4979.0` được kiểm tra ngày
> 31/07/2026, lần đọc UIA ban đầu chỉ thấy `Chrome_WidgetWin_1`, `RootWebArea`
> và các nút khung cửa sổ. Runtime observer sau đó đã nhận diện được Send/Stop
> và chuyển sang `AI_THINKING`, nhưng selector composer/assistant cùng đủ 5
> trạng thái live vẫn chưa được xác minh. Debug state simulator vẫn là cách
> kiểm tra chắc chắn đủ 5 trạng thái.

---

## 🔧 Gỡ cài đặt

1. Nhấp chuột phải widget và tắt **Auto-open with ChatGPT/Codex**.
2. Chọn **Exit**.
3. Xóa thư mục build nếu không còn sử dụng.
4. Tùy chọn xóa cài đặt tại `%LocalAppData%\Remielle Widget`.

---

## ⚠️ Miễn trừ trách nhiệm

- Đây là dự án mã nguồn mở cá nhân, không liên kết với OpenAI hoặc Microsoft.
- ChatGPT/Codex có thể thay đổi giao diện và làm selector UI Automation cần cập
  nhật.
- Ứng dụng hoạt động cục bộ, không thu thập hay lưu trữ dữ liệu cá nhân.

---

## 📜 Nguồn gốc và giấy phép

ChatGPT/Codex tái sử dụng nguyên trạng GIF từ các extension hiện có.
Nguồn asset là [Gemielle](https://github.com/Rainan1010/Gemielle); thông tin
giấy phép nằm trong [`THIRD-PARTY-NOTICES.md`](./THIRD-PARTY-NOTICES.md).

Mã nguồn của dự án dùng giấy phép [MIT](../LICENSE).

---

<div align="center">

Made with 💙 | Inspired by [Gemielle](https://github.com/Rainan1010/Gemielle) by Rainan1010

</div>
