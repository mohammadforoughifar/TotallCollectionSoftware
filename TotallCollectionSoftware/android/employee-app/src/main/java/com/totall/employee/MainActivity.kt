package com.totall.employee

import android.Manifest
import android.app.DownloadManager
import android.content.ActivityNotFoundException
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.view.View
import android.webkit.CookieManager
import android.webkit.GeolocationPermissions
import android.webkit.URLUtil
import android.webkit.ValueCallback
import android.webkit.WebChromeClient
import android.webkit.WebResourceError
import android.webkit.WebResourceRequest
import android.webkit.WebSettings
import android.webkit.WebView
import android.webkit.WebViewClient
import android.widget.ProgressBar
import androidx.activity.result.ActivityResultLauncher
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AlertDialog
import androidx.appcompat.app.AppCompatActivity
import androidx.core.app.ActivityCompat
import androidx.swiperefreshlayout.widget.SwipeRefreshLayout

/**
 * اپ پرسنل «فروغ آریا» — پوسته‌ی بومی اندروید برای PWA سامانه.
 *
 * آدرس سرور را در  res/values/strings.xml  (server_url) تنظیم کنید و حتماً HTTPS معتبر باشد؛
 * ساعت‌زنی با موقعیت (GPS) داخل صفحه‌ی «داشبورد من» با تایید مجوز مکان کار می‌کند.
 */
class MainActivity : AppCompatActivity() {

    private lateinit var web: WebView
    private lateinit var swipe: SwipeRefreshLayout
    private lateinit var progress: ProgressBar

    private var fileChooserCallback: ValueCallback<Array<Uri>>? = null
    private var pendingGeoCallback: GeolocationPermissions.Callback? = null
    private var pendingGeoOrigin: String? = null

    private val serverUrl: String by lazy { getString(R.string.server_url) }

    // ---------- launchers ----------
    private val fileChooserLauncher: ActivityResultLauncher<Intent> =
        registerForActivityResult(ActivityResultContracts.StartActivityForResult()) { result ->
            val callback = fileChooserCallback ?: return@registerForActivityResult
            fileChooserCallback = null
            val uris = WebChromeClient.FileChooserParams.parseResult(result.resultCode, result.data)
            callback.onReceiveValue(uris ?: arrayOf())
        }

    private val locationLauncher: ActivityResultLauncher<String> =
        registerForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
            val cb = pendingGeoCallback
            val origin = pendingGeoOrigin
            pendingGeoCallback = null
            pendingGeoOrigin = null
            cb?.invoke(origin, granted, false)
        }

    private val notificationLauncher: ActivityResultLauncher<String> =
        registerForActivityResult(ActivityResultContracts.RequestPermission()) { }

    // ---------- lifecycle ----------
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        // اعلان‌ها (اندروید ۱۳+) برای آینده — درخواست یک‌باره
        if (Build.VERSION.SDK_INT >= 33 &&
            ActivityCompat.checkSelfPermission(this, Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED
        ) notificationLauncher.launch(Manifest.permission.POST_NOTIFICATIONS)

        swipe = SwipeRefreshLayout(this)
        web = WebView(this)
        progress = ProgressBar(this, null, android.R.attr.progressBarStyleHorizontal).apply {
            max = 100
            visibility = View.GONE
        }

        swipe.addView(web)
        val frame = android.widget.FrameLayout(this)
        frame.addView(swipe)
        frame.addView(progress, android.widget.FrameLayout.LayoutParams(
            android.widget.FrameLayout.LayoutParams.MATCH_PARENT, dp(4)))
        setContentView(frame)

        setupWebView()
        web.loadUrl(serverUrl)
    }

    private fun dp(v: Int): Int = (v * resources.displayMetrics.density).toInt()

    private fun setupWebView() {
        with(web.settings) {
            javaScriptEnabled = true
            domStorageEnabled = true
            databaseEnabled = true
            geolocationEnabled = true           // ساعت‌زنی با GPS
            mediaPlaybackRequiresUserGesture = false
            mixedContentMode = WebSettings.MIXED_CONTENT_NEVER_ALLOW // فقط HTTPS
            allowFileAccess = false
            allowContentAccess = true            // برای انتخاب فایل (آپلود مدارک/عکس)
            cacheMode = WebSettings.LOAD_DEFAULT
            textZoom = 100
        }
        web.setBackgroundColor(0xFF0F172A.toInt())
        CookieManager.getInstance().setAcceptThirdPartyCookies(web, false)

        web.webViewClient = object : WebViewClient() {
            override fun shouldOverrideUrlLoading(view: WebView, request: WebResourceRequest): Boolean {
                val uri = request.url
                val host = uri.host ?: return false
                val serverHost = Uri.parse(serverUrl).host ?: return false
                return when {
                    // صفحات سامانه داخل همین WebView
                    (uri.scheme == "https" || uri.scheme == "http") && host == serverHost -> false
                    // لینک‌های بیرونی و پروتکل‌های دیگر (tel/mailto/telegram/whatsapp/...) → اپ بیرونی
                    else -> {
                        try { startActivity(Intent(Intent.ACTION_VIEW, uri)) } catch (_: ActivityNotFoundException) {}
                        true
                    }
                }
            }

            override fun onReceivedError(view: WebView, request: WebResourceRequest, error: WebResourceError) {
                // فقط خطای فریم اصلی مهم است (تبلیغات/فونت‌های جانبی نه)
                if (request.isForMainFrame) showOfflineDialog()
            }

            override fun onPageFinished(view: WebView, url: String) {
                swipe.isRefreshing = false
                backCallback.isEnabled = view.canGoBack()
            }
        }

        web.webChromeClient = object : WebChromeClient() {
            override fun onProgressChanged(view: WebView, newProgress: Int) {
                progress.progress = newProgress
                progress.visibility = if (newProgress in 1..99) View.VISIBLE else View.GONE
            }

            // ساعت‌زنی با موقعیت: مجوز اندروید را به وب‌کلاک وصل می‌کند
            override fun onGeolocationPermissionsShowPrompt(
                origin: String, callback: GeolocationPermissions.Callback
            ) {
                if (ActivityCompat.checkSelfPermission(
                        this@MainActivity, Manifest.permission.ACCESS_FINE_LOCATION
                    ) == PackageManager.PERMISSION_GRANTED
                ) {
                    callback.invoke(origin, true, false)
                } else {
                    pendingGeoCallback = callback
                    pendingGeoOrigin = origin
                    locationLauncher.launch(Manifest.permission.ACCESS_FINE_LOCATION)
                }
            }

            // آپلود فایل (اسکن مدارک/عکس پرسنل با گالری یا دوربین)
            override fun onShowFileChooser(
                webView: WebView, callback: ValueCallback<Array<Uri>>,
                params: FileChooserParams
            ): Boolean {
                fileChooserCallback?.onReceiveValue(null)
                fileChooserCallback = callback
                return try {
                    fileChooserLauncher.launch(params.createIntent())
                    true
                } catch (_: ActivityNotFoundException) {
                    fileChooserCallback = null
                    false
                }
            }
        }

        // دانلود فایل‌ها (فیش حقوقی PDF و ...) → DownloadManager اندروید
        web.setDownloadListener { url, _, contentDisposition, mimeType, _ ->
            try {
                val name = URLUtil.guessFileName(url, contentDisposition, mimeType)
                val request = DownloadManager.Request(Uri.parse(url))
                    .setNotificationVisibility(DownloadManager.Request.VISIBILITY_VISIBLE_NOTIFY_COMPLETED)
                    .setDestinationInExternalPublicDir(
                        android.os.Environment.DIRECTORY_DOWNLOADS, name
                    )
                    CookieManager.getInstance().getCookie(url)?.let { request.addRequestHeader("Cookie", it) }
                (getSystemService(Context.DOWNLOAD_SERVICE) as DownloadManager).enqueue(request)
            } catch (_: Exception) {
            }
        }

        swipe.setOnRefreshListener { web.reload() }
    }

    private fun showOfflineDialog() {
        AlertDialog.Builder(this)
            .setTitle(R.string.offline_title)
            .setMessage(R.string.offline_message)
            .setPositiveButton(R.string.retry) { _, _ -> web.loadUrl(serverUrl) }
            .setNegativeButton(android.R.string.cancel, null)
            .show()
    }

    // دکمه‌ی بازگشت اندروید → عقب‌رفتن در تاریخچه‌ی صفحات
    private val backCallback = androidx.activity.OnBackPressedCallback(false) {
        if (web.canGoBack()) web.goBack() else this.disable()
    }

    override fun onStart() {
        super.onStart()
        onBackPressedDispatcher.addCallback(this, backCallback)
    }

    override fun onPause() {
        web.onPause()
        super.onPause()
    }

    override fun onResume() {
        super.onResume()
        web.onResume()
        backCallback.isEnabled = web.canGoBack()
    }

    override fun onDestroy() {
        web.destroy()
        super.onDestroy()
    }
}
