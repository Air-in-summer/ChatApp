# Luồng hoạt động Thời gian thực (Real-time)

Tài liệu này đặc tả cách hệ thống MultiRoomChatWebApp vận hành các luồng dữ liệu thời gian thực thông qua WebSockets. 

## 1. Đặc thù bài toán Real-time của hệ thống
Hệ thống kết hợp cả mô hình đàm thoại Nhóm (Group chia thành nhiều Room) và Nhắn tin trực tiếp (1-1 DM). Yêu cầu kỹ thuật cốt lõi là phải đồng bộ trạng thái liên tục và chọn lọc:
- **Phân phối chính xác:** Tin nhắn mới chỉ được tải tự động cho những người đang mở đúng phòng chat đó, nhưng những người dùng khác trong nhóm hoặc bạn bè vẫn phải nhận được sự kiện để cập nhật thông báo (badge chưa đọc).
- **Đồng bộ trạng thái phụ:** Các tín hiệu như "đang gõ", "đã xem", hay trạng thái trực tuyến (online/offline) phải phản hồi với độ trễ thấp nhất.
- **Tiết kiệm tài nguyên:** Thay vì sử dụng cơ chế Polling liên tục tạo gánh nặng cho máy chủ, ứng dụng duy trì một kết nối WebSocket bền vững để truyền tải dữ liệu hai chiều.

## 2. Đặc tả kỹ thuật các luồng tương tác

Dưới đây là cơ chế vận hành của các tính năng cốt lõi, không sa đà vào tên gọi API mà tập trung vào luồng chảy của dữ liệu:

### A. Luồng gửi và phân phối tin nhắn
1. **Tiếp nhận:** Khi người dùng gửi tin nhắn, Frontend truyền dữ liệu qua đường ống WebSocket. Server giải mã danh tính người gửi và kiểm tra quyền truy cập (vd: có bị chặn không, có ở trong phòng không).
2. **Xử lý bất đồng bộ:** Thay vì bắt người dùng đợi Server ghi vào Database, Server đóng gói tin nhắn đẩy vào hàng đợi Redis (Chat Broker) và phản hồi "thành công" ngay lập tức để giao diện không bị khựng.
3. **Lưu trữ ngầm:** Một tiến trình ngầm (Background Worker) liên tục lấy tin nhắn từ Redis để ghi vào MongoDB.
4. **Phân phối:** Ngay sau khi ghi xong, Server đẩy nội dung tin nhắn đó về thiết bị của tất cả những người đang online có mặt trong phòng chat đó (hoặc người nhận trong chat 1-1).
5. **Hiển thị & Thông báo:** Lúc này, mã nguồn Frontend trên thiết bị sẽ làm nhiệm vụ phân loại:
   - Nếu người dùng đang mở đúng phòng chat đó: Tin nhắn được chèn thẳng vào màn hình chat để đọc ngay.
   - Nếu người dùng không mở phòng này: Frontend sẽ giấu tin nhắn đi và thay vào đó làm tăng bộ đếm tin chưa đọc (hiện chấm đỏ + số) ở thanh menu bên cạnh.

### B. Trải nghiệm "Đang gõ..." và "Đã xem"
- **Đang gõ (Typing):** Để tránh việc mỗi phím bấm đều gửi một tín hiệu làm nghẽn mạng, trình duyệt sử dụng cơ chế gom tín hiệu (Debounce). Nó chỉ báo cáo cho Server 1 lần sau mỗi vài giây gõ liên tục. Server nhận được sẽ thông báo cho các thành viên trong phòng hiện chữ "...đang gõ". Nếu người dùng dừng gõ quá lâu, một tín hiệu hủy sẽ được gửi để xóa dòng chữ này.
- **Đã xem (Read Receipt):** Khi người dùng cuộn chuột đến cuối cùng của màn hình chat, hệ thống chốt mốc thời gian và báo cho Server. Server đồng bộ thông tin này để các thiết bị khác tự động đánh dấu đã xem xuống dòng tin nhắn tương ứng.

### C. Quản lý trạng thái trực tuyến (Presence)
- **Tự động theo dõi:** Khi trình duyệt kết nối WebSockets thành công, Server tự động ghi danh người dùng vào bộ nhớ Redis là "Đang online", đồng thời báo cho danh sách bạn bè của họ để bật chấm xanh.
- **Ngắt kết nối:** Khi trình duyệt đóng, kết nối đứt, Server lập tức gỡ tên khỏi Redis và báo chấm xám cho bạn bè.
- **Dự phòng lỗi (Anti-ghosting):** Hệ thống có cơ chế để xử lý 2 trường hợp ngắt kết nối không lường trước (ví dụ: trình duyệt của người dùng mất WiFi đột ngột, hoặc máy chủ Server bị crash đột ngột nên không kịp chạy lệnh xóa).
  - Để giải quyết, nhãn "Online" trong Redis luôn bị gắn một bộ đếm thời gian tự hủy (TTL). Trình duyệt khi còn hoạt động phải liên tục gửi tín hiệu tim đập (Heartbeat) lên để gia hạn bộ đếm này.
  - Khi đứt mạng hoặc Server crash, tín hiệu Heartbeat ngừng lại. Bộ đếm TTL hết hạn khiến nhãn "Online" tự mất khỏi Redis. Một tiến trình dọn dẹp ngầm (Cleanup Worker) ở các máy chủ còn sống sẽ nhận diện được sự biến mất này và tự động phát tín hiệu báo offline thay cho máy chủ đã chết.

### D. Luồng điều hướng cuộc gọi (Call Signaling)
Dù luồng truyền hình ảnh và âm thanh đi qua máy chủ LiveKit, việc "đổ chuông" lại do WebSockets quản lý:
- Khi có người gọi, Server gửi lệnh qua WebSockets ép thiết bị của người nhận hiển thị giao diện đổ chuông.
- Nếu người nhận bấm "Từ chối", tín hiệu đi ngược về Server và truyền ngay sang máy người gọi để dập chuông, tránh việc khởi tạo phòng WebRTC vô ích gây tốn tài nguyên.

## 3. Lý do dùng Redis thay vì RabbitMQ / Kafka

Dự án lựa chọn hệ sinh thái Redis (Sử dụng cấu trúc Hash cho Presence và Streams cho Chat Broker) dựa trên các đánh giá thực tiễn sau:

1. **Tối ưu hóa nguồn lực hạ tầng:** Hệ thống vốn đã bắt buộc phải có Redis để làm Bộ nhớ đệm (Cache) và Quản lý trạng thái (Presence). Việc tận dụng luôn tính năng Streams của Redis làm Broker cho các luồng tin nhắn giúp nhóm phát triển không phải cài đặt, cấp phát tài nguyên và vận hành thêm một cụm cluster rườm rà.
2. **Đủ độ an toàn cho luồng Chat chính:** Đối với dữ liệu quan trọng là Nội dung tin nhắn (không được phép mất), tính năng Redis Streams cung cấp cơ chế Consumer Groups và ACK. Mức độ đảm bảo này là vừa đủ an toàn và tối ưu cho lưu lượng hiện tại, chưa đòi hỏi một công nghệ nặng nề hơn.

Hệ thống sẽ chỉ chuyển đổi sang Kafka hoặc RabbitMQ nếu phát sinh các giới hạn về tải hoặc yêu cầu nghiệp vụ phức tạp hơn:
- **Giới hạn bộ nhớ:** Redis lưu trữ dữ liệu chủ yếu trên RAM. Nếu khối lượng tin nhắn chờ xử lý (backlog) tăng đột biến vượt quá khả năng cấp phát RAM của hệ thống, kiến trúc sẽ cần nâng cấp lên Kafka để tận dụng khả năng lưu đệm dung lượng lớn trên ổ cứng (Disk-based storage).
- **Yêu cầu định tuyến phức tạp:** Nếu kiến trúc phần mềm sau này phân mảnh thành nhiều dịch vụ độc lập (ví dụ: bóc tách dịch vụ xử lý file đính kèm, dịch vụ lọc từ khóa), hệ thống sẽ cần một Message Queue hỗ trợ định tuyến chi tiết (như RabbitMQ Exchanges/Routing Keys) thay vì luồng xử lý tuyến tính hiện tại.
