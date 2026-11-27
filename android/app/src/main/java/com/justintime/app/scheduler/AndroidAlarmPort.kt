package com.justintime.app.scheduler

import android.app.AlarmManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Build
import com.justintime.app.core.AlarmKind
import com.justintime.app.core.AlarmPort

/**
 * Будильники через AlarmManager. setExactAndAllowWhileIdle срабатывает и в режиме Doze (с ограничением Android
 * по частоте для одного приложения), точное время требует разрешения SCHEDULE_EXACT_ALARM.
 */
class AndroidAlarmPort(private val context: Context) : AlarmPort {
    private val alarmManager: AlarmManager = context.getSystemService(AlarmManager::class.java)

    override fun canScheduleExact(): Boolean =
        Build.VERSION.SDK_INT < Build.VERSION_CODES.S || alarmManager.canScheduleExactAlarms()

    override fun schedule(taskId: String, atMillis: Long): AlarmKind {
        val intent = pendingIntent(taskId)

        return if (canScheduleExact()) {
            alarmManager.setExactAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, atMillis, intent)
            AlarmKind.EXACT
        } else {
            // Разрешения нет: неточный будильник лучше, чем никакого, но интерфейс об этом честно предупредит.
            alarmManager.setAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, atMillis, intent)
            AlarmKind.INEXACT
        }
    }

    override fun cancel(taskId: String) {
        alarmManager.cancel(pendingIntent(taskId))
    }

    private fun pendingIntent(taskId: String): PendingIntent {
        // Уникальный Uri делает PendingIntent отдельным для каждой задачи без риска коллизий requestCode.
        val intent = Intent(context, AlarmReceiver::class.java)
            .setAction(AlarmReceiver.ACTION_RUN)
            .setData(Uri.parse("justintime://task/$taskId"))
            .putExtra(AlarmReceiver.EXTRA_TASK_ID, taskId)

        return PendingIntent.getBroadcast(context, 0, intent, PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE)
    }
}
