import SwiftUI

struct SettingsView: View {
    @EnvironmentObject var authService: AuthService
    
    var body: some View {
        NavigationStack {
            List {
                Section("Cuenta") {
                    HStack {
                        ZStack {
                            Circle()
                                .fill(Color.purple.opacity(0.3))
                                .frame(width: 50, height: 50)
                            Text(String(authService.fullName.prefix(1)).uppercased())
                                .font(.title2.bold())
                                .foregroundColor(.purple)
                        }
                        VStack(alignment: .leading) {
                            Text(authService.fullName)
                                .font(.headline)
                            Text("@\(authService.userName)")
                                .font(.caption)
                                .foregroundColor(.gray)
                        }
                    }
                    .padding(.vertical, 4)
                }
                
                Section("Servidor") {
                    HStack {
                        Image(systemName: "server.rack")
                            .foregroundColor(.purple)
                        Text(authService.serverUrl)
                            .font(.system(size: 13, design: .monospaced))
                            .foregroundColor(.gray)
                    }
                }
                
                Section("Información") {
                    HStack {
                        Text("Versión")
                        Spacer()
                        Text("1.0.0")
                            .foregroundColor(.gray)
                    }
                    HStack {
                        Text("Plataforma")
                        Spacer()
                        Text("iOS 📱")
                            .foregroundColor(.gray)
                    }
                }
                
                Section {
                    Button(role: .destructive, action: { authService.logout() }) {
                        HStack {
                            Image(systemName: "rectangle.portrait.and.arrow.right")
                            Text("Cerrar Sesión")
                        }
                    }
                }
            }
            .navigationTitle("Ajustes")
            .navigationBarTitleDisplayMode(.inline)
        }
    }
}
