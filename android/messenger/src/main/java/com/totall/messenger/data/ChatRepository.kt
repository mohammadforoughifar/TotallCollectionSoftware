package com.totall.messenger.data

import android.content.Context
import com.totall.messenger.data.model.ChatConversationDto
import com.totall.messenger.data.model.ChatMemberDto
import com.totall.messenger.data.model.ChatMessageDto
import com.totall.messenger.data.model.ChatSummaryDto
import com.totall.messenger.data.model.ChatUploadResultDto
import com.totall.messenger.data.model.ChatUserDto
import com.totall.messenger.data.model.CreateDirectChatRequest
import com.totall.messenger.data.model.CreateGroupChatRequest
import com.totall.messenger.data.model.EditChatMessageRequest
import com.totall.messenger.data.model.MarkChatReadRequest
import com.totall.messenger.data.model.ReactChatMessageRequest
import com.totall.messenger.data.model.SendChatMessageRequest
import com.totall.messenger.data.remote.ChatApi
import com.totall.messenger.data.remote.NetworkProvider
import okhttp3.MediaType.Companion.toMediaTypeOrNull
import okhttp3.MultipartBody
import okhttp3.RequestBody.Companion.asRequestBody
import java.io.File

/** مخزن چت — همه‌ی فراخوانی‌های REST پیام‌رسان از اینجا می‌گذرد */
class ChatRepository(
    private val api: ChatApi = NetworkProvider.chatApi(),
) {
    suspend fun conversations(
        search: String? = null,
        type: Int? = null,
        onlyUnread: Boolean = false,
    ): List<ChatConversationDto> = api.getConversations(search, type, onlyUnread)

    suspend fun conversation(id: Int): ChatConversationDto = api.getConversation(id)

    suspend fun createDirect(targetUserId: Int): ChatConversationDto =
        api.createDirect(CreateDirectChatRequest(targetUserId))

    suspend fun createGroup(title: String, memberIds: List<Int>, type: Int): ChatConversationDto =
        api.createGroup(CreateGroupChatRequest(title = title, memberUserIds = memberIds, type = type))

    suspend fun messages(
        conversationId: Int,
        beforeId: Int? = null,
        pageSize: Int = 50,
    ): List<ChatMessageDto> = api.getMessages(conversationId, beforeId, pageSize)

    suspend fun send(req: SendChatMessageRequest): ChatMessageDto =
        api.sendMessage(req.conversationId, req)

    suspend fun edit(messageId: Int, newText: String): ChatMessageDto =
        api.editMessage(messageId, EditChatMessageRequest(newText))

    suspend fun delete(messageId: Int) = api.deleteMessage(messageId)

    suspend fun react(messageId: Int, emoji: String) =
        api.react(messageId, ReactChatMessageRequest(emoji))

    suspend fun togglePinMessage(messageId: Int): ChatMessageDto = api.togglePinMessage(messageId)

    suspend fun markRead(conversationId: Int, lastReadId: Int?) =
        api.markRead(conversationId, MarkChatReadRequest(lastReadId))

    suspend fun togglePinConversation(id: Int) = api.togglePinConversation(id)

    suspend fun toggleMuteConversation(id: Int) = api.toggleMuteConversation(id)

    suspend fun members(conversationId: Int): List<ChatMemberDto> = api.getMembers(conversationId)

    suspend fun users(search: String? = null): List<ChatUserDto> = api.getUsers(search)

    suspend fun summary(): ChatSummaryDto = api.getSummary()

    /** آپلود فایل (حداکثر ۵۰MB) و برگرداندن fileUrl برای ارسال پیام */
    suspend fun uploadAttachment(
        conversationId: Int,
        context: Context,
        file: File,
        mimeType: String,
    ): ChatUploadResultDto {
        val part = MultipartBody.Part.createFormData(
            "file",
            file.name,
            file.asRequestBody(mimeType.toMediaTypeOrNull()),
        )
        return api.uploadAttachment(conversationId, part)
    }
}
