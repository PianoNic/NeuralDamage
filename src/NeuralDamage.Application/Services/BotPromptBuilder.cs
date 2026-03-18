using NeuralDamage.Application.Interfaces;
using NeuralDamage.Domain;

namespace NeuralDamage.Application.Services;

public static class BotPromptBuilder
{
    private const int MaxHistoryChars = 12_000;
    private const int MaxMessageChars = 1_500;

    public static string BuildSystemPrompt(Bot bot, List<string> participantNames)
    {
        var participants = string.Join(", ", participantNames);
        return $"""
            You are "{bot.Name}" in a casual group chat.

            People and bots in this chat: {participants}
            Messages in the history are prefixed with [Name]: so you know who said what. This prefix is ONLY for context — NEVER include [Name]: or any name prefix in YOUR responses. Just write your message directly.

            Your personality/role: {bot.SystemPrompt}
            {(string.IsNullOrWhiteSpace(bot.Personality) ? "" : $"\nAdditional personality: {bot.Personality}")}

            === RULES ===
            - You are ONLY "{bot.Name}". NEVER speak as or impersonate other participants.
            - Keep EVERY response to 1-3 sentences MAX. Like texting, not writing emails.
            - NO bullet points, NO numbered lists, NO markdown formatting.
            - Be casual, natural, use slang/humor when it fits your personality.
            - If asked a question, give a quick direct answer.
            - Never say "As an AI" or break character.
            - Match the energy of the conversation.
            - Use @TheirName when you want to address another bot specifically.
            - Do NOT pile on or echo what was already said.
            - NEVER include "(replying to ...)" text in your messages.

            === CONTENT MODERATION ===
            - NEVER engage with slurs, hate speech, or offensive content.
            - If someone uses hate speech, briefly refuse and move on.
            - Do NOT roleplay violent or illegal scenarios.
            """;
    }

    public static List<ChatMessage> BuildHistory(List<Message> messages, Guid currentBotId)
    {
        var history = new List<ChatMessage>();
        var totalChars = 0;

        // Messages should be in chronological order
        foreach (var msg in messages)
        {
            var senderName = msg.SenderUser?.DisplayName ?? msg.SenderBot?.Name ?? "Unknown";
            var content = msg.Content.Length > MaxMessageChars
                ? msg.Content[..MaxMessageChars] + "..."
                : msg.Content;

            var formatted = $"[{senderName}]: {content}";

            if (totalChars + formatted.Length > MaxHistoryChars)
                break;

            var role = msg.SenderBotId == currentBotId ? "assistant" : "user";
            history.Add(new ChatMessage(role, formatted));
            totalChars += formatted.Length;
        }

        return history;
    }
}
