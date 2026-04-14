import SwiftUI
import Charts

struct StationDataView: View {
    @EnvironmentObject var authService: AuthService
    @StateObject private var service = StationDataService()
    
    @State private var onlyCfe = true
    @State private var selectedStation: StationInfo?
    @State private var selectedVariable: StationVariable?
    @State private var startDate = Calendar.current.date(byAdding: .day, value: -1, to: Date())!
    @State private var endDate = Date()
    @State private var showStationPicker = false
    @State private var searchText = ""
    @State private var showResults = false
    
    var filteredStations: [StationInfo] {
        if searchText.isEmpty { return service.stations }
        return service.stations.filter {
            $0.name.localizedCaseInsensitiveContains(searchText) ||
            $0.stationId.localizedCaseInsensitiveContains(searchText)
        }
    }
    
    var body: some View {
        NavigationView {
            ZStack {
                Color(red: 0.08, green: 0.08, blue: 0.12).ignoresSafeArea()
                
                ScrollView {
                    VStack(spacing: 14) {
                        filtersSection
                        
                        if showResults {
                            if service.isLoadingData {
                                ProgressView("Consultando datos...")
                                    .tint(.white)
                                    .foregroundColor(.white)
                                    .padding(.top, 40)
                            } else if let data = service.analysisData, let series = data.series.first {
                                chartSection(series)
                                tableSection(series)
                            } else if let error = service.errorMessage {
                                errorView(error)
                            }
                        }
                    }
                    .padding(.horizontal)
                    .padding(.top, 8)
                    .padding(.bottom, 20)
                }
            }
            .navigationTitle("Estaciones")
            .navigationBarTitleDisplayMode(.inline)
            .toolbarColorScheme(.dark, for: .navigationBar)
            .toolbarBackground(Color(red: 0.08, green: 0.08, blue: 0.12), for: .navigationBar)
            .toolbarBackground(.visible, for: .navigationBar)
            .sheet(isPresented: $showStationPicker) {
                stationPickerSheet
            }
        }
        .onAppear {
            service.configure(serverUrl: authService.serverUrl, token: authService.token)
            Task { await service.loadStations(onlyCfe: onlyCfe) }
        }
    }
    
    // MARK: - Filters
    
    private var filtersSection: some View {
        VStack(spacing: 12) {
            // Solo CFE toggle
            HStack {
                Image(systemName: "building.2")
                    .foregroundColor(.yellow)
                Text("Solo CFE")
                    .foregroundColor(.white)
                    .font(.system(size: 14, weight: .medium))
                Spacer()
                Toggle("", isOn: $onlyCfe)
                    .tint(.yellow)
                    .onChange(of: onlyCfe) { _ in
                        selectedStation = nil
                        selectedVariable = nil
                        showResults = false
                        Task { await service.loadStations(onlyCfe: onlyCfe) }
                    }
            }
            .padding(.horizontal, 14)
            .padding(.vertical, 10)
            .background(RoundedRectangle(cornerRadius: 10).fill(Color.white.opacity(0.06)))
            
            // Station selector
            Button(action: { showStationPicker = true }) {
                HStack {
                    Image(systemName: "antenna.radiowaves.left.and.right")
                        .foregroundColor(.cyan)
                    Text(selectedStation?.name ?? "Seleccionar estación")
                        .foregroundColor(selectedStation != nil ? .white : .gray)
                        .font(.system(size: 14))
                        .lineLimit(1)
                    Spacer()
                    if service.isLoadingStations {
                        ProgressView().tint(.gray)
                    } else {
                        Image(systemName: "chevron.down")
                            .foregroundColor(.gray)
                            .font(.system(size: 12))
                    }
                }
                .padding(.horizontal, 14)
                .padding(.vertical, 12)
                .background(RoundedRectangle(cornerRadius: 10).fill(Color.white.opacity(0.06)))
            }
            
            // Variable selector
            if !service.variables.isEmpty {
                ScrollView(.horizontal, showsIndicators: false) {
                    HStack(spacing: 8) {
                        ForEach(service.variables.filter { $0.hasData }) { v in
                            Button(action: { selectedVariable = v }) {
                                Text(v.displayName)
                                    .font(.system(size: 12, weight: .medium))
                                    .foregroundColor(selectedVariable?.variable == v.variable ? .black : .white)
                                    .padding(.horizontal, 12)
                                    .padding(.vertical, 7)
                                    .background(
                                        RoundedRectangle(cornerRadius: 8)
                                            .fill(selectedVariable?.variable == v.variable ? Color.cyan : Color.white.opacity(0.1))
                                    )
                            }
                        }
                    }
                }
            } else if service.isLoadingVariables {
                HStack {
                    ProgressView().tint(.gray)
                    Text("Cargando variables...")
                        .font(.system(size: 12))
                        .foregroundColor(.gray)
                }
            }
            
            // Date range
            HStack(spacing: 10) {
                VStack(alignment: .leading, spacing: 3) {
                    Text("Desde")
                        .font(.system(size: 10))
                        .foregroundColor(.gray)
                    DatePicker("", selection: $startDate, displayedComponents: [.date, .hourAndMinute])
                        .labelsHidden()
                        .colorScheme(.dark)
                        .scaleEffect(0.85, anchor: .leading)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                
                VStack(alignment: .leading, spacing: 3) {
                    Text("Hasta")
                        .font(.system(size: 10))
                        .foregroundColor(.gray)
                    DatePicker("", selection: $endDate, displayedComponents: [.date, .hourAndMinute])
                        .labelsHidden()
                        .colorScheme(.dark)
                        .scaleEffect(0.85, anchor: .leading)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
            }
            .padding(.horizontal, 14)
            .padding(.vertical, 8)
            .background(RoundedRectangle(cornerRadius: 10).fill(Color.white.opacity(0.06)))
            
            // Quick period buttons
            HStack(spacing: 8) {
                ForEach(["6h", "12h", "24h", "3d", "7d"], id: \.self) { period in
                    Button(action: { applyPeriod(period) }) {
                        Text(period)
                            .font(.system(size: 12, weight: .bold))
                            .foregroundColor(.white)
                            .frame(maxWidth: .infinity)
                            .padding(.vertical, 8)
                            .background(RoundedRectangle(cornerRadius: 8).fill(Color.white.opacity(0.1)))
                    }
                }
            }
            
            // Consultar button
            Button(action: { queryData() }) {
                HStack {
                    Image(systemName: "magnifyingglass")
                    Text("Consultar")
                        .font(.system(size: 15, weight: .bold))
                }
                .foregroundColor(.black)
                .frame(maxWidth: .infinity)
                .padding(.vertical, 12)
                .background(
                    RoundedRectangle(cornerRadius: 10)
                        .fill(selectedStation != nil && selectedVariable != nil ? Color.cyan : Color.gray.opacity(0.3))
                )
            }
            .disabled(selectedStation == nil || selectedVariable == nil)
        }
    }
    
    // MARK: - Chart
    
    @ViewBuilder
    private func chartSection(_ series: DataSeries) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                Text(series.stationName)
                    .font(.system(size: 15, weight: .bold))
                    .foregroundColor(.white)
                Spacer()
                Text(service.analysisData?.aggregationLevel ?? "")
                    .font(.system(size: 11))
                    .foregroundColor(.gray)
                    .padding(.horizontal, 8)
                    .padding(.vertical, 3)
                    .background(Color.white.opacity(0.08))
                    .cornerRadius(8)
            }
            
            let validPoints = series.dataPoints.filter { $0.value != nil && $0.date != nil }
            
            if validPoints.isEmpty {
                HStack {
                    Spacer()
                    Text("Sin datos en el período")
                        .foregroundColor(.gray)
                    Spacer()
                }
                .padding(.vertical, 40)
            } else {
                Chart {
                    ForEach(validPoints) { point in
                        if let date = point.date, let val = point.value {
                            if selectedVariable?.variable.lowercased().contains("precipitación") == true ||
                               selectedVariable?.variable.lowercased().contains("precipitacion") == true {
                                BarMark(
                                    x: .value("Hora", date),
                                    y: .value("Valor", val)
                                )
                                .foregroundStyle(Color.cyan.gradient)
                            } else {
                                LineMark(
                                    x: .value("Hora", date),
                                    y: .value("Valor", val)
                                )
                                .foregroundStyle(Color.cyan)
                                .lineStyle(StrokeStyle(lineWidth: 2))
                                
                                AreaMark(
                                    x: .value("Hora", date),
                                    y: .value("Valor", val)
                                )
                                .foregroundStyle(
                                    LinearGradient(
                                        colors: [Color.cyan.opacity(0.3), Color.cyan.opacity(0.0)],
                                        startPoint: .top, endPoint: .bottom
                                    )
                                )
                            }
                        }
                    }
                }
                .chartXAxis {
                    AxisMarks(values: .automatic(desiredCount: 5)) {
                        AxisGridLine(stroke: StrokeStyle(lineWidth: 0.3))
                            .foregroundStyle(Color.white.opacity(0.15))
                        AxisValueLabel()
                            .foregroundStyle(Color.gray)
                            .font(.system(size: 9))
                    }
                }
                .chartYAxis {
                    AxisMarks(values: .automatic(desiredCount: 5)) {
                        AxisGridLine(stroke: StrokeStyle(lineWidth: 0.3))
                            .foregroundStyle(Color.white.opacity(0.15))
                        AxisValueLabel()
                            .foregroundStyle(Color.gray)
                            .font(.system(size: 10))
                    }
                }
                .frame(height: 220)
                .padding(.vertical, 8)
            }
            
            // Stats row
            if !validPoints.isEmpty {
                let values = validPoints.compactMap { $0.value }
                HStack(spacing: 0) {
                    StatBadge(label: "Mín", value: String(format: "%.2f", values.min() ?? 0), color: .blue)
                    StatBadge(label: "Máx", value: String(format: "%.2f", values.max() ?? 0), color: .red)
                    StatBadge(label: "Prom", value: String(format: "%.2f", values.reduce(0, +) / Double(values.count)), color: .green)
                    StatBadge(label: "Puntos", value: "\(values.count)", color: .purple)
                }
            }
        }
        .padding(14)
        .background(RoundedRectangle(cornerRadius: 12).fill(Color.white.opacity(0.05)))
    }
    
    // MARK: - Table
    
    @ViewBuilder
    private func tableSection(_ series: DataSeries) -> some View {
        let validPoints = series.dataPoints.filter { $0.value != nil }
        if !validPoints.isEmpty {
            VStack(alignment: .leading, spacing: 8) {
                Text("Tabla de Datos (\(validPoints.count) registros)")
                    .font(.system(size: 15, weight: .bold))
                    .foregroundColor(.white)
                
                ScrollView(.horizontal, showsIndicators: false) {
                    VStack(spacing: 0) {
                        // Header
                        HStack(spacing: 0) {
                            Text("Fecha/Hora")
                                .font(.system(size: 10, weight: .bold))
                                .foregroundColor(.gray)
                                .frame(width: 130, alignment: .leading)
                            Text("Valor")
                                .font(.system(size: 10, weight: .bold))
                                .foregroundColor(.gray)
                                .frame(width: 80, alignment: .trailing)
                            Text("Válido")
                                .font(.system(size: 10, weight: .bold))
                                .foregroundColor(.gray)
                                .frame(width: 50)
                        }
                        .padding(.horizontal, 10)
                        .padding(.vertical, 8)
                        .background(Color.white.opacity(0.08))
                        
                        // Show last 100 to avoid performance issues
                        let displayPoints = validPoints.suffix(100)
                        ForEach(Array(displayPoints.enumerated()), id: \.element.id) { idx, point in
                            HStack(spacing: 0) {
                                Text(point.timeString)
                                    .font(.system(size: 11, design: .monospaced))
                                    .foregroundColor(.white)
                                    .frame(width: 130, alignment: .leading)
                                Text(point.value != nil ? String(format: "%.2f", point.value!) : "—")
                                    .font(.system(size: 11, weight: .medium, design: .monospaced))
                                    .foregroundColor(.cyan)
                                    .frame(width: 80, alignment: .trailing)
                                Image(systemName: point.isValid == true ? "checkmark.circle.fill" : "xmark.circle.fill")
                                    .font(.system(size: 10))
                                    .foregroundColor(point.isValid == true ? .green : .red)
                                    .frame(width: 50)
                            }
                            .padding(.horizontal, 10)
                            .padding(.vertical, 5)
                            .background(idx % 2 == 0 ? Color.white.opacity(0.02) : Color.clear)
                        }
                        
                        if validPoints.count > 100 {
                            Text("Mostrando últimos 100 de \(validPoints.count) registros")
                                .font(.system(size: 10))
                                .foregroundColor(.gray)
                                .padding(.vertical, 8)
                        }
                    }
                }
            }
            .padding(14)
            .background(RoundedRectangle(cornerRadius: 12).fill(Color.white.opacity(0.05)))
        }
    }
    
    // MARK: - Station Picker Sheet
    
    private var stationPickerSheet: some View {
        NavigationView {
            ZStack {
                Color(red: 0.08, green: 0.08, blue: 0.12).ignoresSafeArea()
                
                VStack(spacing: 0) {
                    // Search bar
                    HStack {
                        Image(systemName: "magnifyingglass")
                            .foregroundColor(.gray)
                        TextField("Buscar estación...", text: $searchText)
                            .foregroundColor(.white)
                            .autocorrectionDisabled()
                    }
                    .padding(10)
                    .background(RoundedRectangle(cornerRadius: 10).fill(Color.white.opacity(0.08)))
                    .padding(.horizontal)
                    .padding(.top, 8)
                    
                    Text("\(filteredStations.count) estaciones")
                        .font(.system(size: 11))
                        .foregroundColor(.gray)
                        .padding(.top, 6)
                    
                    List(filteredStations) { station in
                        Button(action: {
                            selectedStation = station
                            selectedVariable = nil
                            showResults = false
                            showStationPicker = false
                            Task { await service.loadVariables(stationId: station.stationId) }
                        }) {
                            HStack {
                                VStack(alignment: .leading, spacing: 2) {
                                    Text(station.name)
                                        .font(.system(size: 14, weight: .medium))
                                        .foregroundColor(.white)
                                    Text(station.stationId)
                                        .font(.system(size: 11, design: .monospaced))
                                        .foregroundColor(.gray)
                                }
                                Spacer()
                                if selectedStation?.stationId == station.stationId {
                                    Image(systemName: "checkmark.circle.fill")
                                        .foregroundColor(.cyan)
                                }
                            }
                            .padding(.vertical, 4)
                        }
                        .listRowBackground(Color.white.opacity(0.04))
                    }
                    .listStyle(.plain)
                    .scrollContentBackground(.hidden)
                }
            }
            .navigationTitle("Estaciones")
            .navigationBarTitleDisplayMode(.inline)
            .toolbarColorScheme(.dark, for: .navigationBar)
            .toolbar {
                ToolbarItem(placement: .navigationBarTrailing) {
                    Button("Cerrar") { showStationPicker = false }
                        .foregroundColor(.cyan)
                }
            }
        }
    }
    
    // MARK: - Error View
    
    @ViewBuilder
    private func errorView(_ message: String) -> some View {
        VStack(spacing: 10) {
            Image(systemName: "exclamationmark.triangle")
                .font(.system(size: 30))
                .foregroundColor(.orange)
            Text(message)
                .foregroundColor(.gray)
                .multilineTextAlignment(.center)
                .font(.system(size: 13))
        }
        .padding(.top, 40)
    }
    
    // MARK: - Helpers
    
    private func applyPeriod(_ period: String) {
        endDate = Date()
        switch period {
        case "6h": startDate = Calendar.current.date(byAdding: .hour, value: -6, to: endDate)!
        case "12h": startDate = Calendar.current.date(byAdding: .hour, value: -12, to: endDate)!
        case "24h": startDate = Calendar.current.date(byAdding: .day, value: -1, to: endDate)!
        case "3d": startDate = Calendar.current.date(byAdding: .day, value: -3, to: endDate)!
        case "7d": startDate = Calendar.current.date(byAdding: .day, value: -7, to: endDate)!
        default: break
        }
    }
    
    private func queryData() {
        guard let station = selectedStation, let variable = selectedVariable else { return }
        showResults = true
        Task {
            await service.loadAnalysisData(
                stationIds: [station.stationId],
                variable: variable.variable,
                startDate: startDate,
                endDate: endDate
            )
        }
    }
}

struct StatBadge: View {
    let label: String
    let value: String
    let color: Color
    
    var body: some View {
        VStack(spacing: 2) {
            Text(label)
                .font(.system(size: 9))
                .foregroundColor(.gray)
            Text(value)
                .font(.system(size: 13, weight: .bold, design: .monospaced))
                .foregroundColor(color)
        }
        .frame(maxWidth: .infinity)
    }
}
