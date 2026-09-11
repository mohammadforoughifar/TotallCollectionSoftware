package com.totall.messenger.data.remote

import retrofit2.converter.kotlinx.serialization.asConverterFactory
import kotlinx.serialization.json.Json
import okhttp3.Interceptor
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.logging.HttpLoggingInterceptor
import retrofit2.Retrofit

/** آدرس پایه‌ی سرور را هنگام ورود کاربر تنظیم کنید؛ مثل http://192.168.1.10:5000/ */
object ServerConfig {
    var baseUrl: String = "http://10.0.2.2:5000/"
        private set

    fun setBaseUrl(url: String) {
        baseUrl = if (url.endsWith("/")) url else "$url/"
        NetworkProvider.reset()
    }
}

/** هدر Authorization: Bearer <JWT> روی همه‌ی درخواست‌ها */
class AuthInterceptor(private val tokenProvider: () -> String?) : Interceptor {
    override fun intercept(chain: Interceptor.Chain) =
        chain.proceed(
            chain.request().newBuilder().apply {
                tokenProvider()?.takeIf { it.isNotBlank() }?.let {
                    header("Authorization", "Bearer $it")
                }
            }.build(),
        )
}

object NetworkProvider {
    var tokenProvider: () -> String? = { null }

    val json = Json {
        ignoreUnknownKeys = true
        isLenient = true
        coerceInputValues = true
    }

    @Volatile
    private var api: ChatApi? = null

    fun reset() { api = null }

    fun chatApi(): ChatApi = api ?: synchronized(this) {
        api ?: buildApi().also { api = it }
    }

    private fun buildApi(): ChatApi {
        val logging = HttpLoggingInterceptor().apply { level = HttpLoggingInterceptor.Level.BASIC }
        val client = OkHttpClient.Builder()
            .addInterceptor(AuthInterceptor(tokenProvider))
            .addInterceptor(logging)
            .build()
        return Retrofit.Builder()
            .baseUrl(ServerConfig.baseUrl)
            .client(client)
            .addConverterFactory(json.asConverterFactory("application/json".toMediaType()))
            .build()
            .create(ChatApi::class.java)
    }

    /**
     * ساخت URL دانلود/نمایش مدیا با توکن — چون Coil/img نمی‌تواند هدر Bearer بفرستد،
     * اندپوینت preview/download توکن را از query می‌خواند (همان منطق نسخه وب).
     */
    fun authedMediaUrl(messageId: Int, preview: Boolean = true): String {
        val token = tokenProvider().orEmpty()
        val kind = if (preview) "preview" else "download"
        return "${ServerConfig.baseUrl}api/chat/messages/$messageId/$kind?access_token=$token"
    }
}
