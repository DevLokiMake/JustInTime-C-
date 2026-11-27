package com.justintime.app.core

import com.justintime.app.data.HistoryDao
import com.justintime.app.data.TaskDao
import java.time.Clock
import java.time.Instant
import java.time.ZoneId
import java.time.ZoneOffset
import java.time.ZonedDateTime
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** Исполнитель и сервис задач на поддельных зависимостях: Android не нужен. */
class TaskRunnerTest {
    private val zone: ZoneId = ZoneOffset.UTC

    private class MutableClock(var now: Instant) : Clock() {
        override fun getZone(): ZoneId = ZoneOffset.UTC
        override fun withZone(zone: ZoneId): Clock = this
        override fun instant(): Instant = now
    }

    /** Поддельный DAO: повторяет условия SQL из настоящего TaskDao. */
    private class FakeTaskDao : TaskDao {
        val rows = linkedMapOf<String, ScheduledTask>()
        private val flow = MutableStateFlow<List<ScheduledTask>>(emptyList())

        override suspend fun insert(task: ScheduledTask) { rows[task.id] = task }
        override fun observeAll(): Flow<List<ScheduledTask>> = flow
        override suspend fun get(id: String) = rows[id]
        override suspend fun getStartable() = rows.values.filter { it.status == TaskStatus.PENDING || it.status == TaskStatus.SCHEDULED }
        override suspend fun getRunning() = rows.values.filter { it.status == TaskStatus.RUNNING }

        override suspend fun tryClaim(id: String, now: Long, deadline: Long): Int {
            val t = rows[id] ?: return 0
            val startable = t.status == TaskStatus.PENDING || t.status == TaskStatus.SCHEDULED
            if (!startable || t.scheduledAtMillis > deadline) return 0
            rows[id] = t.copy(status = TaskStatus.RUNNING, attemptCount = t.attemptCount + 1, updatedAtMillis = now)
            return 1
        }

        override suspend fun finishRun(id: String, status: String, error: String?, lastRun: Long, scheduledAt: Long, now: Long): Int {
            val t = rows[id] ?: return 0
            if (t.status != TaskStatus.RUNNING) return 0
            rows[id] = t.copy(
                status = TaskStatus.valueOf(status), lastError = error, lastRunAtMillis = lastRun,
                scheduledAtMillis = scheduledAt, updatedAtMillis = now,
            )
            return 1
        }

        override suspend fun tryCancel(id: String, now: Long): Int {
            val t = rows[id] ?: return 0
            val cancellable = t.status in setOf(
                TaskStatus.PENDING, TaskStatus.SCHEDULED, TaskStatus.WAITING_FOR_UNLOCK,
                TaskStatus.WAITING_FOR_NETWORK, TaskStatus.NEEDS_USER_ACTION,
            )
            if (!cancellable) return 0
            rows[id] = t.copy(status = TaskStatus.CANCELLED, updatedAtMillis = now)
            return 1
        }

        override suspend fun reschedule(id: String, scheduledAt: Long, zoneId: String, now: Long): Int {
            val t = rows[id] ?: return 0
            if (t.status != TaskStatus.PENDING && t.status != TaskStatus.SCHEDULED) return 0
            rows[id] = t.copy(scheduledAtMillis = scheduledAt, zoneId = zoneId, updatedAtMillis = now)
            return 1
        }

        override suspend fun deleteFinished(id: String): Int {
            val t = rows[id] ?: return 0
            if (t.status !in setOf(TaskStatus.COMPLETED, TaskStatus.FAILED, TaskStatus.CANCELLED)) return 0
            rows.remove(id)
            return 1
        }
    }

    private class FakeHistoryDao : HistoryDao {
        val rows = mutableListOf<HistoryEntry>()
        private val flow = MutableStateFlow<List<HistoryEntry>>(emptyList())

        override suspend fun insert(entry: HistoryEntry): Long {
            val id = rows.size + 1L
            rows += entry.copy(id = id)
            return id
        }

        override suspend fun finish(id: Long, finishedAt: Long, result: String, message: String?): Int {
            val i = rows.indexOfFirst { it.id == id }
            if (i < 0) return 0
            rows[i] = rows[i].copy(finishedAtMillis = finishedAt, result = RunResult.valueOf(result), message = message)
            return 1
        }

        override suspend fun closeOpen(taskId: String, finishedAt: Long, result: String, message: String?): Int {
            var count = 0
            rows.indices.forEach { i ->
                if (rows[i].taskId == taskId && rows[i].finishedAtMillis == null) {
                    rows[i] = rows[i].copy(finishedAtMillis = finishedAt, result = RunResult.valueOf(result), message = message)
                    count++
                }
            }
            return count
        }

        override fun observeRecent(limit: Int): Flow<List<HistoryEntry>> = flow
    }

    private class FakeBluetooth(var outcome: BtOutcome = BtOutcome.Done(alreadyInState = false)) : BluetoothPort {
        val calls = mutableListOf<Boolean>()
        var throwOnCall: Exception? = null

        override suspend fun set(enable: Boolean): BtOutcome {
            calls += enable
            throwOnCall?.let { throw it }
            return outcome
        }
    }

    private class FakeAlarms(var exact: Boolean = true) : AlarmPort {
        val scheduled = mutableMapOf<String, Long>()
        val cancelled = mutableListOf<String>()

        override fun canScheduleExact() = exact
        override fun schedule(taskId: String, atMillis: Long): AlarmKind {
            scheduled[taskId] = atMillis
            return if (exact) AlarmKind.EXACT else AlarmKind.INEXACT
        }
        override fun cancel(taskId: String) { cancelled += taskId; scheduled.remove(taskId) }
    }

    private class FakeNotify : NotifyPort {
        val problems = mutableListOf<Pair<String, String>>()
        override fun problem(title: String, text: String) { problems += title to text }
    }

    private class FakeTexts : Texts {
        override fun defaultTitle(enable: Boolean, timeOfDayMinutes: Int) = "BT ${if (enable) "on" else "off"} $timeOfDayMinutes"
        override fun btDone(enable: Boolean) = "done"
        override fun btAlready(enable: Boolean) = "already"
        override fun btNoAdapter() = "no adapter"
        override fun btNoPermission() = "no permission"
        override fun btRefused() = "refused"
        override fun btNotConfirmed(state: String) = "not confirmed $state"
        override fun missed(lateMinutes: Long, graceMinutes: Long) = "missed $lateMinutes/$graceMinutes"
        override fun interrupted() = "interrupted"
        override fun unexpected(message: String?) = "unexpected $message"
        override fun inexactAlarm() = "inexact"
    }

    private val clock = MutableClock(ZonedDateTime.of(2026, 11, 27, 2, 59, 50, 0, zone).toInstant())
    private val tasks = FakeTaskDao()
    private val history = FakeHistoryDao()
    private val bluetooth = FakeBluetooth()
    private val alarms = FakeAlarms()
    private val notify = FakeNotify()
    private val runner = TaskRunner(tasks, history, bluetooth, alarms, notify, FakeTexts(), clock) { zone }
    private val service = TaskService(tasks, alarms, FakeTexts(), clock) { zone }

    private fun millis(hour: Int, minute: Int, day: Int = 27): Long =
        ZonedDateTime.of(2026, 11, day, hour, minute, 0, 0, zone).toInstant().toEpochMilli()

    private suspend fun createTask(repeat: Repeat = Repeat.DAILY, enable: Boolean = false): ScheduledTask {
        service.create(3 * 60, enable, repeat)
        return tasks.rows.values.single()
    }

    // Создание и отмена

    @Test
    fun `create saves the task and sets an exact alarm for the next occurrence`() = runTest {
        val task = createTask()

        assertEquals(TaskStatus.SCHEDULED, task.status)
        assertEquals(millis(3, 0), task.scheduledAtMillis)
        assertEquals(millis(3, 0), alarms.scheduled[task.id])
        assertEquals("BT off 180", task.title)
    }

    @Test
    fun `create warns when only an inexact alarm is possible but still saves the task`() = runTest {
        alarms.exact = false

        val result = service.create(3 * 60, false, Repeat.DAILY)

        assertTrue(result.ok)
        assertEquals("inexact", result.warning)
        assertEquals(1, tasks.rows.size)
    }

    @Test
    fun `create rejects a time outside the day`() = runTest {
        assertFalse(service.create(24 * 60, false, Repeat.NONE).ok)
        assertTrue(tasks.rows.isEmpty())
    }

    @Test
    fun `cancel stops the alarm and a cancelled task is never executed`() = runTest {
        val task = createTask()

        assertTrue(service.cancel(task.id))
        clock.now = Instant.ofEpochMilli(millis(3, 0))
        runner.run(task.id)

        assertTrue(alarms.cancelled.contains(task.id))
        assertEquals(TaskStatus.CANCELLED, tasks.rows[task.id]!!.status)
        assertTrue(bluetooth.calls.isEmpty())
    }

    // Выполнение

    @Test
    fun `due task turns bluetooth off and a one time task completes`() = runTest {
        val task = createTask(repeat = Repeat.NONE)
        clock.now = Instant.ofEpochMilli(millis(3, 0))

        runner.run(task.id)

        val after = tasks.rows[task.id]!!
        assertEquals(listOf(false), bluetooth.calls)
        assertEquals(TaskStatus.COMPLETED, after.status)
        assertNull(after.lastError)
        assertEquals(1, after.attemptCount)
        assertEquals(RunResult.SUCCESS, history.rows.single().result)
        assertNotNull(history.rows.single().finishedAtMillis)
        assertTrue(notify.problems.isEmpty())
    }

    @Test
    fun `daily task is re-armed for the next day after a run`() = runTest {
        val task = createTask(repeat = Repeat.DAILY)
        clock.now = Instant.ofEpochMilli(millis(3, 0))

        runner.run(task.id)

        val after = tasks.rows[task.id]!!
        assertEquals(TaskStatus.SCHEDULED, after.status)
        assertEquals(millis(3, 0, day = 28), after.scheduledAtMillis)
        assertEquals(millis(3, 0, day = 28), alarms.scheduled[task.id])
    }

    @Test
    fun `a second alarm for the same task does not run it twice`() = runTest {
        val task = createTask(repeat = Repeat.NONE)
        clock.now = Instant.ofEpochMilli(millis(3, 0))

        runner.run(task.id)
        runner.run(task.id)

        assertEquals(1, bluetooth.calls.size)
        assertEquals(1, history.rows.size)
    }

    @Test
    fun `alarm that fires far too early does nothing`() = runTest {
        val task = createTask()
        clock.now = Instant.ofEpochMilli(millis(2, 50))

        runner.run(task.id)

        assertTrue(bluetooth.calls.isEmpty())
        assertEquals(TaskStatus.SCHEDULED, tasks.rows[task.id]!!.status)
    }

    @Test
    fun `refused or unconfirmed change is a failure with the reason and a notification`() = runTest {
        val task = createTask(repeat = Repeat.NONE)
        bluetooth.outcome = BtOutcome.Refused
        clock.now = Instant.ofEpochMilli(millis(3, 0))

        runner.run(task.id)

        val after = tasks.rows[task.id]!!
        assertEquals(TaskStatus.FAILED, after.status)
        assertEquals("refused", after.lastError)
        assertEquals(RunResult.FAILED, history.rows.single().result)
        assertEquals(1, notify.problems.size)
    }

    @Test
    fun `not confirmed result is not treated as success`() = runTest {
        val task = createTask(repeat = Repeat.NONE)
        bluetooth.outcome = BtOutcome.NotConfirmed("TURNING_OFF")
        clock.now = Instant.ofEpochMilli(millis(3, 0))

        runner.run(task.id)

        assertEquals(TaskStatus.FAILED, tasks.rows[task.id]!!.status)
        assertEquals("not confirmed TURNING_OFF", tasks.rows[task.id]!!.lastError)
    }

    @Test
    fun `a failed daily task still gets its next run and keeps the error`() = runTest {
        val task = createTask(repeat = Repeat.DAILY)
        bluetooth.outcome = BtOutcome.NoPermission
        clock.now = Instant.ofEpochMilli(millis(3, 0))

        runner.run(task.id)

        val after = tasks.rows[task.id]!!
        assertEquals(TaskStatus.SCHEDULED, after.status)
        assertEquals("no permission", after.lastError)
        assertEquals(millis(3, 0, day = 28), after.scheduledAtMillis)
    }

    @Test
    fun `unexpected exception becomes a failed run instead of crashing`() = runTest {
        val task = createTask(repeat = Repeat.NONE)
        bluetooth.throwOnCall = IllegalStateException("boom")
        clock.now = Instant.ofEpochMilli(millis(3, 0))

        runner.run(task.id)

        assertEquals(TaskStatus.FAILED, tasks.rows[task.id]!!.status)
        assertEquals("unexpected boom", tasks.rows[task.id]!!.lastError)
    }

    @Test
    fun `task that is late beyond the grace period is skipped and not executed`() = runTest {
        val task = createTask(repeat = Repeat.NONE)
        clock.now = Instant.ofEpochMilli(millis(3, 0) + TaskLimits.GRACE_MILLIS + 60_000)

        runner.run(task.id)

        assertTrue(bluetooth.calls.isEmpty())
        assertEquals(TaskStatus.FAILED, tasks.rows[task.id]!!.status)
        assertEquals(RunResult.SKIPPED, history.rows.single().result)
    }

    // Восстановление

    @Test
    fun `recover runs a task that is due within the grace period`() = runTest {
        val task = createTask(repeat = Repeat.NONE)
        clock.now = Instant.ofEpochMilli(millis(3, 10))

        runner.recoverAll(zoneChanged = false)

        assertEquals(listOf(false), bluetooth.calls)
        assertEquals(TaskStatus.COMPLETED, tasks.rows[task.id]!!.status)
    }

    @Test
    fun `recover skips an old due task and re-arms a daily one`() = runTest {
        val task = createTask(repeat = Repeat.DAILY)
        clock.now = Instant.ofEpochMilli(millis(8, 0))

        runner.recoverAll(zoneChanged = false)

        assertTrue(bluetooth.calls.isEmpty())
        assertEquals(RunResult.SKIPPED, history.rows.single().result)
        assertEquals(millis(3, 0, day = 28), tasks.rows[task.id]!!.scheduledAtMillis)
        assertEquals(millis(3, 0, day = 28), alarms.scheduled[task.id])
    }

    @Test
    fun `recover puts the alarm back for a future task after a reboot`() = runTest {
        val task = createTask()
        alarms.scheduled.clear()

        runner.recoverAll(zoneChanged = false)

        assertEquals(task.scheduledAtMillis, alarms.scheduled[task.id])
    }

    @Test
    fun `recover closes a run that was interrupted and re-arms a daily task`() = runTest {
        val task = createTask(repeat = Repeat.DAILY)
        clock.now = Instant.ofEpochMilli(millis(3, 0))
        tasks.tryClaim(task.id, clock.millis(), clock.millis())
        history.insert(HistoryEntry(taskId = task.id, taskTitle = task.title, startedAtMillis = clock.millis()))
        clock.now = clock.now.plusMillis(TaskLimits.STUCK_MILLIS + 1_000)

        runner.recoverAll(zoneChanged = false)

        val after = tasks.rows[task.id]!!
        assertEquals(TaskStatus.SCHEDULED, after.status)
        assertEquals("interrupted", after.lastError)
        assertEquals(RunResult.FAILED, history.rows.single().result)
    }

    @Test
    fun `a run that has just started is not treated as stuck`() = runTest {
        val task = createTask(repeat = Repeat.DAILY)
        clock.now = Instant.ofEpochMilli(millis(3, 0))
        tasks.tryClaim(task.id, clock.millis(), clock.millis())

        runner.recoverAll(zoneChanged = false)

        assertEquals(TaskStatus.RUNNING, tasks.rows[task.id]!!.status)
    }

    @Test
    fun `time zone change recalculates the next run in the new zone`() = runTest {
        val task = createTask()
        val newYork = ZoneId.of("America/New_York")
        val movedRunner = TaskRunner(tasks, history, bluetooth, alarms, notify, FakeTexts(), clock) { newYork }

        movedRunner.recoverAll(zoneChanged = true)

        val after = tasks.rows[task.id]!!
        val expected = ZonedDateTime.of(2026, 11, 27, 3, 0, 0, 0, newYork).toInstant().toEpochMilli()
        assertEquals(expected, after.scheduledAtMillis)
        assertEquals("America/New_York", after.zoneId)
        assertEquals(expected, alarms.scheduled[task.id])
    }

    // Удаление

    @Test
    fun `only finished tasks can be deleted`() = runTest {
        val task = createTask(repeat = Repeat.NONE)

        assertFalse(service.delete(task.id))

        service.cancel(task.id)

        assertTrue(service.delete(task.id))
        assertTrue(tasks.rows.isEmpty())
    }
}
