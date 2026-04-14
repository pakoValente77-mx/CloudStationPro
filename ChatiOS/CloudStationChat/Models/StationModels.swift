import Foundation

// MARK: - Station List

struct StationInfo: Codable, Identifiable, Hashable {
    var id: String { stationId }
    let stationId: String
    let name: String
    let lat: Double?
    let lon: Double?
    
    enum CodingKeys: String, CodingKey {
        case stationId = "id"
        case name
        case lat
        case lon
    }
}

// MARK: - Station Variables

struct StationVariable: Codable, Identifiable, Hashable {
    var id: String { variable }
    let variable: String
    let displayName: String
    let hasData: Bool
    let lastUpdate: String?
    let sensorId: String?
}

// MARK: - Analysis Request/Response

struct DataAnalysisRequest: Codable {
    let stationIds: [String]
    let variable: String
    let startDate: String
    let endDate: String
}

struct DataAnalysisResponse: Codable {
    let aggregationLevel: String?
    let variable: String?
    let startDate: String?
    let endDate: String?
    let series: [DataSeries]
}

struct DataSeries: Codable, Identifiable {
    var id: String { stationId }
    let stationId: String
    let stationName: String
    let minLimit: Double?
    let maxLimit: Double?
    let enMantenimiento: Bool?
    let dataPoints: [DataPoint]
}

struct DataPoint: Codable, Identifiable {
    var id: String { timestamp }
    let timestamp: String
    let value: Double?
    let isValid: Bool?
    
    var date: Date? {
        let formatters: [DateFormatter] = {
            let f1 = DateFormatter()
            f1.dateFormat = "yyyy-MM-dd'T'HH:mm:ssZ"
            let f2 = DateFormatter()
            f2.dateFormat = "yyyy-MM-dd'T'HH:mm:ss"
            return [f1, f2]
        }()
        for f in formatters {
            if let d = f.date(from: timestamp) { return d }
        }
        // Try ISO8601
        let iso = ISO8601DateFormatter()
        iso.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return iso.date(from: timestamp)
    }
    
    var timeString: String {
        guard let d = date else { return timestamp }
        let f = DateFormatter()
        f.dateFormat = "dd/MM HH:mm"
        return f.string(from: d)
    }
    
    var shortTime: String {
        guard let d = date else { return "" }
        let f = DateFormatter()
        f.dateFormat = "HH:mm"
        return f.string(from: d)
    }
}
