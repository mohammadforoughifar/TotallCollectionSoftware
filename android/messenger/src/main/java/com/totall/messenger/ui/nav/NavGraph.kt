package com.totall.messenger.ui.nav

import androidx.compose.runtime.Composable
import androidx.navigation.NavType
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.rememberNavController
import androidx.navigation.navArgument
import com.totall.messenger.ui.chat.ChatScreen
import com.totall.messenger.ui.group.GroupInfoScreen
import com.totall.messenger.ui.list.ConversationListScreen
import com.totall.messenger.ui.newchat.NewChatScreen

object Routes {
    const val CHATS = "chats"
    const val CHAT = "chat/{conversationId}"
    const val NEW_CHAT = "new-chat"
    const val GROUP_INFO = "group-info/{conversationId}"

    fun chat(id: Int) = "chat/$id"
    fun groupInfo(id: Int) = "group-info/$id"
}

@Composable
fun MessengerNavGraph() {
    val nav = rememberNavController()
    NavHost(navController = nav, startDestination = Routes.CHATS) {
        composable(Routes.CHATS) {
            ConversationListScreen(
                onOpenChat = { nav.navigate(Routes.chat(it)) },
                onNewChat = { nav.navigate(Routes.NEW_CHAT) },
            )
        }
        composable(
            route = Routes.CHAT,
            arguments = listOf(navArgument("conversationId") { type = NavType.IntType }),
        ) { backStack ->
            val id = backStack.arguments?.getInt("conversationId") ?: return@composable
            ChatScreen(
                conversationId = id,
                onBack = { nav.popBackStack() },
                onOpenGroupInfo = { nav.navigate(Routes.groupInfo(it)) },
            )
        }
        composable(Routes.NEW_CHAT) {
            NewChatScreen(
                onOpenChat = {
                    nav.navigate(Routes.chat(it)) {
                        popUpTo(Routes.CHATS)
                    }
                },
                onBack = { nav.popBackStack() },
            )
        }
        composable(
            route = Routes.GROUP_INFO,
            arguments = listOf(navArgument("conversationId") { type = NavType.IntType }),
        ) { backStack ->
            val id = backStack.arguments?.getInt("conversationId") ?: return@composable
            GroupInfoScreen(conversationId = id, onBack = { nav.popBackStack() })
        }
    }
}
