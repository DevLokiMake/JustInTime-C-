package com.justintime.app.ui

import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewModelScope
import com.justintime.app.AppContainer
import com.justintime.app.core.HistoryEntry
import com.justintime.app.core.Repeat
import com.justintime.app.core.ScheduledTask
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch

data class PermissionsState(val exactAlarms: Boolean, val bluetooth: Boolean)

class MainViewModel(private val container: AppContainer) : ViewModel() {
    val tasks: StateFlow<List<ScheduledTask>> = container.db.tasks().observeAll()
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList())

    val history: StateFlow<List<HistoryEntry>> = container.db.history().observeRecent(HISTORY_LIMIT)
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList())

    private val _permissions = MutableStateFlow(readPermissions())
    val permissions: StateFlow<PermissionsState> = _permissions.asStateFlow()

    private val _message = MutableStateFlow<String?>(null)
    val message: StateFlow<String?> = _message.asStateFlow()

    fun refreshPermissions() {
        val before = _permissions.value
        val after = readPermissions()
        _permissions.value = after

        if (after.exactAlarms && !before.exactAlarms) {
            // Разрешение на точные будильники только что выдали: ставим уже созданные задачи заново, теперь точно.
            viewModelScope.launch { container.runner.recoverAll(zoneChanged = false) }
        }
    }

    fun create(timeOfDayMinutes: Int, enable: Boolean, repeat: Repeat) {
        viewModelScope.launch {
            val result = container.service.create(timeOfDayMinutes, enable, repeat)
            _message.value = result.warning
        }
    }

    fun cancel(id: String) {
        viewModelScope.launch { container.service.cancel(id) }
    }

    fun delete(id: String) {
        viewModelScope.launch { container.service.delete(id) }
    }

    fun dismissMessage() {
        _message.value = null
    }

    private fun readPermissions() = PermissionsState(
        exactAlarms = container.alarms.canScheduleExact(),
        bluetooth = container.bluetooth.hasPermission(),
    )

    class Factory(private val container: AppContainer) : ViewModelProvider.Factory {
        @Suppress("UNCHECKED_CAST")
        override fun <T : ViewModel> create(modelClass: Class<T>): T = MainViewModel(container) as T
    }

    private companion object {
        const val HISTORY_LIMIT = 100
    }
}
