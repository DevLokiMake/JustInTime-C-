// Версии подобраны как проверенная связка: AGP 8.7.x + Kotlin 2.0.21 + KSP 2.0.21-1.0.28 + Compose BOM 2024.12.
// Android Studio может предложить обновление, его можно принять.
plugins {
    id("com.android.application") version "8.7.3" apply false
    id("org.jetbrains.kotlin.android") version "2.0.21" apply false
    id("org.jetbrains.kotlin.plugin.compose") version "2.0.21" apply false
    id("com.google.devtools.ksp") version "2.0.21-1.0.28" apply false
}
