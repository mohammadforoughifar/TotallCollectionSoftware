package com.totall.messenger.data.model

import kotlinx.serialization.Serializable

// آینه‌ی Inventory.Shared/Dtos/ChatDtos.cs
// ⚠️ سرور enumها را عددی می‌فرستد (بدون JsonStringEnumConverter) —
// پس فیلدهای type/messageType/role به‌صورت Int نگه داشته شده‌اند و با هلپرهای زیر تفسیر می‌شوند.

object ChatType {
    const val DIRECT = 1   // گفتگوی خصوصی دونفره
    const val GROUP = 2    // گروه کاری سازمانی
    const val CHANNEL = 3  // کانال اطلاع‌رسانی
}

object ChatMessageType {
    const val TEXT = 1
    const val IMAGE = 2
    const val FILE = 3
    const val AUDIO = 4
    const val VIDEO = 5
    const val SYSTEM = 6
    const val ERP_SHARE = 7
}

object ChatMemberRole {
    const val OWNER = 1
    const val ADMIN = 2
    const val MEMBER = 3
}

@Serializable
data class ChatConversationDto(
    val id: Int = 0,
    val title: String = "",
    val type: Int = ChatType.DIRECT,
    val description: String? = null,
    val avatarUrl: String? = null,
    val lastMessageAt: String = "",
    val lastMessageSnippet: String? = null,
    val lastMessageSenderName: String? = null,
    val pinnedMessageId: Int? = null,
    val pinnedMessageSnippet: String? = null,
    val unreadCount: Int = 0,
    val isPinned: Boolean = false,
    val isMuted: Boolean = false,
    val isArchived: Boolean = false,
    val lastReadMessageId: Int = 0,
    val directPeerUserId: Int? = null,
    val directPeerName: String? = null,
    val directPeerAvatarUrl: String? = null,
    val directPeerIsOnline: Boolean = false,
    val directPeerLastSeen: String? = null,
    val membersCount: Int = 0,
)

@Serializable
data class ChatReactionUserDto(
    val userId: Int = 0,
    val userName: String = "",
)

@Serializable
data class ChatMessageDto(
    val id: Int = 0,
    val conversationId: Int = 0,
    val senderUserId: Int = 0,
    val senderName: String = "",
    val senderAvatarUrl: String? = null,
    val text: String? = null,
    val messageType: Int = ChatMessageType.TEXT,
    val fileUrl: String? = null,
    val fileName: String? = null,
    val fileSizeBytes: Long? = null,
    val fileContentType: String? = null,
    val replyToMessageId: Int? = null,
    val replyToSenderName: String? = null,
    val replyToSnippet: String? = null,
    val forwardFromMessageId: Int? = null,
    val forwardFromSenderName: String? = null,
    val erpModule: String? = null,
    val erpEntityId: String? = null,
    val erpEntityTitle: String? = null,
    val erpEntitySummary: String? = null,
    val isEdited: Boolean = false,
    val isDeleted: Boolean = false,
    val isPinned: Boolean = false,
    val reactions: Map<String, List<ChatReactionUserDto>> = emptyMap(),
    val createdAt: String = "",
    val isOutgoing: Boolean = false,
    val isReadByPeer: Boolean = false,
)

@Serializable
data class ChatMemberDto(
    val id: Int = 0,
    val conversationId: Int = 0,
    val userId: Int = 0,
    val userName: String = "",
    val userDisplayName: String = "",
    val userAvatarUrl: String? = null,
    val role: Int = ChatMemberRole.MEMBER,
    val joinedAt: String = "",
    val isOnline: Boolean = false,
    val lastSeen: String? = null,
)

@Serializable
data class ChatUserDto(
    val id: Int = 0,
    val username: String = "",
    val displayName: String = "",
    val avatarUrl: String? = null,
    val department: String? = null,
    val role: String? = null,
    val isOnline: Boolean = false,
    val lastSeen: String? = null,
    val existingConversationId: Int? = null,
)

@Serializable
data class SendChatMessageRequest(
    val conversationId: Int,
    val text: String? = null,
    val messageType: Int = ChatMessageType.TEXT,
    val fileUrl: String? = null,
    val fileName: String? = null,
    val fileSizeBytes: Long? = null,
    val fileContentType: String? = null,
    val replyToMessageId: Int? = null,
    val forwardFromMessageId: Int? = null,
    val erpModule: String? = null,
    val erpEntityId: String? = null,
    val erpEntityTitle: String? = null,
    val erpEntitySummary: String? = null,
)

@Serializable
data class CreateDirectChatRequest(val targetUserId: Int)

@Serializable
data class CreateGroupChatRequest(
    val title: String,
    val description: String? = null,
    val avatarUrl: String? = null,
    val type: Int = ChatType.GROUP,
    val memberUserIds: List<Int> = emptyList(),
)

@Serializable
data class EditChatMessageRequest(val newText: String)

@Serializable
data class ReactChatMessageRequest(val emoji: String)

@Serializable
data class MarkChatReadRequest(val lastReadMessageId: Int? = null)

@Serializable
data class ChatSummaryDto(
    val totalUnreadMessages: Int = 0,
    val unreadConversationsCount: Int = 0,
)

@Serializable
data class ChatUploadResultDto(
    val id: String = "",
    val fileUrl: String = "",
    val fileName: String = "",
    val fileSizeBytes: Long = 0,
    val fileContentType: String = "",
    val messageType: Int = ChatMessageType.FILE,
)
