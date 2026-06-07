# Ứng dụng Giao tiếp Nhóm Đa phòng Thời gian Thực

> **Tên đề tài**: Xây dựng ứng dụng giao tiếp nhóm đa phòng với văn bản và thoại thời gian thực  
> **Mô tả ngắn**: "Light Discord" - Web app chat nhóm với kiến trúc phân cấp Group → Room, hỗ trợ text/voice real-time.

---

## 🎯 Mục tiêu dự án

- Xây dựng ứng dụng web cho phép tạo **nhóm (Group)** chứa nhiều **phòng (Room)** độc lập
- Hỗ trợ **chat văn bản** và **thoại thời gian thực** trong từng phòng
- Đảm bảo đồng bộ trạng thái tức thì (online, typing, voice status) không cần reload
- Áp dụng kiến trúc **Modular Monolith** với **3 lớp database**: PostgreSQL + MongoDB + Redis

---

## 📐 Phạm vi (Scope)

| Hạng mục | Chi tiết |
|----------|----------|
| **Loại hình** | Web Application (SPA) |
| **Thời gian** | 13 tuần |
| **Nhân sự** | 1 Developer (Full-stack) |
| **Ràng buộc** | Không AI/Bot, không Mobile App native, không video call/screen share |

### ✅ Trong scope (Must-have)

- Auth: Đăng ký, đăng nhập, JWT
- Group: Tạo, mời thành viên, quản lý cơ bản
- Room: Tạo phòng Text/Voice, phân loại, xóa
- Chat: Gửi tin text/ảnh, lịch sử theo phòng, typing indicator
- Voice: Join/leave, audio 2 chiều, mute/deafen, visualizer người đang nói
- Real-time: Online status, sync danh sách member, không cần F5
- Database: PostgreSQL (structured) + MongoDB (logs) + Redis (cache/state)

### ❕ Ngoài scope (Cần mở rộng sau này)

- Video call / Screen sharing
- Push notification mobile
- Phân quyền phức tạp (chỉ Admin/Member)
- Search tin nhắn nâng cao (chỉ query cơ bản)
- Reaction/emoji tùy chỉnh
- Quick Room auto-delete (tính năng mở rộng, làm nếu còn thời gian)

---

## 🛠️ Tech Stack

**Frontend:**

- React 18 + TypeScript
- Vite (build tool)
- Redux Toolkit / Zustand (state management)
- Socket.IO Client / SignalR Client
- Ant Design / MUI (UI library)
- Simple-Peer / WebRTC API (voice)

**Backend:**

- .NET 8 + ASP.NET Core Web API
- C# 12, async/await throughout
- SignalR (WebSocket management)
- Entity Framework Core (PostgreSQL)
- MongoDB.Driver (MongoDB)
- StackExchange.Redis (Redis)
- FluentValidation, AutoMapper

**Database:**

- PostgreSQL: Users, Groups, Rooms, Memberships, Roles
- MongoDB: Messages, ActivityLogs, CallMetadata
- Redis: Session cache, typing state, online status, SignalR backplane

**Infrastructure:** (để sau)

---

## 🏗️ Kiến trúc hệ thống

```
┌─────────────────┐
│    Frontend     │
│   (ReactTS)     │
└────┬────────────┘
     │ HTTPS / WSS
     ▼
┌─────────────────┐
│     Nginx       │
│ (Reverse Proxy) │
└────┬────────────┘
     │
     ▼
┌─────────────────┐
│    Backend      │
│    (.NET 8)     │
│                 │
│  ┌───────────┐  │
│  │   Auth    │  │
│  ├───────────┤  │
│  │   Group   │  │
│  ├───────────┤  │
│  │   Chat    │◄─┼── SignalR Hub
│  ├───────────┤  │
│  │   Voice   │  │
│  └───────────┘  │
└────┬────┬────┬──┘
     │    │    │
     ▼    ▼    ▼
┌────────┐ ┌────────┐ ┌────────┐
│PostgreSQL│ │MongoDB │ │ Redis  │
│(Users,  │ │(Messages│ │(Cache, │
│ Groups, │ │  Logs)  │ │ State, │
│ Rooms)  │ │         │ │Backplane│
└────────┘ └────────┘ └────────┘
```

### Modular Monolith Structure (Backend)

Sử dụng kiến trúc lai giữa **Modular Monolith** và **Vertical Slice**: Mọi logic từ đường dẫn API đến xử lý nghiệp vụ đều đóng gói gọn trong một module.

```
Backend/
├── Modules/                   # 📦 CHIA THEO NGHIỆP VỤ (Tính năng)
│   ├── Auth/
│   │   ├── Controllers/       # API Endpoints (Vd: Login, Register)
│   │   ├── Core/              # Entities, Interfaces, DTOs
│   │   └── Services/          # Business Logic
│   ├── Group/                 # (Tương tự Auth)
│   ├── Chat/
│   │   ├── Core/
│   │   ├── Hubs/              # SignalR Hub của riêng Chat
│   │   └── Services/
│   └── Voice/                 # Xử lý WebRTC Signaling
├── Infrastructure/            # 🔌 CẤU HÌNH HẠ TẦNG (Không chứa logic nghiệp vụ)
│   ├── Database/              # Chứa Postgres DbContext & Mongo Config
│   └── Cache/                 # Code cấu hình kết nối Redis
├── Shared/                    # 🤝 CODE DÙNG CHUNG CỦA TOÀN APP
│   ├── Exceptions/            # Custom Exceptions (NotFound, BadRequest...)
│   └── Middleware/            # Global Error Handler, JWT Validation
└── Program.cs                 # File khởi chạy duy nhất (Composition Root)
```

---

## 🚀 Quick Start (Local Development)

```bash
# 1. Clone repo
git clone <repo-url>
cd Solution

# 2. Start infrastructure (Docker)
docker-compose up -d postgres mongodb redis redis-broker

# 3. Backend setup
cd Backend
dotnet restore
dotnet ef database update  # PostgreSQL migration
dotnet run

# 4. Frontend setup
cd ../Frontend
npm install
npm run dev

# 5. Access
# Frontend: http://localhost:5173
# Backend API: http://localhost:5000
# Swagger: http://localhost:5000/swagger
```

---

## Testing Strategy

| Loại test | Công cụ | Phạm vi |
|-----------|---------|---------|
| Unit Test | xUnit + Moq | Services, Validators, Helpers |
| Integration Test | WebApplicationFactory + TestServer | API Endpoints, SignalR Hub |
| E2E Test | Playwright (optional) | User flow: login → join room → chat |
| Load Test | k6 / Artillery | SignalR connection concurrency, message throughput |

---

## Tài liệu liên quan

- `.agent/Agent.md` → Entry point cho AI Agent
- `.agent/rules/` → Luật hành vi cốt lõi (AI đọc đầu tiên)
- `.agent/behaviors/` → Knowledge base, hướng dẫn chi tiết
- `.agent/memory/` → Trạng thái làm việc hiện tại
- `.agent/skills/` → Kỹ năng chuyên biệt của AI
- `.agent/commands/` → Slash commands để tương tác
