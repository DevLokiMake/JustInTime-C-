package com.justintime.app

import android.content.Context
import com.justintime.app.core.TaskRunner
import com.justintime.app.core.TaskService
import com.justintime.app.data.AppDatabase
import com.justintime.app.scheduler.AndroidAlarmPort
import com.justintime.app.system.AndroidBluetoothPort
import com.justintime.app.system.AndroidNotifyPort
import com.justintime.app.system.AndroidTexts
import java.time.Clock

/** Сборка зависимостей вручную: приложение маленькое, DI-фреймворк только добавил бы риск. */
class AppContainer(context: Context) {
    private val appContext = context.applicationContext

    val db: AppDatabase = AppDatabase.create(appContext)
    val alarms = AndroidAlarmPort(appContext)
    val bluetooth = AndroidBluetoothPort(appContext)
    private val texts = AndroidTexts(appContext)
    private val notifier = AndroidNotifyPort(appContext)
    private val clock: Clock = Clock.systemUTC()

    val runner = TaskRunner(db.tasks(), db.history(), bluetooth, alarms, notifier, texts, clock)
    val service = TaskService(db.tasks(), alarms, texts, clock)
}
