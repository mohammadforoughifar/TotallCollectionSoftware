package com.totall.messenger.ui.theme

import androidx.compose.material3.Typography
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.Font
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.sp
import com.totall.messenger.R

/*
 * فونت فارسی وزیرمتن — فایل‌ها را در res/font بگذارید:
 *   vazirmatn_regular.ttf / vazirmatn_medium.ttf / vazirmatn_bold.ttf
 * (همان فونت‌های پوشه erp-preview/fonts در ریپو، نسخه woff2 را به ttf تبدیل کنید
 *  یا از مخزن وزیرمتن نسخه ttf را دانلود کنید)
 */
val Vazirmatn = FontFamily(
    Font(R.font.vazirmatn_regular, FontWeight.Normal),
    Font(R.font.vazirmatn_medium, FontWeight.Medium),
    Font(R.font.vazirmatn_bold, FontWeight.Bold),
)

val MessengerTypography = Typography(
    titleLarge = TextStyle(fontFamily = Vazirmatn, fontWeight = FontWeight.Bold, fontSize = 20.sp, lineHeight = 30.sp),
    titleMedium = TextStyle(fontFamily = Vazirmatn, fontWeight = FontWeight.Bold, fontSize = 16.sp, lineHeight = 26.sp),
    titleSmall = TextStyle(fontFamily = Vazirmatn, fontWeight = FontWeight.Medium, fontSize = 14.sp, lineHeight = 24.sp),
    bodyLarge = TextStyle(fontFamily = Vazirmatn, fontWeight = FontWeight.Normal, fontSize = 15.sp, lineHeight = 26.sp),
    bodyMedium = TextStyle(fontFamily = Vazirmatn, fontWeight = FontWeight.Normal, fontSize = 14.sp, lineHeight = 24.sp),
    bodySmall = TextStyle(fontFamily = Vazirmatn, fontWeight = FontWeight.Normal, fontSize = 12.sp, lineHeight = 20.sp),
    labelLarge = TextStyle(fontFamily = Vazirmatn, fontWeight = FontWeight.Medium, fontSize = 13.sp, lineHeight = 20.sp),
    labelSmall = TextStyle(fontFamily = Vazirmatn, fontWeight = FontWeight.Normal, fontSize = 11.sp, lineHeight = 16.sp),
)
