import Foundation

// MARK: - Rainfall Report Response

struct RainfallReportResponse: Codable {
    let titulo: String
    let tipo: String
    let periodoInicio: String
    let periodoFin: String
    let periodoInicioLocal: String
    let periodoFinLocal: String
    let generado: String
    let totalEstaciones: Int
    let estacionesConLluvia: Int
    let subcuencas: [SubcuencaReporte]
}

struct SubcuencaReporte: Codable, Identifiable {
    var id: String { subcuenca }
    let subcuenca: String
    let estaciones: [EstacionLluvia]
    let promedioMm: Double
}

struct EstacionLluvia: Codable, Identifiable {
    var id: String { idAsignado }
    let idAsignado: String
    let dcpId: String
    let nombre: String
    let cuenca: String
    let subcuenca: String
    let acumuladoMm: Double
    let horasConDato: Int
}
