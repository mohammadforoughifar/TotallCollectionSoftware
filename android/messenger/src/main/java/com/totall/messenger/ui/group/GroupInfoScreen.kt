package com.totall.messenger.ui.group

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.PersonAdd
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.tooling.preview.Preview
import androidx.compose.ui.unit.dp
import com.totall.messenger.data.ChatRepository
import com.totall.messenger.data.model.ChatConversationDto
import com.totall.messenger.data.model.ChatMemberDto
import com.totall.messenger.data.model.ChatMemberRole
import com.totall.messenger.ui.SampleData
import com.totall.messenger.ui.components.ChatAvatar
import com.totall.messenger.ui.components.toFaDigits
import com.totall.messenger.ui.theme.TotallMessengerTheme

@Composable
fun GroupInfoScreen(
    conversationId: Int,
    onBack: () -> Unit,
    repo: ChatRepository = remember { ChatRepository() },
) {
    var conv by remember { mutableStateOf<ChatConversationDto?>(null) }
    var members by remember { mutableStateOf<List<ChatMemberDto>>(emptyList()) }
    var loading by remember { mutableStateOf(true) }

    LaunchedEffect(conversationId) {
        loading = true
        try {
            conv = repo.conversation(conversationId)
            members = repo.members(conversationId)
        } catch (_: Exception) {
            conv = SampleData.conversations.find { it.id == conversationId }
            members = SampleData.members
        }
        loading = false
    }

    GroupInfoContent(conv = conv, members = members, loading = loading, onBack = onBack)
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun GroupInfoContent(
    conv: ChatConversationDto?,
    members: List<ChatMemberDto>,
    loading: Boolean,
    onBack: () -> Unit,
) {
    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("اطلاعات گروه", fontWeight = FontWeight.Bold) },
                navigationIcon = {
                    IconButton(onClick = onBack) {
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "بازگشت")
                    }
                },
            )
        },
    ) { padding ->
        if (loading && conv == null) {
            CircularProgressIndicator(modifier = Modifier.padding(32.dp))
            return@Scaffold
        }
        LazyColumn(
            modifier = Modifier.fillMaxSize().padding(padding),
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            item {
                Spacer(Modifier.height(16.dp))
                ChatAvatar(name = conv?.title ?: "", avatarUrl = conv?.avatarUrl, size = 88.dp)
                Spacer(Modifier.height(10.dp))
                Text(conv?.title ?: "",
                    style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Bold)
                Text("${(conv?.membersCount ?: members.size).toFaDigits()} عضو",
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant)
                if (!conv?.description.isNullOrBlank()) {
                    Spacer(Modifier.height(6.dp))
                    Text(conv?.description ?: "",
                        style = MaterialTheme.typography.bodyMedium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        modifier = Modifier.padding(horizontal = 32.dp))
                }
                Spacer(Modifier.height(14.dp))
            }
            item {
                Row(
                    modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 8.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Icon(Icons.Default.PersonAdd, null, tint = MaterialTheme.colorScheme.primary)
                    Spacer(Modifier.width(10.dp))
                    Text("افزودن عضو",
                        style = MaterialTheme.typography.titleSmall,
                        fontWeight = FontWeight.Bold,
                        color = MaterialTheme.colorScheme.primary)
                }
            }
            items(members, key = { it.id }) { m ->
                Row(
                    modifier = Modifier.fillMaxWidth()
                        .padding(horizontal = 16.dp, vertical = 8.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    ChatAvatar(
                        name = m.userDisplayName,
                        avatarUrl = m.userAvatarUrl,
                        size = 44.dp,
                        showOnlineDot = true,
                        isOnline = m.isOnline,
                    )
                    Spacer(Modifier.width(12.dp))
                    Column(Modifier.weight(1f)) {
                        Text(m.userDisplayName,
                            style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.Bold)
                        Text(
                            text = when (m.role) {
                                ChatMemberRole.OWNER -> "مالک گروه"
                                ChatMemberRole.ADMIN -> "مدیر"
                                else -> if (m.isOnline) "آنلاین" else "آفلاین"
                            },
                            style = MaterialTheme.typography.bodySmall,
                            color = if (m.role != ChatMemberRole.MEMBER) MaterialTheme.colorScheme.primary
                            else MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                    }
                }
            }
        }
    }
}

@Preview(showBackground = true, locale = "fa")
@Composable
private fun GroupInfoPreview() {
    TotallMessengerTheme {
        GroupInfoContent(
            conv = SampleData.conversations[1],
            members = SampleData.members,
            loading = false,
            onBack = {},
        )
    }
}
