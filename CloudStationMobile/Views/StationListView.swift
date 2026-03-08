import SwiftUI

struct StationListView: View {
    @ObservedObject var api: APIService
    @State private var searchText = ""
    
    var filteredStations: [StationData] {
        if searchText.isEmpty {
            return api.stations
        } else {
            return api.stations.filter { $0.nombre.localizedCaseInsensitiveContains(searchText) }
        }
    }
    
    var body: some View {
        NavigationView {
            ZStack {
                Color.cloudBackground.ignoresSafeArea()
                
                List(filteredStations) { station in
                    NavigationLink(destination: StationDetailView(api: api, station: station)) {
                        StationRow(station: station)
                    }
                    .listRowBackground(Color.white.opacity(0.05))
                }
                .listStyle(.insetGrouped)
                .searchable(text: $searchText, prompt: "Buscar estación...")
            }
            .navigationTitle("Estaciones")
            .toolbarColorScheme(.dark, for: .navigationBar)
        }
    }
}

struct StationRow: View {
    let station: StationData
    
    var body: some View {
        HStack(spacing: 15) {
            Circle()
                .fill(statusColor(station.estatusColor))
                .frame(width: 12, height: 12)
            
            VStack(alignment: .leading, spacing: 4) {
                Text(station.nombre)
                    .font(.system(size: 16, weight: .semibold))
                    .foregroundColor(.white)
                
                Text(station.variableActual.capitalized)
                    .font(.system(size: 12))
                    .foregroundColor(.white.opacity(0.6))
            }
            
            Spacer()
            
            Text(station.valorActual != nil ? String(format: "%.1f", station.valorActual!) : "N/A")
                .font(.system(size: 18, weight: .bold, design: .monospaced))
                .foregroundColor(.brandBlue)
        }
        .padding(.vertical, 8)
    }
    
    func statusColor(_ color: String) -> Color {
        switch color.uppercased() {
        case "VERDE": return .successGreen
        case "AMARILLO": return .warningYellow
        case "ROJO": return .criticalRed
        default: return .gray
        }
    }
}
