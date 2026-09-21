# Kiến trúc Xác thực & Bảo mật (BFF Pattern)

Tài liệu này đặc tả cơ chế bảo vệ danh tính và dữ liệu người dùng, nhằm giảm thiểu các rủi ro bảo mật phổ biến ở tầng ứng dụng web (XSS, CSRF). 

> [!WARNING] 
>  Dù đã áp dụng các tiêu chuẩn an toàn, kiến trúc hệ thống hoàn toàn có thể vẫn tồn tại những nhược điểm hoặc lỗ hổng bảo mật chưa được người thực hiện dự án nhận diện đầy đủ. Nếu phát hiện bất kỳ sai sót nào trong thiết kế, rất mong nhận được sự góp ý để dự án tiếp tục hoàn thiện.

## 1. Mô hình Backend-For-Frontend (BFF)
Các ứng dụng Single Page Application (SPA) thông thường có xu hướng lưu trữ Access Token trực tiếp tại trình duyệt (Local Storage hoặc Session Storage). Quyết định thiết kế này bộc lộ rủi ro bảo mật nghiêm trọng trước các cuộc tấn công **XSS (Cross-Site Scripting)**: Nếu giao diện ứng dụng vô tình bị chèn mã độc (ví dụ thông qua các thư viện bên thứ ba), mã độc đó có toàn quyền truy xuất Local Storage, đánh cắp Token và chiếm quyền điều khiển tài khoản người dùng hoàn toàn.

Nhằm khắc phục triệt để lỗ hổng XSS, dự án áp dụng kiến trúc **BFF kết hợp HTTP-Only Cookie**, biến trình duyệt thành một môi trường đóng:
- **Cấp phát phi văn bản:** Sau quá trình định danh (đăng nhập tài khoản hoặc qua Google OAuth 2.0), Backend không trả về Token dưới dạng JSON. Thay vào đó, dữ liệu định danh (Session/Token) được mã hóa và đóng gói vào một Cookie.
- **Áp dụng cờ bảo mật (Security Flags):** Cookie này được Backend áp đặt các chính sách nghiêm ngặt nhất:
  - `HttpOnly`: Trình duyệt từ chối mọi lệnh truy xuất Cookie từ mã nguồn JavaScript (triệt tiêu hoàn toàn rủi ro XSS).
  - `Secure`: Dữ liệu định danh chỉ được phép luân chuyển trên đường truyền mã hóa (HTTPS).
- **Giao tiếp tự động:** Ở các phiên làm việc tiếp theo, trình duyệt sẽ tự động đính kèm Cookie định danh vào mỗi yêu cầu HTTP. Mã nguồn Frontend hoàn toàn không lưu trữ và không can thiệp vào vòng đời của Token.

## 2. Lớp phòng vệ chống giả mạo (CSRF Protection)
Bản chất tự động đính kèm của Cookie sinh ra một hệ lụy mới: Rủi ro tấn công **CSRF (Cross-Site Request Forgery)** — khi một trang web độc hại lừa trình duyệt gửi yêu cầu mạo danh người dùng hợp lệ.
Để bảo vệ hệ thống, tiến trình `BffCsrfProtectionMiddleware` được triển khai với hai lớp kiểm duyệt đối với mọi yêu cầu thay đổi trạng thái dữ liệu (POST, PUT, DELETE):

- **Xác thực nguồn gốc (Source-Origin Check):** Backend chủ động đối chiếu tiêu đề `Origin` hoặc `Referer` của mọi gói tin. Nếu yêu cầu xuất phát từ một tên miền không nằm trong danh sách cho phép (CORS Allowed Origins), luồng xử lý bị chặn đứng ngay lập tức.
- **Mã đồng bộ (Anti-Forgery Token):** Sau khi đăng nhập, hệ thống cấp phát một mã xác thực thứ cấp (CSRF Token) đi kèm. Mọi yêu cầu thay đổi dữ liệu từ Frontend bắt buộc phải đính kèm mã này trong tiêu đề HTTP (Header). Các trang web giả mạo dù có ép được trình duyệt đính kèm Cookie, nhưng do rào cản truy xuất chéo (Same-Origin Policy), chúng không thể lấy được mã CSRF Token để điền vào Header, dẫn đến yêu cầu bị máy chủ từ chối (HTTP 403 Forbidden).

## 3. Đặc tả luồng định danh WebSockets (SignalR)
Việc xác thực danh tính cho hệ thống WebSockets được thực thi ngay tại pha khởi tạo kết nối (HTTP Handshake) trước khi nâng cấp (Upgrade) lên giao thức WebSockets.
Dựa vào cơ chế trình duyệt tự động đính kèm HTTP-Only Cookie trong gói tin Handshake, Backend có thể định danh người dùng một cách minh bạch mà không yêu cầu Frontend đính kèm Token qua chuỗi URL (Query String) — một thao tác thiết kế tồi vốn tiềm ẩn rủi ro lộ lọt dữ liệu nhạy cảm thông qua nhật ký định tuyến (Server Access Logs).

## 4. Ngoại lệ ủy quyền (Webhooks)
Các luồng dữ liệu xuất phát từ máy chủ đối tác (ví dụ: tín hiệu giám sát rớt mạng từ máy chủ LiveKit SFU gửi về) không xuất phát từ trình duyệt và không sử dụng Cookie. Do đó, các Endpoint này (ví dụ: `/api/v1/voice/livekit/webhook`) được cấu hình ngoại lệ (Exempt) khỏi Middleware CSRF, chuyển sang sử dụng cơ chế xác thực máy chủ bằng Header mật mã (API Key/Signature).
