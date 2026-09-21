# Hướng dẫn Triển khai Máy chủ (Server Deployment) cơ bản

Tài liệu này trình bày tư tưởng và quy trình tổng quan để đưa ứng dụng lên môi trường Internet thực tế (VPS/Server) mà không đi sâu vào các cấu hình đặc thù của bất kỳ máy chủ cụ thể nào.

Lưu ý: Tài liệu này chỉ mô tả quy trình triển khai cơ bản dựa trên kinh nghiệm thực tế của tác giả với mô hình triển khai được sử dụng trong dự án. Đây không phải là tài liệu hướng dẫn DevOps chuyên sâu hay quy trình triển khai thực tế hoàn toàn. Người sử dụng nên tự tìm hiểu thêm và điều chỉnh các bước cấu hình, bảo mật, backup và vận hành tùy theo yêu cầu thực tế của hệ thống.

## 1. Mô hình Kiến trúc Triển khai

Dự án áp dụng mô hình **Single-Server Deployment** thông qua Docker Compose. Toàn bộ các dịch vụ được đóng gói thành các container và chạy chung trên một máy chủ (VPS) duy nhất để tiết kiệm chi phí, dễ vận hành, nhưng vẫn đảm bảo tính cô lập (Isolation).

- **Caddy (Reverse Proxy):** Đứng ngoài cùng (Public) để nhận toàn bộ lưu lượng truy cập từ Internet thông qua hai cổng 80 (HTTP) và 443 (HTTPS).
  - *Về HTTPS:* Caddy tích hợp sẵn giao thức ACME, tự động liên hệ với Let's Encrypt để xin cấp phát và gia hạn chứng chỉ SSL hợp lệ cho tên miền mà không cần sự can thiệp thủ công.
  - *Về Routing:* Sau khi mã hóa/giải mã SSL thành công ở Gateway, Caddy sẽ làm nhiệm vụ Proxy Ngược (Reverse Proxy), đẩy luồng dữ liệu thô (HTTP nội bộ) thẳng vào cổng `8080` của container `app`. Cần lưu ý cổng `8080` này chỉ được `expose` trong nội bộ mạng Docker để Caddy gọi tới, hoàn toàn không được publish ra Internet.
- **App Container:** Chứa mã nguồn Backend (.NET) và Frontend (đã được build tĩnh và phục vụ chung bởi Backend).
- **Hệ sinh thái Database (Docker Internal Network):** PostgreSQL, MongoDB, Redis (Cache & Broker) và MinIO. 4 dịch vụ CSDL này chỉ giao tiếp qua mạng nội bộ của Docker (Internal Network) và **không publish bất kỳ port nào ra Host OS**. Chúng hoàn toàn cách ly với Internet.
- **Voice/Video Server (Ngoại lệ):** Để giảm tải cho máy chủ chính, dịch vụ LiveKit (xử lý Media) được đẩy lên hạ tầng LiveKit Cloud thay vì tự host trên VPS.

## 2. Quy trình Triển khai Lần đầu (Initial Deployment)

Khi VPS vừa khởi tạo trắng, máy chủ cần được gia cố hạ tầng nền tảng (OS-level) nghiêm ngặt trước khi chạy bất kỳ ứng dụng nào:

1. **Gia cố Hệ điều hành & Phân quyền (SSH Hardening):**
   - Cập nhật hệ thống (OS Update) và đồng bộ múi giờ (Timezone) chuẩn để log ghi nhận chính xác.
   - Vô hiệu hóa khả năng đăng nhập bằng tài khoản `root` và tắt tính năng đăng nhập bằng Mật khẩu qua cổng SSH (Port 22). Thay vào đó, tạo một tài khoản vận hành riêng (ví dụ: `deploy`) và bắt buộc xác thực bằng **SSH Key**.
2. **Tối ưu Tài nguyên (Chống sập RAM):**
   - Do có tới 4 loại CSDL chạy đồng thời, hệ thống được cấp phát thêm một phân vùng RAM Ảo (Swap Space) trên ổ cứng. Điều này giúp giảm tình trạng tràn RAM (Out-Of-Memory) khi có lượng truy cập đột biến.
3. **Thiết lập Tường lửa (Firewall):**
   - Kích hoạt Firewall (UFW), đảm bảo hệ thống chỉ chấp nhận kết nối từ bên ngoài vào đúng 3 cổng cơ bản: 22 (SSH), 80 (HTTP) và 443 (HTTPS).
4. **Trỏ Tên miền (DNS Mapping):**
   - Thiết lập bản ghi (A Record) trên DNS để trỏ tên miền chính (Giao diện App) và tên miền phụ (Lưu trữ Media) về đúng IP của VPS.
5. **Cấu hình Biến môi trường (Secrets):**
   - Cài đặt Docker & Docker Compose. Sinh các chuỗi mật khẩu ngẫu nhiên độ khó cao cho các CSDL và đưa vào một tệp tin `.env` duy nhất trên máy chủ (Tuyệt đối không đẩy file này lên Git).
6. **Khởi động Nền móng (Cold Start):**
   - Khởi động nhóm container hạ tầng (PostgreSQL, MongoDB, Redis, MinIO) trước để khởi tạo các service, network và persistent volumes cần thiết, sau đó mới triển khai và khởi động container App.

## 3. Quản lý Dữ liệu & Sao lưu (Persistence & Backup)

- **Nguyên tắc Lưu trữ Dai dẳng (Persistent Volumes):** Toàn bộ 4 hệ thống CSDL (Postgres, Mongo, Redis AOF, MinIO) đều được liên kết (Mount) ra các thư mục vật lý (Volumes) trên ổ cứng của VPS. Do đó, việc stop/remove/recreate container không làm mất dữ liệu trong các persistent volumes.
- **Cơ chế Sao lưu (Backup & Recovery):** Dự án không cấu hình sao lưu tự động lên Cloud. Thay vào đó, người vận hành thực hiện sao lưu thủ công bằng cách vào container và chạy các lệnh đặc thù (`pg_dump` cho Postgres, `mongodump` cho Mongo) để xuất ra tệp tin nén. Sau đó, tệp backup này được tải về máy cá nhân lưu trữ. Nếu xảy ra sự cố, người vận hành sẽ phục hồi (Recovery) thủ công từ các tệp tin này.
  - *Lưu ý:* Đối với **Redis** và **MinIO**, dự án **hiện không có** công cụ hoặc kịch bản backup tự động nào. Dữ liệu Media và Cache/Stream hoàn toàn sống dựa vào các Volume vật lý. Nếu ổ cứng VPS hỏng hoặc mất Volume, các dữ liệu này sẽ bị mất.

## 4. Quy trình Đưa bản cập nhật lên Server (Update Flow)

Quy trình triển khai ứng dụng (App) được thiết kế theo hướng "Build Local - Run Anywhere". Image phiên bản cũ được giữ lại để có thể rollback khi bản cập nhật gặp lỗi:

1. **Build Image:** Biên dịch toàn bộ Frontend và Backend thành một Docker Image duy nhất ngay tại máy cá nhân.
2. **Đóng gói (Export):** Xuất Docker Image vừa build ra thành một tệp nén `.tar`.
3. **Vận chuyển (Transfer):** Dùng lệnh an toàn (`scp`) đẩy file nén `.tar` từ máy cá nhân lên VPS.
4. **Giải nén (Load):** Truy cập VPS qua SSH, nạp (load) file `.tar` trở lại thành Docker Image.
5. **Khởi chạy (Recreate):** Chỉnh cấu hình trỏ sang Image mới, sau đó ra lệnh Docker chỉ khởi động lại riêng container của App (Các CSDL hạ tầng vẫn chạy ngầm không gián đoạn).

## 5. Nguyên tắc An toàn & Bảo mật Bổ sung

- **Forwarded Headers:** Vì Caddy đứng trước làm Proxy, Backend phải được cấu hình để đọc đúng địa chỉ IP thật của người dùng và nhận diện giao thức HTTPS thông qua các cờ `X-Forwarded-*`.
- **Anti-CSRF & Cookie:** Môi trường thực tế bắt buộc bật các hàng rào bảo vệ BFF (Backend-For-Frontend) với Cookie cấu hình cờ `Secure=true`, `HttpOnly=true` và `SameSite=Lax`.
