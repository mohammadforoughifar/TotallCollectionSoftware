package com.totall.messenger.ui.components

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import coil.compose.AsyncImage
import com.totall.messenger.ui.theme.AvatarPalette
import com.totall.messenger.ui.theme.OnlineGreen
import com.totall.messenger.ui.theme.ReadBlue
import java.time.LocalDateTime
import java.time.format.DateTimeFormatter

/** آواتار دایره‌ای: عکس اگر بود، وگرنه حرف اول نام روی رنگ هشی */
@Composable
fun ChatAvatar(
    name: String,
    avatarUrl: String?,
    modifier: Modifier = Modifier,
    size: Dp = 48.dp,
    showOnlineDot: Boolean = false,
    isOnline: Boolean = false,
) {
    val bg = remember(name) {
        AvatarPalette[kotlin.math.abs(name.hashCode()) % AvatarPalette.size]
    }
    val initial = remember(name) { name.trim().firstOrNull()?.toString() ?: "؟" }
    Box(modifier = modifier.size(size)) {
        if (!avatarUrl.isNullOrBlank()) {
            AsyncImage(
                model = avatarUrl,
                contentDescription = name,
                modifier = Modifier
                    .size(size)
                    .clip(CircleShape),
                contentScale = ContentScale.Crop,
            )
        } else {
            Box(
                modifier = Modifier
                    .size(size)
                    .clip(CircleShape)
                    .background(bg),
                contentAlignment = Alignment.Center,
            ) {
                Text(
                    text = initial,
                    color = Color.White,
                    style = MaterialTheme.typography.titleMedium,
                    fontWeight = FontWeight.Bold,
                )
            }
        }
        if (showOnlineDot && isOnline) {
            Box(
                modifier = Modifier
                    .align(Alignment.BottomEnd)
                    .size(size * 0.28f)
                    .clip(CircleShape)
                    .background(OnlineGreen),
            )
        }
    }
}

/** تیک‌های وضعیت پیام: ✓ ارسال شد | ✓✓ خوانده شد */
@Composable
fun ReadTicks(
    isReadByPeer: Boolean,
    modifier: Modifier = Modifier,
    dark: Boolean = false,
) {
    Text(
        text = if (isReadByPeer) "✓✓" else "✓",
        modifier = modifier,
        color = if (isReadByPeer) ReadBlue else if (dark) Color(0xFFB9C2D8) else Color(0xFF9AA3BC),
        style = MaterialTheme.typography.labelSmall,
        fontWeight = FontWeight.Bold,
    )
}

private val FaDigits = mapOf(
    '0' to '۰', '1' to '۱', '2' to '۲', '3' to '۳', '4' to '۴',
    '5' to '۵', '6' to '۶', '7' to '۷', '8' to '۸', '9' to '۹',
)

/** ارقام فارسی */
fun String.toFaDigits(): String = map { FaDigits[it] ?: it }.joinToString("")

fun Int.toFaDigits(): String = toString().toFaDigits()

/** ساعت پیام: 14:32 ← ۱۴:۳۲ */
fun formatMessageTime(isoDateTime: String): String {
    return try {
        val dt = LocalDateTime.parse(isoDateTime.take(19))
        "%02d:%02d".format(dt.hour, dt.minute).toFaDigits()
    } catch (_: Exception) {
        ""
    }
}

/** برچسب تاریخ برای جداکننده روز: امروز / دیروز / ۱۴۰۳/۰۶/۱۹ */
fun formatDayLabel(isoDateTime: String): String {
    return try {
        val dt = LocalDateTime.parse(isoDateTime.take(19)).toLocalDate()
        val today = java.time.LocalDate.now()
        when (dt) {
            today -> "امروز"
            today.minusDays(1) -> "دیروز"
            else -> dt.format(DateTimeFormatter.ofPattern("yyyy/MM/dd")).toFaDigits()
        }
    } catch (_: Exception) {
        ""
    }
}

fun formatFileSize(bytes: Long?): String {
    if (bytes == null || bytes <= 0) return ""
    val kb = bytes / 1024.0
    if (kb < 1024) return "%.0f KB".format(kb).toFaDigits()
    val mb = kb / 1024.0
    if (mb < 1024) return "%.1f MB".format(mb).toFaDigits()
    return "%.2f GB".format(mb / 1024.0).toFaDigits()
}
