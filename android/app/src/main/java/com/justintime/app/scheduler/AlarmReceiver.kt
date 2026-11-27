package com.justintime.app.scheduler

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import com.justintime.app.JustInTimeApp
import kotlinx.coroutines.launch

/** Срабатывание будильника: выполняет одну задачу. goAsync держит приёмник живым, пока работа не закончится. */
class AlarmReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        val taskId = intent.getStringExtra(EXTRA_TASK_ID) ?: return
        val app = context.applicationContext as JustInTimeApp
        val pending = goAsync()

        app.appScope.launch {
            try {
                app.container.runner.run(taskId)
            } finally {
                pending.finish()
            }
        }
    }

    companion object {
        const val ACTION_RUN = "com.justintime.app.action.RUN_TASK"
        const val EXTRA_TASK_ID = "taskId"
    }
}
