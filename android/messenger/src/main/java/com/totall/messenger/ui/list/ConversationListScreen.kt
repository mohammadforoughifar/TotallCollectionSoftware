package com.totall.messenger.ui.list

import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.background
import androidx.compose.foundation.combinedClickable
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
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.Campaign
import androidx.compose.material.icons.filled.Group
import androidx.compose.material.icons.filled.PushPin
import androidx.compose.material.icons.filled.Search
import androidx.compose.material.icons.filled.VolumeOff
import androidx.compose.material3.Badge
import androidx.compose.material3.BadgedBox
import androidx.compose.material3.CenterAlignedTopAppBar
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FloatingActionButton
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.ScrollableTabRow
import androidx.compose.material3.Tab
import androidx.compose.material3.Text
import androidx.compose.material3.pulltorefresh.PullToRefreshBox
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.tooling.preview.Preview
import androidx.compose.ui.unit.dp
import androidx.lifecycle.viewmodel.compose.viewModel
import com.totall.messenger.data.model.ChatConversationDto
import com.totall.messenger.data.model.ChatType
import com.totall.messenger.ui.SampleData
import com.totall.messenger.ui.components.ChatAvatar
import com.totall.messenger.ui.components.formatMessageTime
import com.totall.messenger.ui.components.toFaDigits
import com.totall.messenger.ui.theme.TotallMessengerTheme

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ConversationListScreen(
    onOpenChat: (Int) -> Unit,
    onNewChat: () -> Unit,
    vm: ConversationListViewModel = viewModel(),
) {
    val state by vm.uiState.collectAsState()
    ConversationListContent(
        state = state,
        onSearch = vm::onSearchChange,
        onFilter = vm::onFilterChange,
        onRefresh = vm::refresh,
        onOpenChat = onOpenChat,
        onNewChat = onNewChat,
        onTogglePin = vm::togglePin,
        onToggleMute = vm::toggleMute,
    )
}

@OptIn(ExperimentalMaterial3Api::class, ExperimentalFoundationApi::class)
@Composable
fun ConversationListContent(
    state: ConversationListUiState,
    onSearch: (String) -> Unit,
    onFilter: (ChatFilter) -> Unit,
    onRefresh: () -> Unit,
    onOpenChat: (Int) -> Unit,
    onNewChat: () -> Unit,
    onTogglePin: (Int) -> Unit,
    onToggleMute: (Int) -> Unit,
) {
    Scaffold(
        topBar = {
            CenterAlignedTopAppBar(title = {
                Text("پیام‌رسان توتال", style = MaterialTheme.typography.titleLarge)
            })
        },
        floatingActionButton = {
            FloatingActionButton(onClick = onNewChat) {
                Icon(Icons.Default.Add, contentDescription = "گفتگوی جدید")
            }
        },
    ) { padding ->
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(padding),
        ) {
            // جستجو
            OutlinedTextField(
                value = state.search,
                onValueChange = onSearch,
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(horizontal = 16.dp, vertical = 8.dp),
                placeholder = { Text("جستجو…") },
                leadingIcon = { Icon(Icons.Default.Search, contentDescription = null) },
                singleLine = true,
                shape = RoundedCornerShape(24.dp),
            )
            // تب‌های فیلتر
            ScrollableTabRow(
                selectedTabIndex = state.filter.ordinal,
                edgePadding = 16.dp,
            ) {
                Tab(selected = state.filter == ChatFilter.ALL, onClick = { onFilter(ChatFilter.ALL) }, text = { Text("همه") })
                Tab(selected = state.filter == ChatFilter.UNREAD, onClick = { onFilter(ChatFilter.UNREAD) }, text = { Text("خوانده‌نشده") })
                Tab(selected = state.filter == ChatFilter.GROUPS, onClick = { onFilter(ChatFilter.GROUPS) }, text = { Text("گروه‌ها") })
            }
            // لیست
            PullToRefreshBox(
                isRefreshing = state.isLoading,
                onRefresh = onRefresh,
                modifier = Modifier.fillMaxSize(),
            ) {
                if (!state.isLoading && state.conversations.isEmpty()) {
                    Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                        Text("گفتگویی نیست 🙂", color = MaterialTheme.colorScheme.onSurfaceVariant)
                    }
                } else {
                    LazyColumn(modifier = Modifier.fillMaxSize()) {
                        items(state.conversations, key = { it.id }) { conv ->
                            ConversationRow(
                                conv = conv,
                                onClick = { onOpenChat(conv.id) },
                                onTogglePin = { onTogglePin(conv.id) },
                                onToggleMute = { onToggleMute(conv.id) },
                            )
                        }
                        item { Spacer(Modifier.height(88.dp)) }
                    }
                }
            }
        }
    }
}

@OptIn(ExperimentalFoundationApi::class)
@Composable
private fun ConversationRow(
    conv: ChatConversationDto,
    onClick: () -> Unit,
    onTogglePin: () -> Unit,
    onToggleMute: () -> Unit,
) {
    var menuOpen by remember { mutableStateOf(false) }
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .combinedClickable(onClick = onClick, onLongClick = { menuOpen = true })
            .padding(horizontal = 16.dp, vertical = 10.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Box {
            ChatAvatar(
                name = conv.title,
                avatarUrl = conv.avatarUrl ?: conv.directPeerAvatarUrl,
                showOnlineDot = conv.type == ChatType.DIRECT,
                isOnline = conv.directPeerIsOnline,
            )
            // نشان نوع گروه/کانال
            if (conv.type != ChatType.DIRECT) {
                Box(
                    modifier = Modifier
                        .align(Alignment.BottomEnd)
                        .size(20.dp)
                        .clip(CircleShape)
                        .background(MaterialTheme.colorScheme.primary),
                    contentAlignment = Alignment.Center,
                ) {
                    Icon(
                        imageVector = if (conv.type == ChatType.GROUP) Icons.Default.Group else Icons.Default.Campaign,
                        contentDescription = null,
                        tint = Color.White,
                        modifier = Modifier.size(13.dp),
                    )
                }
            }
        }
        Spacer(Modifier.width(12.dp))
        Column(modifier = Modifier.weight(1f)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(
                    text = conv.title,
                    style = MaterialTheme.typography.titleSmall,
                    fontWeight = FontWeight.Bold,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                    modifier = Modifier.weight(1f),
                )
                Text(
                    text = formatMessageTime(conv.lastMessageAt),
                    style = MaterialTheme.typography.labelSmall,
                    color = if (conv.unreadCount > 0) MaterialTheme.colorScheme.primary
                    else MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
            Spacer(Modifier.height(2.dp))
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(
                    text = conv.lastMessageSnippet ?: "…",
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                    modifier = Modifier.weight(1f),
                )
                if (conv.isMuted) {
                    Icon(Icons.Default.VolumeOff, null, Modifier.size(15.dp),
                        tint = MaterialTheme.colorScheme.onSurfaceVariant)
                    Spacer(Modifier.width(4.dp))
                }
                if (conv.isPinned) {
                    Icon(Icons.Default.PushPin, null, Modifier.size(15.dp),
                        tint = MaterialTheme.colorScheme.onSurfaceVariant)
                    Spacer(Modifier.width(4.dp))
                }
                if (conv.unreadCount > 0) {
                    BadgedBox(badge = {
                        Badge { Text(conv.unreadCount.toFaDigits()) }
                    }) { }
                }
            }
        }
        DropdownMenu(expanded = menuOpen, onDismissRequest = { menuOpen = false }) {
            DropdownMenuItem(
                text = { Text(if (conv.isPinned) "برداشتن سنجاق" else "سنجاق") },
                onClick = { menuOpen = false; onTogglePin() },
            )
            DropdownMenuItem(
                text = { Text(if (conv.isMuted) "روشن‌کردن صدا" else "بی‌صدا") },
                onClick = { menuOpen = false; onToggleMute() },
            )
        }
    }
}

@Preview(showBackground = true, locale = "fa")
@Composable
private fun ConversationListPreview() {
    TotallMessengerTheme {
        ConversationListContent(
            state = ConversationListUiState(
                conversations = SampleData.conversations,
                isLoading = false,
            ),
            onSearch = {}, onFilter = {}, onRefresh = {},
            onOpenChat = {}, onNewChat = {}, onTogglePin = {}, onToggleMute = {},
        )
    }
}
