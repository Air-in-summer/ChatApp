# Quản lý Media & Lưu trữ Tệp tĩnh (Object Storage)

Tài liệu này đặc tả cơ chế quản lý tệp tin đa phương tiện (Hình ảnh, Video, Tệp đính kèm, Avatar) trong hệ thống thông qua dịch vụ lưu trữ tương thích S3 (như MinIO).

## 1. Lý do không lưu Media vào Database
Lưu trữ dữ liệu nhị phân (BLOB) trực tiếp vào PostgreSQL hay MongoDB là một Anti-pattern kinh điển, dẫn đến:
- Làm phình to dung lượng Database cực kỳ nhanh.
- Ăn mòn bộ nhớ RAM (do DB cố cache dữ liệu).
- Tăng thời gian sao lưu (Backup/Restore).

Do đó, toàn bộ tệp tĩnh trong dự án được bóc tách và giao cho **MinIO** - một Object Storage chuyên dụng tương thích 100% với giao thức AWS S3, cho phép truy xuất tốc độ cao và dễ dàng kết nối qua Mạng phân phối nội dung (CDN) ở môi trường Production.

## 2. Chiến lược phân mảnh Buckets (Public vs Private)
Hệ thống phân chia ranh giới bảo mật ngay từ tầng Storage bằng 2 Bucket độc lập:

1. **`avatars` (Public Bucket):**
   - **Chức năng:** Lưu trữ ảnh đại diện của người dùng (User Avatar) và biểu tượng của nhóm (Group Icon).
   - **Bảo mật:** Bucket này được gán chính sách (Policy) `PublicRead`. Trình duyệt có thể tải trực tiếp file bằng đường dẫn thẳng (vd: `https://minio.../avatars/user_1.png`) mà không cần xác thực.

2. **`attachments` (Private Bucket):**
   - **Chức năng:** Lưu trữ các tệp đính kèm trong phòng chat (Hình ảnh riêng tư, tài liệu, video).
   - **Bảo mật:** Bị khóa hoàn toàn. Không ai có quyền truy cập trực tiếp từ Internet. Muốn tải được file, hệ thống áp dụng cơ chế **Presigned URL**.

## 3. Cơ chế Truy xuất File Riêng tư (Presigned URL)
Việc sử dụng Access Token (Bearer) hay Cookie HTTP-Only để truy xuất file nhị phân thường không khả thi nếu dùng thẻ `<img src="...">` (vì trình duyệt không tự đính kèm Bearer Token vào thẻ img). 

Thay vào đó, dự án dùng giải pháp **Presigned URL**:
1. Frontend gọi API Backend (được bảo vệ bằng BFF Session) xin quyền xem 1 tệp đính kèm có Id là `X`.
2. Backend kiểm tra quyền lợi (Authorization) của User: *Người dùng có đang là thành viên của phòng chat chứa tệp `X` này không?*
3. Nếu hợp lệ, Backend dùng `SecretKey` của S3 để ký sinh ra một đường link (Presigned URL). Đường link này chứa chữ ký số (Signature) và có hạn sử dụng rất ngắn (ví dụ: 15 phút).
4. Frontend nhận URL này và gắn vào thẻ `<img src="...">`. Trình duyệt sẽ tải ảnh thành công từ MinIO thông qua link đã ký. Hết 15 phút, link tự động vô hiệu hóa để chống rò rỉ.

## 4. Vòng đời Tệp đính kèm Tin nhắn (Lifecycle)
Để tránh tình trạng "rác" không gian lưu trữ (Tình huống người dùng tải ảnh lên nhưng lại tắt trình duyệt không bấm gửi), hệ thống áp dụng vòng đời 3 bước chặt chẽ:

1. **Upload (Trạng thái `Pending`):** 
   - Người dùng tải ảnh lên giao diện. Backend lập tức lưu file vào MinIO và ghi một bản ghi vào PostgreSQL (`MediaAsset` table) với trạng thái `Pending` (Chưa được gắn với tin nhắn nào).
2. **Gửi tin (Trạng thái `Reserved` / `Attached`):** 
   - Khi người dùng bấm "Gửi tin nhắn", Frontend đính kèm danh sách `MediaId` vào gói tin. 
   - Chat Broker khi xử lý tin nhắn sẽ duyệt qua các `MediaId` này, chuyển trạng thái chúng sang `Reserved` (để tránh bị tin nhắn khác cướp mất) rồi cuối cùng là `Attached` (Đã chốt gắn liền với tin nhắn này).
3. **Dọn rác ngầm (Garbage Collection):** 
   - Hệ thống có một tiến trình chạy ngầm tên là `PendingMediaCleanupWorker`. Tiến trình này liên tục rà quét bảng `MediaAsset`. Bất kỳ file nào nằm ở trạng thái `Pending` quá lâu (thường là sau vài giờ) sẽ bị coi là rác và bị Worker xóa sổ hoàn toàn khỏi MinIO và Database để giải phóng dung lượng.
