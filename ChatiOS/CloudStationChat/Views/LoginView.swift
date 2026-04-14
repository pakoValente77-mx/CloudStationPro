import SwiftUI

struct LoginView: View {
    @EnvironmentObject var authService: AuthService
    @State private var username = ""
    @State private var password = ""
    @State private var showPassword = false
    @State private var showServerConfig = false
    
    private var serverUrl: String {
        UserDefaults.standard.string(forKey: "server_url") ?? "https://hidrometria.mx"
    }
    
    var body: some View {
        ZStack {
            Color(red: 0.08, green: 0.08, blue: 0.12)
                .ignoresSafeArea()
            
            VStack(spacing: 24) {
                Spacer()
                
                // Logo
                Image(systemName: "cloud.fill")
                    .font(.system(size: 60))
                    .foregroundColor(.purple)
                
                Text("PIH")
                    .font(.system(size: 42, weight: .bold, design: .rounded))
                    .foregroundColor(.white)
                
                Text("Plataforma Integral Hidrometeorológica")
                    .font(.system(size: 14, weight: .medium))
                    .foregroundColor(.gray)
                
                // Server indicator
                Button { showServerConfig = true } label: {
                    HStack(spacing: 6) {
                        Image(systemName: "server.rack")
                            .font(.system(size: 11))
                        Text(serverUrl)
                            .font(.system(size: 11))
                        Image(systemName: "chevron.right")
                            .font(.system(size: 9))
                    }
                    .foregroundColor(.gray)
                    .padding(.horizontal, 12)
                    .padding(.vertical, 6)
                    .background(Color.white.opacity(0.06))
                    .cornerRadius(8)
                }
                
                // Form
                VStack(spacing: 16) {
                    // Username
                    HStack {
                        Image(systemName: "person.fill")
                            .foregroundColor(.gray)
                        TextField("Usuario", text: $username)
                            .textContentType(.username)
                            .autocapitalization(.none)
                            .disableAutocorrection(true)
                            .foregroundColor(.white)
                    }
                    .padding()
                    .background(Color.white.opacity(0.08))
                    .cornerRadius(12)
                    
                    // Password
                    HStack {
                        Image(systemName: "lock.fill")
                            .foregroundColor(.gray)
                        if showPassword {
                            TextField("Contraseña", text: $password)
                                .foregroundColor(.white)
                        } else {
                            SecureField("Contraseña", text: $password)
                                .foregroundColor(.white)
                        }
                        Button(action: { showPassword.toggle() }) {
                            Image(systemName: showPassword ? "eye.slash" : "eye")
                                .foregroundColor(.gray)
                        }
                    }
                    .padding()
                    .background(Color.white.opacity(0.08))
                    .cornerRadius(12)
                }
                .padding(.horizontal)
                
                // Error
                if let error = authService.errorMessage {
                    Text(error)
                        .font(.caption)
                        .foregroundColor(.red)
                        .padding(.horizontal)
                }
                
                // Login button
                Button(action: doLogin) {
                    HStack {
                        if authService.isLoading {
                            ProgressView()
                                .tint(.white)
                        }
                        Text("Iniciar Sesión")
                            .fontWeight(.semibold)
                    }
                    .frame(maxWidth: .infinity)
                    .padding()
                    .background(LinearGradient(colors: [.purple, .blue], startPoint: .leading, endPoint: .trailing))
                    .foregroundColor(.white)
                    .cornerRadius(12)
                }
                .disabled(authService.isLoading || username.isEmpty || password.isEmpty)
                .padding(.horizontal)
                
                Spacer()
                
                Text("CFE - Subgerencia HidroGrijalva")
                    .font(.caption2)
                    .foregroundColor(.gray)
                    .padding(.bottom)
            }
        }
        .sheet(isPresented: $showServerConfig) {
            ServerConfigSheet()
        }
    }
    
    private func doLogin() {
        Task {
            await authService.login(server: serverUrl, user: username, password: password)
        }
    }
}

// MARK: - Server Configuration Sheet

struct ServerConfigSheet: View {
    @Environment(\.dismiss) var dismiss
    @State private var serverText: String = ""
    
    var body: some View {
        NavigationView {
            ZStack {
                Color(red: 0.08, green: 0.08, blue: 0.12).ignoresSafeArea()
                
                VStack(spacing: 20) {
                    // Server URL field
                    VStack(alignment: .leading, spacing: 8) {
                        Text("URL del Servidor")
                            .font(.system(size: 13, weight: .semibold))
                            .foregroundColor(.gray)
                        HStack {
                            Image(systemName: "server.rack")
                                .foregroundColor(.purple)
                            TextField("https://hidrometria.mx", text: $serverText)
                                .textContentType(.URL)
                                .autocapitalization(.none)
                                .disableAutocorrection(true)
                                .foregroundColor(.white)
                        }
                        .padding()
                        .background(Color.white.opacity(0.08))
                        .cornerRadius(12)
                    }
                    .padding(.horizontal)
                    
                    Spacer()
                }
                .padding(.top, 16)
            }
            .navigationTitle("Servidor")
            .navigationBarTitleDisplayMode(.inline)
            .toolbarColorScheme(.dark, for: .navigationBar)
            .toolbarBackground(Color(red: 0.08, green: 0.08, blue: 0.12), for: .navigationBar)
            .toolbarBackground(.visible, for: .navigationBar)
            .toolbar {
                ToolbarItem(placement: .navigationBarLeading) {
                    Button("Cancelar") { dismiss() }
                        .foregroundColor(.gray)
                }
                ToolbarItem(placement: .navigationBarTrailing) {
                    Button("Guardar") {
                        let trimmed = serverText.trimmingCharacters(in: .whitespacesAndNewlines)
                        if !trimmed.isEmpty {
                            UserDefaults.standard.set(trimmed, forKey: "server_url")
                        }
                        dismiss()
                    }
                    .fontWeight(.semibold)
                    .foregroundColor(.purple)
                    .disabled(serverText.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
                }
            }
        }
        .presentationDetents([.medium])
        .onAppear {
            serverText = UserDefaults.standard.string(forKey: "server_url") ?? "https://hidrometria.mx"
        }
    }
}
