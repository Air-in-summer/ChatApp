# MultiRoomChatWebApp

MultiRoomChatWebApp là ứng dụng chat web thời gian thực theo mô hình nhóm nhiều phòng: người dùng có thể đăng ký/đăng nhập, kết bạn, tạo nhóm, tạo phòng text/voice, nhắn tin, gửi media và gọi voice/direct call qua LiveKit.

Project hiện là monorepo full-stack:

- Frontend: React + TypeScript + Vite.
- Backend: ASP.NET Core Web API + SignalR.
- Dữ liệu: PostgreSQL, MongoDB, Redis, Redis Streams.
- Media/voice: S3-compatible storage/MinIO và LiveKit.

> Lưu ý bảo mật: ứng dụng dùng BFF session cookie + CSRF. Browser không lưu bearer access token cho API chính.

## Tính năng chính

- Xác thực bằng email/password, Google OAuth, BFF session cookie, CSRF protection và session cleanup.
- Hồ sơ người dùng, tìm kiếm người dùng, kết bạn, chặn người dùng và trạng thái online/offline.
- Nhóm/phòng theo mô hình Group -> Room, hỗ trợ room text, room voice, DM, room private, mã mời, role Owner/Admin/Member, chuyển quyền sở hữu, kick/leave/delete group.
- Chat realtime qua SignalR: gửi tin, lịch sử tin nhắn, typing indicator, read receipt, sửa/xóa tin, reaction và ghim tin nhắn.
- Chat broker dựa trên Redis Streams: tách luồng admission, persistence, delivery, retry/dead-letter và idempotency.
- Lưu trữ message/media: MongoDB cho message, PostgreSQL cho dữ liệu quan hệ, MinIO/S3-compatible storage cho avatar, icon nhóm và attachment.
- Voice qua LiveKit: voice channel trong group, direct call trong DM, mic/camera/screen share, floating call UI và xử lý missed call.
- Production/demo compose với Caddy reverse proxy, app container, PostgreSQL, MongoDB, Redis, Redis broker và MinIO.

## Kiến trúc tổng quan

```text
React/Vite SPA
  |  HTTPS + BFF cookie + CSRF
  |  SignalR WebSocket (/hub/chat)
  v
ASP.NET Core API
  |-- Auth/User/Group/Room modules
  |-- Chat module + SignalR hub
  |-- Media module + S3-compatible storage
  |-- Voice module + LiveKit token/session
  |
  |-- PostgreSQL: users, sessions, groups, rooms, memberships, read receipts
  |-- MongoDB: messages, attachments, voice session records
  |-- Redis: cache, presence, read receipt buffer
  `-- Redis Streams: durable chat broker

LiveKit handles browser media transport directly.
Caddy fronts the production/demo app and media endpoint.
```

## Tech stack

| Phần | Công nghệ |
| --- | --- |
| Frontend | React 19, TypeScript 5.9, Vite 8, React Router 7, Zustand, Axios, SignalR client, LiveKit client |
| Backend | .NET 8, ASP.NET Core, SignalR, EF Core/Npgsql, MongoDB.Driver, StackExchange.Redis, MediatR, FluentValidation, Serilog |
| Database/cache | PostgreSQL 16, MongoDB 7, Redis 7 |
| Media/voice | MinIO hoặc S3-compatible storage, LiveKit |
| Deploy | Docker multi-stage build, `compose.demo.yml`, Caddy |

## Cấu trúc repo

```text
.
|-- MultiRoomChatWebApp.Server/        # ASP.NET Core API + SignalR
|   |-- Modules/
|   |   |-- Auth/
|   |   |-- Chat/
|   |   |-- Group/
|   |   |-- Media/
|   |   |-- Notification/
|   |   |-- Room/
|   |   |-- User/
|   |   `-- Voice/
|   |-- Infrastructure/Database/       # DbContext + EF migrations
|   |-- Shared/                        # middleware, options, exceptions
|   |-- Dockerfile
|   `-- Program.cs
|-- multiroomchatwebapp.client/        # React/Vite SPA
|   |-- src/api/
|   |-- src/components/
|   |-- src/context/
|   |-- src/hooks/
|   |-- src/pages/
|   |-- src/services/
|   |-- src/store/
|   `-- src/types/
|-- deploy/migrations/                 # SQL migration artifact cho deploy
|-- compose.demo.yml                   # Production/demo compose
|-- Caddyfile                          # Reverse proxy cho app/media
|-- .env.example                       # Template biến môi trường production/demo
`-- MultiRoomChatWebApp.sln
```

Một số file dùng cho máy local như `appsettings*.json`, `.env`, `docker-compose.yml`, `livekit.yaml`, build output, log và tài liệu nội bộ đang được ignore để tránh đẩy nhầm secret hoặc artifact lên GitHub.

## Yêu cầu môi trường

- .NET SDK 8.
- Node.js 22 và npm.
- Docker/Docker Compose nếu chạy hạ tầng bằng container.
- PostgreSQL, MongoDB, Redis, Redis broker, MinIO/S3-compatible storage.
- LiveKit local hoặc LiveKit Cloud.
- Tuỳ chọn: `dotnet-ef` nếu cần chạy EF migrations từ CLI.

## Chạy local development

### 1. Cài dependency

```bash
dotnet restore MultiRoomChatWebApp.sln

cd multiroomchatwebapp.client
npm ci
```

### 2. Chuẩn bị hạ tầng local

Backend cần các service sau:

| Service | Gợi ý port local |
| --- | --- |
| PostgreSQL | `5433 -> 5432` |
| MongoDB | `27018 -> 27017` |
| Redis cache/presence | `6379` |
| Redis broker | `6380` |
| MinIO/S3 API | `9000` |
| MinIO Console | `9001` |
| LiveKit | `7880`, `7881`, UDP `50000-50100` |

Bạn có thể tự dựng bằng Docker Compose local, Docker run, service có sẵn trên máy, hoặc LiveKit Cloud. Miễn là connection string trong bước tiếp theo trỏ đúng.

### 3. Tạo cấu hình backend local

Tạo file:

```text
MultiRoomChatWebApp.Server/appsettings.Development.json
```

File này đã được `.gitignore` ignore. Không commit secret thật. Ví dụ tối giản:

```json
{
  "ConnectionStrings": {
    "PostgreSQL": "Host=127.0.0.1;Port=5433;Database=ChatAppDB;User Id=<user>;Password=<password>",
    "MongoDB": "mongodb://<user>:<password>@127.0.0.1:27018",
    "Redis": "127.0.0.1:6379",
    "ChatBrokerRedis": "127.0.0.1:6380"
  },
  "Auth": {
    "Bff": {
      "Enabled": true,
      "RequireCsrf": true
    }
  },
  "Authentication": {
    "Google": {
      "ClientId": "",
      "ClientSecret": "",
      "CallbackPath": "/api/auth/google/callback",
      "FrontendCallbackUrl": "https://localhost:5173/oauth/callback"
    }
  },
  "Cors": {
    "AllowedOrigins": [
      "https://localhost:5173",
      "http://localhost:5173"
    ]
  },
  "LiveKit": {
    "Host": "ws://localhost:7880",
    "ApiKey": "<livekit-api-key>",
    "ApiSecret": "<livekit-api-secret>"
  },
  "MediaStorage": {
    "Provider": "S3",
    "Endpoint": "http://127.0.0.1:9000",
    "PublicEndpoint": "http://127.0.0.1:9000",
    "AccessKey": "<minio-access-key>",
    "SecretKey": "<minio-secret-key>",
    "PublicBucket": "chatapp-public-media",
    "PrivateBucket": "chatapp-private-media",
    "ForcePathStyle": true
  }
}
```

### 4. Apply database migration

```bash
dotnet tool install --global dotnet-ef
dotnet ef database update --project MultiRoomChatWebApp.Server
```

Nếu máy đã có `dotnet-ef`, bỏ qua lệnh install.

### 5. Chạy backend

```bash
dotnet run --project MultiRoomChatWebApp.Server
```

Backend dev thường chạy HTTPS ở `https://localhost:7222` và Swagger có ở `/swagger` khi môi trường là Development.

### 6. Chạy frontend

```bash
cd multiroomchatwebapp.client
npm run dev
```

Frontend chạy ở:

```text
https://localhost:5173
```

Vite dev server proxy:

- `/api/*` -> backend.
- `/hub/*` -> backend SignalR.

Mặc định proxy trỏ tới `https://localhost:7222`. Có thể đổi bằng biến:

```bash
VITE_DEV_BACKEND_TARGET=https://localhost:<backend-port>
```

## Lệnh phát triển thường dùng

```bash
# Build toàn solution
dotnet build MultiRoomChatWebApp.sln

# Build frontend production bundle
cd multiroomchatwebapp.client
npm run build

# Lint frontend
cd multiroomchatwebapp.client
npm run lint
```

Hiện solution chỉ gồm project backend và frontend; chưa có test project tự động được track trong solution.

## API và realtime endpoints

| Endpoint | Mục đích |
| --- | --- |
| `/api/auth/*` | Register, login, logout, session, CSRF, Google OAuth |
| `/api/v1/users/*` | Hồ sơ, đổi mật khẩu, tìm kiếm user |
| `/api/v1/users/relationships/*` | Bạn bè, lời mời kết bạn, block, presence |
| `/api/v1/groups/*` | Nhóm, invite, members, roles, ownership, room trong group |
| `/api/v1/rooms/*` | Room của user và direct message |
| `/api/v1/chat/*` | Lịch sử tin, edit/delete/reaction/pin |
| `/api/v1/media/*` | Avatar, icon group, attachment, signed URL/content |
| `/api/v1/voice/*` | LiveKit token, voice channel, direct call session |
| `/hub/chat` | SignalR hub cho chat/presence/typing/read receipt/call events |
| `/health/chat-broker` | Healthcheck Redis Streams chat broker |

## Production/demo deploy

Các file đã được track cho production/demo:

- `.env.example`: template biến môi trường, không chứa secret thật.
- `compose.demo.yml`: compose stack app + Caddy + PostgreSQL + MongoDB + Redis + Redis broker + MinIO.
- `Caddyfile`: reverse proxy app domain và media domain.
- `MultiRoomChatWebApp.Server/Dockerfile`: build React frontend, publish .NET backend và serve SPA cùng origin từ ASP.NET Core.

Quy trình tổng quát:

```bash
# Build image từ root repo
docker build -f MultiRoomChatWebApp.Server/Dockerfile -t multiroomchat:<tag> .

# Trên server, tạo .env từ template rồi điền secret thật
cp .env.example .env

# Chạy stack demo/production
docker compose -f compose.demo.yml --env-file .env up -d
```

Dockerfile production cố ý loại `appsettings*.json` và `.env*` khỏi publish output. Runtime production nhận cấu hình qua environment variables.

## Checklist trước khi push GitHub

- Chỉ commit README, source code, migration và template an toàn.
- Không commit `.env`, `appsettings*.json`, secret LiveKit/Google/MinIO, log, build output, local Docker Compose hoặc file tài liệu nội bộ nếu chúng chứa thông tin riêng.
- Không dùng `git add -f` với file đang bị ignore nếu chưa rà soát kỹ.
- Kiểm tra nhanh trước khi commit:

```bash
git status --short --ignored
git ls-files | rg -i "(\.env|appsettings|secret|credential|token|\.pem|\.key|\.pfx)"
```

Nếu secret từng bị commit vào lịch sử Git, hãy rotate secret đó trước khi public repo.
