# Kiến trúc Tổng thể (Modular Monolith) & Phân quyền (RBAC)

Tài liệu này đặc tả cách phân tách các luồng nghiệp vụ trong mã nguồn (Backend) và cơ chế phân cấp quyền hạn (Roles) của hệ thống.

## 1. Kiến trúc Modular Monolith
Thay vì xây dựng toàn bộ mã nguồn dồn cục (Spaghetti Code) hoặc xé quá nhỏ thành Microservices ngay từ đầu gây tốn kém hạ tầng, dự án áp dụng kiến trúc **Modular Monolith**.

Ứng dụng chạy trên một máy chủ (Instance) duy nhất để dễ triển khai, nhưng mã nguồn bên trong được bóc tách thành các Domain (Module) hoàn toàn độc lập:
- `Auth/`: Đảm nhiệm việc phát hành Session Cookie, Google OAuth và xác thực CSRF.
- `User/`: Quản lý hồ sơ người dùng, danh sách bạn bè và chặn (Block).
- `Group/` & `Room/`: Quản lý cấp phát máy chủ cộng đồng và phòng chat.
- `Chat/`: Chứa SignalR Hub và kiến trúc luồng tin nhắn qua Redis Streams.
- `Media/`: Quản lý tải tệp và tích hợp MinIO.
- `Voice/`: Cấp phát JWT Token cho LiveKit và xử lý Webhooks đàm thoại.

Các module giao tiếp với nhau qua các Interfaces (Dependency Injection) hoặc qua MediatR, đảm bảo tương lai có thể dễ dàng "rút" một module ra thành Microservice độc lập nếu hệ thống bị quá tải ở một tính năng cụ thể.

## 2. Tổ chức Cấu trúc Phân cấp (Hierarchy)
Hệ thống kết hợp cả 2 hình thái Chat phổ biến hiện nay: Đàm thoại cá nhân (như Messenger/Zalo) và Cộng đồng máy chủ (như Discord/Slack). Cấu trúc này được quy hoạch như sau:

- **Group (Cộng đồng):** Tương đương với một "Server" trên Discord. Nó là một vùng không gian có tên, có ảnh đại diện và chứa một danh sách thành viên khổng lồ (GroupMember).
- **Room (Phòng Chat):** Là đơn vị cốt lõi để giao tiếp. Một Room có thể là:
  - **Kênh (Channel):** Nằm bên trong một Group (Có thuộc tính `GroupId`). Kênh này thừa hưởng danh sách thành viên của Group đó.
  - **Trò chuyện trực tiếp (Direct Message - 1:1):** Không phụ thuộc vào bất kỳ Group nào (`GroupId` = null). Chỉ chứa chính xác 2 thành viên.

## 3. Cơ chế Phân quyền (Role-Based Access Control)
Quyền hạn của người dùng được xác định dựa trên vai trò (Role) của họ tại 2 cấp độ: Cấp độ Nhóm (Group) và Cấp độ Phòng (Room).

### Cấp độ Nhóm (Group Roles)
Được định nghĩa tại `GroupRole.cs`:
1. **`Owner` (Chủ sở hữu):** Có toàn quyền tối cao. Được phép xóa Group, chuyển quyền Owner cho người khác.
2. **`Admin` (Quản trị viên):** Được phép tạo/xóa các Room bên trong Group, đá (Kick) thành viên thường ra khỏi Group.
3. **`Member` (Thành viên):** Chỉ có quyền tham gia các kênh (Room) công khai và gửi tin nhắn.

### Cấp độ Phòng (Room Roles)
Được định nghĩa tại `RoomRole.cs`:
1. **`Admin` (Quản lý phòng):** Có quyền ghim tin nhắn, xóa tin nhắn rác của người khác để điều phối phòng. Hiện tại để đơn giản thì cấp độ này chưa phân biệt 
2. **`Member` (Thành viên phòng):** Chỉ có quyền đọc, gửi và chỉnh sửa/xóa tin nhắn do chính mình tạo ra.

### Tối ưu Hiệu năng Phân quyền (Permission Caching)
Việc kiểm tra "User A có quyền đăng ảnh vào Room B không" diễn ra liên tục trên mỗi gói tin. Thay vì truy vấn CSDL PostgreSQL (`GroupMember`, `RoomMember`) liên tục gây thắt cổ chai, hệ thống cung cấp các Service Bộ đệm:
- `IGroupPermissionsCache`
- `IRoomPermissionsCache`

Các service này lưu trạng thái phân quyền của User vào RAM (hoặc Redis) để các truy vấn Authorization diễn ra với độ trễ gần như bằng 0.
