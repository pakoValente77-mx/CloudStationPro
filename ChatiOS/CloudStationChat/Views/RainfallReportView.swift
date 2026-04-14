import SwiftUI

struct RainfallReportView: View {
    @EnvironmentObject var authService: AuthService
    @StateObject private var service = RainfallService()

    @State private var selectedTipo = "parcial"
    private let tipos = [("parcial", "Parcial"), ("24h", "24 Horas")]

    var body: some View {
        NavigationView {
            ZStack {
                Color(red: 0.08, green: 0.08, blue: 0.12).ignoresSafeArea()

                ScrollView {
                    VStack(spacing: 14) {
                        tipoSelector
                        
                        if service.isLoading {
                            ProgressView("Consultando lluvia...")
                                .tint(.white)
                                .foregroundColor(.white)
                                .padding(.top, 40)
                        } else if let report = service.report {
                            reportHeader(report)
                            summaryCards(report)
                            subcuencasList(report)
                        } else if let error = service.errorMessage {
                            Text(error)
                                .foregroundColor(.red)
                                .padding(.top, 40)
                        } else {
                            Text("Selecciona tipo de reporte")
                                .foregroundColor(.gray)
                                .padding(.top, 60)
                        }
                    }
                    .padding(.horizontal)
                    .padding(.top, 8)
                    .padding(.bottom, 20)
                }
            }
            .navigationTitle("Corte de Lluvia")
            .navigationBarTitleDisplayMode(.inline)
            .toolbarColorScheme(.dark, for: .navigationBar)
            .toolbarBackground(Color(red: 0.08, green: 0.08, blue: 0.12), for: .navigationBar)
            .toolbarBackground(.visible, for: .navigationBar)
        }
        .onAppear {
            service.configure(serverUrl: authService.serverUrl, token: authService.token)
            Task { await service.loadReport(tipo: selectedTipo) }
        }
    }

    // MARK: - Tipo Selector

    private var tipoSelector: some View {
        HStack(spacing: 8) {
            ForEach(tipos, id: \.0) { (key, label) in
                Button(action: {
                    selectedTipo = key
                    Task { await service.loadReport(tipo: key) }
                }) {
                    Text(label)
                        .font(.system(size: 13, weight: .semibold))
                        .foregroundColor(selectedTipo == key ? .black : .white)
                        .padding(.horizontal, 16)
                        .padding(.vertical, 8)
                        .background(
                            RoundedRectangle(cornerRadius: 8)
                                .fill(selectedTipo == key ? Color.green : Color.white.opacity(0.1))
                        )
                }
            }
            Spacer()
        }
    }

    // MARK: - Header

    private func reportHeader(_ report: RainfallReportResponse) -> some View {
        VStack(spacing: 4) {
            Text(report.titulo)
                .font(.system(size: 15, weight: .bold))
                .foregroundColor(.white)
            Text("Período: \(report.periodoInicioLocal) — \(report.periodoFinLocal)")
                .font(.system(size: 12))
                .foregroundColor(.gray)
        }
        .frame(maxWidth: .infinity)
        .padding(.vertical, 10)
        .background(RoundedRectangle(cornerRadius: 10).fill(Color.white.opacity(0.06)))
    }

    // MARK: - Summary Cards

    private func summaryCards(_ report: RainfallReportResponse) -> some View {
        let maxLluvia = report.subcuencas.flatMap { $0.estaciones }.map { $0.acumuladoMm }.max() ?? 0
        let avgLluvia = report.subcuencas.flatMap { $0.estaciones }.map { $0.acumuladoMm }
        let avg = avgLluvia.isEmpty ? 0 : avgLluvia.reduce(0, +) / Double(avgLluvia.count)

        return HStack(spacing: 8) {
            summaryCard(title: "Estaciones", value: "\(report.totalEstaciones)", icon: "antenna.radiowaves.left.and.right", color: .cyan)
            summaryCard(title: "Con lluvia", value: "\(report.estacionesConLluvia)", icon: "cloud.rain", color: .green)
            summaryCard(title: "Máxima", value: String(format: "%.1f mm", maxLluvia), icon: "arrow.up", color: .orange)
            summaryCard(title: "Promedio", value: String(format: "%.1f mm", avg), icon: "equal", color: .purple)
        }
    }

    private func summaryCard(title: String, value: String, icon: String, color: Color) -> some View {
        VStack(spacing: 4) {
            Image(systemName: icon)
                .font(.system(size: 14))
                .foregroundColor(color)
            Text(value)
                .font(.system(size: 13, weight: .bold))
                .foregroundColor(.white)
                .lineLimit(1)
                .minimumScaleFactor(0.7)
            Text(title)
                .font(.system(size: 9))
                .foregroundColor(.gray)
        }
        .frame(maxWidth: .infinity)
        .padding(.vertical, 10)
        .background(RoundedRectangle(cornerRadius: 8).fill(Color.white.opacity(0.06)))
    }

    // MARK: - Subcuencas List

    private func subcuencasList(_ report: RainfallReportResponse) -> some View {
        let globalMax = report.subcuencas.flatMap { $0.estaciones }.map { $0.acumuladoMm }.max() ?? 1

        return ForEach(report.subcuencas) { sub in
            VStack(spacing: 0) {
                // Subcuenca header
                HStack {
                    Image(systemName: "drop.fill")
                        .foregroundColor(.white)
                        .font(.system(size: 12))
                    Text(sub.subcuenca)
                        .font(.system(size: 14, weight: .bold))
                        .foregroundColor(.white)
                    Spacer()
                    Text("Prom: \(String(format: "%.1f", sub.promedioMm)) mm")
                        .font(.system(size: 12, weight: .semibold))
                        .foregroundColor(.white.opacity(0.9))
                }
                .padding(.horizontal, 12)
                .padding(.vertical, 8)
                .background(
                    LinearGradient(
                        colors: [Color(red: 0.13, green: 0.55, blue: 0.13), Color(red: 0.18, green: 0.65, blue: 0.18)],
                        startPoint: .leading, endPoint: .trailing
                    )
                )
                .cornerRadius(8, corners: [.topLeft, .topRight])

                // Stations
                VStack(spacing: 0) {
                    ForEach(Array(sub.estaciones.enumerated()), id: \.element.id) { index, est in
                        HStack(spacing: 8) {
                            Text(est.nombre)
                                .font(.system(size: 11))
                                .foregroundColor(.white)
                                .frame(width: 120, alignment: .leading)
                                .lineLimit(1)

                            GeometryReader { geo in
                                let barWidth = globalMax > 0
                                    ? CGFloat(est.acumuladoMm / globalMax) * geo.size.width
                                    : 0
                                RoundedRectangle(cornerRadius: 3)
                                    .fill(
                                        LinearGradient(
                                            colors: [Color.green.opacity(0.7), Color.green],
                                            startPoint: .leading, endPoint: .trailing
                                        )
                                    )
                                    .frame(width: max(barWidth, 2), height: 14)
                            }
                            .frame(height: 14)

                            Text(String(format: "%.1f", est.acumuladoMm))
                                .font(.system(size: 11, weight: .semibold, design: .monospaced))
                                .foregroundColor(.cyan)
                                .frame(width: 50, alignment: .trailing)
                        }
                        .padding(.horizontal, 12)
                        .padding(.vertical, 5)
                        .background(
                            index % 2 == 0
                                ? Color.white.opacity(0.03)
                                : Color.clear
                        )
                    }
                }
                .background(Color.white.opacity(0.04))
                .cornerRadius(8, corners: [.bottomLeft, .bottomRight])
            }
            .padding(.top, 6)
        }
    }
}

// MARK: - Corner Radius Extension

extension View {
    func cornerRadius(_ radius: CGFloat, corners: UIRectCorner) -> some View {
        clipShape(RoundedCornerShape(radius: radius, corners: corners))
    }
}

struct RoundedCornerShape: Shape {
    var radius: CGFloat
    var corners: UIRectCorner

    func path(in rect: CGRect) -> Path {
        let path = UIBezierPath(
            roundedRect: rect,
            byRoundingCorners: corners,
            cornerRadii: CGSize(width: radius, height: radius)
        )
        return Path(path.cgPath)
    }
}
