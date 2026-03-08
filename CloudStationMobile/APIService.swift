import Foundation

@MainActor
class APIService: ObservableObject {
    @Published var stations: [StationData] = []
    @Published var isLoading = false
    @Published var errorMessage: String?
    
    // Cambia esto a la IP local de tu servidor si pruebas en un dispositivo real
    private let baseURL = "http://localhost:5215"
    
    func fetchStations(variable: String = "precipitación") async {
        isLoading = true
        errorMessage = nil
        
        guard let url = URL(string: "\(baseURL)/Map/GetMapData?variable=\(variable.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed) ?? "")&onlyCfe=true") else {
            errorMessage = "URL Inválida"
            isLoading = false
            return
        }
        
        do {
            let (data, _) = try await URLSession.shared.data(from: url)
            let decodedData = try JSONDecoder().decode([StationData].self, from: data)
            self.stations = decodedData
            isLoading = false
        } catch {
            errorMessage = "Error al cargar estaciones: \(error.localizedDescription)"
            isLoading = false
        }
    }
    
    func fetchHistory(stationId: String, variable: String) async -> [StationHistoryEntry] {
        guard let url = URL(string: "\(baseURL)/Map/GetStationHistory?stationId=\(stationId)&variable=\(variable.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed) ?? "")") else {
            return []
        }
        
        do {
            let (data, _) = try await URLSession.shared.data(from: url)
            return try JSONDecoder().decode([StationHistoryEntry].self, from: data)
        } catch {
            print("Error historial: \(error)")
            return []
        }
    }
}
