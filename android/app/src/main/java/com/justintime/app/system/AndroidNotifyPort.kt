package com.justintime.app.system

import android.app.NotificationChannel
import android.app.NotificationManager
import android.content.Context
import androidx.core.app.NotificationCompat
import com.justintime.app.R
import com.justintime.app.core.NotifyPort

/** Уведомления о проблемах выполнения. Успешные запуски не шумят: результат виден в журнале. */
class AndroidNotifyPort(private val context: Context) : NotifyPort {
    private val manager: NotificationManager = context.getSystemService(NotificationManager::class.java)

    init {
        // minSdk 26: каналы уведомлений есть всегда.
        manager.createNotificationChannel(
            NotificationChannel(CHANNEL_ID, context.getString(R.string.notify_channel_problems), NotificationManager.IMPORTANCE_DEFAULT),
        )
    }

    override fun problem(title: String, text: String) {
        val notification = NotificationCompat.Builder(context, CHANNEL_ID)
            .setSmallIcon(android.R.drawable.stat_notify_error)
            .setContentTitle(title)
            .setContentText(text)
            .setStyle(NotificationCompat.BigTextStyle().bigText(text))
            .setAutoCancel(true)
            .build()

        manager.notify(System.currentTimeMillis().toInt(), notification)
    }

    private companion object {
        const val CHANNEL_ID = "problems"
    }
}
