package com.totall.messenger.ui.list

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.totall.messenger.data.ChatRepository
import com.totall.messenger.data.model.ChatConversationDto
import com.totall.messenger.data.model.ChatType
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

enum class ChatFilter { ALL, UNREAD, GROUPS }

data class ConversationListUiState(
    val conversations: List<ChatConversationDto> = emptyList(),
    val isLoading: Boolean = true,
    val search: String = "",
    val filter: ChatFilter = ChatFilter.ALL,
    val error: String? = null,
)

class ConversationListViewModel(
    private val repo: ChatRepository = ChatRepository(),
) : ViewModel() {

    private val _uiState = MutableStateFlow(ConversationListUiState())
    val uiState: StateFlow<ConversationListUiState> = _uiState.asStateFlow()

    init { refresh() }

    fun refresh() {
        viewModelScope.launch {
            _uiState.update { it.copy(isLoading = true, error = null) }
            try {
                val s = _uiState.value
                val list = repo.conversations(
                    search = s.search.ifBlank { null },
                    type = if (s.filter == ChatFilter.GROUPS) ChatType.GROUP else null,
                    onlyUnread = s.filter == ChatFilter.UNREAD,
                )
                _uiState.update { it.copy(conversations = sorted(list), isLoading = false) }
            } catch (e: Exception) {
                _uiState.update { it.copy(isLoading = false, error = e.message ?: "خطای اتصال") }
            }
        }
    }

    fun onSearchChange(q: String) {
        _uiState.update { it.copy(search = q) }
        refresh()
    }

    fun onFilterChange(f: ChatFilter) {
        _uiState.update { it.copy(filter = f) }
        refresh()
    }

    fun togglePin(id: Int) {
        viewModelScope.launch {
            try {
                repo.togglePinConversation(id)
                _uiState.update { s ->
                    s.copy(conversations = sorted(s.conversations.map {
                        if (it.id == id) it.copy(isPinned = !it.isPinned) else it
                    }))
                }
            } catch (_: Exception) { }
        }
    }

    fun toggleMute(id: Int) {
        viewModelScope.launch {
            try {
                repo.toggleMuteConversation(id)
                _uiState.update { s ->
                    s.copy(conversations = s.conversations.map {
                        if (it.id == id) it.copy(isMuted = !it.isMuted) else it
                    })
                }
            } catch (_: Exception) { }
        }
    }

    /** سنجاق‌شده‌ها اول، بعد بر اساس آخرین پیام */
    private fun sorted(list: List<ChatConversationDto>): List<ChatConversationDto> =
        list.sortedWith(compareByDescending<ChatConversationDto> { it.isPinned }
            .thenByDescending { it.lastMessageAt })
}
