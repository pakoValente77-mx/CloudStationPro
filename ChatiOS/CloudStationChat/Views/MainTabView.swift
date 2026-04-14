import SwiftUI

struct MainTabView: View {
    @EnvironmentObject var authService: AuthService
    @StateObject private var chatService = ChatService()
    @State private var selectedTab = 0
    
    var totalUnread: Int {
        chatService.unreadCounts.values.reduce(0, +)
    }
    
    var body: some View {
        TabView(selection: $selectedTab) {
            FunVasosView()
                .tabItem {
                    Image(systemName: "water.waves")
                    Text("Vasos")
                }
                .tag(0)
            
            StationMapView()
                .tabItem {
                    Image(systemName: "map.fill")
                    Text("Mapa")
                }
                .tag(1)
            
            StationDataView()
                .tabItem {
                    Image(systemName: "antenna.radiowaves.left.and.right")
                    Text("Estaciones")
                }
                .tag(2)
            
            RainfallReportView()
                .tabItem {
                    Image(systemName: "cloud.rain.fill")
                    Text("Lluvia")
                }
                .tag(3)
            
            ChatContainerView()
                .environmentObject(chatService)
                .tabItem {
                    Image(systemName: "bubble.left.and.bubble.right.fill")
                    Text("Chat")
                }
                .tag(4)
                .badge(totalUnread)
            
            OnlineUsersView()
                .environmentObject(chatService)
                .tabItem {
                    Image(systemName: "person.2.fill")
                    Text("En Línea")
                }
                .tag(5)
            
            SettingsView()
                .tabItem {
                    Image(systemName: "gear")
                    Text("Ajustes")
                }
                .tag(6)
        }
        .preferredColorScheme(.dark)
        .tint(.purple)
        .onAppear {
            chatService.configure(auth: authService)
            chatService.connect()
        }
        .onDisappear {
            chatService.disconnect()
        }
        .onChange(of: selectedTab) { newTab in
            if newTab == 4 {
                // Clear badge when entering chat tab
                NotificationService.shared.clearBadge()
            }
        }
        .onReceive(NotificationCenter.default.publisher(for: .chatNotificationTapped)) { notification in
            if let room = notification.object as? String {
                selectedTab = 4
                chatService.joinRoom(room)
            }
        }
    }
}
