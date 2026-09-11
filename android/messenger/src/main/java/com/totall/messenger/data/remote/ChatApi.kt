package com.totall.messenger.data.remote

import com.totall.messenger.data.model.ChatConversationDto
import com.totall.messenger.data.model.ChatMemberDto
import com.totall.messenger.data.model.ChatMessageDto
import com.totall.messenger.data.model.ChatReactionUserDto
import com.totall.messenger.data.model.ChatSummaryDto
import com.totall.messenger.data.model.ChatUploadResultDto
import com.totall.messenger.data.model.ChatUserDto
import com.totall.messenger.data.model.CreateDirectChatRequest
import com.totall.messenger.data.model.CreateGroupChatRequest
import com.totall.messenger.data.model.EditChatMessageRequest
import com.totall.messenger.data.model.MarkChatReadRequest
import com.totall.messenger.data.model.ReactChatMessageRequest
import com.totall.messenger.data.model.SendChatMessageRequest
import okhttp3.MultipartBody
import retrofit2.http.Body
import retrofit2.http.DELETE
import retrofit2.http.GET
import retrofit2.http.Multipart
import retrofit2.http.POST
import retrofit2.http.PUT
import retrofit2.http.Part
import retrofit2.http.Path
import retrofit2.http.Query

/**
 * آینه‌ی Inventory.Api/Controllers/System/ChatController.cs  —  Route: api/chat
 * احراز هویت با هدر Bearer (توکن JWT لاگین) انجام می‌شود.
 */
interface ChatApi {

    @GET("api/chat/conversations")
    suspend fun getConversations(
        @Query("search") search: String? = null,
        @Query("type") type: Int? = null,
        @Query("onlyUnread") onlyUnread: Boolean = false,
    ): List<ChatConversationDto>

    @GET("api/chat/conversations/{id}")
    suspend fun getConversation(@Path("id") id: Int): ChatConversationDto

    @POST("api/chat/conversations/direct")
    suspend fun createDirect(@Body req: CreateDirectChatRequest): ChatConversationDto

    @POST("api/chat/conversations/group")
    suspend fun createGroup(@Body req: CreateGroupChatRequest): ChatConversationDto

    @GET("api/chat/conversations/{id}/messages")
    suspend fun getMessages(
        @Path("id") id: Int,
        @Query("beforeId") beforeId: Int? = null,
        @Query("pageSize") pageSize: Int = 50,
        @Query("search") search: String? = null,
    ): List<ChatMessageDto>

    @POST("api/chat/conversations/{id}/messages")
    suspend fun sendMessage(
        @Path("id") id: Int,
        @Body req: SendChatMessageRequest,
    ): ChatMessageDto

    @PUT("api/chat/messages/{id}")
    suspend fun editMessage(
        @Path("id") id: Int,
        @Body req: EditChatMessageRequest,
    ): ChatMessageDto

    @DELETE("api/chat/messages/{id}")
    suspend fun deleteMessage(@Path("id") id: Int)

    @POST("api/chat/messages/{id}/react")
    suspend fun react(
        @Path("id") id: Int,
        @Body req: ReactChatMessageRequest,
    ): Map<String, List<ChatReactionUserDto>>

    @POST("api/chat/messages/{id}/pin")
    suspend fun togglePinMessage(@Path("id") id: Int): ChatMessageDto

    @POST("api/chat/conversations/{id}/read")
    suspend fun markRead(
        @Path("id") id: Int,
        @Body req: MarkChatReadRequest = MarkChatReadRequest(),
    )

    @POST("api/chat/conversations/{id}/pin")
    suspend fun togglePinConversation(@Path("id") id: Int)

    @POST("api/chat/conversations/{id}/mute")
    suspend fun toggleMuteConversation(@Path("id") id: Int)

    @GET("api/chat/conversations/{id}/members")
    suspend fun getMembers(@Path("id") id: Int): List<ChatMemberDto>

    @GET("api/chat/users")
    suspend fun getUsers(@Query("search") search: String? = null): List<ChatUserDto>

    @GET("api/chat/summary")
    suspend fun getSummary(): ChatSummaryDto

    /** آپلود پیوست (سقف ۵۰ مگابایت) و سپس ارسال پیام با fileUrl برگشتی */
    @Multipart
    @POST("api/chat/conversations/{id}/attachments")
    suspend fun uploadAttachment(
        @Path("id") id: Int,
        @Part file: MultipartBody.Part,
    ): ChatUploadResultDto
}
