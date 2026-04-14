import Foundation

@MainActor
class MapService: NSObject, ObservableObject, URLSessionDelegate {
    @Published var stations: [StationMapData] = []
    @Published var isLoading = false
    @Published var errorMessage: String?
    @Published var selectedVariable: String = "precipitación"
    @Published var availableVariables: [MapVariable] = MapVariable.defaults
    
    struct MapVariable: Identifiable, Hashable {
        let value: String
        let label: String
        var id: String { value }
        
        static let defaults: [MapVariable] = [
            MapVariable(value: "precipitación", label: "Precipitación"),
            MapVariable(value: "nivel_de_agua", label: "Nivel de Agua"),
            MapVariable(value: "temperatura", label: "Temperatura"),
            MapVariable(value: "humedad_relativa", label: "Humedad Relativa"),
            MapVariable(value: "velocidad_del_viento", label: "Velocidad del Viento"),
            MapVariable(value: "voltaje_de_batería", label: "Voltaje de Batería"),
        ]
    }
    
    private var serverUrl = ""
    private var token = ""
    private var urlSession: URLSession!
    
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
    
    nonisolated func urlSession(_ session: URLSession, didReceive challenge: URLAuthenticationChallenge,
                                completionHandler: @escaping (URLSession.AuthChallengeDisposition, URLCredential?) -> Void) {
        if let trust = challenge.protectionSpace.serverTrust {
            completionHandler(.useCredential, URLCredential(trust: trust))
        } else {
            completionHandler(.performDefaultHandling, nil)
        }
    }
    
    func loadMapData() async {
        guard !serverUrl.isEmpty else { return }
        isLoading = true
        errorMessage = nil
        defer { isLoading = false }
        
        do {
            // Load available variables from server on first call
            if availableVariables == MapVariable.defaults {
                await loadVariablesFromServer()
            }
            
            var components = URLComponents(string: "\(serverUrl)/Map/GetMapData")!
            components.queryItems = [
                URLQueryItem(name: "variable", value: selectedVariable),
                URLQueryItem(name: "onlyCfe", value: "true")
            ]
            
            var request = URLRequest(url: components.url!)
            request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
            
            let (data, response) = try await urlSession.data(for: request)
            guard let http = response as? HTTPURLResponse, http.statusCode == 200 else {
                errorMessage = "Error del servidor"
                return
            }
            stations = try JSONDecoder().decode([StationMapData].self, from: data)
        } catch {
            errorMessage = error.localizedDescription
        }
    }
    
    func changeVariable(_ variable: String) async {
        selectedVariable = variable
        await loadMapData()
    }
    
    private func loadVariablesFromServer() async {
        guard let url = URL(string: "\(serverUrl)/Map/GetVariables") else { return }
        var request = URLRequest(url: url)
        request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        
        do {
            let (data, response) = try await urlSession.data(for: request)
            guard let http = response as? HTTPURLResponse, http.statusCode == 200 else { return }
            
            struct ServerVariable: Decodable {
                let value: String
                let label: String
            }
            let serverVars = try JSONDecoder().decode([ServerVariable].self, from: data)
            if !serverVars.isEmpty {
                availableVariables = serverVars.map { MapVariable(value: $0.value, label: $0.label) }
            }
        } catch {
            // Keep defaults if server call fails
        }
    }
}
