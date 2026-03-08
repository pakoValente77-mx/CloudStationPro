import SwiftUI
import MapKit

struct MapView: View {
    @ObservedObject var api: APIService
    @State private var region = MKCoordinateRegion(
        center: CLLocationCoordinate2D(latitude: 17.5, longitude: -93.0),
        span: MKCoordinateSpan(latitudeDelta: 3.0, longitudeDelta: 3.0)
    )
    
    var body: some View {
        ZStack {
            Map(coordinateRegion: $region, annotationItems: api.stations) { station in
                MapAnnotation(coordinate: CLLocationCoordinate2D(latitude: station.lat ?? 0, longitude: station.lon ?? 0)) {
                    VStack {
                        Circle()
                            .fill(statusColor(station.estatusColor))
                            .frame(width: 14, height: 14)
                            .overlay(Circle().stroke(Color.white, lineWidth: 2))
                            .shadow(radius: 4)
                        
                        Text(station.nombre)
                            .font(.caption2)
                            .padding(4)
                            .background(GlassView())
                    }
                }
            }
            .ignoresSafeArea()
            
            VStack {
                Spacer()
                HStack {
                    GlassView()
                        .frame(height: 60)
                        .overlay(
                            HStack {
                                Image(systemName: "clock.fill")
                                Text("Sincronizado: \(Date().formatted(date: .omitted, time: .shortened))")
                                    .font(.system(size: 12, weight: .bold))
                            }
                            .foregroundColor(.white)
                        )
                        .padding()
                }
            }
        }
    }
    
    func statusColor(_ color: String) -> Color {
        switch color.uppercased() {
        case "VERDE": return .successGreen
        case "AMARILLO": return .warningYellow
        case "ROJO": return .criticalRed
        default: return .gray
        }
    }
}
