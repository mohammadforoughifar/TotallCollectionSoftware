package com.totall.messenger.ui

import com.totall.messenger.data.model.ChatConversationDto
import com.totall.messenger.data.model.ChatMemberDto
import com.totall.messenger.data.model.ChatMessageDto
import com.totall.messenger.data.model.ChatMessageType
import com.totall.messenger.data.model.ChatReactionUserDto
import com.totall.messenger.data.model.ChatType
import com.totall.messenger.data.model.ChatUserDto

/** دیتای نمایشی فارسی برای @Preview ها و حالت آفلاین قالب */
object SampleData {
    val conversations = listOf(
        ChatConversationDto(
            id = 1, title = "علی محمدی", type = ChatType.DIRECT,
            lastMessageAt = "2026-09-10T14:32:00",
            lastMessageSnippet = "فاکتور امروز ثبت شد ✅",
            unreadCount = 2, directPeerIsOnline = true,
            directPeerName = "علی محمدی", lastReadMessageId = 40,
        ),
        ChatConversationDto(
            id = 2, title = "گروه انبار مرکزی", type = ChatType.GROUP,
            lastMessageAt = "2026-09-10T13:05:00",
            lastMessageSnippet = "سارا: موجودی قفسه B2 به‌روز شد",
            lastMessageSenderName = "سارا کریمی",
            unreadCount = 0, isPinned = true, membersCount = 12, lastReadMessageId = 88,
        ),
        ChatConversationDto(
            id = 3, title = "کانال اطلاع‌رسانی", type = ChatType.CHANNEL,
            lastMessageAt = "2026-09-10T11:20:00",
            lastMessageSnippet = "📢 تعطیلی پنجشنبه...",
            unreadCount = 5, membersCount = 240, lastReadMessageId = 12,
        ),
        ChatConversationDto(
            id = 4, title = "رضا احمدی", type = ChatType.DIRECT,
            lastMessageAt = "2026-09-09T18:44:00",
            lastMessageSnippet = "باشه، فردا پیگیری می‌کنم",
            unreadCount = 0, isMuted = true, lastReadMessageId = 30,
        ),
    )

    val messages = listOf(
        ChatMessageDto(
            id = 41, conversationId = 1, senderUserId = 2, senderName = "علی محمدی",
            text = "سلام، فاکتور فروش امروز رو ثبت کردی؟", createdAt = "2026-09-10T14:20:00",
        ),
        ChatMessageDto(
            id = 42, conversationId = 1, senderUserId = 1, senderName = "من",
            text = "سلام، بله دارم ثبتش می‌کنم 👍", createdAt = "2026-09-10T14:22:00",
            isOutgoing = true, isReadByPeer = true,
        ),
        ChatMessageDto(
            id = 43, conversationId = 1, senderUserId = 2, senderName = "علی محمدی",
            text = "عالیه، لطفاً رسید انبار رو هم ضمیمه کن",
            replyToMessageId = 42, replyToSenderName = "من", replyToSnippet = "بله دارم ثبتش می‌کنم",
            createdAt = "2026-09-10T14:25:00",
            reactions = mapOf("❤️" to listOf(ChatReactionUserDto(1, "من"))),
        ),
        ChatMessageDto(
            id = 44, conversationId = 1, senderUserId = 1, senderName = "من",
            messageType = ChatMessageType.FILE, fileName = "رسید-انبار-۱۰۴۲.pdf",
            fileSizeBytes = 245_760, text = "اینم رسید انبار",
            createdAt = "2026-09-10T14:28:00", isOutgoing = true, isReadByPeer = true,
        ),
        ChatMessageDto(
            id = 45, conversationId = 1, senderUserId = 2, senderName = "علی محمدی",
            messageType = ChatMessageType.ERP_SHARE,
            erpModule = "انبارداری", erpEntityId = "1042", erpEntityTitle = "رسید انبار شماره ۱۰۴۲",
            erpEntitySummary = "کالا: سیم مفتول | تعداد: ۲۰۰",
            text = "فاکتور امروز ثبت شد ✅", createdAt = "2026-09-10T14:32:00",
        ),
    )

    val users = listOf(
        ChatUserDto(id = 2, username = "mohammadi", displayName = "علی محمدی", department = "فروش", isOnline = true),
        ChatUserDto(id = 3, username = "karimi", displayName = "سارا کریمی", department = "انبار", isOnline = true),
        ChatUserDto(id = 4, username = "ahmadi", displayName = "رضا احمدی", department = "حسابداری", isOnline = false),
        ChatUserDto(id = 5, username = "hassani", displayName = "مریم حسنی", department = "منابع انسانی", isOnline = false),
    )

    val members = listOf(
        ChatMemberDto(id = 1, conversationId = 2, userId = 1, userName = "me", userDisplayName = "من", role = 1, joinedAt = "", isOnline = true),
        ChatMemberDto(id = 2, conversationId = 2, userId = 3, userName = "karimi", userDisplayName = "سارا کریمی", role = 2, joinedAt = "", isOnline = true),
        ChatMemberDto(id = 3, conversationId = 2, userId = 4, userName = "ahmadi", userDisplayName = "رضا احمدی", role = 3, joinedAt = "", isOnline = false),
    )
}
