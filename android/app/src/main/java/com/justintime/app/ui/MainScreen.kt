package com.justintime.app.ui

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FloatingActionButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.RadioButton
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TimePicker
import androidx.compose.material3.rememberTimePickerState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.justintime.app.R
import com.justintime.app.core.HistoryEntry
import com.justintime.app.core.Repeat
import com.justintime.app.core.RunResult
import com.justintime.app.core.ScheduledTask
import com.justintime.app.core.TaskStatus
import java.time.Instant
import java.time.ZoneId
import java.time.ZonedDateTime
import java.time.format.DateTimeFormatter

private val DateFormat: DateTimeFormatter = DateTimeFormatter.ofPattern("dd.MM HH:mm")

private fun formatMillis(millis: Long): String =
    ZonedDateTime.ofInstant(Instant.ofEpochMilli(millis), ZoneId.systemDefault()).format(DateFormat)

@Composable
fun MainScreen(
    viewModel: MainViewModel,
    onRequestBluetooth: () -> Unit,
    onOpenExactAlarmSettings: () -> Unit,
    onOpenBatterySettings: () -> Unit,
) {
    val tasks by viewModel.tasks.collectAsStateWithLifecycle()
    val history by viewModel.history.collectAsStateWithLifecycle()
    val permissions by viewModel.permissions.collectAsStateWithLifecycle()
    val message by viewModel.message.collectAsStateWithLifecycle()

    var tab by rememberSaveable { mutableStateOf(0) }
    var showCreate by rememberSaveable { mutableStateOf(false) }

    Scaffold(
        floatingActionButton = {
            FloatingActionButton(onClick = { showCreate = true }) { Text("+") }
        },
    ) { padding ->
        Column(
            Modifier
                .fillMaxSize()
                .padding(padding)
                .padding(horizontal = 16.dp),
        ) {
            Text(
                stringResource(R.string.screen_title),
                style = MaterialTheme.typography.headlineMedium,
                modifier = Modifier.padding(top = 16.dp, bottom = 8.dp),
            )

            PermissionsCard(permissions, onRequestBluetooth, onOpenExactAlarmSettings, onOpenBatterySettings)

            Row(Modifier.padding(vertical = 8.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                TabButton(stringResource(R.string.tab_tasks), selected = tab == 0) { tab = 0 }
                TabButton(stringResource(R.string.tab_history), selected = tab == 1) { tab = 1 }
            }

            if (tab == 0) {
                TaskList(tasks, onCancel = viewModel::cancel, onDelete = viewModel::delete)
            } else {
                HistoryList(history)
            }
        }
    }

    if (showCreate) {
        CreateTaskDialog(
            onDismiss = { showCreate = false },
            onCreate = { minutes, enable, repeat ->
                showCreate = false
                viewModel.create(minutes, enable, repeat)
            },
        )
    }

    message?.let { text ->
        AlertDialog(
            onDismissRequest = viewModel::dismissMessage,
            text = { Text(text) },
            confirmButton = { TextButton(onClick = viewModel::dismissMessage) { Text(stringResource(R.string.common_ok)) } },
        )
    }
}

@Composable
private fun TabButton(text: String, selected: Boolean, onClick: () -> Unit) {
    if (selected) {
        Button(onClick = onClick) { Text(text) }
    } else {
        OutlinedButton(onClick = onClick) { Text(text) }
    }
}

@Composable
private fun PermissionsCard(
    state: PermissionsState,
    onRequestBluetooth: () -> Unit,
    onOpenExactAlarmSettings: () -> Unit,
    onOpenBatterySettings: () -> Unit,
) {
    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Text(stringResource(R.string.perm_title), style = MaterialTheme.typography.titleMedium)

            PermissionRow(
                title = stringResource(R.string.perm_exact),
                ok = state.exactAlarms,
                okText = stringResource(R.string.perm_exact_ok),
                noText = stringResource(R.string.perm_exact_no),
                onAllow = onOpenExactAlarmSettings,
            )
            PermissionRow(
                title = stringResource(R.string.perm_bt),
                ok = state.bluetooth,
                okText = stringResource(R.string.perm_bt_ok),
                noText = stringResource(R.string.perm_bt_no),
                onAllow = onRequestBluetooth,
            )

            Text(stringResource(R.string.perm_battery), style = MaterialTheme.typography.bodySmall)
            TextButton(onClick = onOpenBatterySettings) { Text(stringResource(R.string.perm_battery_open)) }
        }
    }
}

@Composable
private fun PermissionRow(title: String, ok: Boolean, okText: String, noText: String, onAllow: () -> Unit) {
    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
        Column(Modifier.weight(1f)) {
            Text(title, style = MaterialTheme.typography.bodyMedium)
            Text(
                if (ok) okText else noText,
                style = MaterialTheme.typography.bodySmall,
                color = if (ok) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.error,
            )
        }

        if (!ok) {
            Button(onClick = onAllow) { Text(stringResource(R.string.perm_allow)) }
        }
    }
}

@Composable
private fun TaskList(tasks: List<ScheduledTask>, onCancel: (String) -> Unit, onDelete: (String) -> Unit) {
    if (tasks.isEmpty()) {
        Text(stringResource(R.string.tasks_empty), modifier = Modifier.padding(vertical = 16.dp))
        return
    }

    LazyColumn(verticalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.padding(bottom = 80.dp)) {
        items(tasks, key = { it.id }) { task -> TaskCard(task, onCancel, onDelete) }
    }
}

@Composable
private fun TaskCard(task: ScheduledTask, onCancel: (String) -> Unit, onDelete: (String) -> Unit) {
    val active = task.status in setOf(
        TaskStatus.PENDING, TaskStatus.SCHEDULED, TaskStatus.WAITING_FOR_UNLOCK,
        TaskStatus.WAITING_FOR_NETWORK, TaskStatus.NEEDS_USER_ACTION,
    )
    val finished = task.status in setOf(TaskStatus.COMPLETED, TaskStatus.FAILED, TaskStatus.CANCELLED)

    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
            Text(task.title, style = MaterialTheme.typography.titleMedium)
            Text(statusLabel(task.status), style = MaterialTheme.typography.bodyMedium)

            if (active) {
                Text(stringResource(R.string.task_next, formatMillis(task.scheduledAtMillis)), style = MaterialTheme.typography.bodySmall)
            }

            Text(
                stringResource(if (task.repeat == Repeat.DAILY) R.string.task_repeat_daily else R.string.task_repeat_none),
                style = MaterialTheme.typography.bodySmall,
            )

            task.lastError?.let {
                Text(it, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.error)
            }

            if (active) {
                TextButton(onClick = { onCancel(task.id) }) { Text(stringResource(R.string.task_cancel)) }
            } else if (finished) {
                TextButton(onClick = { onDelete(task.id) }) { Text(stringResource(R.string.task_delete)) }
            }
        }
    }
}

@Composable
private fun HistoryList(entries: List<HistoryEntry>) {
    if (entries.isEmpty()) {
        Text(stringResource(R.string.history_empty), modifier = Modifier.padding(vertical = 16.dp))
        return
    }

    LazyColumn(verticalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.padding(bottom = 80.dp)) {
        items(entries, key = { it.id }) { entry ->
            Card(Modifier.fillMaxWidth()) {
                Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                    Text(entry.taskTitle, style = MaterialTheme.typography.titleMedium)
                    Text(
                        formatMillis(entry.startedAtMillis) + " · " + resultLabel(entry.result),
                        style = MaterialTheme.typography.bodyMedium,
                    )
                    entry.message?.let { Text(it, style = MaterialTheme.typography.bodySmall) }
                }
            }
        }
    }
}

@Composable
private fun statusLabel(status: TaskStatus): String = stringResource(
    when (status) {
        TaskStatus.PENDING -> R.string.status_pending
        TaskStatus.SCHEDULED -> R.string.status_scheduled
        TaskStatus.RUNNING -> R.string.status_running
        TaskStatus.WAITING_FOR_UNLOCK -> R.string.status_waiting_unlock
        TaskStatus.WAITING_FOR_NETWORK -> R.string.status_waiting_network
        TaskStatus.NEEDS_USER_ACTION -> R.string.status_needs_action
        TaskStatus.COMPLETED -> R.string.status_completed
        TaskStatus.FAILED -> R.string.status_failed
        TaskStatus.CANCELLED -> R.string.status_cancelled
    },
)

@Composable
private fun resultLabel(result: RunResult?): String = stringResource(
    when (result) {
        RunResult.SUCCESS -> R.string.result_success
        RunResult.FAILED -> R.string.result_failed
        RunResult.SKIPPED -> R.string.result_skipped
        null -> R.string.result_running
    },
)

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun CreateTaskDialog(onDismiss: () -> Unit, onCreate: (Int, Boolean, Repeat) -> Unit) {
    val timeState = rememberTimePickerState(initialHour = 3, initialMinute = 0, is24Hour = true)
    var enable by remember { mutableStateOf(false) }
    var repeat by remember { mutableStateOf(Repeat.DAILY) }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(stringResource(R.string.create_title)) },
        text = {
            Column(Modifier.verticalScroll(rememberScrollState())) {
                Text(stringResource(R.string.create_time), style = MaterialTheme.typography.titleSmall)
                Spacer(Modifier.height(8.dp))
                TimePicker(state = timeState)

                Spacer(Modifier.height(16.dp))
                Text(stringResource(R.string.create_action), style = MaterialTheme.typography.titleSmall)
                RadioRow(stringResource(R.string.create_bt_off), selected = !enable) { enable = false }
                RadioRow(stringResource(R.string.create_bt_on), selected = enable) { enable = true }

                Spacer(Modifier.height(16.dp))
                Text(stringResource(R.string.create_repeat), style = MaterialTheme.typography.titleSmall)
                RadioRow(stringResource(R.string.task_repeat_daily), selected = repeat == Repeat.DAILY) { repeat = Repeat.DAILY }
                RadioRow(stringResource(R.string.task_repeat_none), selected = repeat == Repeat.NONE) { repeat = Repeat.NONE }
            }
        },
        confirmButton = {
            TextButton(onClick = { onCreate(timeState.hour * 60 + timeState.minute, enable, repeat) }) {
                Text(stringResource(R.string.create_submit))
            }
        },
        dismissButton = { TextButton(onClick = onDismiss) { Text(stringResource(R.string.common_cancel)) } },
    )
}

@Composable
private fun RadioRow(text: String, selected: Boolean, onSelect: () -> Unit) {
    Row(
        Modifier
            .fillMaxWidth()
            .clickable(onClick = onSelect)
            .padding(vertical = 4.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        RadioButton(selected = selected, onClick = null)
        Text(text, modifier = Modifier.padding(start = 8.dp))
    }
}
