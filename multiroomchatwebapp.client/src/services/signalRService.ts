import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';

type MessageCallback = (message: any) => void;
type PresenceCallback = (userId: string) => void;

/**
 * Lớp điều khiển trung tâm (Singleton) quản lý WebSocket (SignalR).
 *
 * @remarks
 * Tại sao dùng class Singleton thay vì React Hook/Context?
 * 1. Đảm bảo toàn bộ ứng dụng (kể cả hàm ngoài React) chỉ có ĐÚNG 1 kết nối mở.
 * 2. Tránh bị ngắt kết nối/tạo lại liện tục theo vòng đời component.
 */
class SignalRService {
  private connection: HubConnection | null = null;
  private messageListeners: Set<MessageCallback> = new Set();
  private isConnecting = false;

  /**
   * Khởi tạo và bắt đầu kết nối tới ChatHub.
   *
   * @param accessToken - Token lấy từ RAM (AuthContext)
   * @remarks
   * SignalR không tự đính kèm được HttpOnly Cookie khi thiết lập Websocket
   * do bản chất của fetch API cho WebSocket handshake ở vài trình duyệt.
   * Nên ta phải đính kèm accessToken qua property `accessTokenFactory`.
   * Backend đã được cấu hình (OnMessageReceived) đọc token này.
   */
  public async startConnection(accessToken: string): Promise<void> {
    // Nếu đang có kết nối rồi thì không mở thêm
    if (this.connection?.state === 'Connected' || this.isConnecting) return;

    this.isConnecting = true;
    try {
      this.connection = new HubConnectionBuilder()
        .withUrl('https://localhost:7222/hub/chat', {
          accessTokenFactory: () => accessToken,
        })
        // Cấu hình tự động kết nối lại khi rớt mạng
        .withAutomaticReconnect()
        .configureLogging(LogLevel.Information)
        .build();

      // Đăng ký nghe sự kiện từ Server
      this.connection.on('ReceiveMessage', (msg) => {
        this.messageListeners.forEach(cb => cb(msg));
      });

      this.connection.on('UserIsOnline', (uid) => {
        console.log(`[SignalR] User connected: ${uid}`);
      });

      this.connection.on('UserIsOffline', (uid) => {
        console.log(`[SignalR] User disconnected: ${uid}`);
      });

      await this.connection.start();
      console.log('[SignalR] Connected successfully');
    } catch (err) {
      console.error('[SignalR] Connection failed: ', err);
    } finally {
      this.isConnecting = false;
    }
  }

  public async stopConnection(): Promise<void> {
    if (this.connection) {
      await this.connection.stop();
      this.connection = null;
      console.log('[SignalR] Disconnected');
    }
  }

  /**
   * Gửi tin nhắn qua Websocket thay vì gọi HTTP API để giảm độ trễ (low latency).
   * Note: Hàm này gọi hàm "SendMessage" trên C# ChatHub.
   */
  public async sendMessage(roomId: string, content: string): Promise<void> {
    if (this.connection?.state === 'Connected') {
      try {
        await this.connection.invoke('SendMessage', { roomId, content });
      } catch (err) {
        console.error('[SignalR] Lỗi khi gửi tin:', err);
        throw err;
      }
    } else {
      throw new Error('Chưa kết nối Websocket!');
    }
  }

  // ==== Quản lý các Component lắng nghe (Observer Pattern) ====
  
  public onMessageReceived(callback: MessageCallback) {
    this.messageListeners.add(callback);
    return () => this.messageListeners.delete(callback); // Trả về hàm cleanup
  }
}

// Export duy nhất 1 instance (Singleton Pattern)
export const signalRService = new SignalRService();
