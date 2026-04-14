package mx.cfe.grijalva.pih

import android.app.Application
import android.app.NotificationChannel
import android.app.NotificationManager
import android.os.Build

class PIHApplication : Application() {
    override fun onCreate() {
        super.onCreate()
        createNotificationChannels()
    }

    private fun createNotificationChannels() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            val manager = getSystemService(NotificationManager::class.java)

            val chatChannel = NotificationChannel(
                "pih_chat",
                "Mensajes de Chat",
                NotificationManager.IMPORTANCE_HIGH
            ).apply {
                description = "Notificaciones de mensajes del chat PIH"
                enableVibration(true)
            }

            val alertsChannel = NotificationChannel(
                "pih_alerts",
                "Alertas Meteorológicas",
                NotificationManager.IMPORTANCE_HIGH
            ).apply {
                description = "Alertas de precipitación y condiciones meteorológicas"
                enableVibration(true)
            }

            manager.createNotificationChannel(chatChannel)
            manager.createNotificationChannel(alertsChannel)
        }
    }
}
