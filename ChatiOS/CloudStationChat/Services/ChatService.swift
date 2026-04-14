import Foundation
import Combine
import SignalRClient

class ChatService: ObservableObject {
    @Published var messages: [ChatMessage] = []
    @Published var rooms: [ChatRoom] = []
    @Published var onlineUsers: [OnlineUser] = []
    @Published var currentRoom: String = "general"
    @Published var isConnected = false
    @Published var connectionError: String?
    @Published var unreadCounts: [String: Int] = [:]
    
    private var hubConnection: HubConnection?
    private var authService: AuthService?
    
    var currentUserName: String { authService?.userName ?? "" }
    
    func configure(auth: AuthService) {
        self.authService = auth
    }
    
    // MARK: - SignalR Connection
    
    func connect() {
        guard let auth = authService, !auth.token.isEmpty else { return }
        
        // Build URL with properly encoded token
        let encodedToken = auth.token.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed) ?? auth.token
        let hubUrl = "\(auth.serverUrl)/hubs/chat?access_token=\(encodedToken)&platform=ios"
        
        guard let url = URL(string: hubUrl) else {
            DispatchQueue.main.async {
                self.connectionError = "URL del hub inválida"
            }
            return
        }
        
        hubConnection = HubConnectionBuilder(url: url)
            .withLogging(minLogLevel: .debug)
            .withAutoReconnect()
            .withHttpConnectionOptions { options in
                options.accessTokenProvider = { return auth.token }
                options.skipNegotiation = true
                options.authenticationChallengeHandler = { session, challenge, completionHandler in
                    if let trust = challenge.protectionSpace.serverTrust {
                        completionHandler(.useCredential, URLCredential(trust: trust))
                    } else {
                        completionHandler(.performDefaultHandling, nil)
                    }
                }
            }
            .build()
        
        // Listen for messages (server sends a single ChatMessage object)
        hubConnection?.on(method: "ReceiveMessage") { [weak self] (msg: ChatMessage) in
            guard let self = self else { return }
            let isSelf = msg.userName == self.authService?.userName
            
            DispatchQueue.main.async {
                if msg.room == self.currentRoom {
                    self.messages.append(msg)
                } else {
                    // Track unread for other rooms
                    self.unreadCounts[msg.room, default: 0] += 1
                }
                
                // Show local notification for messages from others
                NotificationService.shared.showMessageNotification(
                    sender: msg.displayName,
                    message: msg.message,
                    room: msg.room,
                    isSelf: isSelf
                )
            }
            
            // Save current room so AppDelegate knows whether to display
            UserDefaults.standard.set(self.currentRoom, forKey: "current_chat_room")
        }
        
        // Online users update
        hubConnection?.on(method: "UserConnected") { [weak self] (user: String, fullName: String, platform: String) in
            self?.loadOnlineUsers()
        }
        
        hubConnection?.on(method: "UserDisconnected") { [weak self] (user: String, platform: String) in
            self?.loadOnlineUsers()
        }
        
        hubConnection?.on(method: "OnlineUsers") { [weak self] (usersJson: String) in
            if let data = usersJson.data(using: .utf8),
               let users = try? JSONDecoder().decode([OnlineUser].self, from: data) {
                DispatchQueue.main.async {
                    self?.onlineUsers = users
                }
            }
        }
        
        hubConnection?.delegate = self
        hubConnection?.start()
    }
    
    func disconnect() {
        hubConnection?.stop()
        hubConnection = nil
        DispatchQueue.main.async {
            self.isConnected = false
        }
    }
    
    // MARK: - Send Message
    
    func sendMessage(_ text: String) {
        guard !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return }
        hubConnection?.invoke(method: "SendMessage", arguments: [currentRoom, text]) { error in
            if let error = error {
                print("Error sending message: \(error)")
            }
        }
    }
    
    // MARK: - Rooms
    
    func joinRoom(_ room: String) {
        let oldRoom = currentRoom
        currentRoom = room
        unreadCounts[room] = 0
        UserDefaults.standard.set(room, forKey: "current_chat_room")
        
        // Add DM room to rooms list if not already present
        if room.hasPrefix("dm:") && !rooms.contains(where: { $0.id == room }) {
            rooms.append(ChatRoom(id: room, name: room, isDm: true))
        }
        
        hubConnection?.invoke(method: "LeaveRoom", arguments: [oldRoom]) { _ in }
        hubConnection?.invoke(method: "JoinRoom", arguments: [room]) { [weak self] error in
            if error == nil {
                self?.loadHistory(room: room)
            }
        }
    }
    
    // MARK: - REST Calls
    
    func loadRooms() {
        guard let auth = authService else { return }
        guard let url = URL(string: "\(auth.serverUrl)/Chat/Rooms") else { return }
        
        var request = URLRequest(url: url)
        request.setValue("Bearer \(auth.token)", forHTTPHeaderField: "Authorization")
        
        URLSession.shared.dataTask(with: request) { [weak self] data, _, _ in
            guard let data = data else { return }
            // Backend returns [{room, messageCount, lastActivity}]
            struct RoomInfo: Codable { let room: String }
            guard let roomInfos = try? JSONDecoder().decode([RoomInfo].self, from: data) else { return }
            DispatchQueue.main.async {
                self?.rooms = roomInfos.map { ChatRoom(id: $0.room, name: $0.room, isDm: $0.room.hasPrefix("dm:")) }
                // Add general if not present
                if !(self?.rooms.contains(where: { $0.id == "general" }) ?? false) {
                    self?.rooms.insert(ChatRoom(id: "general", name: "general"), at: 0)
                }
                if !(self?.rooms.contains(where: { $0.id == "centinela" }) ?? false) {
                    let idx = self?.rooms.lastIndex(where: { !$0.isDm }).map { $0 + 1 } ?? 1
                    self?.rooms.insert(ChatRoom(id: "centinela", name: "centinela"), at: idx)
                }
            }
        }.resume()
    }
    
    func loadHistory(room: String) {
        guard let auth = authService else { return }
        guard let url = URL(string: "\(auth.serverUrl)/Chat/History?room=\(room.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed) ?? room)") else { return }
        
        var request = URLRequest(url: url)
        request.setValue("Bearer \(auth.token)", forHTTPHeaderField: "Authorization")
        
        URLSession.shared.dataTask(with: request) { [weak self] data, _, _ in
            guard let data = data,
                  let msgs = try? JSONDecoder().decode([ChatMessage].self, from: data) else { return }
            DispatchQueue.main.async {
                self?.messages = msgs
            }
        }.resume()
    }
    
    func loadOnlineUsers() {
        guard let auth = authService else { return }
        guard let url = URL(string: "\(auth.serverUrl)/Chat/OnlineUsers") else { return }
        
        var request = URLRequest(url: url)
        request.setValue("Bearer \(auth.token)", forHTTPHeaderField: "Authorization")
        
        URLSession.shared.dataTask(with: request) { [weak self] data, _, _ in
            guard let data = data,
                  let users = try? JSONDecoder().decode([OnlineUser].self, from: data) else { return }
            DispatchQueue.main.async {
                self?.onlineUsers = users
            }
        }.resume()
    }
    
    // MARK: - File Upload
    
    func uploadFile(data fileData: Data, fileName: String, mimeType: String, completion: @escaping (Bool) -> Void) {
        guard let auth = authService else { completion(false); return }
        guard let url = URL(string: "\(auth.serverUrl)/Chat/UploadFile") else { completion(false); return }
        
        let boundary = UUID().uuidString
        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        request.setValue("Bearer \(auth.token)", forHTTPHeaderField: "Authorization")
        request.setValue("multipart/form-data; boundary=\(boundary)", forHTTPHeaderField: "Content-Type")
        
        var body = Data()
        // Room field
        body.append("--\(boundary)\r\n".data(using: .utf8)!)
        body.append("Content-Disposition: form-data; name=\"room\"\r\n\r\n".data(using: .utf8)!)
        body.append("\(currentRoom)\r\n".data(using: .utf8)!)
        // File field
        body.append("--\(boundary)\r\n".data(using: .utf8)!)
        body.append("Content-Disposition: form-data; name=\"file\"; filename=\"\(fileName)\"\r\n".data(using: .utf8)!)
        body.append("Content-Type: \(mimeType)\r\n\r\n".data(using: .utf8)!)
        body.append(fileData)
        body.append("\r\n--\(boundary)--\r\n".data(using: .utf8)!)
        
        request.httpBody = body
        
        URLSession.shared.dataTask(with: request) { _, response, _ in
            let ok = (response as? HTTPURLResponse)?.statusCode == 200
            DispatchQueue.main.async { completion(ok) }
        }.resume()
    }
}

// MARK: - HubConnectionDelegate

extension ChatService: HubConnectionDelegate {
    func connectionDidOpen(hubConnection: HubConnection) {
        DispatchQueue.main.async {
            self.isConnected = true
            self.connectionError = nil
        }
        // Register device token for push notifications
        NotificationService.shared.registerDeviceToken()
        
        hubConnection.invoke(method: "JoinRoom", arguments: [currentRoom]) { [weak self] _ in
            self?.loadHistory(room: self?.currentRoom ?? "general")
            self?.loadRooms()
            self?.loadOnlineUsers()
        }
        // Auto-join alertas-precipitacion room to receive alerts
        hubConnection.invoke(method: "JoinRoom", arguments: ["alertas-precipitacion"]) { _ in }
    }
    
    func connectionDidClose(error: Error?) {
        DispatchQueue.main.async {
            self.isConnected = false
            if let error = error {
                self.connectionError = error.localizedDescription
            }
        }
    }
    
    func connectionDidFailToOpen(error: Error) {
        DispatchQueue.main.async {
            self.isConnected = false
            self.connectionError = "No se pudo conectar: \(error.localizedDescription)"
        }
    }
    
    func connectionDidReconnect() {
        DispatchQueue.main.async {
            self.isConnected = true
            self.connectionError = nil
        }
        hubConnection?.invoke(method: "JoinRoom", arguments: [currentRoom]) { _ in }
    }
    
    func connectionWillReconnect(error: Error) {
        DispatchQueue.main.async {
            self.isConnected = false
        }
    }
}
