package com.totall.messenger.ui.newchat

import androidx.compose.foundation.clickable
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
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.GroupAdd
import androidx.compose.material.icons.filled.Search
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.tooling.preview.Preview
import androidx.compose.ui.unit.dp
import com.totall.messenger.data.ChatRepository
import com.totall.messenger.data.model.ChatUserDto
import com.totall.messenger.ui.SampleData
import com.totall.messenger.ui.components.ChatAvatar
import com.totall.messenger.ui.theme.TotallMessengerTheme
import kotlinx.coroutines.launch

@Composable
fun NewChatScreen(
    onOpenChat: (Int) -> Unit,
    onBack: () -> Unit,
    repo: ChatRepository = remember { ChatRepository() },
) {
    var users by remember { mutableStateOf<List<ChatUserDto>>(emptyList()) }
    var loading by remember { mutableStateOf(true) }
    var search by remember { mutableStateOf("") }
    val scope = rememberCoroutineScope()

    LaunchedEffect(Unit) {
        loading = true
        users = try { repo.users() } catch (_: Exception) { SampleData.users }
        loading = false
    }

    NewChatContent(
        users = users.filter {
            search.isBlank() || it.displayName.contains(search) || it.department?.contains(search) == true
        },
        loading = loading,
        search = search,
        onSearch = { search = it },
        onBack = onBack,
        onPickUser = { user ->
            scope.launch {
                // اگر گفتگوی قبلی هست بازش کن، وگرنه بساز
                val convId = user.existingConversationId ?: try {
                    repo.createDirect(user.id).id
                } catch (_: Exception) { null }
                if (convId != null) onOpenChat(convId)
            }
        },
        onNewGroup = { /* دیالوگ ساخت گروه */ },
    )
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun NewChatContent(
    users: List<ChatUserDto>,
    loading: Boolean,
    search: String,
    onSearch: (String) -> Unit,
    onBack: () -> Unit,
    onPickUser: (ChatUserDto) -> Unit,
    onNewGroup: () -> Unit,
) {
    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("گفتگوی جدید", fontWeight = FontWeight.Bold) },
                navigationIcon = {
                    IconButton(onClick = onBack) {
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "بازگشت")
                    }
                },
            )
        },
    ) { padding ->
        Column(Modifier.fillMaxSize().padding(padding)) {
            OutlinedTextField(
                value = search,
                onValueChange = onSearch,
                modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 8.dp),
                placeholder = { Text("جستجوی همکار…") },
                leadingIcon = { Icon(Icons.Default.Search, contentDescription = null) },
                singleLine = true,
                shape = RoundedCornerShape(24.dp),
            )
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .clickable { onNewGroup() }
                    .padding(horizontal = 16.dp, vertical = 10.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Icon(Icons.Default.GroupAdd, null, tint = MaterialTheme.colorScheme.primary)
                Spacer(Modifier.width(12.dp))
                Text("ساخت گروه جدید",
                    style = MaterialTheme.typography.titleSmall,
                    fontWeight = FontWeight.Bold,
                    color = MaterialTheme.colorScheme.primary)
            }
            Spacer(Modifier.height(4.dp))
            Text("مخاطبین سازمانی",
                style = MaterialTheme.typography.labelLarge,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(horizontal = 16.dp, vertical = 4.dp))
            if (loading) {
                CircularProgressIndicator(modifier = Modifier.padding(32.dp))
            } else {
                LazyColumn(Modifier.fillMaxSize()) {
                    items(users, key = { it.id }) { user ->
                        Row(
                            modifier = Modifier
                                .fillMaxWidth()
                                .clickable { onPickUser(user) }
                                .padding(horizontal = 16.dp, vertical = 9.dp),
                            verticalAlignment = Alignment.CenterVertically,
                        ) {
                            ChatAvatar(
                                name = user.displayName,
                                avatarUrl = user.avatarUrl,
                                showOnlineDot = true,
                                isOnline = user.isOnline,
                            )
                            Spacer(Modifier.width(12.dp))
                            Column(Modifier.weight(1f)) {
                                Text(user.displayName,
                                    style = MaterialTheme.typography.titleSmall,
                                    fontWeight = FontWeight.Bold)
                                Text(
                                    text = listOfNotNull(
                                        user.department,
                                        if (user.isOnline) "آنلاین" else "آفلاین",
                                    ).joinToString(" • "),
                                    style = MaterialTheme.typography.bodySmall,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                                )
                            }
                        }
                    }
                }
            }
        }
    }
}

@Preview(showBackground = true, locale = "fa")
@Composable
private fun NewChatPreview() {
    TotallMessengerTheme {
        NewChatContent(
            users = SampleData.users, loading = false, search = "",
            onSearch = {}, onBack = {}, onPickUser = {}, onNewGroup = {},
        )
    }
}
