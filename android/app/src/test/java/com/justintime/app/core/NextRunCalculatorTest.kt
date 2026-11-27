package com.justintime.app.core

import java.time.Instant
import java.time.LocalDateTime
import java.time.ZoneId
import java.time.ZonedDateTime
import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Test

class NextRunCalculatorTest {
    private val almaty = ZoneId.of("Asia/Almaty")
    private val newYork = ZoneId.of("America/New_York")

    private fun at(zone: ZoneId, year: Int, month: Int, day: Int, hour: Int, minute: Int): Instant =
        ZonedDateTime.of(LocalDateTime.of(year, month, day, hour, minute), zone).toInstant()

    @Test
    fun `time later today runs today`() {
        val now = at(almaty, 2026, 11, 27, 1, 0)

        val next = NextRunCalculator.nextOccurrence(now, almaty, 3 * 60)

        assertEquals(at(almaty, 2026, 11, 27, 3, 0), next)
    }

    @Test
    fun `time already passed today runs tomorrow`() {
        val now = at(almaty, 2026, 11, 27, 4, 0)

        val next = NextRunCalculator.nextOccurrence(now, almaty, 3 * 60)

        assertEquals(at(almaty, 2026, 11, 28, 3, 0), next)
    }

    @Test
    fun `exactly now is not in the future so it runs tomorrow`() {
        val now = at(almaty, 2026, 11, 27, 3, 0)

        val next = NextRunCalculator.nextOccurrence(now, almaty, 3 * 60)

        assertEquals(at(almaty, 2026, 11, 28, 3, 0), next)
    }

    @Test
    fun `midnight and last minute of the day are valid`() {
        val now = at(almaty, 2026, 11, 27, 12, 0)

        assertEquals(at(almaty, 2026, 11, 28, 0, 0), NextRunCalculator.nextOccurrence(now, almaty, 0))
        assertEquals(at(almaty, 2026, 11, 27, 23, 59), NextRunCalculator.nextOccurrence(now, almaty, 23 * 60 + 59))
    }

    @Test
    fun `out of range minutes are rejected`() {
        val now = at(almaty, 2026, 11, 27, 12, 0)

        assertThrows(IllegalArgumentException::class.java) { NextRunCalculator.nextOccurrence(now, almaty, -1) }
        assertThrows(IllegalArgumentException::class.java) { NextRunCalculator.nextOccurrence(now, almaty, 24 * 60) }
    }

    @Test
    fun `time that does not exist because of spring forward moves to the first valid time after the gap`() {
        // 2026-03-08 в Нью-Йорке часы переходят с 02:00 сразу на 03:00, времени 02:30 не существует.
        val now = at(newYork, 2026, 3, 8, 0, 30)

        val next = NextRunCalculator.nextOccurrence(now, newYork, 2 * 60 + 30)

        assertEquals(at(newYork, 2026, 3, 8, 3, 30), next)
    }

    @Test
    fun `time that happens twice on fall back runs at the first occurrence`() {
        // 2026-11-01 в Нью-Йорке 01:30 бывает дважды (EDT, затем EST). Берём первое.
        val now = at(newYork, 2026, 11, 1, 0, 30)

        val next = NextRunCalculator.nextOccurrence(now, newYork, 60 + 30)

        assertEquals(ZonedDateTime.of(LocalDateTime.of(2026, 11, 1, 1, 30), newYork).withEarlierOffsetAtOverlap().toInstant(), next)
    }

    @Test
    fun `daily time keeps the same wall clock time across the daylight saving change`() {
        val beforeChange = at(newYork, 2026, 3, 7, 12, 0)

        val first = NextRunCalculator.nextOccurrence(beforeChange, newYork, 6 * 60)
        val second = NextRunCalculator.nextOccurrence(first, newYork, 6 * 60)

        assertEquals(at(newYork, 2026, 3, 8, 6, 0), first)
        assertEquals(at(newYork, 2026, 3, 9, 6, 0), second)
    }

    @Test
    fun `same instant gives a different next run in another time zone`() {
        val now = Instant.parse("2026-11-27T00:00:00Z")

        val inAlmaty = NextRunCalculator.nextOccurrence(now, almaty, 3 * 60)
        val inNewYork = NextRunCalculator.nextOccurrence(now, newYork, 3 * 60)

        assertEquals(at(almaty, 2026, 11, 27, 3, 0), inAlmaty)
        assertEquals(at(newYork, 2026, 11, 27, 3, 0), inNewYork)
    }
}
