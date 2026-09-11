# پیام‌رسان توتال — فعلاً minify خاموش است؛ اگر روشن کردید این قوانین لازم‌اند:
-keep class com.totall.messenger.data.model.** { *; }
-keepattributes *Annotation*, Signature, InnerClasses, EnclosingMethod
-dontwarn com.microsoft.signalr.**
-keep class com.microsoft.signalr.** { *; }
