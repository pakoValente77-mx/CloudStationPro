import SwiftUI

struct OnlineUsersView: View {
    @EnvironmentObject var chatService: ChatService
    
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
                        Text("No hay usuarios conectados en este momento")
                            .font(.caption)
                            .foregroundColor(.gray)
                    }
                    .frame(maxWidth: .infinity)
                    .padding(.top, 40)
                    .listRowBackground(Color.clear)
                } else {
                    ForEach(chatService.onlineUsers) { user in
                        HStack(spacing: 12) {
                            // Avatar
                            ZStack {
                                Circle()
                                    .fill(Color.purple.opacity(0.3))
                                    .frame(width: 40, height: 40)
                                Text(String(user.displayName.prefix(1)).uppercased())
                                    .font(.headline)
                                    .foregroundColor(.purple)
                            }
                            
                            VStack(alignment: .leading, spacing: 2) {
                                Text(user.displayName)
                                    .font(.body)
                                    .foregroundColor(.white)
                                HStack(spacing: 4) {
                                    Circle()
                                        .fill(.green)
                                        .frame(width: 6, height: 6)
                                    Text("En línea  \(user.platformIcons)")
                                        .font(.caption)
                                        .foregroundColor(.gray)
                                }
                            }
                            
                            Spacer()
                            
                            // DM button
                            Button(action: { startDM(with: user) }) {
                                Image(systemName: "paperplane.fill")
                                    .foregroundColor(.purple)
                            }
                        }
                        .padding(.vertical, 4)
                    }
                }
            }
            .navigationTitle("En Línea (\(chatService.onlineUsers.count))")
            .navigationBarTitleDisplayMode(.inline)
            .refreshable {
                chatService.loadOnlineUsers()
            }
        }
    }
    
    private func startDM(with user: OnlineUser) {
        let currentUser = chatService.currentUserName
        let sorted = [currentUser, user.userName].sorted { $0.localizedCaseInsensitiveCompare($1) == .orderedAscending }
        let dmRoom = "dm:\(sorted[0]):\(sorted[1])"
        // Navigate to Chat tab and join the DM room
        NotificationCenter.default.post(name: .chatNotificationTapped, object: dmRoom)
    }
}
