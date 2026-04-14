package mx.cfe.grijalva.pih.data.service

import android.util.Log
import com.google.firebase.messaging.FirebaseMessagingService
import com.google.firebase.messaging.RemoteMessage
import mx.cfe.grijalva.pih.data.network.NetworkModule

class PIHFirebaseMessagingService : FirebaseMessagingService() {
    private val TAG = "PIHFcm"

    override fun onNewToken(token: String) {
        super.onNewToken(token)
        Log.d(TAG, "New FCM token: $token")
        NetworkModule.getPrefs(applicationContext).edit()
            .putString("fcm_token", token)
            .apply()
    }

    override fun onMessageReceived(message: RemoteMessage) {
        super.onMessageReceived(message)

        val data = message.data
        val title = message.notification?.title ?: data["sender"] ?: "PIH"
        val body = message.notification?.body ?: data["message"] ?: ""
        val room = data["room"] ?: "general"

        NotificationHelper.showMessageNotification(
            applicationContext, title, body, room, ""
        )
    }
}
