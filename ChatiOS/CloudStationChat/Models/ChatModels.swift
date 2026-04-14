import Foundation

struct ChatMessage: Identifiable, Codable, Equatable {
    let id: String
    let chatId: String
    let room: String
    let userId: String
    let userName: String
    let fullName: String?
    let message: String
    let timestamp: String
    let fileName: String?
    let fileUrl: String?
    let fileSize: Int64?
    let fileType: String?
    
    var displayName: String {
        fullName ?? userName
    }
    
    var date: Date? {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return formatter.date(from: timestamp) ?? ISO8601DateFormatter().date(from: timestamp)
    }
    
    var timeString: String {
        guard let date = date else { return "" }
        let fmt = DateFormatter()
        fmt.dateFormat = "HH:mm"
        return fmt.string(from: date)
    }
    
    var isBot: Bool { userName == "Centinela" }

    var hasFile: Bool {
        fileName != nil && !fileName!.isEmpty
    }
    
    var isImage: Bool {
        guard let ft = fileType?.lowercased() else { return false }
        return ft.hasPrefix("image/")
    }
    
    var fileSizeFormatted: String {
        guard let size = fileSize else { return "" }
        if size < 1024 { return "\(size) B" }
        if size < 1024 * 1024 { return String(format: "%.1f KB", Double(size) / 1024) }
        return String(format: "%.1f MB", Double(size) / (1024 * 1024))
    }
}

struct ChatRoom: Identifiable, Codable {
    let id: String
    let name: String
    let isDm: Bool
    
    init(id: String, name: String, isDm: Bool = false) {
        self.id = id
        self.name = name
        self.isDm = isDm
    }
}

struct OnlineUser: Identifiable, Codable {
    var id: String { userName }
    let userName: String
    let fullName: String?
    let platforms: [String]?
    
    var displayName: String {
        fullName ?? userName
    }
    
    var platformIcons: String {
        guard let platforms = platforms else { return "🌐" }
        return platforms.map { p in
            switch p.lowercased() {
            case "desktop": return "🖥️"
            case "ios", "android", "mobile": return "📱"
            default: return "🌐"
            }
        }.joined(separator: " ")
    }
}

struct LoginResponse: Codable {
    let token: String
    let usuario: String
    let nombre: String
    let roles: [String]
}

struct AuthToken {
    let token: String
    let userName: String
    let fullName: String
    let roles: [String]
}
