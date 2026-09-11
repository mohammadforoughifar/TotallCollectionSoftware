package com.totall.messenger.ui.theme

import android.app.Activity
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.SideEffect
import androidx.compose.runtime.remember
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.toArgb
import androidx.compose.ui.platform.LocalLayoutDirection
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.unit.LayoutDirection
import androidx.core.view.WindowCompat

private val LightScheme = lightColorScheme(
    primary = LightPrimary,
    onPrimary = LightOnPrimary,
    primaryContainer = LightPrimaryContainer,
    background = LightBackground,
    surface = LightSurface,
    surfaceVariant = LightSurfaceVariant,
    onSurface = LightOnSurface,
    onSurfaceVariant = LightOnSurfaceVariant,
    error = DangerRed,
)

private val DarkScheme = darkColorScheme(
    primary = DarkPrimary,
    onPrimary = DarkOnPrimary,
    primaryContainer = DarkPrimaryContainer,
    background = DarkBackground,
    surface = DarkSurface,
    surfaceVariant = DarkSurfaceVariant,
    onSurface = DarkOnSurface,
    onSurfaceVariant = DarkOnSurfaceVariant,
    error = DangerRed,
)

/** رنگ‌های اختصاصی حباب چت که در MaterialScheme نیستند */
data class ChatColors(
    val bubbleOut: Color,
    val bubbleOutText: Color,
    val bubbleIn: Color,
    val bubbleInText: Color,
    val chatBackground: Color,
)

val LightChatColors = ChatColors(
    bubbleOut = LightBubbleOut,
    bubbleOutText = LightBubbleOutText,
    bubbleIn = LightBubbleIn,
    bubbleInText = LightBubbleInText,
    chatBackground = LightChatBg,
)

val DarkChatColors = ChatColors(
    bubbleOut = DarkBubbleOut,
    bubbleOutText = DarkBubbleOutText,
    bubbleIn = DarkBubbleIn,
    bubbleInText = DarkBubbleInText,
    chatBackground = DarkChatBg,
)

@Composable
fun TotallMessengerTheme(
    darkTheme: Boolean = isSystemInDarkTheme(),
    content: @Composable () -> Unit,
) {
    val scheme = if (darkTheme) DarkScheme else LightScheme
    val chatColors = remember(darkTheme) { if (darkTheme) DarkChatColors else LightChatColors }

    // رنگ استاتوس‌بار هماهنگ با تم
    val view = LocalView.current
    SideEffect {
        val window = (view.context as? Activity)?.window ?: return@SideEffect
        window.statusBarColor = scheme.surface.toArgb()
        WindowCompat.getInsetsController(window, view).isAppearanceLightStatusBars = !darkTheme
    }

    // کل قالب راست‌به‌چپ (فارسی)
    CompositionLocalProvider(LocalLayoutDirection provides LayoutDirection.Rtl) {
        MaterialTheme(
            colorScheme = scheme,
            typography = MessengerTypography,
            content = { ChatColorsProvider(chatColors, content) },
        )
    }
}

// CompositionLocal برای دسترسی به رنگ‌های چت از هر کامپوزبل
private val LocalChatColors = androidx.compose.runtime.staticCompositionLocalOf { LightChatColors }

@Composable
private fun ChatColorsProvider(colors: ChatColors, content: @Composable () -> Unit) {
    CompositionLocalProvider(LocalChatColors provides colors, content = content)
}

object MessengerChat {
    val colors: ChatColors
        @Composable get() = LocalChatColors.current
}
