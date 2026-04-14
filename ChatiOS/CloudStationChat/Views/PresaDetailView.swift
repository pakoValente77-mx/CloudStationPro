import SwiftUI
import Charts

struct PresaDetailView: View {
    let presaName: String
    @ObservedObject var service: FunVasosService
    @Environment(\.dismiss) var dismiss
    @State private var loaded = false
    
    private var presaData: FunVasosResumenPresa? {
        service.allPresas.first(where: { $0.presa == presaName })
    }
    
    private var presaColor: Color {
        switch presaName.lowercased() {
        case let n where n.contains("angostura"): return Color(red: 0.3, green: 0.7, blue: 1.0)
        case let n where n.contains("chicoas"): return Color(red: 0.4, green: 0.85, blue: 0.55)
        case let n where n.contains("malpaso"): return Color(red: 1.0, green: 0.75, blue: 0.3)
        case let n where n.contains("tap"): return Color(red: 0.85, green: 0.45, blue: 0.85)
        case let n where n.contains("pe"): return Color(red: 1.0, green: 0.45, blue: 0.45)
        default: return .blue
        }
    }
    
    var body: some View {
        NavigationView {
            ZStack {
                Color(red: 0.08, green: 0.08, blue: 0.12).ignoresSafeArea()
                
                if service.isLoading && !loaded {
                    ProgressView("Cargando datos...")
                        .tint(.white)
                        .foregroundColor(.white)
                } else if let presa = presaData {
                    ScrollView {
                        VStack(spacing: 16) {
                            // Summary cards
                            summarySection(presa)
                            
                            // Combined chart: Elevation + Aportaciones + Extracciones
                            combinedChartSection(presa)
                            
                            // Hourly table
                            hourlyTableSection(presa)
                        }
                        .padding(.horizontal)
                        .padding(.top, 8)
                        .padding(.bottom, 20)
                    }
                } else {
                    VStack(spacing: 12) {
                        Image(systemName: "tray")
                            .font(.system(size: 40))
                            .foregroundColor(.gray)
                        Text("Sin datos disponibles")
                            .foregroundColor(.gray)
                    }
                }
            }
            .navigationTitle(presaName)
            .navigationBarTitleDisplayMode(.inline)
            .toolbarColorScheme(.dark, for: .navigationBar)
            .toolbarBackground(Color(red: 0.08, green: 0.08, blue: 0.12), for: .navigationBar)
            .toolbarBackground(.visible, for: .navigationBar)
            .toolbar {
                ToolbarItem(placement: .navigationBarLeading) {
                    Button("Cerrar") { dismiss() }
                        .foregroundColor(.cyan)
                }
            }
        }
        .task {
            if !loaded {
                await service.loadData()
                loaded = true
            }
        }
    }
    
    // MARK: - Summary
    @ViewBuilder
    private func summarySection(_ presa: FunVasosResumenPresa) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("Resumen")
                .font(.system(size: 15, weight: .bold))
                .foregroundColor(.white)
            
            LazyVGrid(columns: [
                GridItem(.flexible()),
                GridItem(.flexible()),
                GridItem(.flexible())
            ], spacing: 10) {
                SummaryCard(title: "Elevación", value: fmt(presa.ultimaElevacion), unit: "msnm", color: .cyan)
                SummaryCard(title: "Almac.", value: fmt(presa.ultimoAlmacenamiento), unit: "Mm³", color: .blue)
                SummaryCard(title: "Generación", value: fmt(presa.totalGeneracion), unit: "GWh", color: .yellow)
                SummaryCard(title: "Aportaciones", value: fmt(presa.totalAportacionesV), unit: "Mm³", color: .green)
                SummaryCard(title: "Extracciones", value: fmt(presa.totalExtraccionesV), unit: "Mm³", color: .orange)
                SummaryCard(title: "Última Hora", value: "\(presa.ultimaHora)", unit: ":00", color: .purple)
            }
        }
    }
    
    // MARK: - Combined Chart (Elevation + Aportaciones + Extracciones) — Dual Y Axis
    @ViewBuilder
    private func combinedChartSection(_ presa: FunVasosResumenPresa) -> some View {
        let sortedData = presa.datos.sorted(by: { $0.hora < $1.hora })
        let hasElev = sortedData.contains(where: { $0.elevacion != nil })
        let hasFlow = sortedData.contains(where: { $0.aportacionesV != nil || $0.extraccionesTotalV != nil })
        
        // Compute hour range
        let hours = sortedData.map { $0.hora }
        let minH = hours.min() ?? 0
        let maxH = hours.max() ?? 24
        
        if hasElev || hasFlow {
            VStack(alignment: .leading, spacing: 8) {
                HStack {
                    Image(systemName: "chart.xyaxis.line")
                        .foregroundColor(presaColor)
                    Text("Elevación, Aportaciones y Extracciones")
                        .font(.system(size: 14, weight: .bold))
                        .foregroundColor(.white)
                }
                
                // Legend
                HStack(spacing: 14) {
                    if hasElev {
                        HStack(spacing: 4) {
                            RoundedRectangle(cornerRadius: 1)
                                .fill(presaColor)
                                .frame(width: 14, height: 2.5)
                            Text("Elevación (msnm)")
                                .font(.system(size: 9))
                                .foregroundColor(presaColor)
                        }
                    }
                    if hasFlow {
                        HStack(spacing: 4) {
                            Circle().fill(Color.green).frame(width: 7, height: 7)
                            Text("Apor. (Mm³)")
                                .font(.system(size: 9))
                                .foregroundColor(.green)
                        }
                        HStack(spacing: 4) {
                            Circle().fill(Color.orange).frame(width: 7, height: 7)
                            Text("Extr. (Mm³)")
                                .font(.system(size: 9))
                                .foregroundColor(.orange)
                        }
                    }
                }
                
                // Dual-axis overlay
                ZStack {
                    // LAYER 1: Bars — Aportaciones & Extracciones (right Y axis)
                    if hasFlow {
                        Chart {
                            ForEach(sortedData) { row in
                                if let apor = row.aportacionesV {
                                    BarMark(
                                        x: .value("Hora", row.hora),
                                        y: .value("Volumen", apor)
                                    )
                                    .foregroundStyle(Color.green.opacity(0.55))
                                }
                                if let ext = row.extraccionesTotalV {
                                    BarMark(
                                        x: .value("Hora", row.hora),
                                        y: .value("Volumen", ext)
                                    )
                                    .foregroundStyle(Color.orange.opacity(0.55))
                                }
                            }
                        }
                        .chartXAxis(.hidden)
                        .chartXScale(domain: minH...maxH)
                        .chartYAxis {
                            AxisMarks(position: .trailing) { value in
                                AxisValueLabel {
                                    if let v = value.as(Float.self) {
                                        Text(String(format: "%.1f", v))
                                            .font(.system(size: 8))
                                            .foregroundColor(.green.opacity(0.7))
                                    }
                                }
                            }
                        }
                    }
                    
                    // LAYER 2: Line — Elevación (left Y axis, on top)
                    if hasElev {
                        Chart {
                            ForEach(sortedData) { row in
                                if let elev = row.elevacion {
                                    LineMark(
                                        x: .value("Hora", row.hora),
                                        y: .value("Elevación", elev)
                                    )
                                    .foregroundStyle(presaColor)
                                    .lineStyle(StrokeStyle(lineWidth: 2.5))
                                    
                                    AreaMark(
                                        x: .value("Hora", row.hora),
                                        y: .value("Elevación", elev)
                                    )
                                    .foregroundStyle(
                                        LinearGradient(
                                            colors: [presaColor.opacity(0.2), presaColor.opacity(0.01)],
                                            startPoint: .top,
                                            endPoint: .bottom
                                        )
                                    )
                                    
                                    PointMark(
                                        x: .value("Hora", row.hora),
                                        y: .value("Elevación", elev)
                                    )
                                    .foregroundStyle(presaColor)
                                    .symbolSize(20)
                                }
                            }
                        }
                        .chartXScale(domain: minH...maxH)
                        .chartXAxis {
                            AxisMarks(values: .automatic(desiredCount: 8)) { value in
                                AxisGridLine(stroke: StrokeStyle(lineWidth: 0.3))
                                    .foregroundStyle(Color.white.opacity(0.15))
                                AxisValueLabel {
                                    if let h = value.as(Int.self) {
                                        Text("\(h)h")
                                            .font(.system(size: 9))
                                            .foregroundColor(.gray)
                                    }
                                }
                            }
                        }
                        .chartYAxis {
                            AxisMarks(position: .leading) { value in
                                AxisGridLine(stroke: StrokeStyle(lineWidth: 0.3))
                                    .foregroundStyle(Color.white.opacity(0.1))
                                AxisValueLabel {
                                    if let v = value.as(Float.self) {
                                        Text(String(format: "%.1f", v))
                                            .font(.system(size: 8))
                                            .foregroundColor(presaColor.opacity(0.8))
                                    }
                                }
                            }
                        }
                        .chartYScale(domain: .automatic(includesZero: false))
                    }
                }
                .frame(height: 240)
                .padding(12)
                .background(
                    RoundedRectangle(cornerRadius: 12)
                        .fill(Color.white.opacity(0.04))
                )
            }
        }
    }
    
    // MARK: - Hourly table
    @ViewBuilder
    private func hourlyTableSection(_ presa: FunVasosResumenPresa) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("Datos Horarios")
                .font(.system(size: 15, weight: .bold))
                .foregroundColor(.white)
            
            // Table header
            ScrollView(.horizontal, showsIndicators: false) {
                VStack(spacing: 0) {
                    tableHeader()
                    
                    ForEach(presa.datos.sorted(by: { $0.hora < $1.hora })) { row in
                        tableRow(row)
                    }
                }
            }
            .background(
                RoundedRectangle(cornerRadius: 10)
                    .fill(Color.white.opacity(0.04))
            )
        }
    }
    
    @ViewBuilder
    private func tableHeader() -> some View {
        HStack(spacing: 0) {
            headerCell("Hr", width: 35)
            headerCell("Elev.", width: 72)
            headerCell("Almac.", width: 72)
            headerCell("Apor.V", width: 72)
            headerCell("Ext.V", width: 72)
            headerCell("Gen.", width: 65)
            headerCell("Uds", width: 40)
        }
        .padding(.vertical, 8)
        .background(Color.white.opacity(0.08))
    }
    
    @ViewBuilder
    private func headerCell(_ text: String, width: CGFloat) -> some View {
        Text(text)
            .font(.system(size: 10, weight: .bold))
            .foregroundColor(.gray)
            .frame(width: width)
    }
    
    @ViewBuilder
    private func tableRow(_ row: FunVasosHorario) -> some View {
        HStack(spacing: 0) {
            Text("\(row.hora)")
                .font(.system(size: 11, weight: .bold, design: .monospaced))
                .foregroundColor(.white)
                .frame(width: 35)
            dataCell(row.elevacion, width: 72, color: .cyan)
            dataCell(row.almacenamiento, width: 72, color: .blue)
            dataCell(row.aportacionesV, width: 72, color: .green)
            dataCell(row.extraccionesTotalV, width: 72, color: .orange)
            dataCell(row.generacion, width: 65, color: .yellow)
            Text(row.numUnidades != nil ? "\(row.numUnidades!)" : "—")
                .font(.system(size: 11, design: .monospaced))
                .foregroundColor(.purple)
                .frame(width: 40)
        }
        .padding(.vertical, 5)
        .background(row.hora % 2 == 0 ? Color.white.opacity(0.02) : Color.clear)
    }
    
    @ViewBuilder
    private func dataCell(_ value: Float?, width: CGFloat, color: Color) -> some View {
        Text(value != nil ? String(format: "%.2f", value!) : "—")
            .font(.system(size: 11, design: .monospaced))
            .foregroundColor(color)
            .frame(width: width)
    }
    
    private func fmt(_ value: Float?) -> String {
        guard let v = value else { return "—" }
        return String(format: "%.2f", v)
    }
}

struct SummaryCard: View {
    let title: String
    let value: String
    let unit: String
    let color: Color
    
    var body: some View {
        VStack(spacing: 4) {
            Text(title)
                .font(.system(size: 10))
                .foregroundColor(.gray)
            Text(value)
                .font(.system(size: 18, weight: .bold, design: .monospaced))
                .foregroundColor(color)
            Text(unit)
                .font(.system(size: 9))
                .foregroundColor(color.opacity(0.6))
        }
        .frame(maxWidth: .infinity)
        .padding(.vertical, 10)
        .background(
            RoundedRectangle(cornerRadius: 10)
                .fill(Color.white.opacity(0.05))
        )
    }
}
