import Foundation

// MARK: - API Response Models

struct FunVasosViewModel: Codable {
    let fechaInicio: String
    let fechaFin: String
    let presas: [FunVasosResumenPresa]
    let fechasDisponibles: [String]
}

struct FunVasosResumenPresa: Codable, Identifiable {
    var id: String { presa }
    let presa: String
    let ultimaElevacion: Float?
    let ultimoAlmacenamiento: Float?
    let totalAportacionesV: Float?
    let totalExtraccionesV: Float?
    let totalGeneracion: Float?
    let ultimaHora: Int
    let datos: [FunVasosHorario]
}

struct FunVasosHorario: Codable, Identifiable {
    var id: String { "\(ts)-\(presa)-\(hora)" }
    let ts: String
    let presa: String
    let hora: Int
    let elevacion: Float?
    let almacenamiento: Float?
    let diferencia: Float?
    let aportacionesQ: Float?
    let aportacionesV: Float?
    let extraccionesTurbQ: Float?
    let extraccionesTurbV: Float?
    let extraccionesVertQ: Float?
    let extraccionesVertV: Float?
    let extraccionesTotalQ: Float?
    let extraccionesTotalV: Float?
    let generacion: Float?
    let numUnidades: Int?
    let aportacionCuencaPropia: Float?
    let aportacionPromedio: Float?
}

struct CascadeResponse: Codable {
    let presas: [CascadePresa]
    let fecha: String
}

struct CascadePresa: Codable, Identifiable {
    var id: String { key }
    let key: String
    let name: String
    let currentElev: Float?
    let generation: Float?
    let activeUnits: Int?
    let almacenamiento: Float?
    let ultimaHora: Int?
    let fecha: String?
    let aportacionesV: Float?
    let extraccionesV: Float?
}
