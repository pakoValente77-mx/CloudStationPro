import SwiftUI

struct ChatContainerView: View {
    @EnvironmentObject var chatService: ChatService
    @State private var showRoomPicker = false
    @State private var showOnlineUsers = false
    
    private var roomDisplayTitle: String {
        let room = chatService.currentRoom
        if room == "centinela" { return "🤖 Centinela" }
        if room.hasPrefix("dm:") {
            let parts = room.replacingOccurrences(of: "dm:", with: "").split(separator: ":")
            let other = parts.first(where: { String($0) != chatService.currentUserName }) ?? parts.first ?? ""
            return "✉ \(other)"
        }
        return room
    }
    
    var body: some View {
        NavigationStack {
            ChatView()
                .environmentObject(chatService)
                .navigationTitle(roomDisplayTitle)
                .navigationBarTitleDisplayMode(.inline)
                .toolbar {
                    ToolbarItem(placement: .topBarLeading) {
                        HStack(spacing: 6) {
                            Circle()
                                .fill(chatService.isConnected ? .green : .red)
                                .frame(width: 8, height: 8)
                            Text(chatService.isConnected ? "Conectado" : "Desconectado")
                                .font(.caption2)
                                .foregroundColor(.gray)
                        }
                    }
                    ToolbarItemGroup(placement: .topBarTrailing) {
                        Button(action: { showOnlineUsers = true }) {
                            HStack(spacing: 3) {
                                Image(systemName: "person.2.fill")
                                Text("\(chatService.onlineUsers.count)")
                                    .font(.caption2.bold())
                            }
                        }
                        Button(action: { showRoomPicker = true }) {
                            Image(systemName: "list.bullet")
                        }
                    }
                }
                .sheet(isPresented: $showRoomPicker) {
                    RoomPickerView()
                        .environmentObject(chatService)
                }
                .sheet(isPresented: $showOnlineUsers) {
                    OnlineUsersSheet()
                        .environmentObject(chatService)
                }
        }
    }
}

// Inline online users sheet for starting DMs from within chat
struct OnlineUsersSheet: View {
    @EnvironmentObject var chatService: ChatService
    @Environment(\.dismiss) var dismiss
    
    var body: some View {
        NavigationStack {
            List {
                if chatService.onlineUsers.isEmpty {
                    VStack(spacing: 12) {
                        Image(systemName: "person.slash")
                            .font(.system(size: 40))
                            .foregroundColor(.gray)
                        Text("Sin usuarios en línea")
                            .font(.headline)
                    }
                    .frame(maxWidth: .infinity)
                    .padding(.top, 40)
                    .listRowBackground(Color.clear)
                } else {
                    ForEach(chatService.onlineUsers) { user in
                        if user.userName != chatService.currentUserName {
                            Button(action: { startDM(with: user) }) {
                                HStack(spacing: 12) {
                                    ZStack {
                                        Circle()
                                            .fill(Color.purple.opacity(0.3))
                                            .frame(width: 36, height: 36)
                                        Text(String(user.displayName.prefix(1)).uppercased())
                                            .font(.headline)
                                            .foregroundColor(.purple)
                                    }
                                    VStack(alignment: .leading, spacing: 2) {
                                        Text(user.displayName)
                                            .font(.body)
                                            .foregroundColor(.white)
                                        HStack(spacing: 4) {
                                            Circle().fill(.green).frame(width: 6, height: 6)
                                            Text("En línea \(user.platformIcons)")
                                                .font(.caption)
                                                .foregroundColor(.gray)
                                        }
                                    }
                                    Spacer()
                                    Image(systemName: "paperplane.fill")
                                        .foregroundColor(.purple)
                                }
                            }
                        }
                    }
                }
            }
            .navigationTitle("En Línea (\(chatService.onlineUsers.count))")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .topBarTrailing) {
                    Button("Cerrar") { dismiss() }
                }
            }
        }
        .preferredColorScheme(.dark)
        .onAppear { chatService.loadOnlineUsers() }
    }
    
    private func startDM(with user: OnlineUser) {
        let currentUser = chatService.currentUserName
        let sorted = [currentUser, user.userName].sorted { $0.localizedCaseInsensitiveCompare($1) == .orderedAscending }
        let dmRoom = "dm:\(sorted[0]):\(sorted[1])"
        chatService.joinRoom(dmRoom)
        dismiss()
    }
}

struct RoomPickerView: View {
    @EnvironmentObject var chatService: ChatService
    @Environment(\.dismiss) var dismiss
    
    var body: some View {
        NavigationStack {
            List {
                Section("Salas") {
                    ForEach(chatService.rooms.filter { !$0.isDm }) { room in
                        Button(action: {
                            chatService.joinRoom(room.id)
                            dismiss()
                        }) {
                            HStack {
                                if room.id == "centinela" {
                                    Text("🤖")
                                } else {
                                    Image(systemName: "number")
                                        .foregroundColor(.purple)
                                }
                                Text(room.id == "centinela" ? "Centinela IA" : room.name)
                                    .foregroundColor(.white)
                                Spacer()
                                if let count = chatService.unreadCounts[room.id], count > 0 {
                                    Text("\(count)")
                                        .font(.caption2.bold())
                                        .foregroundColor(.white)
                                        .padding(.horizontal, 6)
                                        .padding(.vertical, 2)
                                        .background(Color.red)
                                        .clipShape(Capsule())
                                }
                                if room.id == chatService.currentRoom {
                                    Image(systemName: "checkmark")
                                        .foregroundColor(.purple)
                                }
                            }
                        }
                    }
                }
                
                let dmRooms = chatService.rooms.filter { $0.isDm }
                if !dmRooms.isEmpty {
                    Section("Mensajes Directos") {
                        ForEach(dmRooms) { room in
                            Button(action: {
                                chatService.joinRoom(room.id)
                                dismiss()
                            }) {
                                HStack {
                                    Image(systemName: "person.fill")
                                        .foregroundColor(.blue)
                                    Text(room.id.replacingOccurrences(of: "dm:", with: "").replacingOccurrences(of: ":", with: " ↔ "))
                                        .foregroundColor(.white)
                                    Spacer()
                                    if let count = chatService.unreadCounts[room.id], count > 0 {
                                        Text("\(count)")
                                            .font(.caption2.bold())
                                            .foregroundColor(.white)
                                            .padding(.horizontal, 6)
                                            .padding(.vertical, 2)
                                            .background(Color.red)
                                            .clipShape(Capsule())
                                    }
                                    if room.id == chatService.currentRoom {
                                        Image(systemName: "checkmark")
                                            .foregroundColor(.purple)
                                    }
                                }
                            }
                        }
                    }
                }
            }
            .navigationTitle("Salas")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .topBarTrailing) {
                    Button("Cerrar") { dismiss() }
                }
            }
        }
        .preferredColorScheme(.dark)
    }
}
