# Lưu trữ Đa mô hình (Hybrid Database Architecture)

Dự án áp dụng chiến lược **Hybrid Database (Polyglot Persistence)**, từ chối phương pháp "một cơ sở dữ liệu cho mọi bài toán". Thay vào đó, hệ thống phân tách và lưu trữ dữ liệu vào các loại Database chuyên biệt nhằm tối ưu hóa vòng đời, cấu trúc và yêu cầu truy xuất (Read/Write I/O) của từng nghiệp vụ.

## 1. PostgreSQL (Relational Database)
**Vai trò:** Lưu trữ dữ liệu có cấu trúc chặt chẽ, đòi hỏi tính toàn vẹn (Data Integrity) và hỗ trợ giao dịch (ACID Transactions) ở mức cao nhất.

**Giao tiếp:** Entity Framework Core (EF Core). 

**Lý do lựa chọn:** Dữ liệu hồ sơ người dùng, phân quyền và các mối quan hệ xã hội đòi hỏi các phép nối (JOIN) phức tạp để truy vấn. 

### Danh sách 13 Bảng (Tables)
Để xem chi tiết các thuộc tính, vui lòng tham khảo trực tiếp mã nguồn của các Entity Models:

1. **User** (`MultiRoomChatWebApp.Server/Modules/User/Core/Entities/User.cs`) - Lưu hồ sơ người dùng cốt lõi.
2. **UserBlock** (`MultiRoomChatWebApp.Server/Modules/User/Core/Entities/UserBlock.cs`) - Danh sách chặn người dùng (Block list).
3. **Friendship** (`MultiRoomChatWebApp.Server/Modules/User/Core/Entities/Friendship.cs`) - Quan hệ bạn bè 2 chiều (đã chuẩn hóa).
4. **FriendRequest** (`MultiRoomChatWebApp.Server/Modules/User/Core/Entities/FriendRequest.cs`) - Lời mời kết bạn 1 chiều.
5. **AuthSession** (`MultiRoomChatWebApp.Server/Modules/Auth/Core/Entities/AuthSession.cs`) - Phiên đăng nhập bảo mật cho kiến trúc BFF (HTTP-Only Cookie).
6. **ExternalLogin** (`MultiRoomChatWebApp.Server/Modules/Auth/Core/Entities/ExternalLogin.cs`) - Dữ liệu định danh từ bên thứ ba (Google OAuth).
7. **Group** (`MultiRoomChatWebApp.Server/Modules/Group/Core/Entities/Group.cs`) - Máy chủ cộng đồng.
8. **GroupMember** (`MultiRoomChatWebApp.Server/Modules/Group/Core/Entities/GroupMember.cs`) - Quan hệ N-N, phân quyền thành viên trong Group.
9. **Room** (`MultiRoomChatWebApp.Server/Modules/Room/Core/Entities/Room.cs`) - Phòng chat (1-1 hoặc thuộc Group).
10. **RoomMember** (`MultiRoomChatWebApp.Server/Modules/Room/Core/Entities/RoomMember.cs`) - Quan hệ N-N, phân quyền thành viên trong Room.
11. **ReadReceipt** (`MultiRoomChatWebApp.Server/Modules/Chat/Core/Entities/ReadReceipt.cs`) - Đánh dấu trạng thái "Đã xem" (tách riêng để tối ưu MVCC).
12. **MediaAsset** (`MultiRoomChatWebApp.Server/Modules/Media/Core/Entities/MediaAsset.cs`) - Metadata của các tệp tĩnh được lưu trữ.
13. **UserPresenceState** (`MultiRoomChatWebApp.Server/Modules/User/Core/Entities/UserPresenceState.cs`) - Ánh xạ trạng thái trực tuyến (Online/Offline) của thiết bị.

---

## 2. MongoDB (NoSQL Document Database)
**Vai trò:** Lưu trữ dữ liệu có tốc độ sinh ra lớn (Write-heavy), khối lượng khổng lồ và cấu trúc có thể thay đổi linh hoạt theo thời gian.

**Giao tiếp:** MongoDB.Driver.

**Lý do lựa chọn:** Khối lượng tin nhắn khổng lồ yêu cầu Sharding ngang. Cấu trúc Document linh hoạt cho phép nhúng (Embed) các sub-documents để truy xuất nguyên khối (Tránh JOIN).

### Danh sách Documents và Sub-documents

1. **Message** (`MultiRoomChatWebApp.Server/Modules/Chat/Core/Entities/Message.cs`) - Document gốc chứa nội dung tin nhắn.
   - *Sub-document:* **Attachment** (`MultiRoomChatWebApp.Server/Modules/Chat/Core/Entities/Attachment.cs`) - Tệp đính kèm trong tin.
   - *Sub-document:* **MessageReaction** (`MultiRoomChatWebApp.Server/Modules/Chat/Core/Entities/MessageReaction.cs`) - Biểu tượng cảm xúc (Emoji).
   - *Sub-document:* **MessageMeta** (`MultiRoomChatWebApp.Server/Modules/Chat/Core/Entities/MessageMeta.cs`) - Dữ liệu siêu liên kết hoặc trích xuất nhúng.
2. **VoiceSession** (`MultiRoomChatWebApp.Server/Modules/Voice/Core/Entities/VoiceSession.cs`) - Document gốc lưu trạng thái phiên đàm thoại (Ringing, Active).
   - *Sub-document:* **VoiceSessionParticipant** (`MultiRoomChatWebApp.Server/Modules/Voice/Core/Entities/VoiceSessionParticipant.cs`) - Trạng thái của từng cá nhân tham gia luồng thoại.

---

## 3. Redis (In-Memory Data Store)
**Vai trò:** Lớp lưu đệm tốc độ cao (Cache) và Điều phối luồng sự kiện (Message Broker).

**Lý do lựa chọn:** Hoạt động hoàn toàn trên RAM, cung cấp độ trễ siêu thấp (sub-millisecond). Vì dữ liệu trên Redis mang tính chất tạm thời, hệ thống không coi đây là kho lưu trữ bền vững (Entities) mà chỉ dùng cho các cơ chế logic trung chuyển.

### Các ứng dụng logic cốt lõi
- **Trạng thái Trực tuyến (Presence State):** Ánh xạ ConnectionId với UserId để quản lý trạng thái Online/Idle, tận dụng tính năng tự hủy (TTL) để đánh dấu Offline khi người dùng rớt mạng.
- **Hàng đợi Tin nhắn (Message Streams):** Vận hành hàng đợi Stream tốc độ cao làm vùng đệm cho tiến trình Chat Broker (như đã phân tích tại kiến trúc luồng chat).

---

## 4. MinIO (S3-Compatible Object Storage)
**Vai trò:** Quản lý và phân phối tệp tin nhị phân tĩnh (Static Files / Media).
**Lý do lựa chọn:** Việc lưu trữ tệp nhị phân (BLOB) vào DB là "Anti-pattern" gây phình to bộ nhớ. MinIO được thiết kế chuyên biệt để phân phối tệp và hỗ trợ Public/Private Buckets độc lập (`avatars` và `attachments`).
