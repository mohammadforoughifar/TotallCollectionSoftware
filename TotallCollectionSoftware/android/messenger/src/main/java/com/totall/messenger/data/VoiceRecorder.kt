package com.totall.messenger.data

import android.content.Context
import android.media.MediaRecorder
import android.os.Build
import java.io.File

/**
 * ضبط پیام صوتی (کدک AAC در کانتینر m4a) در کش برنامه.
 * خروجی همان چیزی است که سرور با نوع «audio/mp4» به‌عنوان پیام صوتی (Audio) می‌پذیرد.
 *
 * استفاده:
 * ```
 * val recorder = remember { VoiceRecorder(context) }
 * if (recorder.start()) { ... }          // شروع ضبط
 * val file = recorder.stop()             // پایان؛ فایل یا null
 * recorder.cancel()                      // لغو و حذف فایل
 * ```
 */
class VoiceRecorder(private val context: Context) {

    private var recorder: MediaRecorder? = null
    private var output: File? = null

    val isRecording: Boolean get() = recorder != null

    /** شروع ضبط؛ false یعنی مجوز نداریم یا میکروفون در دسترس نیست. */
    @Suppress("DEPRECATION")
    fun start(): Boolean {
        if (recorder != null) return true
        return try {
            val r = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                MediaRecorder(context)
            } else {
                MediaRecorder()
            }
            val file = File(context.cacheDir, "voice-${System.currentTimeMillis()}.m4a")
            r.setAudioSource(MediaRecorder.AudioSource.MIC)
            r.setOutputFormat(MediaRecorder.OutputFormat.MPEG_4)
            r.setAudioEncoder(MediaRecorder.AudioEncoder.AAC)
            r.setAudioEncodingBitRate(128_000)
            r.setAudioSamplingRate(44_100)
            r.setOutputFile(file.absolutePath)
            r.prepare()
            r.start()
            recorder = r
            output = file
            true
        } catch (e: Exception) {
            release(delete = true)
            false
        }
    }

    /** پایان ضبط و تحویل فایل؛ اگر ضبطی در جریان نباشد یا فایل خالی باشد null. */
    fun stop(): File? {
        val r = recorder ?: return null
        val file = output
        recorder = null
        output = null
        return try {
            r.stop()
            r.release()
            if (file != null && file.exists() && file.length() > 0) file else { file?.delete(); null }
        } catch (e: Exception) {
            file?.delete()
            null
        }
    }

    /** لغو ضبط و حذف فایل موقت. */
    fun cancel() = release(delete = true)

    private fun release(delete: Boolean) {
        val r = recorder
        recorder = null
        if (r != null) {
            try { r.stop() } catch (_: Exception) { }
            try { r.release() } catch (_: Exception) { }
        }
        val file = output
        output = null
        if (delete) file?.delete()
    }
}
