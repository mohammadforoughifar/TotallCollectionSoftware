package com.totall.messenger.ui.chat

import android.Manifest
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Bundle
import android.speech.RecognitionListener
import android.speech.RecognizerIntent
import android.speech.SpeechRecognizer
import android.widget.Toast
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.automirrored.filled.ArrowForward
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.KeyboardArrowDown
import androidx.compose.material.icons.filled.MoreVert
import androidx.compose.material.icons.filled.PushPin
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FloatingActionButton
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.derivedStateOf
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.runtime.snapshotFlow
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalClipboardManager
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.tooling.preview.Preview
import androidx.compose.ui.unit.dp
import androidx.lifecycle.viewmodel.compose.viewModel
import com.totall.messenger.data.VoiceRecorder
import com.totall.messenger.data.model.ChatConversationDto
import com.totall.messenger.data.model.ChatMessageDto
import com.totall.messenger.data.model.ChatType
import com.totall.messenger.ui.SampleData
import com.totall.messenger.ui.components.ChatAvatar
import com.totall.messenger.ui.components.formatDayLabel
import com.totall.messenger.ui.theme.MessengerChat
import com.totall.messenger.ui.theme.TotallMessengerTheme
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.launch

@Composable
fun ChatScreen(
    conversationId: Int,
    onBack: () -> Unit,
    onOpenGroupInfo: (Int) -> Unit,
    vm: ChatViewModel = viewModel(factory = ChatViewModel.Factory(conversationId)),
) {
    val state by vm.uiState.collectAsState()
    var input by remember { mutableStateOf("") }
    var attachOpen by remember { mutableStateOf(false) }
    val clipboard = LocalClipboardManager.current
    val context = LocalContext.current

    // ===================== پیام صوتی: مجوز + ضبط =====================
    val recorder = remember { VoiceRecorder(context.applicationContext) }
    var recording by remember { mutableStateOf(false) }
    var recSeconds by remember { mutableStateOf(0) }
    var pendingAudioAction by remember { mutableStateOf<(() -> Unit)?>(null) }
    val audioPermissionLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.RequestPermission(),
    ) { granted ->
        val action = pendingAudioAction
        pendingAudioAction = null
        if (granted) action?.invoke()
        else Toast.makeText(
            context,
            "برای ضبط صدا و گفتار به نوشتار، مجوز میکروفون لازم است.",
            Toast.LENGTH_LONG,
        ).show()
    }
    val withAudioPermission: (() -> Unit) -> Unit = { action ->
        if (context.checkSelfPermission(Manifest.permission.RECORD_AUDIO) == PackageManager.PERMISSION_GRANTED) {
            action()
        } else {
            pendingAudioAction = action
            audioPermissionLauncher.launch(Manifest.permission.RECORD_AUDIO)
        }
    }

    // تایمر زندهٔ ضبط
    LaunchedEffect(recording) {
        if (recording) {
            recSeconds = 0
            while (recording) {
                delay(1000)
                recSeconds++
            }
        }
    }

    fun startRecordingVoice() {
        if (recorder.start()) {
            recording = true
            recSeconds = 0
        } else {
            Toast.makeText(context, "ضبط صدا شروع نشد؛ میکروفون در دسترس نیست.", Toast.LENGTH_SHORT).show()
        }
    }

    fun finishRecording(send: Boolean) {
        if (!recording && !recorder.isRecording) return
        val file = if (send) recorder.stop() else { recorder.cancel(); null }
        recording = false
        recSeconds = 0
        if (!send) return
        if (file != null) vm.sendVoice(file)
        else Toast.makeText(context, "ضبط انجام نشد؛ دوباره تلاش کنید.", Toast.LENGTH_SHORT).show()
    }

    // ===================== گفتار به نوشتار =====================
    var dictating by remember { mutableStateOf(false) }
    val speechRecognizer = remember {
        if (SpeechRecognizer.isRecognitionAvailable(context)) {
            SpeechRecognizer.createSpeechRecognizer(context)
        } else null
    }
    val speechListener = remember {
        object : RecognitionListener {
            override fun onResults(results: Bundle?) {
                val text = results?.getStringArrayList(SpeechRecognizer.RESULTS_RECOGNITION)?.firstOrNull()
                if (!text.isNullOrBlank()) input = if (input.isBlank()) text else "$input $text"
                dictating = false
            }
            override fun onError(error: Int) { dictating = false }
            override fun onReadyForSpeech(params: Bundle?) { }
            override fun onBeginningOfSpeech() { }
            override fun onRmsChanged(rmsdB: Float) { }
            override fun onBufferReceived(buffer: ByteArray?) { }
            override fun onEndOfSpeech() { }
            override fun onPartialResults(partialResults: Bundle?) { }
            override fun onEvent(eventType: Int, params: Bundle?) { }
        }
    }

    fun startDictation() {
        val recognizer = speechRecognizer
        if (recognizer == null) {
            Toast.makeText(context, "گفتار به نوشتار در این دستگاه در دسترس نیست.", Toast.LENGTH_SHORT).show()
            return
        }
        if (dictating) return
        try {
            recognizer.setRecognitionListener(speechListener)
            recognizer.startListening(Intent(RecognizerIntent.ACTION_RECOGNIZE_SPEECH).apply {
                putExtra(RecognizerIntent.EXTRA_LANGUAGE_MODEL, RecognizerIntent.LANGUAGE_MODEL_FREE_FORM)
                putExtra(RecognizerIntent.EXTRA_LANGUAGE, "fa-IR")
                putExtra(RecognizerIntent.EXTRA_MAX_RESULTS, 1)
            })
            dictating = true
        } catch (e: Exception) {
            dictating = false
            Toast.makeText(context, "گفتار به نوشتار قابل شروع نیست.", Toast.LENGTH_SHORT).show()
        }
    }

    // خطاهای ارسال (از جمله پیام صوتی) وقتی لیست پیام باز است به‌صورت Toast نمایش داده می‌شوند؛
    // خطای لیست خالی برای UI حالت خطا می‌ماند.
    LaunchedEffect(state.error, state.messages.isNotEmpty()) {
        val err = state.error
        if (err != null && state.messages.isNotEmpty()) {
            Toast.makeText(context, err, Toast.LENGTH_SHORT).show()
            vm.clearError()
        }
    }

    DisposableEffect(Unit) {
        onDispose {
            recorder.cancel()
            dictating = false
            speechRecognizer?.destroy()
        }
    }

    ChatContent(
        state = state,
        input = input,
        onInputChange = { input = it; vm.onInputTyping() },
        onSend = { vm.sendText(input); input = "" },
        onBack = onBack,
        onOpenInfo = { onOpenGroupInfo(conversationId) },
        onLoadMore = vm::loadMore,
        onRetry = vm::loadInitial,
        onAction = { action, msg ->
            when {
                action == "reply" -> vm.setReplyTo(msg)
                action == "copy" -> clipboard.setText(AnnotatedString(msg.text ?: ""))
                action == "delete" -> vm.deleteMessage(msg.id)
                action.startsWith("react:") -> vm.sendReaction(msg.id, action.removePrefix("react:"))
            }
        },
        onCancelReply = { vm.setReplyTo(null) },
        onAttach = { attachOpen = true },
        onRecord = { withAudioPermission { startRecordingVoice() } },
        recording = recording,
        recSeconds = recSeconds,
        onCancelRecord = { finishRecording(send = false) },
        onSendRecorded = { finishRecording(send = true) },
        onDictate = { withAudioPermission { startDictation() } },
        dictating = dictating,
    )
    if (attachOpen) {
        AttachmentSheet(onDismiss = { attachOpen = false }, onPick = { kind ->
            attachOpen = false
            // «🎵 صدا» از همان مسیر ضبط پیام صوتی می‌رود؛ فایل/تصویر فعلاً فقط بسته می‌شود.
            if (kind == "audio") withAudioPermission { startRecordingVoice() }
        })
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ChatContent(
    state: ChatUiState,
    input: String,
    onInputChange: (String) -> Unit,
    onSend: () -> Unit,
    onBack: () -> Unit,
    onOpenInfo: () -> Unit,
    onLoadMore: () -> Unit,
    onRetry: () -> Unit,
    onAction: (String, ChatMessageDto) -> Unit,
    onCancelReply: () -> Unit,
    onAttach: () -> Unit,
    onRecord: () -> Unit,
    recording: Boolean = false,
    recSeconds: Int = 0,
    onCancelRecord: () -> Unit = {},
    onSendRecorded: () -> Unit = {},
    onDictate: () -> Unit = {},
    dictating: Boolean = false,
) {
    val conv = state.conversation
    val listState = rememberLazyListState()
    val scope = rememberCoroutineScope()
    val showScrollDown by remember {
        derivedStateOf {
            val last = listState.layoutInfo.visibleItemsInfo.lastOrNull()?.index ?: 0
            last < state.messages.size - 3
        }
    }

    // اسکرول به آخرین پیام هنگام ورود و پیام جدید
    LaunchedEffect(state.messages.size) {
        if (state.messages.isNotEmpty()) listState.animateScrollToItem(state.messages.size - 1)
    }
    // لود پیام‌های قدیمی‌تر با رسیدن به بالای لیست
    LaunchedEffect(listState) {
        snapshotFlow { listState.firstVisibleItemIndex }
            .distinctUntilChanged()
            .collect { if (it == 0) onLoadMore() }
    }

    Scaffold(
        topBar = {
            ChatTopBar(
                conv = conv,
                typingNames = state.typingNames,
                onBack = onBack,
                onOpenInfo = onOpenInfo,
            )
        },
        bottomBar = {
            MessageInputBar(
                text = input,
                onTextChange = onInputChange,
                onSend = onSend,
                onAttach = onAttach,
                onRecord = onRecord,
                replyTo = state.replyTo,
                onCancelReply = onCancelReply,
                recording = recording,
                recSeconds = recSeconds,
                onCancelRecord = onCancelRecord,
                onSendRecorded = onSendRecorded,
                onDictate = onDictate,
                dictating = dictating,
            )
        },
        floatingActionButton = {
            if (showScrollDown) {
                FloatingActionButton(
                    onClick = { scope.launch { listState.animateScrollToItem(state.messages.size - 1) } },
                    modifier = Modifier.size(44.dp),
                ) {
                    Icon(Icons.Default.KeyboardArrowDown, contentDescription = "آخرین پیام")
                }
            }
        },
    ) { padding ->
        Box(
            modifier = Modifier
                .fillMaxSize()
                .padding(padding)
                .background(MessengerChat.colors.chatBackground),
        ) {
            ChatWallpaper()
            when {
                state.isLoading -> Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                    CircularProgressIndicator()
                }
                state.error != null && state.messages.isEmpty() -> Box(
                    Modifier.fillMaxSize().clickable { onRetry() },
                    contentAlignment = Alignment.Center,
                ) {
                    Text("⚠️ ${state.error}\nبرای تلاش مجدد بزنید",
                        color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
                else -> LazyColumn(
                    state = listState,
                    modifier = Modifier.fillMaxSize(),
                    contentPadding = androidx.compose.foundation.layout.PaddingValues(vertical = 8.dp),
                ) {
                    if (state.isLoadingMore) {
                        item { Box(Modifier.fillMaxWidth(), contentAlignment = Alignment.Center) {
                            CircularProgressIndicator(Modifier.size(28.dp))
                        } }
                    }
                    itemsIndexed(state.messages, key = { _, m -> m.id }) { index, msg ->
                        val prev = state.messages.getOrNull(index - 1)
                        if (prev == null || dayOf(prev.createdAt) != dayOf(msg.createdAt)) {
                            DayDivider(formatDayLabel(msg.createdAt))
                        }
                        MessageBubble(
                            message = msg,
                            showSenderName = conv?.type == ChatType.GROUP,
                            onAction = onAction,
                        )
                    }
                    if (state.typingNames.isNotEmpty()) {
                        item { TypingRow(state.typingNames) }
                    }
                    item { Spacer(Modifier.height(4.dp)) }
                }
            }
            // بنر پیام سنجاق‌شده
            if (conv?.pinnedMessageSnippet != null) {
                PinnedBanner(conv.pinnedMessageSnippet!!, onClose = { /* برداشتن سنجاق */ })
            }
        }
    }
}

private fun dayOf(iso: String): String = iso.take(10)

@Composable
private fun DayDivider(label: String) {
    Row(Modifier.fillMaxWidth().padding(vertical = 8.dp), horizontalArrangement = Arrangement.Center) {
        Text(
            text = label,
            modifier = Modifier
                .clip(RoundedCornerShape(12.dp))
                .background(MaterialTheme.colorScheme.surfaceVariant.copy(alpha = .92f))
                .padding(horizontal = 14.dp, vertical = 4.dp),
            style = MaterialTheme.typography.labelLarge,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
    }
}

@Composable
private fun TypingRow(names: List<String>) {
    Row(
        modifier = Modifier.padding(horizontal = 16.dp, vertical = 4.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(
            text = "${names.joinToString("، ")} در حال نوشتن…",
            style = MaterialTheme.typography.labelLarge,
            color = MaterialTheme.colorScheme.primary,
            modifier = Modifier
                .clip(RoundedCornerShape(12.dp))
                .background(MaterialTheme.colorScheme.surface.copy(alpha = .9f))
                .padding(horizontal = 12.dp, vertical = 6.dp),
        )
    }
}

@Composable
private fun PinnedBanner(snippet: String, onClose: () -> Unit) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .background(MaterialTheme.colorScheme.surface.copy(alpha = .96f))
            .clickable { }
            .padding(horizontal = 16.dp, vertical = 8.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Icon(Icons.Default.PushPin, null, Modifier.size(18.dp),
            tint = MaterialTheme.colorScheme.primary)
        Spacer(Modifier.width(8.dp))
        Text(snippet, Modifier.weight(1f),
            style = MaterialTheme.typography.bodySmall,
            maxLines = 1, overflow = TextOverflow.Ellipsis)
        IconButton(onClick = onClose, modifier = Modifier.size(28.dp)) {
            Icon(Icons.Default.Close, null, Modifier.size(16.dp))
        }
    }
}

/** بافت نقطه‌ای پس‌زمینه چت */
@Composable
private fun ChatWallpaper() {
    val dot = MaterialTheme.colorScheme.onSurface.copy(alpha = .05f)
    Canvas(Modifier.fillMaxSize()) {
        val step = 46f
        var y = 0f
        var row = 0
        while (y < size.height) {
            var x = if (row % 2 == 0) 0f else step / 2
            while (x < size.width) {
                drawCircle(dot, radius = 2.2f, center = Offset(x, y))
                x += step
            }
            y += step
            row++
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun ChatTopBar(
    conv: ChatConversationDto?,
    typingNames: List<String>,
    onBack: () -> Unit,
    onOpenInfo: () -> Unit,
) {
    var menuOpen by remember { mutableStateOf(false) }
    TopAppBar(
        title = {
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .clickable { onOpenInfo() },
                verticalAlignment = Alignment.CenterVertically,
            ) {
                ChatAvatar(
                    name = conv?.title ?: "",
                    avatarUrl = conv?.avatarUrl ?: conv?.directPeerAvatarUrl,
                    size = 40.dp,
                )
                Spacer(Modifier.width(10.dp))
                Column(Modifier.weight(1f)) {
                    Text(
                        text = conv?.title ?: "…",
                        style = MaterialTheme.typography.titleSmall,
                        fontWeight = FontWeight.Bold,
                        maxLines = 1, overflow = TextOverflow.Ellipsis,
                    )
                    Text(
                        text = when {
                            typingNames.isNotEmpty() -> "${typingNames.first()} در حال نوشتن…"
                            conv?.type == ChatType.DIRECT && conv.directPeerIsOnline -> "آنلاین"
                            conv?.type == ChatType.DIRECT -> "آخرین بازدید اخیراً"
                            conv != null -> "${conv.membersCount} عضو"
                            else -> ""
                        },
                        style = MaterialTheme.typography.labelSmall,
                        color = if (typingNames.isNotEmpty()) MaterialTheme.colorScheme.primary
                        else MaterialTheme.colorScheme.onSurfaceVariant,
                        maxLines = 1, overflow = TextOverflow.Ellipsis,
                    )
                }
            }
        },
        navigationIcon = {
            IconButton(onClick = onBack) {
                Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "بازگشت")
            }
        },
        actions = {
            IconButton(onClick = { menuOpen = true }) {
                Icon(Icons.Default.MoreVert, contentDescription = "بیشتر")
            }
            DropdownMenu(expanded = menuOpen, onDismissRequest = { menuOpen = false }) {
                DropdownMenuItem(text = { Text("مشاهده اعضا") }, onClick = { menuOpen = false; onOpenInfo() })
                DropdownMenuItem(text = { Text("جستجو در گفتگو") }, onClick = { menuOpen = false })
                DropdownMenuItem(text = { Text("بی‌صدا / باصدا") }, onClick = { menuOpen = false })
            }
        },
    )
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun AttachmentSheet(onDismiss: () -> Unit, onPick: (String) -> Unit) {
    ModalBottomSheet(onDismissRequest = onDismiss, sheetState = rememberModalBottomSheetState()) {
        Column(Modifier.padding(16.dp)) {
            Text("ارسال ضمیمه", style = MaterialTheme.typography.titleMedium,
                fontWeight = FontWeight.Bold)
            Spacer(Modifier.height(12.dp))
            listOf("🖼️ عکس" to "image", "🎬 ویدیو" to "video",
                "📄 فایل" to "file", "🎵 صدا" to "audio").forEach { (label, kind) ->
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .clip(RoundedCornerShape(12.dp))
                        .clickable { onPick(kind) }
                        .padding(vertical = 12.dp, horizontal = 8.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Text(label, style = MaterialTheme.typography.bodyLarge)
                    Spacer(Modifier.weight(1f))
                    Icon(Icons.AutoMirrored.Filled.ArrowForward, null,
                        tint = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            }
            Spacer(Modifier.height(24.dp))
        }
    }
}

@Preview(showBackground = true, locale = "fa")
@Composable
private fun ChatScreenPreview() {
    TotallMessengerTheme {
        ChatContent(
            state = ChatUiState(
                conversation = SampleData.conversations[0],
                messages = SampleData.messages,
                isLoading = false,
                typingNames = listOf("علی محمدی"),
            ),
            input = "سلام!",
            onInputChange = {}, onSend = {}, onBack = {}, onOpenInfo = {},
            onLoadMore = {}, onRetry = {}, onAction = { _, _ -> },
            onCancelReply = {}, onAttach = {}, onRecord = {},
        )
    }
}
