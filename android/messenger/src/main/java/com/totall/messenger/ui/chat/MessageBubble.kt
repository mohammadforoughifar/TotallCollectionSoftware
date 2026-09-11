package com.totall.messenger.ui.chat

import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Description
import androidx.compose.material.icons.filled.Forward
import androidx.compose.material.icons.filled.PlayArrow
import androidx.compose.material.icons.filled.PushPin
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.tooling.preview.Preview
import androidx.compose.ui.unit.dp
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import coil.compose.AsyncImage
import com.totall.messenger.data.model.ChatMessageDto
import com.totall.messenger.data.model.ChatMessageType
import com.totall.messenger.ui.SampleData
import com.totall.messenger.ui.components.ReadTicks
import com.totall.messenger.ui.components.formatFileSize
import com.totall.messenger.ui.components.formatMessageTime
import com.totall.messenger.ui.components.toFaDigits
import com.totall.messenger.ui.theme.MessengerChat
import com.totall.messenger.ui.theme.SystemChipBg
import com.totall.messenger.ui.theme.TotallMessengerTheme

/**
 * یک ردیف پیام — راست‌چین برای خودم، چپ‌چین برای دیگران (در چیدمان RTL برعکس دیده می‌شود).
 * onAction: reply | react:👍 | delete | pin | copy
 */
@OptIn(ExperimentalFoundationApi::class)
@Composable
fun MessageBubble(
    message: ChatMessageDto,
    showSenderName: Boolean,
    onAction: (String, ChatMessageDto) -> Unit,
    modifier: Modifier = Modifier,
) {
    if (message.messageType == ChatMessageType.SYSTEM) {
        SystemMessageRow(message, modifier)
        return
    }
    val chat = MessengerChat.colors
    val out = message.isOutgoing
    val bubbleColor = if (out) chat.bubbleOut else chat.bubbleIn
    val textColor = if (out) chat.bubbleOutText else chat.bubbleInText
    var menuOpen by remember { mutableStateOf(false) }

    Row(
        modifier = modifier
            .fillMaxWidth()
            .padding(horizontal = 12.dp, vertical = 2.dp),
        horizontalArrangement = if (out) Arrangement.End else Arrangement.Start,
    ) {
        Column(
            modifier = Modifier
                .widthIn(max = 300.dp)
                .clip(
                    RoundedCornerShape(
                        topStart = 16.dp, topEnd = 16.dp,
                        bottomStart = if (out) 16.dp else 4.dp,
                        bottomEnd = if (out) 4.dp else 16.dp,
                    ),
                )
                .background(bubbleColor)
                .combinedClickable(
                    onClick = {},
                    onLongClick = { menuOpen = true },
                )
                .padding(10.dp),
        ) {
            // نام فرستنده در گروه
            if (!out && showSenderName) {
                Text(
                    text = message.senderName,
                    style = MaterialTheme.typography.labelLarge,
                    fontWeight = FontWeight.Bold,
                    color = MaterialTheme.colorScheme.primary,
                )
                Spacer(Modifier.height(2.dp))
            }
            // نشان فوروارد
            if (message.forwardFromMessageId != null) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Icon(Icons.Default.Forward, null, Modifier.size(14.dp), tint = textColor.copy(alpha = .7f))
                    Spacer(Modifier.width(4.dp))
                    Text(
                        text = "فوروارد از ${message.forwardFromSenderName ?: ""}",
                        style = MaterialTheme.typography.labelSmall,
                        color = textColor.copy(alpha = .7f),
                    )
                }
                Spacer(Modifier.height(4.dp))
            }
            // پیش‌نمایش ریپلای
            if (message.replyToMessageId != null) {
                ReplyPreview(
                    sender = message.replyToSenderName ?: "",
                    snippet = message.replyToSnippet ?: "",
                    accent = if (out) Color.White else MaterialTheme.colorScheme.primary,
                )
                Spacer(Modifier.height(4.dp))
            }
            // بدنه بر اساس نوع
            when (message.messageType) {
                ChatMessageType.IMAGE -> MediaBody(message, textColor, isVideo = false)
                ChatMessageType.VIDEO -> MediaBody(message, textColor, isVideo = true)
                ChatMessageType.FILE -> FileBody(message, textColor)
                ChatMessageType.AUDIO -> AudioBody(message, textColor)
                ChatMessageType.ERP_SHARE -> ErpShareBody(message, out)
                else -> {
                    if (message.isDeleted) {
                        Text("🚫 این پیام حذف شد",
                            style = MaterialTheme.typography.bodyMedium,
                            color = textColor.copy(alpha = .6f))
                    } else {
                        Text(message.text ?: "",
                            style = MaterialTheme.typography.bodyLarge,
                            color = textColor)
                    }
                }
            }
            // واکنش‌ها
            if (message.reactions.isNotEmpty()) {
                Spacer(Modifier.height(6.dp))
                Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                    message.reactions.forEach { (emoji, users) ->
                        Box(
                            modifier = Modifier
                                .clip(RoundedCornerShape(12.dp))
                                .background(textColor.copy(alpha = .15f))
                                .padding(horizontal = 8.dp, vertical = 2.dp),
                        ) {
                            Text("$emoji ${users.size.toFaDigits()}",
                                style = MaterialTheme.typography.labelLarge)
                        }
                    }
                }
            }
            // ساعت + ویرایش‌شده + تیک
            Spacer(Modifier.height(2.dp))
            Row(
                modifier = Modifier.align(Alignment.End),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                if (message.isPinned) {
                    Icon(Icons.Default.PushPin, null, Modifier.size(12.dp),
                        tint = textColor.copy(alpha = .7f))
                    Spacer(Modifier.width(3.dp))
                }
                if (message.isEdited) {
                    Text("ویرایش‌شده ", style = MaterialTheme.typography.labelSmall,
                        color = textColor.copy(alpha = .7f))
                }
                Text(
                    formatMessageTime(message.createdAt),
                    style = MaterialTheme.typography.labelSmall,
                    color = textColor.copy(alpha = .75f),
                )
                if (out) {
                    Spacer(Modifier.width(4.dp))
                    ReadTicks(message.isReadByPeer, dark = true)
                }
            }
        }
        // منوی نگه‌داشتن
        DropdownMenu(expanded = menuOpen, onDismissRequest = { menuOpen = false }) {
            DropdownMenuItem(text = { Text("👍 ❤️ 😂  واکنش") }, onClick = {
                menuOpen = false; onAction("react:👍", message)
            })
            DropdownMenuItem(text = { Text("پاسخ") }, onClick = {
                menuOpen = false; onAction("reply", message)
            })
            DropdownMenuItem(text = { Text("کپی") }, onClick = {
                menuOpen = false; onAction("copy", message)
            })
            if (out) DropdownMenuItem(text = { Text("حذف") }, onClick = {
                menuOpen = false; onAction("delete", message)
            })
            DropdownMenuItem(text = { Text("سنجاق") }, onClick = {
                menuOpen = false; onAction("pin", message)
            })
        }
    }
}

@Composable
private fun ReplyPreview(sender: String, snippet: String, accent: Color) {
    Row(
        modifier = Modifier
            .clip(RoundedCornerShape(8.dp))
            .background(accent.copy(alpha = .12f))
            .padding(horizontal = 8.dp, vertical = 6.dp),
    ) {
        Box(Modifier.width(3.dp).height(32.dp).clip(RoundedCornerShape(2.dp)).background(accent))
        Spacer(Modifier.width(8.dp))
        Column {
            Text(sender, style = MaterialTheme.typography.labelLarge,
                fontWeight = FontWeight.Bold, color = accent)
            Text(snippet, style = MaterialTheme.typography.bodySmall,
                color = accent.copy(alpha = .85f), maxLines = 2)
        }
    }
}

@Composable
private fun MediaBody(message: ChatMessageDto, textColor: Color, isVideo: Boolean) {
    val url = message.fileUrl.orEmpty()
    Box(
        modifier = Modifier
            .clip(RoundedCornerShape(12.dp))
            .background(Color.Black.copy(alpha = .1f)),
    ) {
        AsyncImage(
            model = url,
            contentDescription = message.fileName,
            modifier = Modifier.size(width = 240.dp, height = 170.dp),
            contentScale = ContentScale.Crop,
        )
        if (isVideo) {
            Box(
                modifier = Modifier
                    .align(Alignment.Center)
                    .size(46.dp)
                    .clip(androidx.compose.foundation.shape.CircleShape)
                    .background(Color.Black.copy(alpha = .55f)),
                contentAlignment = Alignment.Center,
            ) {
                Icon(Icons.Default.PlayArrow, null, tint = Color.White, modifier = Modifier.size(28.dp))
            }
        }
    }
    if (!message.text.isNullOrBlank()) {
        Spacer(Modifier.height(6.dp))
        Text(message.text!!, style = MaterialTheme.typography.bodyLarge, color = textColor)
    }
}

@Composable
private fun FileBody(message: ChatMessageDto, textColor: Color) {
    Row(
        modifier = Modifier
            .clip(RoundedCornerShape(12.dp))
            .background(textColor.copy(alpha = .12f))
            .clickable { }
            .padding(10.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Box(
            modifier = Modifier.size(42.dp)
                .clip(RoundedCornerShape(10.dp))
                .background(textColor.copy(alpha = .18f)),
            contentAlignment = Alignment.Center,
        ) {
            Icon(Icons.Default.Description, null, tint = textColor)
        }
        Spacer(Modifier.width(10.dp))
        Column(Modifier.weight(1f)) {
            Text(message.fileName ?: "فایل",
                style = MaterialTheme.typography.bodyMedium,
                fontWeight = FontWeight.Bold, color = textColor, maxLines = 2)
            Text(formatFileSize(message.fileSizeBytes),
                style = MaterialTheme.typography.labelSmall,
                color = textColor.copy(alpha = .7f))
        }
    }
    if (!message.text.isNullOrBlank()) {
        Spacer(Modifier.height(6.dp))
        Text(message.text!!, style = MaterialTheme.typography.bodyLarge, color = textColor)
    }
}

@Composable
private fun AudioBody(message: ChatMessageDto, textColor: Color) {
    Row(
        modifier = Modifier.width(220.dp).padding(vertical = 4.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Box(
            modifier = Modifier.size(38.dp)
                .clip(androidx.compose.foundation.shape.CircleShape)
                .background(textColor.copy(alpha = .18f))
                .clickable { },
            contentAlignment = Alignment.Center,
        ) {
            Icon(Icons.Default.PlayArrow, null, tint = textColor)
        }
        Spacer(Modifier.width(8.dp))
        // موج صوتی نمایشی
        Row(
            modifier = Modifier.weight(1f).height(28.dp),
            horizontalArrangement = Arrangement.spacedBy(2.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            val bars = remember { listOf(8, 14, 20, 12, 24, 16, 10, 22, 14, 18, 8, 12, 20, 10, 16, 12, 22, 14, 8, 16) }
            bars.forEach { h ->
                Box(Modifier.width(3.dp).height(h.dp)
                    .clip(RoundedCornerShape(2.dp))
                    .background(textColor.copy(alpha = .55f)))
            }
        }
        Spacer(Modifier.width(8.dp))
        Text("۰:۴۵", style = MaterialTheme.typography.labelSmall, color = textColor.copy(alpha = .75f))
    }
}

@Composable
private fun ErpShareBody(message: ChatMessageDto, out: Boolean) {
    val bg = if (out) Color.White.copy(alpha = .16f)
    else MaterialTheme.colorScheme.primaryContainer.copy(alpha = .5f)
    Column(
        modifier = Modifier
            .clip(RoundedCornerShape(12.dp))
            .background(bg)
            .clickable { }
            .padding(10.dp),
    ) {
        Text("📦 ${message.erpModule ?: "سند ERP"}",
            style = MaterialTheme.typography.labelLarge,
            fontWeight = FontWeight.Bold,
            color = if (out) Color.White else MaterialTheme.colorScheme.primary)
        Spacer(Modifier.height(2.dp))
        Text(message.erpEntityTitle ?: "",
            style = MaterialTheme.typography.bodyMedium,
            fontWeight = FontWeight.Bold,
            color = if (out) Color.White else MaterialTheme.colorScheme.onSurface)
        if (!message.erpEntitySummary.isNullOrBlank()) {
            Text(message.erpEntitySummary!!,
                style = MaterialTheme.typography.bodySmall,
                color = (if (out) Color.White else MaterialTheme.colorScheme.onSurface).copy(alpha = .8f))
        }
        Spacer(Modifier.height(6.dp))
        Text("مشاهده در سامانه ←",
            style = MaterialTheme.typography.labelLarge,
            color = if (out) Color.White else MaterialTheme.colorScheme.primary)
    }
}

@Composable
private fun SystemMessageRow(message: ChatMessageDto, modifier: Modifier = Modifier) {
    Row(
        modifier = modifier.fillMaxWidth().padding(vertical = 6.dp),
        horizontalArrangement = Arrangement.Center,
    ) {
        Text(
            text = message.text ?: "",
            modifier = Modifier
                .clip(RoundedCornerShape(12.dp))
                .background(SystemChipBg.copy(alpha = .85f))
                .padding(horizontal = 12.dp, vertical = 5.dp),
            style = MaterialTheme.typography.labelLarge,
            color = Color.White,
        )
    }
}

@Preview(showBackground = true, locale = "fa")
@Composable
private fun MessageBubblePreview() {
    TotallMessengerTheme {
        Column(Modifier.background(MaterialTheme.colorScheme.background).padding(vertical = 8.dp)) {
            SampleData.messages.forEach {
                MessageBubble(message = it, showSenderName = true, onAction = { _, _ -> })
            }
        }
    }
}
