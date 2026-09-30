import { ChatDto } from '../../core/models';

export interface ChatGroup {
  label: string;
  chats: readonly ChatDto[];
}

/** Buckets chats by their last activity into Today and Earlier, keeping their order and dropping empty buckets. */
export function groupChatsByDay(chats: readonly ChatDto[], now: Date): ChatGroup[] {
  const today = new Date(now.getFullYear(), now.getMonth(), now.getDate()).getTime();
  const groups: ChatGroup[] = [
    { label: 'Today', chats: chats.filter((chat) => Date.parse(chat.updatedAt) >= today) },
    { label: 'Earlier', chats: chats.filter((chat) => Date.parse(chat.updatedAt) < today) },
  ];
  return groups.filter((group) => group.chats.length);
}
