package com.totall.messenger.ui.chat

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.Send
import androidx.compose.material.icons.filled.AttachFile
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.Mic
import androidx.compose.material3.FilledIconButton
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextField
import androidx.compose.material3.TextFieldDefaults
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.totall.messenger.data.model.ChatMessageDto

/** نوار ورودی پیام: ضمیمه + متن + ارسال/ضبط + نوار ریپلای */
@Composable
fun MessageInputBar(
    text: String,
    onTextChange: (String) -> Unit,
    onSend: () -> Unit,
    onAttach: () -> Unit,
    onRecord: () -> Unit,
    replyTo: ChatMessageDto?,
    onCancelReply: () -> Unit,
    modifier: Modifier = Modifier,
) {
    Surface(shadowElevation = 8.dp) {
        Column(modifier = modifier.padding(horizontal = 8.dp, vertical = 6.dp)) {
            // نوار «پاسخ به …»
            if (replyTo != null) {
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .clip(RoundedCornerShape(10.dp))
                        .padding(horizontal = 8.dp, vertical = 6.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    IconButton(onClick = onCancelReply, modifier = Modifier.size(28.dp)) {
                        Icon(Icons.Default.Close, contentDescription = "انصراف",
                            modifier = Modifier.size(18.dp))
                    }
                    Spacer(Modifier.width(4.dp))
                    Column(Modifier.weight(1f)) {
                        Text("پاسخ به ${replyTo.senderName}",
                            style = MaterialTheme.typography.labelLarge,
                            fontWeight = FontWeight.Bold,
                            color = MaterialTheme.colorScheme.primary)
                        Text(replyTo.text ?: replyTo.fileName ?: "…",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                            maxLines = 1)
                    }
                }
            }
            Row(verticalAlignment = Alignment.Bottom) {
                IconButton(onClick = onAttach) {
                    Icon(Icons.Default.AttachFile, contentDescription = "ضمیمه")
                }
                TextField(
                    value = text,
                    onValueChange = onTextChange,
                    modifier = Modifier.weight(1f),
                    placeholder = { Text("پیام بنویسید…") },
                    shape = RoundedCornerShape(24.dp),
                    colors = TextFieldDefaults.colors(
                        focusedIndicatorColor = Color.Transparent,
                        unfocusedIndicatorColor = Color.Transparent,
                    ),
                    maxLines = 5,
                )
                Spacer(Modifier.width(6.dp))
                if (text.isBlank()) {
                    FilledIconButton(onClick = onRecord, modifier = Modifier.size(48.dp)) {
                        Icon(Icons.Default.Mic, contentDescription = "پیام صوتی")
                    }
                } else {
                    FilledIconButton(
                        onClick = onSend,
                        modifier = Modifier.size(48.dp),
                        shape = CircleShape,
                    ) {
                        Icon(Icons.AutoMirrored.Filled.Send, contentDescription = "ارسال")
                    }
                }
            }
        }
    }
}
