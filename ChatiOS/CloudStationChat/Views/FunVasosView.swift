import SwiftUI
import Charts

struct FunVasosView: View {
    @EnvironmentObject var authService: AuthService
    @StateObject private var service = FunVasosService()
    @State private var selectedPresa: CascadePresa?
    @State private var showCascade = true
    @State private var showDatePicker = false
    @State private var selectedDate = Date()
    
    private let dateFormatter: DateFormatter = {
        let f = DateFormatter()
        f.dateFormat = "yyyy-MM-dd"
        return f
    }()
    
    private let displayDateFormatter: DateFormatter = {
        let f = DateFormatter()
        f.dateFormat = "dd MMM yyyy"
        f.locale = Locale(identifier: "es_MX")
        return f
    }()
    
    var body: some View {
        NavigationView {
            ZStack {
                Color(red: 0.08, green: 0.08, blue: 0.12).ignoresSafeArea()
                
                if service.isLoading && service.cascadeData.isEmpty {
                    ProgressView("Cargando...")
                        .tint(.white)
                        .foregroundColor(.white)
                } else if let error = service.errorMessage, service.cascadeData.isEmpty {
                    VStack(spacing: 12) {
                        Image(systemName: "exclamationmark.triangle")
                            .font(.system(size: 40))
                            .foregroundColor(.orange)
                        Text(error)
                            .foregroundColor(.gray)
                            .multilineTextAlignment(.center)
                        Button("Reintentar") {
                            Task { await service.loadCascade() }
                        }
                        .buttonStyle(.bordered)
                        .tint(.blue)
                    }
                    .padding()
                } else {
                    ScrollView {
                        VStack(spacing: 14) {
                            // Date selector bar
                            dateSelector
                            
                            // Mini Cascade Flow Diagram
                            if !service.cascadeData.isEmpty {
                                MiniCascadeView(presas: service.cascadeData)
                                    .padding(.bottom, 4)
                            }
                            
                            // Presa cards
                            ForEach(service.cascadeData) { presa in
                                PresaCardView(presa: presa)
                                    .onTapGesture {
                                        selectedPresa = presa
                                    }
                            }
                        }
                        .padding(.horizontal)
                        .padding(.top, 8)
                        .padding(.bottom, 20)
                    }
                    .refreshable {
                        await service.loadCascade()
                    }
                }
            }
            .navigationTitle("Fun. Vasos")
            .navigationBarTitleDisplayMode(.inline)
            .toolbarColorScheme(.dark, for: .navigationBar)
            .toolbarBackground(Color(red: 0.08, green: 0.08, blue: 0.12), for: .navigationBar)
            .toolbarBackground(.visible, for: .navigationBar)
            .toolbar {
                ToolbarItem(placement: .navigationBarTrailing) {
                    Button {
                        Task { await service.loadCascade() }
                    } label: {
                        Image(systemName: "arrow.clockwise")
                            .font(.system(size: 14, weight: .semibold))
                            .foregroundColor(.cyan)
                            .rotationEffect(.degrees(service.isLoading ? 360 : 0))
                            .animation(service.isLoading ? .linear(duration: 1).repeatForever(autoreverses: false) : .default, value: service.isLoading)
                    }
                    .disabled(service.isLoading)
                }
            }
            .sheet(item: $selectedPresa) { presa in
                PresaDetailView(presaName: presa.name, service: service)
            }
        }
        .onAppear {
            service.configure(serverUrl: authService.serverUrl, token: authService.token)
            Task {
                await service.loadCascade()
                await service.loadData()
            }
            service.startAutoRefresh()
        }
        .onDisappear {
            service.stopAutoRefresh()
        }
    }
    
    // MARK: - Date Selector
    private var dateSelector: some View {
        VStack(spacing: 8) {
            // Quick date buttons from fechasDisponibles
            if !service.fechasDisponibles.isEmpty {
                ScrollView(.horizontal, showsIndicators: false) {
                    HStack(spacing: 8) {
                        ForEach(service.fechasDisponibles, id: \.self) { fecha in
                            Button {
                                Task {
                                    await service.loadDataForDate(fecha)
                                }
                            } label: {
                                Text(displayDate(fecha))
                                    .font(.system(size: 12, weight: service.selectedFecha == fecha ? .bold : .regular))
                                    .foregroundColor(service.selectedFecha == fecha ? .white : .gray)
                                    .padding(.horizontal, 12)
                                    .padding(.vertical, 6)
                                    .background(
                                        RoundedRectangle(cornerRadius: 8)
                                            .fill(service.selectedFecha == fecha ? Color.cyan.opacity(0.3) : Color.white.opacity(0.06))
                                    )
                                    .overlay(
                                        RoundedRectangle(cornerRadius: 8)
                                            .stroke(service.selectedFecha == fecha ? Color.cyan.opacity(0.6) : Color.clear, lineWidth: 1)
                                    )
                            }
                        }
                        
                        // Calendar picker button
                        Button {
                            showDatePicker.toggle()
                        } label: {
                            Image(systemName: "calendar")
                                .font(.system(size: 14))
                                .foregroundColor(.cyan)
                                .padding(8)
                                .background(
                                    RoundedRectangle(cornerRadius: 8)
                                        .fill(Color.white.opacity(0.06))
                                )
                        }
                    }
                    .padding(.horizontal, 4)
                }
            }
            
            // Expandable date picker
            if showDatePicker {
                DatePicker("Seleccionar fecha", selection: $selectedDate, displayedComponents: .date)
                    .datePickerStyle(.graphical)
                    .tint(.cyan)
                    .colorScheme(.dark)
                    .padding(12)
                    .background(
                        RoundedRectangle(cornerRadius: 12)
                            .fill(Color.white.opacity(0.06))
                    )
                    .onChange(of: selectedDate) { newDate in
                        let fecha = dateFormatter.string(from: newDate)
                        showDatePicker = false
                        Task {
                            await service.loadDataForDate(fecha)
                        }
                    }
            }
            
            // Auto-refresh indicator
            HStack(spacing: 4) {
                Circle()
                    .fill(Color.green)
                    .frame(width: 6, height: 6)
                Text("Auto-refresh cada 5 min")
                    .font(.system(size: 9))
                    .foregroundColor(.gray)
                Spacer()
                if !service.selectedFecha.isEmpty {
                    Text("Datos: \(displayDate(service.selectedFecha))")
                        .font(.system(size: 10, weight: .medium))
                        .foregroundColor(.cyan)
                }
            }
            .padding(.horizontal, 4)
        }
    }
    
    private func displayDate(_ isoDate: String) -> String {
        if let date = dateFormatter.date(from: isoDate) {
            return displayDateFormatter.string(from: date)
        }
        return isoDate
    }
}

// MARK: - Mini Cascade Flow Diagram

struct MiniCascadeView: View {
    let presas: [CascadePresa]
    @State private var animateWater = false
    
    private let cascadeOrder = ["angostura", "chicoas", "malpaso", "tap", "pe"]
    private let cascadeNames = ["Angostura", "Chicoasén", "Malpaso", "Tapón", "Peñitas"]
    private let cascadeColors: [Color] = [
        Color(red: 0.3, green: 0.7, blue: 1.0),
        Color(red: 0.4, green: 0.85, blue: 0.55),
        Color(red: 1.0, green: 0.75, blue: 0.3),
        Color(red: 0.85, green: 0.45, blue: 0.85),
        Color(red: 1.0, green: 0.45, blue: 0.45)
    ]
    private let cascadeIcons = ["drop.fill", "bolt.fill", "water.waves", "mountain.2.fill", "wind"]
    
    private func presaFor(_ prefix: String) -> CascadePresa? {
        presas.first(where: { $0.key.lowercased().contains(prefix) })
    }
    
    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                Image(systemName: "arrow.down.to.line.compact")
                    .foregroundColor(.cyan)
                Text("Cascada Grijalva")
                    .font(.system(size: 15, weight: .bold))
                    .foregroundColor(.white)
                Spacer()
                if let fecha = presas.first?.fecha {
                    Text(fecha)
                        .font(.system(size: 10))
                        .foregroundColor(.gray)
                }
            }
            .padding(.horizontal, 12)
            .padding(.top, 12)
            
            // Flow diagram
            VStack(spacing: 0) {
                ForEach(0..<cascadeOrder.count, id: \.self) { i in
                    let prefix = cascadeOrder[i]
                    let presa = presaFor(prefix)
                    
                    // Dam node
                    HStack(spacing: 10) {
                        // Icon circle with pulse
                        ZStack {
                            Circle()
                                .fill(cascadeColors[i].opacity(0.2))
                                .frame(width: 36, height: 36)
                            Circle()
                                .stroke(cascadeColors[i].opacity(animateWater ? 0.4 : 0.1), lineWidth: 2)
                                .frame(width: 36, height: 36)
                                .scaleEffect(animateWater ? 1.3 : 1.0)
                                .opacity(animateWater ? 0.0 : 0.6)
                                .animation(
                                    .easeOut(duration: 2.0)
                                    .repeatForever(autoreverses: false)
                                    .delay(Double(i) * 0.4),
                                    value: animateWater
                                )
                            Image(systemName: cascadeIcons[i])
                                .font(.system(size: 14, weight: .bold))
                                .foregroundColor(cascadeColors[i])
                        }
                        
                        // Name + elevation
                        VStack(alignment: .leading, spacing: 2) {
                            Text(cascadeNames[i])
                                .font(.system(size: 13, weight: .bold))
                                .foregroundColor(.white)
                            if let elev = presa?.currentElev {
                                Text(String(format: "%.2f msnm", elev))
                                    .font(.system(size: 11, design: .monospaced))
                                    .foregroundColor(cascadeColors[i])
                            }
                        }
                        
                        Spacer()
                        
                        // Key metrics compact
                        VStack(alignment: .trailing, spacing: 2) {
                            if let almac = presa?.almacenamiento {
                                HStack(spacing: 3) {
                                    Text("Alm")
                                        .font(.system(size: 9))
                                        .foregroundColor(.gray)
                                    Text(String(format: "%.1f", almac))
                                        .font(.system(size: 11, weight: .semibold, design: .monospaced))
                                        .foregroundColor(.blue)
                                }
                            }
                            if let gen = presa?.generation {
                                HStack(spacing: 3) {
                                    Text("Gen")
                                        .font(.system(size: 9))
                                        .foregroundColor(.gray)
                                    Text(String(format: "%.1f", gen))
                                        .font(.system(size: 11, weight: .semibold, design: .monospaced))
                                        .foregroundColor(.yellow)
                                }
                            }
                        }
                        
                        // Extractions arrow (flow to next)
                        if let ext = presa?.extraccionesV {
                            VStack(spacing: 1) {
                                Text(String(format: "%.1f", ext))
                                    .font(.system(size: 10, weight: .bold, design: .monospaced))
                                    .foregroundColor(.orange)
                                Text("Mm³")
                                    .font(.system(size: 7))
                                    .foregroundColor(.orange.opacity(0.6))
                            }
                            .frame(width: 50)
                        }
                    }
                    .padding(.horizontal, 12)
                    .padding(.vertical, 6)
                    
                    // Animated water connector to next dam
                    if i < cascadeOrder.count - 1 {
                        WaterFlowConnector(
                            topColor: cascadeColors[i],
                            bottomColor: cascadeColors[i + 1],
                            index: i,
                            animate: animateWater
                        )
                    }
                }
            }
            .padding(.bottom, 12)
        }
        .background(
            RoundedRectangle(cornerRadius: 14)
                .fill(Color.white.opacity(0.04))
                .overlay(
                    RoundedRectangle(cornerRadius: 14)
                        .stroke(
                            LinearGradient(
                                colors: [Color.cyan.opacity(0.3), Color.purple.opacity(0.3), Color.red.opacity(0.3)],
                                startPoint: .topLeading,
                                endPoint: .bottomTrailing
                            ),
                            lineWidth: 1
                        )
                )
        )
        .onAppear {
            animateWater = true
        }
    }
}

// MARK: - Animated Water Flow Connector

struct WaterFlowConnector: View {
    let topColor: Color
    let bottomColor: Color
    let index: Int
    let animate: Bool
    
    private let connectorHeight: CGFloat = 44
    private let dropCount = 6
    
    var body: some View {
        HStack {
            Spacer().frame(width: 28)
            ZStack {
                // Wider stream (river background)
                RoundedRectangle(cornerRadius: 2)
                    .fill(
                        LinearGradient(
                            colors: [topColor.opacity(0.25), bottomColor.opacity(0.25)],
                            startPoint: .top,
                            endPoint: .bottom
                        )
                    )
                    .frame(width: 6, height: connectorHeight)
                
                // Glowing stream core
                RoundedRectangle(cornerRadius: 1)
                    .fill(
                        LinearGradient(
                            colors: [topColor.opacity(0.5), bottomColor.opacity(0.5)],
                            startPoint: .top,
                            endPoint: .bottom
                        )
                    )
                    .frame(width: 2, height: connectorHeight)
                
                // Shimmer wave 1
                WaterShimmer(connectorHeight: connectorHeight, width: 6, shimmerHeight: 14, opacity: 0.35,
                             duration: 1.0, delay: Double(index) * 0.25, animate: animate)
                
                // Shimmer wave 2 (offset)
                WaterShimmer(connectorHeight: connectorHeight, width: 6, shimmerHeight: 8, opacity: 0.2,
                             duration: 1.4, delay: Double(index) * 0.25 + 0.5, animate: animate)
                
                // Water drops — multiple streams
                // Left stream
                WaterDrop(xOffset: -7, size: 7, duration: 0.75, delay: Double(index) * 0.25,
                          color: topColor.opacity(0.9), connectorHeight: connectorHeight)
                WaterDrop(xOffset: -8, size: 5, duration: 0.9, delay: Double(index) * 0.25 + 0.35,
                          color: bottomColor.opacity(0.7), connectorHeight: connectorHeight)
                WaterDrop(xOffset: -6, size: 4, duration: 1.0, delay: Double(index) * 0.25 + 0.65,
                          color: topColor.opacity(0.5), connectorHeight: connectorHeight)
                
                // Right stream
                WaterDrop(xOffset: 7, size: 6, duration: 0.85, delay: Double(index) * 0.25 + 0.15,
                          color: bottomColor.opacity(0.85), connectorHeight: connectorHeight)
                WaterDrop(xOffset: 8, size: 4, duration: 0.95, delay: Double(index) * 0.25 + 0.5,
                          color: topColor.opacity(0.6), connectorHeight: connectorHeight)
                WaterDrop(xOffset: 6, size: 3, duration: 1.1, delay: Double(index) * 0.25 + 0.8,
                          color: bottomColor.opacity(0.5), connectorHeight: connectorHeight)
                
                // Center splash drops
                WaterDrop(xOffset: -2, size: 3, duration: 0.7, delay: Double(index) * 0.25 + 0.2,
                          color: Color.white.opacity(0.4), connectorHeight: connectorHeight)
                WaterDrop(xOffset: 2, size: 3, duration: 0.8, delay: Double(index) * 0.25 + 0.55,
                          color: Color.white.opacity(0.3), connectorHeight: connectorHeight)
                
                // Chevron at bottom
                Image(systemName: "chevron.down")
                    .font(.system(size: 8, weight: .bold))
                    .foregroundColor(bottomColor.opacity(0.7))
                    .offset(y: connectorHeight / 2 + 6)
            }
            .frame(height: connectorHeight + 14)
            .clipped()
            Spacer()
        }
    }
}

// MARK: - Single Animated Water Drop

struct WaterDrop: View {
    let xOffset: CGFloat
    let size: CGFloat
    let duration: Double
    let delay: Double
    let color: Color
    let connectorHeight: CGFloat
    
    @State private var yOffset: CGFloat = -12
    @State private var opacity: Double = 0.0
    
    var body: some View {
        Image(systemName: "drop.fill")
            .font(.system(size: size))
            .foregroundColor(color)
            .offset(x: xOffset, y: yOffset)
            .opacity(opacity)
            .onAppear {
                withAnimation(
                    .easeIn(duration: duration)
                    .repeatForever(autoreverses: false)
                    .delay(delay)
                ) {
                    yOffset = connectorHeight / 2 + 6
                    opacity = 0.0
                }
                DispatchQueue.main.asyncAfter(deadline: .now() + delay) {
                    opacity = 1.0
                }
            }
    }
}

// MARK: - Shimmer Effect

struct WaterShimmer: View {
    let connectorHeight: CGFloat
    let width: CGFloat
    let shimmerHeight: CGFloat
    let opacity: Double
    let duration: Double
    let delay: Double
    let animate: Bool
    
    var body: some View {
        RoundedRectangle(cornerRadius: 2)
            .fill(
                LinearGradient(
                    colors: [.clear, Color.white.opacity(opacity), Color(red: 0.5, green: 0.8, blue: 1.0).opacity(opacity), .clear],
                    startPoint: .top,
                    endPoint: .bottom
                )
            )
            .frame(width: width, height: shimmerHeight)
            .offset(y: animate ? connectorHeight / 2 : -connectorHeight / 2)
            .animation(
                .easeInOut(duration: duration)
                .repeatForever(autoreverses: false)
                .delay(delay),
                value: animate
            )
    }
}

// MARK: - Color Components Helper

extension Color {
    var components: (r: Double, g: Double, b: Double, a: Double) {
        #if canImport(UIKit)
        var r: CGFloat = 0
        var g: CGFloat = 0
        var b: CGFloat = 0
        var a: CGFloat = 0
        UIColor(self).getRed(&r, green: &g, blue: &b, alpha: &a)
        return (Double(r), Double(g), Double(b), Double(a))
        #else
        return (0.5, 0.5, 0.5, 1.0)
        #endif
    }
}

// MARK: - Presa Card (Cascade overview)

struct PresaCardView: View {
    let presa: CascadePresa
    
    private var presaColor: Color {
        switch presa.key.lowercased() {
        case let k where k.contains("angostura"): return Color(red: 0.3, green: 0.7, blue: 1.0)
        case let k where k.contains("chicoas"): return Color(red: 0.4, green: 0.85, blue: 0.55)
        case let k where k.contains("malpaso"): return Color(red: 1.0, green: 0.75, blue: 0.3)
        case let k where k.contains("tap"): return Color(red: 0.85, green: 0.45, blue: 0.85)
        case let k where k.contains("pe"): return Color(red: 1.0, green: 0.45, blue: 0.45)
        default: return .blue
        }
    }
    
    private var presaIcon: String {
        switch presa.key.lowercased() {
        case let k where k.contains("angostura"): return "drop.fill"
        case let k where k.contains("chicoas"): return "bolt.fill"
        case let k where k.contains("malpaso"): return "water.waves"
        case let k where k.contains("tap"): return "mountain.2.fill"
        case let k where k.contains("pe"): return "wind"
        default: return "drop.fill"
        }
    }
    
    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            // Header
            HStack {
                Image(systemName: presaIcon)
                    .font(.system(size: 18, weight: .bold))
                    .foregroundColor(presaColor)
                Text(presa.name)
                    .font(.system(size: 17, weight: .bold))
                    .foregroundColor(.white)
                Spacer()
                if let hora = presa.ultimaHora {
                    Text("Hora \(hora)")
                        .font(.system(size: 11))
                        .foregroundColor(.gray)
                        .padding(.horizontal, 8)
                        .padding(.vertical, 3)
                        .background(Color.white.opacity(0.08))
                        .cornerRadius(10)
                }
                Image(systemName: "chevron.right")
                    .font(.system(size: 12))
                    .foregroundColor(.gray)
            }
            .padding(.horizontal, 16)
            .padding(.top, 14)
            .padding(.bottom, 10)
            
            // Metrics grid
            HStack(spacing: 0) {
                MetricCell(label: "Elevación", value: formatFloat(presa.currentElev), unit: "msnm", color: .cyan)
                MetricCell(label: "Almac.", value: formatFloat(presa.almacenamiento), unit: "Mm³", color: .blue)
                MetricCell(label: "Generación", value: formatFloat(presa.generation), unit: "GWh", color: .yellow)
            }
            .padding(.horizontal, 12)
            
            HStack(spacing: 0) {
                MetricCell(label: "Aportaciones", value: formatFloat(presa.aportacionesV), unit: "Mm³", color: .green)
                MetricCell(label: "Extracciones", value: formatFloat(presa.extraccionesV), unit: "Mm³", color: .orange)
                MetricCell(label: "Unidades", value: presa.activeUnits != nil ? "\(presa.activeUnits!)" : "—", unit: "", color: .purple)
            }
            .padding(.horizontal, 12)
            .padding(.bottom, 14)
        }
        .background(
            RoundedRectangle(cornerRadius: 14)
                .fill(Color.white.opacity(0.06))
                .overlay(
                    RoundedRectangle(cornerRadius: 14)
                        .stroke(presaColor.opacity(0.25), lineWidth: 1)
                )
        )
    }
    
    private func formatFloat(_ value: Float?) -> String {
        guard let v = value else { return "—" }
        return String(format: "%.2f", v)
    }
}

struct MetricCell: View {
    let label: String
    let value: String
    let unit: String
    let color: Color
    
    var body: some View {
        VStack(spacing: 3) {
            Text(label)
                .font(.system(size: 10))
                .foregroundColor(.gray)
            Text(value)
                .font(.system(size: 16, weight: .bold, design: .monospaced))
                .foregroundColor(color)
            if !unit.isEmpty {
                Text(unit)
                    .font(.system(size: 9))
                    .foregroundColor(color.opacity(0.6))
            }
        }
        .frame(maxWidth: .infinity)
        .padding(.vertical, 8)
    }
}
