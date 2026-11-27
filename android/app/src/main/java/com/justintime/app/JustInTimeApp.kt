package com.justintime.app

import android.app.Application
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch

class JustInTimeApp : Application() {
    lateinit var container: AppContainer
        private set

    /** Живёт, пока жив процесс. Нужен для фоновых работ, которые не должны отменяться вместе с экраном. */
    val appScope = CoroutineScope(SupervisorJob() + Dispatchers.Default)

    override fun onCreate() {
        super.onCreate()
        container = AppContainer(this)

        // Приложение открыли: сверяем расписание с реальностью (прерванные попытки, пропущенные сроки, потерянные будильники).
        appScope.launch { container.runner.recoverAll(zoneChanged = false) }
    }
}
