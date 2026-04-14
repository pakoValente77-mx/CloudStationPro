import Foundation

struct StationMapData: Codable, Identifiable {
    var id: String { idStation }
    
    let idStation: String
    let dcpId: String?
    let nombre: String?
    let lat: Double
    let lon: Double
    let estatusColor: String?
    let valorActual: Float?
    let valorAuxiliar: Float?
    let variableActual: String?
    let ultimaTx: String?
    let isCfe: Bool
    let isGolfoCentro: Bool
    let hasCota: Bool
    let enMantenimiento: Bool
    
    enum CodingKeys: String, CodingKey {
        case idStation = "id"
        case dcpId
        case nombre
        case lat
        case lon
        case estatusColor
        case valorActual
        case valorAuxiliar
        case variableActual
        case ultimaTx
        case isCfe
        case isGolfoCentro
        case hasCota
        case enMantenimiento
    }
}
