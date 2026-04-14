import Foundation
import Combine

@MainActor
class FunVasosService: NSObject, ObservableObject, URLSessionDelegate {
    @Published var cascadeData: [CascadePresa] = []
    @Published var presaDetail: FunVasosResumenPresa?
    @Published var allPresas: [FunVasosResumenPresa] = []
    @Published var fechasDisponibles: [String] = []
    @Published var selectedFecha: String = ""
    @Published var isLoading = false
    @Published var errorMessage: String?
    
    private var serverUrl: String = ""
    private var token: String = ""
    private var urlSession: URLSession!
    private var autoRefreshTask: Task<Void, Never>?
    
    override init() {
        super.init()
        let config = URLSessionConfiguration.ephemeral
        config.timeoutIntervalForRequest = 30
        config.timeoutIntervalForResource = 60
        config.requestCachePolicy = .reloadIgnoringLocalCacheData
        config.httpAdditionalHeaders = ["Accept": "application/json"]
        config.protocolClasses = nil
        urlSession = URLSession(configuration: config, delegate: self, delegateQueue: nil)
    }
    
    func configure(serverUrl: String, token: String) {
        self.serverUrl = serverUrl
        self.token = token
    }
    
    // MARK: - Auto Refresh
    func startAutoRefresh() {
        stopAutoRefresh()
        autoRefreshTask = Task {
            while !Task.isCancelled {
                try? await Task.sleep(nanoseconds: 5 * 60 * 1_000_000_000) // 5 minutos
                if Task.isCancelled { break }
                await loadCascade()
            }
        }
    }
    
    func stopAutoRefresh() {
        autoRefreshTask?.cancel()
        autoRefreshTask = nil
    }
    
    // MARK: - TLS bypass
    nonisolated func urlSession(_ session: URLSession, didReceive challenge: URLAuthenticationChallenge,
                                completionHandler: @escaping (URLSession.AuthChallengeDisposition, URLCredential?) -> Void) {
        if let trust = challenge.protectionSpace.serverTrust {
            completionHandler(.useCredential, URLCredential(trust: trust))
        } else {
            completionHandler(.performDefaultHandling, nil)
        }
    }
    
    // MARK: - Cascade overview (main screen)
    func loadCascade() async {
        guard !serverUrl.isEmpty else { return }
        isLoading = true
        errorMessage = nil
        defer { isLoading = false }
        
        do {
            let url = URL(string: "\(serverUrl)/FunVasos/GetCascadeData")!
            var request = URLRequest(url: url)
            request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
            
            let (data, response) = try await urlSession.data(for: request)
            guard let http = response as? HTTPURLResponse, http.statusCode == 200 else {
                errorMessage = "Error del servidor"
                return
            }
            let decoded = try JSONDecoder().decode(CascadeResponse.self, from: data)
            cascadeData = decoded.presas
        } catch {
            errorMessage = error.localizedDescription
        }
    }
    
    // MARK: - Full data for a date range
    func loadData(fechaInicio: String? = nil, fechaFin: String? = nil) async {
        guard !serverUrl.isEmpty else { return }
        isLoading = true
        errorMessage = nil
        defer { isLoading = false }
        
        do {
            var components = URLComponents(string: "\(serverUrl)/FunVasos/GetData")!
            var queryItems: [URLQueryItem] = []
            if let fi = fechaInicio { queryItems.append(URLQueryItem(name: "fechaInicio", value: fi)) }
            if let ff = fechaFin { queryItems.append(URLQueryItem(name: "fechaFin", value: ff)) }
            if !queryItems.isEmpty { components.queryItems = queryItems }
            
            var request = URLRequest(url: components.url!)
            request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
            
            let (data, response) = try await urlSession.data(for: request)
            guard let http = response as? HTTPURLResponse, http.statusCode == 200 else {
                errorMessage = "Error del servidor"
                return
            }
            let decoded = try JSONDecoder().decode(FunVasosViewModel.self, from: data)
            allPresas = decoded.presas
            fechasDisponibles = decoded.fechasDisponibles
            if selectedFecha.isEmpty, let first = decoded.fechasDisponibles.first {
                selectedFecha = first
            }
        } catch {
            errorMessage = error.localizedDescription
        }
    }
    
    // MARK: - Load data for a specific date
    func loadDataForDate(_ fecha: String) async {
        selectedFecha = fecha
        await loadData(fechaInicio: fecha, fechaFin: fecha)
    }
}
