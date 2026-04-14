import Foundation
import Combine

@MainActor
class StationDataService: NSObject, ObservableObject, URLSessionDelegate {
    @Published var stations: [StationInfo] = []
    @Published var variables: [StationVariable] = []
    @Published var analysisData: DataAnalysisResponse?
    @Published var isLoadingStations = false
    @Published var isLoadingVariables = false
    @Published var isLoadingData = false
    @Published var errorMessage: String?
    
    private var serverUrl: String = ""
    private var token: String = ""
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
    
    // MARK: - TLS bypass
    nonisolated func urlSession(_ session: URLSession, didReceive challenge: URLAuthenticationChallenge,
                                completionHandler: @escaping (URLSession.AuthChallengeDisposition, URLCredential?) -> Void) {
        if let trust = challenge.protectionSpace.serverTrust {
            completionHandler(.useCredential, URLCredential(trust: trust))
        } else {
            completionHandler(.performDefaultHandling, nil)
        }
    }
    
    // MARK: - Load Stations
    func loadStations(onlyCfe: Bool = true) async {
        guard !serverUrl.isEmpty else { return }
        isLoadingStations = true
        errorMessage = nil
        defer { isLoadingStations = false }
        
        do {
            let url = URL(string: "\(serverUrl)/DataAnalysis/GetStations?onlyCfe=\(onlyCfe)")!
            var request = URLRequest(url: url)
            request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
            
            let (data, response) = try await urlSession.data(for: request)
            guard let http = response as? HTTPURLResponse, http.statusCode == 200 else {
                errorMessage = "Error cargando estaciones"
                return
            }
            stations = try JSONDecoder().decode([StationInfo].self, from: data)
        } catch {
            errorMessage = "Error: \(error.localizedDescription)"
        }
    }
    
    // MARK: - Load Variables for Station
    func loadVariables(stationId: String) async {
        guard !serverUrl.isEmpty else { return }
        isLoadingVariables = true
        variables = []
        defer { isLoadingVariables = false }
        
        do {
            let encoded = stationId.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed) ?? stationId
            let url = URL(string: "\(serverUrl)/DataAnalysis/GetStationVariables?stationId=\(encoded)")!
            var request = URLRequest(url: url)
            request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
            
            let (data, response) = try await urlSession.data(for: request)
            guard let http = response as? HTTPURLResponse, http.statusCode == 200 else {
                return
            }
            variables = try JSONDecoder().decode([StationVariable].self, from: data)
        } catch {
            print("Error loading variables: \(error)")
        }
    }
    
    // MARK: - Load Analysis Data
    func loadAnalysisData(stationIds: [String], variable: String, startDate: Date, endDate: Date) async {
        guard !serverUrl.isEmpty else { return }
        isLoadingData = true
        errorMessage = nil
        analysisData = nil
        defer { isLoadingData = false }
        
        do {
            let url = URL(string: "\(serverUrl)/DataAnalysis/GetAnalysisData")!
            var request = URLRequest(url: url)
            request.httpMethod = "POST"
            request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
            request.setValue("application/json", forHTTPHeaderField: "Content-Type")
            
            let iso = ISO8601DateFormatter()
            let body = DataAnalysisRequest(
                stationIds: stationIds,
                variable: variable,
                startDate: iso.string(from: startDate),
                endDate: iso.string(from: endDate)
            )
            request.httpBody = try JSONEncoder().encode(body)
            
            let (data, response) = try await urlSession.data(for: request)
            guard let http = response as? HTTPURLResponse, http.statusCode == 200 else {
                errorMessage = "Error del servidor"
                return
            }
            analysisData = try JSONDecoder().decode(DataAnalysisResponse.self, from: data)
        } catch {
            errorMessage = "Error: \(error.localizedDescription)"
        }
    }
}
