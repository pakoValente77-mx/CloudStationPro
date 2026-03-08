import SwiftUI

struct ContentView: View {
    @StateObject var api = APIService()
    
    var body: some View {
        TabView {
            MapView(api: api)
                .tabItem {
                    Label("Mapa", systemImage: "map.fill")
                }
            
            StationListView(api: api)
                .tabItem {
                    Label("Estaciones", systemImage: "list.bullet")
                }
        }
        .preferredColorScheme(.dark)
        .onAppear {
            Task {
                await api.fetchStations()
            }
        }
    }
}
