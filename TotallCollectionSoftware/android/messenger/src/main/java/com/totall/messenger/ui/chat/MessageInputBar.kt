package com.totall.messenger.ui.chat

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
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
import androidx.compose.material.icons.filled.Hearing
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
import com.totall.messenger.ui.components.toFaDigits

/**
 * نوار ورودی پیام: ضمیمه + متن + ارسال/ضبط + نوار ریپلای
 * حالت‌ها:
 *  - recording: نوار قرمز «در حال ضبط» با لغو/ارسال
 *  - دکمهٔ میکروفون وقتی متن خالی است → پیام صوتی
 *  - دکمهٔ Hearing وقتی متن داریم → گفتار به نوشتار
 */
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
    recording: Boolean = false,
    recSeconds: Int = 0,
    onCancelRecord: () -> Unit = {},
    onSendRecorded: () -> Unit = {},
    onDictate: () -> Unit = {},
    dictating: Boolean = false,
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
            if (recording) {
                // حالت ضبط پیام صوتی: لغو یا ارسال
                Row(
                    modifier = Modifier.fillMaxWidth().padding(vertical = 2.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    IconButton(onClick = onCancelRecord) {
                        Icon(Icons.Default.Close, contentDescription = "لغو ضبط",
                            tint = MaterialTheme.colorScheme.error)
                    }
                    Box(
                        modifier = Modifier.size(10.dp)
                            .background(Color(0xFFDC2626), CircleShape),
                    )
                    Spacer(Modifier.width(8.dp))
                    Text(
                        java.util.Locale.ROOT.let { "%02d:%02d".format(it, recSeconds / 60, recSeconds % 60) }.toFaDigits(),
                        style = MaterialTheme.typography.titleSmall,
                        fontWeight = FontWeight.Bold,
                        color = Color(0xFFDC2626),
                    )
                    Spacer(Modifier.width(8.dp))
                    Text(
                        "در حال ضبط…",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        modifier = Modifier.weight(1f),
                    )
                    FilledIconButton(
                        onClick = onSendRecorded,
                        modifier = Modifier.size(48.dp),
                        shape = CircleShape,
                    ) {
                        Icon(Icons.AutoMirrored.Filled.Send, contentDescription = "ارسال پیام صوتی")
                    }
                }
            } else {
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
                    // گفتار به نوشتار — تبدیل حرف‌ها به متن در همین کادر
                    if (text.isNotBlank()) {
                        IconButton(onClick = onDictate) {
                            Icon(
                                Icons.Default.Hearing,
                                contentDescription = "گفتار به نوشتار",
                                tint = if (dictating) Color(0xFFDC2626)
                                else MaterialTheme.colorScheme.onSurfaceVariant,
                            )
                        }
                    }
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
}
