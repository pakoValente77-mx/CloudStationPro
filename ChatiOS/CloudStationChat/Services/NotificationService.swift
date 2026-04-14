import Foundation
import UserNotifications
import UIKit

class NotificationService {
    static let shared = NotificationService()
    
    private init() {}
    
    /// Show local notification for an incoming chat message
    func showMessageNotification(sender: String, message: String, room: String, isSelf: Bool = false) {
        // Don't notify for own messages
        guard !isSelf else { return }
        
        // Don't notify if app is active and user is viewing that room
        if UIApplication.shared.applicationState == .active {
            let currentRoom = UserDefaults.standard.string(forKey: "current_chat_room")
            if currentRoom == room {
                return // User is looking at this room already
            }
        }
        
        let content = UNMutableNotificationContent()
        content.title = sender
        content.body = message
        content.sound = .default
        content.userInfo = ["room": room, "sender": sender, "isSelf": isSelf]
        content.categoryIdentifier = "CHAT_MESSAGE"
        
        // Increment badge
        let currentBadge = UIApplication.shared.applicationIconBadgeNumber
        content.badge = NSNumber(value: currentBadge + 1)
        
        // Unique ID per message to avoid duplicates
        let requestId = "chat_\(room)_\(Date().timeIntervalSince1970)"
        let request = UNNotificationRequest(identifier: requestId, content: content, trigger: nil)
        
        UNUserNotificationCenter.current().add(request) { error in
            if let error = error {
                print("Error showing notification: \(error)")
            }
        }
    }
    
    /// Clear badge count
    func clearBadge() {
        DispatchQueue.main.async {
            UIApplication.shared.applicationIconBadgeNumber = 0
        }
    }
    
    /// Register device token with the backend server
    func registerDeviceToken() {
        guard let token = UserDefaults.standard.string(forKey: "apns_device_token"),
              let serverURL = UserDefaults.standard.string(forKey: "server_url"),
              let jwtToken = UserDefaults.standard.string(forKey: "auth_token") else {
            return
        }
        
        let urlString = "\(serverURL)/api/MobileApi/RegisterDevice"
        guard let url = URL(string: urlString) else { return }
        
        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.setValue("Bearer \(jwtToken)", forHTTPHeaderField: "Authorization")
        
        let body: [String: String] = [
            "token": token,
            "platform": "ios"
        ]
        
        request.httpBody = try? JSONSerialization.data(withJSONObject: body)
        
        let config = URLSessionConfiguration.default
        config.httpAdditionalHeaders = ["Accept": "application/json"]
        config.protocolClasses = nil
        let session = URLSession(configuration: config)
        
        session.dataTask(with: request) { data, response, error in
            if let error = error {
                print("Failed to register device token: \(error)")
                return
            }
            if let httpResponse = response as? HTTPURLResponse {
                print("Device token registration: \(httpResponse.statusCode)")
            }
        }.resume()
    }
    
    /// Unregister device token from server (call on logout)
    func unregisterDeviceToken() {
        guard let token = UserDefaults.standard.string(forKey: "apns_device_token"),
              let serverURL = UserDefaults.standard.string(forKey: "server_url"),
              let jwtToken = UserDefaults.standard.string(forKey: "auth_token") else {
            return
        }
        
        let urlString = "\(serverURL)/api/MobileApi/UnregisterDevice"
        guard let url = URL(string: urlString) else { return }
        
        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.setValue("Bearer \(jwtToken)", forHTTPHeaderField: "Authorization")
        
        let body: [String: String] = [
            "token": token,
            "platform": "ios"
        ]
        
        request.httpBody = try? JSONSerialization.data(withJSONObject: body)
        
        let config = URLSessionConfiguration.default
        config.httpAdditionalHeaders = ["Accept": "application/json"]
        config.protocolClasses = nil
        let session = URLSession(configuration: config)
        
        session.dataTask(with: request) { _, _, _ in }.resume()
    }
}
