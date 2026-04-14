package mx.cfe.grijalva.pih.data.service

import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.os.Build
import androidx.core.app.NotificationCompat
import mx.cfe.grijalva.pih.MainActivity
import mx.cfe.grijalva.pih.R

object NotificationHelper {
    private const val CHANNEL_CHAT = "pih_chat"
    private const val CHANNEL_ALERTS = "pih_alerts"

    fun createChannels(context: Context) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            val manager = context.getSystemService(NotificationManager::class.java)

            val chatChannel = NotificationChannel(
                CHANNEL_CHAT, "Mensajes de Chat",
                NotificationManager.IMPORTANCE_HIGH
            ).apply { description = "Notificaciones de mensajes del chat PIH" }

            val alertChannel = NotificationChannel(
                CHANNEL_ALERTS, "Alertas de Precipitación",
                NotificationManager.IMPORTANCE_HIGH
            ).apply { description = "Alertas meteorológicas" }

            manager.createNotificationChannel(chatChannel)
            manager.createNotificationChannel(alertChannel)
        }
    }

    fun showMessageNotification(
        context: Context,
        sender: String,
        message: String,
        room: String,
        currentRoom: String
    ) {
        // Don't notify if user is viewing this room
        if (room == currentRoom) return

        val intent = Intent(context, MainActivity::class.java).apply {
            flags = Intent.FLAG_ACTIVITY_SINGLE_TOP or Intent.FLAG_ACTIVITY_CLEAR_TOP
            putExtra("navigate_room", room)
        }

        val pendingIntent = PendingIntent.getActivity(
            context, room.hashCode(), intent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )

        val channelId = if (room == "alertas-precipitacion") CHANNEL_ALERTS else CHANNEL_CHAT
        val notification = NotificationCompat.Builder(context, channelId)
            .setSmallIcon(R.drawable.ic_notification)
            .setContentTitle(sender)
            .setContentText(message)
            .setAutoCancel(true)
            .setPriority(NotificationCompat.PRIORITY_HIGH)
            .setContentIntent(pendingIntent)
            .setStyle(NotificationCompat.BigTextStyle().bigText(message))
            .build()

        val manager = context.getSystemService(NotificationManager::class.java)
        manager.notify(room.hashCode(), notification)
    }
}
