import Foundation
import Combine

@MainActor
class RainfallService: NSObject, ObservableObject, URLSessionDelegate {
    @Published var report: RainfallReportResponse?
    @Published var isLoading = false
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

    func loadReport(tipo: String = "parcial", fecha: String? = nil) async {
        guard !serverUrl.isEmpty else { return }
        isLoading = true
        errorMessage = nil
        report = nil
        defer { isLoading = false }

        do {
            var urlString = "\(serverUrl)/api/lluvia/reporte?tipo=\(tipo)"
            if let f = fecha { urlString += "&fecha=\(f)" }
            guard let url = URL(string: urlString) else { return }

            var request = URLRequest(url: url)
            request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")

            let (data, response) = try await urlSession.data(for: request)
            guard let http = response as? HTTPURLResponse, http.statusCode == 200 else {
                errorMessage = "Error cargando reporte"
                return
            }
            report = try JSONDecoder().decode(RainfallReportResponse.self, from: data)
        } catch {
            errorMessage = "Error: \(error.localizedDescription)"
        }
    }
}
