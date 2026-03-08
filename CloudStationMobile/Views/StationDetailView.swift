import SwiftUI
import Charts

struct StationDetailView: View {
    @ObservedObject var api: APIService
    let station: StationData
    @State private var history: [StationHistoryEntry] = []
    
    var body: some View {
        ZStack {
            Color.cloudBackground.ignoresSafeArea()
            
            ScrollView {
                VStack(alignment: .leading, spacing: 20) {
                    GlassView()
                        .frame(height: 120)
                        .overlay(
                            HStack {
                                VStack(alignment: .leading) {
                                    Text("Valor Actual")
                                        .font(.caption)
                                        .foregroundColor(.white.opacity(0.6))
                                    Text(station.valorActual != nil ? String(format: "%.2f", station.valorActual!) : "--")
                                        .font(.system(size: 40, weight: .black, design: .monospaced))
                                        .foregroundColor(.white)
                                }
                                Spacer()
                                Image(systemName: iconFor(station.variableActual))
                                    .font(.system(size: 40))
                                    .foregroundColor(.brandBlue)
                            }
                            .padding()
                        )
                    
                    Text("Historial (Últimas 24h)")
                        .font(.headline)
                        .foregroundColor(.white)
                        .padding(.top)
                    
                    GlassView()
                        .frame(height: 250)
                        .overlay(
                            Chart {
                                ForEach(history) { entry in
                                    LineMark(
                                        x: .value("Hora", entry.timestamp),
                                        y: .value("Valor", entry.valor)
                                    )
                                    .foregroundStyle(Color.brandBlue)
                                    .interpolationMethod(.catmullRom)
                                    
                                    AreaMark(
                                        x: .value("Hora", entry.timestamp),
                                        y: .value("Valor", entry.valor)
                                    )
                                    .foregroundStyle(LinearGradient(colors: [.brandBlue.opacity(0.3), .clear], startPoint: .top, endPoint: .bottom))
                                }
                            }
                            .chartYStyle(.automatic)
                            .padding()
                        )
                    
                    VStack(alignment: .leading, spacing: 10) {
                        InfoRow(label: "ID de Estación", value: station.id)
                        InfoRow(label: "Última Transmisión", value: station.ultimaTx ?? "Desconocida")
                        InfoRow(label: "Latitud", value: "\(station.lat ?? 0)")
                        InfoRow(label: "Longitud", value: "\(station.lon ?? 0)")
                    }
                    .padding()
                    .background(GlassView())
                }
                .padding()
            }
        }
        .navigationTitle(station.nombre)
        .onAppear {
            Task {
                self.history = await api.fetchHistory(stationId: station.id, variable: station.variableActual)
            }
        }
    }
    
    func iconFor(_ variable: String) -> String {
        let v = variable.lowercased()
        if v.contains("precipit") { return "cloud.rain.fill" }
        if v.contains("temp") { return "thermometer.medium" }
        if v.contains("viento") { return "wind" }
        if v.contains("nivel") { return "drop.fill" }
        return "sensor.fill"
    }
}

struct InfoRow: View {
    let label: String
    let value: String
    var body: some View {
        HStack {
            Text(label).foregroundColor(.white.opacity(0.5))
            Spacer()
            Text(value).foregroundColor(.white).bold()
        }
        .font(.system(size: 14))
        .padding(.vertical, 4)
    }
}
