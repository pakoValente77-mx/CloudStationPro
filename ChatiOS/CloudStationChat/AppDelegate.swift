import UIKit
import UserNotifications

class AppDelegate: NSObject, UIApplicationDelegate, UNUserNotificationCenterDelegate {
    
    func application(_ application: UIApplication, didFinishLaunchingWithOptions launchOptions: [UIApplication.LaunchOptionsKey: Any]?) -> Bool {
        UNUserNotificationCenter.current().delegate = self
        requestNotificationPermission()
        return true
    }
    
    // MARK: - Request Permission
    
    private func requestNotificationPermission() {
        UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .badge, .sound]) { granted, error in
            if granted {
                DispatchQueue.main.async {
                    UIApplication.shared.registerForRemoteNotifications()
                }
            }
            if let error = error {
                print("Notification permission error: \(error)")
            }
        }
    }
    
    // MARK: - APNs Token
    
    func application(_ application: UIApplication, didRegisterForRemoteNotificationsWithDeviceToken deviceToken: Data) {
        let token = deviceToken.map { String(format: "%02.2hhx", $0) }.joined()
        print("APNs Device Token: \(token)")
        // Store for later registration with server
        UserDefaults.standard.set(token, forKey: "apns_device_token")
        NotificationCenter.default.post(name: .deviceTokenReceived, object: token)
    }
    
    func application(_ application: UIApplication, didFailToRegisterForRemoteNotificationsWithError error: Error) {
        print("Failed to register for remote notifications: \(error)")
    }
    
    // MARK: - Foreground Notification Display
    
    func userNotificationCenter(_ center: UNUserNotificationCenter, willPresent notification: UNNotification, withCompletionHandler completionHandler: @escaping (UNNotificationPresentationOptions) -> Void) {
        // Show banner + sound even when app is in foreground (but only for other users' messages)
        let userInfo = notification.request.content.userInfo
        if let isSelf = userInfo["isSelf"] as? Bool, isSelf {
            completionHandler([]) // Don't show own messages
        } else {
            completionHandler([.banner, .sound, .badge])
        }
    }
    
    // MARK: - Notification Tap Handler
    
    func userNotificationCenter(_ center: UNUserNotificationCenter, didReceive response: UNNotificationResponse, withCompletionHandler completionHandler: @escaping () -> Void) {
        let userInfo = response.notification.request.content.userInfo
        if let room = userInfo["room"] as? String {
            NotificationCenter.default.post(name: .chatNotificationTapped, object: room)
        }
        completionHandler()
    }
}

// MARK: - Notification Names

extension Notification.Name {
    static let deviceTokenReceived = Notification.Name("deviceTokenReceived")
    static let chatNotificationTapped = Notification.Name("chatNotificationTapped")
}
