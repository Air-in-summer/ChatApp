# 📬 Kiến trúc Chat Broker (Redis Streams)

Tài liệu này đặc tả cơ chế xử lý tin nhắn bất đồng bộ của ứng dụng, tập trung vào cách hệ thống điều tiết lưu lượng (Traffic) và bảo vệ Cơ sở dữ liệu trước các đợt tăng tải đột biến.

## 1. Vai trò của Chat Broker trong hệ thống
Thay vì để đường truyền WebSockets (SignalR) ghi dữ liệu trực tiếp thẳng vào Cơ sở dữ liệu (MongoDB), hệ thống chủ động chèn một lớp đệm (Broker) vào giữa. Mục đích cốt lõi nhằm giải quyết 2 bài toán thực tế:
- **Chống thắt cổ chai (Bottleneck):** Trong các khung giờ cao điểm, hàng ngàn người có thể tương tác cùng lúc. Nếu bắt máy chủ ghi dữ liệu đồng bộ xuống đĩa cứng của MongoDB, giới hạn tốc độ ổ cứng (Disk I/O) sẽ gây tắc nghẽn toàn bộ luồng kết nối của người dùng.
- **Tách biệt các tiến trình (Decoupling):** Bất kỳ thao tác thao tác ghi cơ sở dữ liệu nào cũng phát sinh độ trễ. Việc sử dụng Broker giúp tách rời tiến trình "tiếp nhận tin nhắn" và "lưu trữ vĩnh viễn" thành hai luồng độc lập, không cản trở lẫn nhau.

## 2. Luồng xử lý dữ liệu qua Broker

Quy trình vận hành chia làm 3 khâu. Điểm đột phá về kiến trúc ở đây là khâu (2) và (3) được thực thi song song thông qua cơ chế Consumer Groups của Redis, thay vì xử lý tuần tự:

### Giai đoạn 1: Tiếp nhận (Admission)
Khi yêu cầu gửi tin nhắn được chuyển tới, máy chủ không tương tác với MongoDB. Hệ thống lập tức đóng gói dữ liệu, đẩy vào hàng đợi Redis Streams (hoạt động trên bộ nhớ RAM) và trả về phản hồi thành công (HTTP 200/Ack). Thao tác trên RAM với độ trễ siêu thấp này giúp giao diện người dùng duy trì phản hồi tức thời, không bị gián đoạn ngay cả khi hệ thống chịu tải cao.

### Giai đoạn 2: Lưu trữ bền vững (Persistence Worker)
Hoạt động ngầm ở tầng nền, tiến trình `MessagePersistenceWorker` chịu trách nhiệm đồng bộ dữ liệu. Nó kéo một lô (batch) tin nhắn từ hàng đợi Redis, nhưng thực thi ghi tuần tự và độc lập từng bản ghi vào MongoDB thông qua lệnh Upsert.
Chiến lược này mang lại 2 lợi ích:
- Tránh rủi ro một bản ghi lỗi định dạng làm thất bại toàn bộ tiến trình ghi của cả lô dữ liệu.
- Chủ động điều tiết lưu lượng Disk I/O theo khả năng đáp ứng của phần cứng, ngăn ngừa tình trạng quá tải Database.

### Giai đoạn 3: Phân phối thời gian thực (Delivery Worker)
Hoàn toàn độc lập với luồng lưu trữ, tiến trình `MessageDeliveryWorker` cùng lúc theo dõi luồng sự kiện trên Redis. Ngay khi xuất hiện tin nhắn mới, hệ thống truy xuất danh sách người nhận và lập tức kích hoạt luồng phát sóng (Broadcast) dữ liệu qua kênh WebSockets.
Thiết kế chạy song song này giúp triệt tiêu hoàn toàn độ trễ I/O: Dù Database có đang trong trạng thái xử lý chậm, tin nhắn vẫn được truyền tải theo thời gian thực tới người nhận.

## 3. Khả năng phục hồi (Resilience & Dead-letter)

Kiến trúc Broker được thiết kế với cơ chế tự phục hồi nhằm duy trì tính toàn vẹn dữ liệu khi phát sinh sự cố:

- **Khi Cơ sở dữ liệu gián đoạn:** Nếu tiến trình Worker mất kết nối với MongoDB, thao tác ghi sẽ thất bại nhưng tin nhắn vẫn được bảo lưu an toàn trong hàng đợi Redis. Tiến trình ngầm sẽ tự động thử lại (Retry mechanism) theo chu kỳ. Trải nghiệm của người gửi không bị ảnh hưởng, và khi kết nối cơ sở dữ liệu được khôi phục, lượng tin nhắn tồn đọng (backlog) tự động được xử lý tiếp.
- **Xử lý bản ghi hỏng (Dead-letter Queue):** Trường hợp một tin nhắn gặp lỗi khiến thao tác lưu thất bại liên tục, việc Worker lặp lại thử nghiệm vô hạn sẽ gây tắc nghẽn luồng xử lý chung. Để giải quyết, sau khi vượt quá số lần thử nghiệm tối đa, hệ thống tự động loại bỏ bản ghi lỗi khỏi luồng chính và chuyển sang một hàng đợi chuyên biệt (Dead-letter Queue - DLQ). Nhờ đó có thể đảm bảo tiến trình xử lý cho các tin nhắn hợp lệ khác được vận hành thông suốt.
