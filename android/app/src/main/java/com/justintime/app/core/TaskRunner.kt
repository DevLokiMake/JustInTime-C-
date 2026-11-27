package com.justintime.app.core

import com.justintime.app.data.HistoryDao
import com.justintime.app.data.TaskDao
import java.time.Clock
import java.time.Instant
import java.time.ZoneId
import kotlin.coroutines.cancellation.CancellationException

/**
 * Выполняет наступившие задачи и восстанавливает расписание. Не знает про Android: всё внешнее приходит через порты,
 * поэтому поведение проверяется обычными тестами.
 *
 * Правила:
 * - задачу берёт в работу только один исполнитель (атомарный UPDATE), повторный сигнал будильника ничего не делает;
 * - опоздавшая больше чем на [TaskLimits.GRACE_MILLIS] задача не выполняется, а пишется как пропущенная;
 * - успех это подтверждённое состояние Bluetooth, а не факт вызова команды;
 * - повторяющаяся задача после попытки (любой) получает следующий запуск.
 */
class TaskRunner(
    private val tasks: TaskDao,
    private val history: HistoryDao,
    private val bluetooth: BluetoothPort,
    private val alarms: AlarmPort,
    private val notifier: NotifyPort,
    private val texts: Texts,
    private val clock: Clock,
    private val currentZone: () -> ZoneId = { ZoneId.systemDefault() },
) {
    suspend fun run(taskId: String) {
        val now = clock.millis()

        if (tasks.tryClaim(taskId, now, now + TaskLimits.EARLY_SLACK_MILLIS) == 0) {
            return // задачи нет, она отменена или её уже взял другой исполнитель
        }

        val task = tasks.get(taskId) ?: return
        val historyId = history.insert(
            HistoryEntry(taskId = task.id, taskTitle = task.title, startedAtMillis = now, attempt = task.attemptCount),
        )

        val late = now - task.scheduledAtMillis

        if (late > TaskLimits.GRACE_MILLIS) {
            val message = texts.missed(late / 60_000, TaskLimits.GRACE_MILLIS / 60_000)
            finish(task, historyId, RunResult.SKIPPED, message)
            return
        }

        val (result, message) = try {
            execute(task)
        } catch (e: CancellationException) {
            throw e // процесс останавливают: задача останется RUNNING, восстановление её закроет
        } catch (e: Exception) {
            RunResult.FAILED to texts.unexpected(e.message)
        }

        finish(task, historyId, result, message)
    }

    /**
     * Вызывается при запуске приложения, после перезагрузки, смены времени и часового пояса.
     * Закрывает прерванные попытки, выполняет задачи, срок которых прошёл (в пределах допуска), остальные ставит заново.
     */
    suspend fun recoverAll(zoneChanged: Boolean) {
        val now = clock.millis()

        for (stuck in tasks.getRunning()) {
            if (now - stuck.updatedAtMillis > TaskLimits.STUCK_MILLIS) {
                history.closeOpen(stuck.id, now, RunResult.FAILED.name, texts.interrupted())
                finish(stuck, historyId = null, result = RunResult.FAILED, message = texts.interrupted())
            }
        }

        for (task in tasks.getStartable()) {
            if (task.scheduledAtMillis <= now) {
                run(task.id)
                continue
            }

            if (zoneChanged) {
                val zone = currentZone()
                val next = NextRunCalculator.nextOccurrence(Instant.ofEpochMilli(now), zone, task.timeOfDayMinutes)
                tasks.reschedule(task.id, next.toEpochMilli(), zone.id, now)
                alarms.schedule(task.id, next.toEpochMilli())
            } else {
                alarms.schedule(task.id, task.scheduledAtMillis)
            }
        }
    }

    private suspend fun execute(task: ScheduledTask): Pair<RunResult, String> = when (task.action) {
        ActionType.BLUETOOTH -> when (val outcome = bluetooth.set(task.enable)) {
            is BtOutcome.Done ->
                RunResult.SUCCESS to (if (outcome.alreadyInState) texts.btAlready(task.enable) else texts.btDone(task.enable))
            BtOutcome.NoAdapter -> RunResult.FAILED to texts.btNoAdapter()
            BtOutcome.NoPermission -> RunResult.FAILED to texts.btNoPermission()
            BtOutcome.Refused -> RunResult.FAILED to texts.btRefused()
            is BtOutcome.NotConfirmed -> RunResult.FAILED to texts.btNotConfirmed(outcome.state)
        }
    }

    private suspend fun finish(task: ScheduledTask, historyId: Long?, result: RunResult, message: String) {
        val finishedAt = clock.millis()

        if (historyId != null) {
            history.finish(historyId, finishedAt, result.name, message)
        }

        val error = if (result == RunResult.SUCCESS) null else message
        val next = if (task.repeat == Repeat.DAILY) {
            NextRunCalculator.nextOccurrence(Instant.ofEpochMilli(finishedAt), currentZone(), task.timeOfDayMinutes)
        } else {
            null
        }

        if (next != null) {
            tasks.finishRun(task.id, TaskStatus.SCHEDULED.name, error, finishedAt, next.toEpochMilli(), finishedAt)
            alarms.schedule(task.id, next.toEpochMilli())
        } else {
            val status = if (result == RunResult.SUCCESS) TaskStatus.COMPLETED else TaskStatus.FAILED
            tasks.finishRun(task.id, status.name, error, finishedAt, task.scheduledAtMillis, finishedAt)
        }

        if (result != RunResult.SUCCESS) {
            notifier.problem(task.title, message)
        }
    }
}
