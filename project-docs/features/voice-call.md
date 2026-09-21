# Kiến trúc Voice & Video Call

Tài liệu này đặc tả nền tảng kiến trúc phục vụ tính năng đàm thoại thời gian thực (Real-time Communication - RTC), tập trung vào phương thức điều hướng kết nối (Signaling) và xử lý luồng Media.

## 1. Mô hình Phân phối Dữ liệu (SFU Topology)
Hệ thống sử dụng **LiveKit (Selective Forwarding Unit - SFU)** thay vì kiến trúc Peer-to-Peer (P2P) truyền thống.
- **Vấn đề của P2P:** Trong các cuộc gọi nhóm, mô hình P2P yêu cầu mỗi thiết bị phải truyền tải độc lập luồng video/audio của mình tới toàn bộ các thành viên khác. Điều này dẫn đến giới hạn vật lý về băng thông tải lên (Upload bandwidth) và làm cạn kiệt tài nguyên CPU/Pin của thiết bị người dùng.
- **Giải pháp SFU:** Thiết bị chỉ đẩy luồng Media lên máy chủ LiveKit một lần duy nhất. Máy chủ sẽ đóng vai trò trung tâm phân phối, định tuyến và tối ưu hóa luồng dữ liệu (ví dụ: tự động giảm độ phân giải video dựa trên băng thông tải xuống của người nhận) trước khi truyền đến các thành viên khác. Kiến trúc này đảm bảo khả năng mở rộng (Scalability) ổn định cho các phòng thoại đông người.

## 2. Luồng Điều hướng Cuộc gọi (Call Signaling Lifecycle)
Hệ thống áp dụng kiến trúc **Lai (Hybrid Signaling)**, phân tách rành mạch giữa nghiệp vụ báo gọi và nghiệp vụ truyền tải luồng Media:

### A. Giai đoạn Tiền kết nối (Pre-call)
Các thao tác khởi tạo cuộc gọi, đổ chuông, hoặc từ chối hoàn toàn không kết nối đến LiveKit. Thay vào đó, Backend tạo một bản ghi trạng thái (`VoiceSession`) vào MongoDB và điều phối thông qua **WebSockets (SignalR)**.
- **Mục đích:** Tránh lãng phí tài nguyên hạ tầng máy chủ Media. Trình duyệt của người gọi và người nhận không cần khởi tạo kết nối WebRTC nặng nề nếu cuộc gọi bị từ chối, hoặc nếu người nhận không trực tuyến.
- **Xử lý trễ:** Nếu người nhận không phản hồi sau một khoảng thời gian quy định, một tiến trình giám sát ngầm ở Backend sẽ quét Cơ sở dữ liệu và tự động chuyển trạng thái cuộc gọi thành "Nhỡ" (Missed Call), đồng thời dập tắt chuông thông qua WebSockets.

### B. Giai đoạn Đàm thoại (Active Call)
Chỉ khi người nhận chính thức xác nhận "Chấp thuận" (Accept), Backend mới cập nhật trạng thái Database và tiến hành cấp phát Mã thông báo bảo mật (JWT Token) cho cả hai thiết bị.
Dựa vào Token này, trình duyệt của người dùng mới bắt đầu kết nối trực tiếp vào máy chủ LiveKit để đàm phán thông số truyền tải (SDP) và bắt đầu trao đổi luồng Audio/Video.

### C. Giai đoạn Hậu kết nối (Post-call & Webhooks)
Việc ngắt kết nối không chỉ phụ thuộc vào thao tác bấm "Tắt máy" từ Frontend (vốn không đáng tin cậy nếu thiết bị sập nguồn hoặc rớt mạng đột ngột).
Thay vào đó, máy chủ LiveKit đóng vai trò giám sát kết nối tầng vật lý (Transport layer). Khi phát hiện một người dùng mất kết nối, LiveKit sẽ gọi Webhook (HTTP POST) báo cáo về Backend. Backend tiếp nhận Webhook, cập nhật thời gian kết thúc vào MongoDB, và phát tín hiệu SignalR để đồng bộ giao diện cho người còn lại trong phòng.

## 3. Phân loại Nghiệp vụ Đàm thoại
- **Cuộc gọi Cá nhân (Direct Call):** Áp dụng nghiêm ngặt quy trình Signaling 3 bước (Ringing -> Accepted -> Joined) nhằm duy trì logic lịch sử cuộc gọi (Bỏ lỡ, Bị từ chối, Đã nghe máy).
- **Phòng thoại Nhóm (Voice Channel):** Hoạt động theo cơ chế phi trạng thái (Stateless). Thành viên có quyền truy cập sẽ chủ động xin Token và kết nối ngay lập tức vào LiveKit Room mà không yêu cầu thủ tục đổ chuông (Ringing) tới các thành viên khác.

## 4. Kiến trúc Giao diện Đa nhiệm (Floating UI)
Trạng thái đàm thoại (được quản lý bởi Zustand Store kết hợp với LiveKit Context) được thiết kế để neo tại tầng cao nhất của cấu trúc giao diện ứng dụng (Root DOM Layer).
Kiến trúc này cho phép người dùng tự do điều hướng (Navigate) giữa các phòng chat văn bản khác nhau, hoặc chuyển đổi giữa các phân hệ của hệ thống mà luồng kết nối âm thanh không bị gián đoạn, cung cấp trải nghiệm đa nhiệm (Picture-in-Picture) liền mạch.
