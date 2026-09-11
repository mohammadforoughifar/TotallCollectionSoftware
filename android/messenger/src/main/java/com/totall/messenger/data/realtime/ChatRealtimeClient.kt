package com.totall.messenger.data.realtime

import com.microsoft.signalr.HubConnection
import com.microsoft.signalr.HubConnectionBuilder
import com.totall.messenger.data.model.ChatMessageDto
import com.totall.messenger.data.model.ChatReactionUserDto
import com.totall.messenger.data.remote.NetworkProvider
import com.totall.messenger.data.remote.ServerConfig
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.launch

/** رویدادهای بلادرنگ سرور — آینه‌ی ChatHub/ChatNotifier */
sealed interface ChatEvent {
    data class MessageReceived(val message: ChatMessageDto) : ChatEvent
    data class MessageUpdated(val message: ChatMessageDto) : ChatEvent
    data class MessageDeleted(val conversationId: Int, val messageId: Int) : ChatEvent
    data class ReactionUpdated(
        val conversationId: Int,
        val messageId: Int,
        val reactions: Map<String, List<ChatReactionUserDto>>,
    ) : ChatEvent
    data class MessagesRead(val conversationId: Int, val readerUserId: Int, val messageId: Int) : ChatEvent
    data class ConversationChanged(val conversationId: Int) : ChatEvent
    data class UserTyping(val conversationId: Int, val userId: Int, val userName: String) : ChatEvent
    data class UserTypingStopped(val conversationId: Int, val userId: Int) : ChatEvent
    data class UserStatusChanged(val userId: Int, val isOnline: Boolean) : ChatEvent
    data class ConnectionChanged(val connected: Boolean) : ChatEvent
}

/**
 * کلاینت SignalR روی hubs/chat با توکن JWT.
 * نکته: مدل‌های kotlinx به Gson جاوا قابل map نیستند؛ پیام‌ها را به‌صورت Map
 * می‌گیریم و با NetworkProvider.json به DTO تبدیل می‌کنیم.
 */
class ChatRealtimeClient(
    private val scope: CoroutineScope = CoroutineScope(SupervisorJob() + Dispatchers.Main),
) {
    private var hub: HubConnection? = null
    private val json = NetworkProvider.json

    private val _events = MutableSharedFlow<ChatEvent>(extraBufferCapacity = 64)
    val events: SharedFlow<ChatEvent> = _events.asSharedFlow()

    val isConnected: Boolean get() = hub?.connectionState ==
        com.microsoft.signalr.HubConnectionState.CONNECTED

    fun start() {
        if (hub != null) return
        val connection = HubConnectionBuilder
            .create("${ServerConfig.baseUrl}hubs/chat")
            .withAccessTokenProvider(io.reactivex.rxjava3.core.Single.just(NetworkProvider.tokenProvider().orEmpty()))
            .withAutomaticReconnect()
            .build()

        connection.onClosed { _ -> scope.launch { _events.emit(ChatEvent.ConnectionChanged(false)) } }

        connection.on("ReceiveMessage", { payload ->
            decodeMessage(payload)?.let { scope.launch { _events.emit(ChatEvent.MessageReceived(it)) } }
        }, Map::class.java)

        connection.on("MessageUpdated", { payload ->
            decodeMessage(payload)?.let { scope.launch { _events.emit(ChatEvent.MessageUpdated(it)) } }
        }, Map::class.java)

        connection.on("MessageDeleted", { convId: Int, msgId: Int ->
            scope.launch { _events.emit(ChatEvent.MessageDeleted(convId, msgId)) }
        }, Int::class.java, Int::class.java)

        connection.on("MessageReactionUpdated", { convId: Int, msgId: Int, reactions: Map<String, List<Map<String, Any>>> ->
            scope.launch {
                val parsed = reactions.mapValues { (_, users) ->
                    users.map {
                        ChatReactionUserDto(
                            userId = (it["userId"] as? Number)?.toInt() ?: 0,
                            userName = it["userName"] as? String ?: "",
                        )
                    }
                }
                _events.emit(ChatEvent.ReactionUpdated(convId, msgId, parsed))
            }
        }, Int::class.java, Int::class.java, Map::class.java)

        connection.on("MessagesRead", { convId: Int, reader: Int, msgId: Int ->
            scope.launch { _events.emit(ChatEvent.MessagesRead(convId, reader, msgId)) }
        }, Int::class.java, Int::class.java, Int::class.java)

        connection.on("ConversationChanged", { convId: Int ->
            scope.launch { _events.emit(ChatEvent.ConversationChanged(convId)) }
        }, Int::class.java)

        connection.on("UserTyping", { convId: Int, userId: Int, name: String ->
            scope.launch { _events.emit(ChatEvent.UserTyping(convId, userId, name)) }
        }, Int::class.java, Int::class.java, String::class.java)

        connection.on("UserStatusChanged", { userId: Int, online: Boolean ->
            scope.launch { _events.emit(ChatEvent.UserStatusChanged(userId, online)) }
        }, Int::class.java, Boolean::class.java)

        hub = connection
        connection.start().doOnComplete {
            scope.launch { _events.emit(ChatEvent.ConnectionChanged(true)) }
        }.subscribe()
    }

    fun stop() {
        try { hub?.stop()?.blockingAwait() } catch (_: Exception) { }
        hub = null
    }

    fun joinConversation(conversationId: Int) {
        try { hub?.send("JoinConversation", conversationId) } catch (_: Exception) { }
    }

    fun leaveConversation(conversationId: Int) {
        try { hub?.send("LeaveConversation", conversationId) } catch (_: Exception) { }
    }

    fun sendTyping(conversationId: Int) {
        try { hub?.send("SendTyping", conversationId) } catch (_: Exception) { }
    }

    fun stopTyping(conversationId: Int) {
        try { hub?.send("StopTyping", conversationId) } catch (_: Exception) { }
    }

    private fun decodeMessage(payload: Map<*, *>?): ChatMessageDto? {
        if (payload == null) return null
        return try {
            json.decodeFromString<ChatMessageDto>(
                json.encodeToString(
                    kotlinx.serialization.json.JsonObject.serializer(),
                    toJsonObject(payload),
                ),
            )
        } catch (_: Exception) { null }
    }

    private fun toJsonObject(map: Map<*, *>): kotlinx.serialization.json.JsonObject {
        fun Any?.toElement(): kotlinx.serialization.json.JsonElement = when (this) {
            null -> kotlinx.serialization.json.JsonNull
            is String -> kotlinx.serialization.json.JsonPrimitive(this)
            is Number -> kotlinx.serialization.json.JsonPrimitive(this)
            is Boolean -> kotlinx.serialization.json.JsonPrimitive(this)
            is Map<*, *> -> kotlinx.serialization.json.JsonObject(
                entries.associate { (k, v) -> k.toString() to v.toElement() },
            )
            is List<*> -> kotlinx.serialization.json.JsonArray(map { it.toElement() })
            else -> kotlinx.serialization.json.JsonPrimitive(toString())
        }
        return map.toElement() as kotlinx.serialization.json.JsonObject
    }
}
