import Foundation

struct StationData: Codable, Identifiable {
    let id: String
    let nombre: String
    let lat: Double?
    let lon: Double?
    let valorActual: Double?
    let variableActual: String
    let estatusColor: String
    let ultimaTx: String?
    let valorAuxiliar: Double?
}

struct StationHistoryEntry: Codable, Identifiable {
    var id: UUID { UUID() }
    let ts: String
    let valor: Double
    let variable: String
    
    var timestamp: Date {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return formatter.date(from: ts) ?? Date()
    }
}
