# پیام‌رسان اندروید توتال 📱

قالب نیتیو اندروید (**Kotlin + Jetpack Compose + Material3**) برای بخش پیام‌رسان سامانه —
دقیقاً روی همان API و SignalR نسخه وب سوار می‌شود.

## ✨ امکانات قالب

| بخش | امکانات |
|---|---|
| لیست گفتگوها | جستجو، تب همه/خوانده‌نشده/گروه‌ها، سنجاق، بی‌صدا، بج خوانده‌نشده، نقطه آنلاین، Pull-to-refresh |
| صفحه گفتگو | حباب متن/عکس/ویدیو/فایل/صوت، کارت اشتراک ERP، پیام سیستمی، ریپلای، فوروارد، واکنش ایموجی، ویرایش/حذف/سنجاق، تیک خوانده‌شدن، جداکننده روز، بنر پیام سنجاق، «در حال نوشتن…»، لود پیام‌های قدیمی با اسکرول |
| بلادرنگ | SignalR روی `hubs/chat`: دریافت لحظه‌ای پیام، ویرایش/حذف، واکنش، خوانده‌شدن، تایپینگ، وضعیت آنلاین |
| گفتگوی جدید | لیست مخاطبین سازمانی + ساخت گروه |
| اطلاعات گروه | اعضا با نقش (مالک/مدیر/عضو) و وضعیت آنلاین |
| تم | روشن/تیره، راست‌به‌چپ، فونت وزیرمتن، رنگ برند `#4f46e5` |

## 🗂️ ساختار

```
android/
├── settings.gradle.kts
├── build.gradle.kts
└── messenger/                          # ماژول اپ
    ├── build.gradle.kts
    ├── src/main/
    │   ├── AndroidManifest.xml
    │   ├── java/com/totall/messenger/
    │   │   ├── MainActivity.kt
    │   │   ├── data/
    │   │   │   ├── model/ChatModels.kt       # آینه‌ی ChatDtos.cs (enum عددی!)
    │   │   │   ├── remote/ChatApi.kt         # آینه‌ی ChatController (api/chat)
    │   │   │   ├── remote/NetworkProvider.kt # Retrofit + Bearer + URL مدیا
    │   │   │   ├── realtime/ChatRealtimeClient.kt  # SignalR
    │   │   │   └── ChatRepository.kt
    │   │   └── ui/
    │   │       ├── theme/                    # رنگ/فونت/تم روشن‌وتیره
    │   │       ├── components/               # آواتار، تیک، تاریخ فارسی
    │   │       ├── list/  chat/  newchat/  group/  nav/
    │   │       └── SampleData.kt             # دیتای نمایشی فارسی
    │   └── res/values/ (strings, themes)
```

## 🚀 اجرا در Android Studio

1. از منوی **File › Open** پوشه‌ی `android/` را باز کنید و صبر کنید Gradle Sync شود.
2. فونت‌های وزیرمتن (`vazirmatn_regular/medium/bold.ttf`) را در `messenger/src/main/res/font/` بگذارید.
3. در `MainActivity.kt` آدرس سرور و توکن JWT را تنظیم کنید:
   ```kotlin
   ServerConfig.setBaseUrl("http://192.168.1.10:5000/")
   NetworkProvider.tokenProvider = { sessionManager.jwtToken }
   ```
   > روی شبیه‌ساز، `10.0.2.2` معادل `localhost` کامپیوتر شماست (پیش‌فرض همین است).
4. **Run ▶** روی گوشی یا شبیه‌ساز (حداقل اندروید ۸).

هر صفحه `@Preview` فارسی دارد — بدون اجرا هم می‌توانید طراحی را در پنل Preview ببینید.

## ⚠️ نکات فنی مهم

- **enumهای سرور عددی‌اند** (مثلاً `type: 1/2/3`) — مدل‌های کاتلین Int نگه می‌دارند، نه String.
- **نمایش عکس/ویدیو پیام** باید با `NetworkProvider.authedMediaUrl(id)` ساخته شود چون Coil هدر Bearer نمی‌فرستد و سرور توکن را از query می‌خواند.
- سقف آپلود پیوست **۵۰ مگابایت** است.
- برای اعلان پس‌زمینه (FCM) و ضبط صدا، دسترسی‌ها در Manifest هست؛ پیاده‌سازی کاملشان با شماست.
