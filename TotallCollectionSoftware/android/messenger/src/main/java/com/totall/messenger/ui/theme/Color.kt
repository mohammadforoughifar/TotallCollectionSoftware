package com.totall.messenger.ui.theme

import androidx.compose.ui.graphics.Color

// رنگ برند سازمانی — هماهنگ با نسخه وب (‎--primary: #4f46e5‎)
val BrandIndigo = Color(0xFF4F46E5)
val BrandIndigoDark = Color(0xFF4338CA)
val BrandIndigoLight = Color(0xFFE0E7FF)
val BrandIndigoSoft = Color(0xFFEEF2FF)

// ---------- تم روشن ----------
val LightPrimary = BrandIndigo
val LightOnPrimary = Color(0xFFFFFFFF)
val LightPrimaryContainer = BrandIndigoLight
val LightBackground = Color(0xFFF4F5FA)
val LightSurface = Color(0xFFFFFFFF)
val LightSurfaceVariant = Color(0xFFE9ECF3)
val LightOnSurface = Color(0xFF1C2130)
val LightOnSurfaceVariant = Color(0xFF5B6478)

// حباب پیام در تم روشن
val LightBubbleOut = BrandIndigo          // پیام خودم
val LightBubbleOutText = Color.White
val LightBubbleIn = Color(0xFFFFFFFF)    // پیام دیگران
val LightBubbleInText = Color(0xFF1C2130)

// ---------- تم تیره ----------
val DarkPrimary = Color(0xFF818CF8)
val DarkOnPrimary = Color(0xFF0B0E1A)
val DarkPrimaryContainer = Color(0xFF312E81)
val DarkBackground = Color(0xFF0F1220)
val DarkSurface = Color(0xFF171B2E)
val DarkSurfaceVariant = Color(0xFF232842)
val DarkOnSurface = Color(0xFFE8EAF2)
val DarkOnSurfaceVariant = Color(0xFF9AA3BC)

// حباب پیام در تم تیره
val DarkBubbleOut = Color(0xFF4F46E5)
val DarkBubbleOutText = Color.White
val DarkBubbleIn = Color(0xFF232842)
val DarkBubbleInText = Color(0xFFE8EAF2)

// ---------- رنگ‌های معنایی ----------
val OnlineGreen = Color(0xFF22C55E)
val ReadBlue = Color(0xFF53BDEB)   // تیک خوانده‌شده
val UnreadBadge = BrandIndigo
val DangerRed = Color(0xFFE0245E)
val SystemChipBg = Color(0xFF6B7280)

// پس‌زمینه صفحه چت (بافت نقطه‌ای تلگرام‌مانند با کد روی Canvas کشیده می‌شود)
val LightChatBg = Color(0xFFE7E9F2)
val DarkChatBg = Color(0xFF0B0E1A)

// رنگ‌های آواتار (بر اساس هش نام انتخاب می‌شود)
val AvatarPalette = listOf(
    Color(0xFF4F46E5), Color(0xFF0EA5E9), Color(0xFF10B981),
    Color(0xFFF59E0B), Color(0xFFEF4444), Color(0xFF8B5CF6),
    Color(0xFFEC4899), Color(0xFF14B8A6),
)
