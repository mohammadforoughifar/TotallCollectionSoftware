package com.totall.messenger.ui.chat

import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewModelScope
import com.totall.messenger.data.ChatRepository
import com.totall.messenger.data.model.ChatConversationDto
import com.totall.messenger.data.model.ChatMessageDto
import com.totall.messenger.data.model.ChatMessageType
import com.totall.messenger.data.model.SendChatMessageRequest
import com.totall.messenger.data.realtime.ChatEvent
import com.totall.messenger.data.realtime.ChatRealtimeClient
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class ChatUiState(
    val conversation: ChatConversationDto? = null,
    val messages: List<ChatMessageDto> = emptyList(), // قدیمی → جدید
    val isLoading: Boolean = true,
    val isLoadingMore: Boolean = false,
    val hasMore: Boolean = true,
    val typingNames: List<String> = emptyList(),
    val replyTo: ChatMessageDto? = null,
    val error: String? = null,
)

class ChatViewModel(
    private val conversationId: Int,
    private val repo: ChatRepository = ChatRepository(),
    private val realtime: ChatRealtimeClient = ChatRealtimeClient(),
) : ViewModel() {

    private val _uiState = MutableStateFlow(ChatUiState())
    val uiState: StateFlow<ChatUiState> = _uiState.asStateFlow()
    private var typingJob: Job? = null

    init {
        realtime.start()
        realtime.joinConversation(conversationId)
        loadInitial()
        observeRealtime()
    }

    override fun onCleared() {
        realtime.leaveConversation(conversationId)
        super.onCleared()
    }

    fun loadInitial() {
        viewModelScope.launch {
            _uiState.update { it.copy(isLoading = true, error = null) }
            try {
                val conv = repo.conversation(conversationId)
                val msgs = repo.messages(conversationId).sortedBy { it.id }
                _uiState.update {
                    it.copy(conversation = conv, messages = msgs, isLoading = false,
                        hasMore = msgs.size >= 50)
                }
                markRead(msgs.lastOrNull()?.id)
            } catch (e: Exception) {
                _uiState.update { it.copy(isLoading = false, error = e.message ?: "خطای اتصال") }
            }
        }
    }

    /** اسکرول به بالا ← پیام‌های قدیمی‌تر */
    fun loadMore() {
        val s = _uiState.value
        if (s.isLoadingMore || !s.hasMore || s.messages.isEmpty()) return
        viewModelScope.launch {
            _uiState.update { it.copy(isLoadingMore = true) }
            try {
                val older = repo.messages(conversationId, beforeId = s.messages.first().id)
                    .sortedBy { it.id }
                _uiState.update {
                    it.copy(messages = older + it.messages, isLoadingMore = false,
                        hasMore = older.size >= 50)
                }
            } catch (_: Exception) {
                _uiState.update { it.copy(isLoadingMore = false) }
            }
        }
    }

    fun sendText(text: String) {
        val body = text.trim()
        if (body.isEmpty()) return
        val replyId = _uiState.value.replyTo?.id
        _uiState.update { it.copy(replyTo = null) }
        viewModelScope.launch {
            try {
                val sent = repo.send(
                    SendChatMessageRequest(
                        conversationId = conversationId,
                        text = body,
                        messageType = ChatMessageType.TEXT,
                        replyToMessageId = replyId,
                    ),
                )
                appendIfNew(sent)
            } catch (e: Exception) {
                _uiState.update { it.copy(error = e.message ?: "ارسال ناموفق") }
            }
        }
    }

    fun sendReaction(messageId: Int, emoji: String) {
        viewModelScope.launch {
            try {
                val reactions = repo.react(messageId, emoji)
                _uiState.update { s ->
                    s.copy(messages = s.messages.map {
                        if (it.id == messageId) it.copy(reactions = reactions) else it
                    })
                }
            } catch (_: Exception) { }
        }
    }

    fun deleteMessage(messageId: Int) {
        viewModelScope.launch {
            try {
                repo.delete(messageId)
                _uiState.update { s ->
                    s.copy(messages = s.messages.map {
                        if (it.id == messageId) it.copy(isDeleted = true, text = null) else it
                    })
                }
            } catch (_: Exception) { }
        }
    }

    fun setReplyTo(msg: ChatMessageDto?) = _uiState.update { it.copy(replyTo = msg) }

    fun onInputTyping() {
        realtime.sendTyping(conversationId)
        typingJob?.cancel()
        typingJob = viewModelScope.launch {
            delay(2500)
            realtime.stopTyping(conversationId)
        }
    }

    private fun observeRealtime() {
        viewModelScope.launch {
            realtime.events.collect { ev ->
                when (ev) {
                    is ChatEvent.MessageReceived ->
                        if (ev.message.conversationId == conversationId) {
                            appendIfNew(ev.message)
                            markRead(ev.message.id)
                        }
                    is ChatEvent.MessageUpdated ->
                        if (ev.message.conversationId == conversationId) {
                            _uiState.update { s -> s.copy(messages = s.messages.map {
                                if (it.id == ev.message.id) ev.message else it
                            }) }
                        }
                    is ChatEvent.MessageDeleted ->
                        if (ev.conversationId == conversationId) {
                            _uiState.update { s -> s.copy(messages = s.messages.map {
                                if (it.id == ev.messageId) it.copy(isDeleted = true, text = null) else it
                            }) }
                        }
                    is ChatEvent.ReactionUpdated ->
                        if (ev.conversationId == conversationId) {
                            _uiState.update { s -> s.copy(messages = s.messages.map {
                                if (it.id == ev.messageId) it.copy(reactions = ev.reactions) else it
                            }) }
                        }
                    is ChatEvent.MessagesRead ->
                        if (ev.conversationId == conversationId) {
                            _uiState.update { s -> s.copy(messages = s.messages.map {
                                if (it.isOutgoing && it.id <= ev.messageId) it.copy(isReadByPeer = true) else it
                            }) }
                        }
                    is ChatEvent.UserTyping ->
                        if (ev.conversationId == conversationId) {
                            _uiState.update { s ->
                                s.copy(typingNames = (s.typingNames + ev.userName).distinct())
                            }
                            viewModelScope.launch {
                                delay(4000)
                                _uiState.update { s -> s.copy(typingNames = s.typingNames - ev.userName) }
                            }
                        }
                    else -> Unit
                }
            }
        }
    }

    private fun appendIfNew(msg: ChatMessageDto) {
        _uiState.update { s ->
            if (s.messages.any { it.id == msg.id }) s
            else s.copy(messages = (s.messages + msg).sortedBy { it.id })
        }
    }

    private fun markRead(lastId: Int?) {
        if (lastId == null) return
        viewModelScope.launch {
            try { repo.markRead(conversationId, lastId) } catch (_: Exception) { }
        }
    }

    class Factory(private val conversationId: Int) : ViewModelProvider.Factory {
        @Suppress("UNCHECKED_CAST")
        override fun <T : ViewModel> create(modelClass: Class<T>): T =
            ChatViewModel(conversationId) as T
    }
}
