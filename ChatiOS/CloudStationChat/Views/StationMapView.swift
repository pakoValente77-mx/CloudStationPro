import SwiftUI
import MapKit

struct StationMapView: View {
    @EnvironmentObject var authService: AuthService
    @StateObject private var service = MapService()
    @State private var selectedStation: StationMapData?
    @State private var region = MKCoordinateRegion(
        center: CLLocationCoordinate2D(latitude: 17.0, longitude: -93.0),
        span: MKCoordinateSpan(latitudeDelta: 3.5, longitudeDelta: 3.5)
    )
    
    var body: some View {
        NavigationView {
            ZStack {
                Color(red: 0.08, green: 0.08, blue: 0.12).ignoresSafeArea()
                
                VStack(spacing: 0) {
                    // Variable selector
                    variableSelector
                    
                    // Map
                    mapContent
                }
                
                // Station detail overlay
                if let station = selectedStation {
                    VStack {
                        Spacer()
                        stationCard(station)
                            .transition(.move(edge: .bottom).combined(with: .opacity))
                    }
                    .animation(.spring(response: 0.3), value: selectedStation?.id)
                }
                
                if service.isLoading && service.stations.isEmpty {
                    ProgressView("Cargando estaciones...")
                        .tint(.white)
                        .foregroundColor(.white)
                }
            }
            .navigationTitle("Mapa CFE")
            .navigationBarTitleDisplayMode(.inline)
            .toolbarColorScheme(.dark, for: .navigationBar)
            .toolbarBackground(Color(red: 0.08, green: 0.08, blue: 0.12), for: .navigationBar)
            .toolbarBackground(.visible, for: .navigationBar)
        }
        .onAppear {
            service.configure(serverUrl: authService.serverUrl, token: authService.token)
            Task { await service.loadMapData() }
        }
    }
    
    // MARK: - Variable Selector
    private var variableSelector: some View {
        ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: 8) {
                ForEach(service.availableVariables) { variable in
                    Button {
                        Task { await service.changeVariable(variable.value) }
                    } label: {
                        HStack(spacing: 4) {
                            Image(systemName: iconFor(variable.value))
                                .font(.system(size: 11))
                            Text(variable.label)
                                .font(.system(size: 12, weight: service.selectedVariable == variable.value ? .bold : .regular))
                        }
                        .foregroundColor(service.selectedVariable == variable.value ? .white : .gray)
                        .padding(.horizontal, 12)
                        .padding(.vertical, 7)
                        .background(
                            RoundedRectangle(cornerRadius: 8)
                                .fill(service.selectedVariable == variable.value ? colorFor(variable.value).opacity(0.3) : Color.white.opacity(0.06))
                        )
                        .overlay(
                            RoundedRectangle(cornerRadius: 8)
                                .stroke(service.selectedVariable == variable.value ? colorFor(variable.value).opacity(0.6) : Color.clear, lineWidth: 1)
                        )
                    }
                }
            }
            .padding(.horizontal, 12)
            .padding(.vertical, 8)
        }
        .background(Color(red: 0.08, green: 0.08, blue: 0.12))
    }
    
    // MARK: - Map Content
    private var mapContent: some View {
        Map(coordinateRegion: $region, annotationItems: service.stations.filter { $0.lat != 0 && $0.lon != 0 }) { station in
            MapAnnotation(coordinate: CLLocationCoordinate2D(latitude: station.lat, longitude: station.lon)) {
                Button {
                    withAnimation { selectedStation = station }
                } label: {
                    stationPin(station)
                }
            }
        }
        .ignoresSafeArea(edges: .bottom)
    }
    
    // MARK: - Station Pin
    private func stationPin(_ station: StationMapData) -> some View {
        VStack(spacing: 2) {
            // Value badge
            if let valor = station.valorActual {
                Text(formatValue(valor))
                    .font(.system(size: 9, weight: .bold, design: .monospaced))
                    .foregroundColor(.white)
                    .padding(.horizontal, 4)
                    .padding(.vertical, 2)
                    .background(
                        RoundedRectangle(cornerRadius: 4)
                            .fill(pinColor(station))
                    )
            }
            
            // Pin dot
            ZStack {
                Circle()
                    .fill(pinColor(station).opacity(0.3))
                    .frame(width: 18, height: 18)
                Circle()
                    .fill(pinColor(station))
                    .frame(width: 10, height: 10)
                if station.enMantenimiento {
                    Image(systemName: "wrench.fill")
                        .font(.system(size: 6))
                        .foregroundColor(.white)
                }
            }
        }
    }
    
    // MARK: - Station Card
    private func stationCard(_ station: StationMapData) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                Circle()
                    .fill(pinColor(station))
                    .frame(width: 10, height: 10)
                Text(station.nombre ?? "Sin nombre")
                    .font(.system(size: 16, weight: .bold))
                    .foregroundColor(.white)
                Spacer()
                Button {
                    withAnimation { selectedStation = nil }
                } label: {
                    Image(systemName: "xmark.circle.fill")
                        .font(.system(size: 20))
                        .foregroundColor(.gray)
                }
            }
            
            HStack(spacing: 16) {
                infoItem(label: "ID", value: station.idStation)
                if let dcp = station.dcpId, !dcp.isEmpty {
                    infoItem(label: "DCP", value: dcp)
                }
            }
            
            HStack(spacing: 16) {
                if let valor = station.valorActual {
                    infoItem(label: station.variableActual?.capitalized ?? "Valor",
                             value: formatValue(valor) + " " + unitFor(service.selectedVariable),
                             color: pinColor(station))
                }
                if let tx = station.ultimaTx {
                    infoItem(label: "Última Tx", value: formatDate(tx))
                }
            }
            
            HStack(spacing: 16) {
                infoItem(label: "Lat", value: String(format: "%.4f", station.lat))
                infoItem(label: "Lon", value: String(format: "%.4f", station.lon))
                if station.hasCota {
                    HStack(spacing: 3) {
                        Image(systemName: "water.waves")
                            .font(.system(size: 10))
                            .foregroundColor(.cyan)
                        Text("Cota")
                            .font(.system(size: 10))
                            .foregroundColor(.cyan)
                    }
                }
                if station.enMantenimiento {
                    HStack(spacing: 3) {
                        Image(systemName: "wrench.fill")
                            .font(.system(size: 10))
                            .foregroundColor(.orange)
                        Text("Mant.")
                            .font(.system(size: 10))
                            .foregroundColor(.orange)
                    }
                }
            }
        }
        .padding(16)
        .background(
            RoundedRectangle(cornerRadius: 16)
                .fill(Color(red: 0.12, green: 0.12, blue: 0.16))
                .overlay(
                    RoundedRectangle(cornerRadius: 16)
                        .stroke(pinColor(station).opacity(0.4), lineWidth: 1)
                )
                .shadow(color: .black.opacity(0.4), radius: 10, y: -4)
        )
        .padding(.horizontal, 12)
        .padding(.bottom, 16)
    }
    
    private func infoItem(label: String, value: String, color: Color = .white) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(label)
                .font(.system(size: 9))
                .foregroundColor(.gray)
            Text(value)
                .font(.system(size: 12, weight: .semibold, design: .monospaced))
                .foregroundColor(color)
        }
    }
    
    // MARK: - Helpers
    private func pinColor(_ station: StationMapData) -> Color {
        if station.enMantenimiento { return .orange }
        guard let color = station.estatusColor?.lowercased() else { return .gray }
        switch color {
        case "green", "verde": return .green
        case "yellow", "amarillo": return .yellow
        case "red", "rojo": return .red
        case "blue", "azul": return .blue
        case "gray", "gris": return .gray
        default: return colorFor(service.selectedVariable)
        }
    }
    
    private func colorFor(_ variable: String) -> Color {
        switch variable.lowercased() {
        case let v where v.contains("precip"): return .cyan
        case let v where v.contains("nivel"): return .blue
        case let v where v.contains("temp"): return .orange
        case let v where v.contains("humedad"): return .teal
        case let v where v.contains("viento"): return .mint
        case let v where v.contains("presi"): return .purple
        case let v where v.contains("radiaci"): return .yellow
        case let v where v.contains("voltaje") || v.contains("bater"): return .green
        case let v where v.contains("gasto"): return .indigo
        default: return .cyan
        }
    }
    
    private func iconFor(_ variable: String) -> String {
        switch variable.lowercased() {
        case let v where v.contains("precip"): return "cloud.rain.fill"
        case let v where v.contains("nivel"): return "water.waves"
        case let v where v.contains("temp"): return "thermometer"
        case let v where v.contains("humedad"): return "humidity.fill"
        case let v where v.contains("velocidad") && v.contains("viento"): return "wind"
        case let v where v.contains("direcci") && v.contains("viento"): return "safari"
        case let v where v.contains("presi"): return "gauge.medium"
        case let v where v.contains("radiaci"): return "sun.max.fill"
        case let v where v.contains("voltaje") || v.contains("bater"): return "bolt.fill"
        case let v where v.contains("gasto"): return "arrow.right.circle.fill"
        default: return "circle.fill"
        }
    }
    
    private func unitFor(_ variable: String) -> String {
        switch variable.lowercased() {
        case let v where v.contains("precip"): return "mm"
        case let v where v.contains("nivel"): return "msnm"
        case let v where v.contains("temp"): return "°C"
        case let v where v.contains("humedad"): return "%"
        case let v where v.contains("velocidad") && v.contains("viento"): return "m/s"
        case let v where v.contains("direcci") && v.contains("viento"): return "°"
        case let v where v.contains("presi"): return "hPa"
        case let v where v.contains("radiaci"): return "W/m²"
        case let v where v.contains("voltaje") || v.contains("bater"): return "V"
        case let v where v.contains("gasto"): return "m³/s"
        default: return ""
        }
    }
    
    private func formatValue(_ value: Float) -> String {
        if value == Float(Int(value)) {
            return String(format: "%.0f", value)
        }
        return String(format: "%.1f", value)
    }
    
    private func formatDate(_ isoString: String) -> String {
        // Try to parse ISO date and show compact format
        let parts = isoString.components(separatedBy: "T")
        if parts.count == 2 {
            let timeParts = parts[1].components(separatedBy: ".")
            return timeParts[0]
        }
        return isoString
    }
}
