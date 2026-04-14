import Foundation
import Combine

class AuthService: NSObject, ObservableObject, URLSessionDelegate {
    @Published var isAuthenticated = false
    @Published var token: String = ""
    @Published var userName: String = ""
    @Published var fullName: String = ""
    @Published var roles: [String] = []
    @Published var serverUrl: String = ""
    @Published var errorMessage: String?
    @Published var isLoading = false
    
    private let serverUrlKey = "server_url"
    private let tokenKey = "auth_token"
    private let userNameKey = "user_name"
    private let fullNameKey = "full_name"
    
    private var urlSession: URLSession!
    
    override init() {
        super.init()
        let config = URLSessionConfiguration.ephemeral
        config.timeoutIntervalForRequest = 30
        config.timeoutIntervalForResource = 60
        config.waitsForConnectivity = true
        config.requestCachePolicy = .reloadIgnoringLocalCacheData
        urlSession = URLSession(configuration: config, delegate: self, delegateQueue: nil)
        loadSaved()
    }
    
    // Accept any server certificate (development flexibility)
    func urlSession(_ session: URLSession, didReceive challenge: URLAuthenticationChallenge,
                    completionHandler: @escaping (URLSession.AuthChallengeDisposition, URLCredential?) -> Void) {
        if challenge.protectionSpace.authenticationMethod == NSURLAuthenticationMethodServerTrust,
           let trust = challenge.protectionSpace.serverTrust {
            completionHandler(.useCredential, URLCredential(trust: trust))
        } else {
            completionHandler(.performDefaultHandling, nil)
        }
    }
    
    private func loadSaved() {
        serverUrl = UserDefaults.standard.string(forKey: serverUrlKey) ?? ""
        token = UserDefaults.standard.string(forKey: tokenKey) ?? ""
        userName = UserDefaults.standard.string(forKey: userNameKey) ?? ""
        fullName = UserDefaults.standard.string(forKey: fullNameKey) ?? ""
        if !token.isEmpty && !serverUrl.isEmpty {
            isAuthenticated = true
        }
    }
    
    func login(server: String, user: String, password: String) async {
        await MainActor.run { isLoading = true; errorMessage = nil }
        
        let baseUrl: String
        do {
            var u = server.trimmingCharacters(in: .whitespacesAndNewlines)
            if !u.hasPrefix("http") { u = "https://\(u)" }
            if u.hasSuffix("/") { u.removeLast() }
            baseUrl = u
        }
        
        guard let url = URL(string: "\(baseUrl)/api/auth/login") else {
            await MainActor.run { errorMessage = "URL inválida"; isLoading = false }
            return
        }
        
        let bodyDict: [String: String] = ["userName": user, "password": password]
        guard let bodyData = try? JSONSerialization.data(withJSONObject: bodyDict) else {
            await MainActor.run { errorMessage = "Error interno"; isLoading = false }
            return
        }
        
        // Retry up to 2 times for -1005 error
        var lastError: NSError?
        for attempt in 1...3 {
            var request = URLRequest(url: url)
            request.httpMethod = "POST"
            request.setValue("application/json", forHTTPHeaderField: "Content-Type")
            request.setValue("application/json", forHTTPHeaderField: "Accept")
            
            do {
                let (data, response) = try await urlSession.upload(for: request, from: bodyData)
                guard let httpResponse = response as? HTTPURLResponse else {
                    throw URLError(.badServerResponse)
                }
                
                if httpResponse.statusCode == 200 {
                    let loginResp = try JSONDecoder().decode(LoginResponse.self, from: data)
                    await MainActor.run {
                        self.token = loginResp.token
                        self.userName = loginResp.usuario
                        self.fullName = loginResp.nombre
                        self.roles = loginResp.roles
                        self.serverUrl = baseUrl
                        self.isAuthenticated = true
                        self.isLoading = false
                        
                        UserDefaults.standard.set(baseUrl, forKey: self.serverUrlKey)
                        UserDefaults.standard.set(loginResp.token, forKey: self.tokenKey)
                        UserDefaults.standard.set(loginResp.usuario, forKey: self.userNameKey)
                        UserDefaults.standard.set(loginResp.nombre, forKey: self.fullNameKey)
                    }
                    return // Success
                } else {
                    let msg = String(data: data, encoding: .utf8) ?? "Error HTTP \(httpResponse.statusCode)"
                    await MainActor.run { errorMessage = msg; isLoading = false }
                    return
                }
            } catch let error as NSError {
                lastError = error
                if error.code == -1005 && attempt < 3 {
                    // Retry after brief pause for -1005
                    try? await Task.sleep(nanoseconds: 500_000_000)
                    continue
                }
                break
            }
        }
        
        let detail = lastError.map { "\($0.localizedDescription) (code: \($0.code), domain: \($0.domain))" } ?? "Error desconocido"
        await MainActor.run { errorMessage = detail; isLoading = false }
    }
    
    func logout() {
        token = ""
        userName = ""
        fullName = ""
        roles = []
        isAuthenticated = false
        UserDefaults.standard.removeObject(forKey: tokenKey)
        UserDefaults.standard.removeObject(forKey: userNameKey)
        UserDefaults.standard.removeObject(forKey: fullNameKey)
    }
}
