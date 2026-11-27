package com.justintime.app.scheduler

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import com.justintime.app.JustInTimeApp
import kotlinx.coroutines.launch

/**
 * Перезагрузка, смена времени или часового пояса, обновление приложения: все будильники при этом пропадают
 * или становятся неверными, поэтому расписание восстанавливается из базы.
 */
class RescheduleReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        val zoneChanged = intent.action == Intent.ACTION_TIMEZONE_CHANGED
        val app = context.applicationContext as JustInTimeApp
        val pending = goAsync()

        app.appScope.launch {
            try {
                app.container.runner.recoverAll(zoneChanged)
            } finally {
                pending.finish()
            }
        }
    }
}
