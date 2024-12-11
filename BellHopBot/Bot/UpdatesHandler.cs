using BellHopBot.Configuration;
using BellHopBot.Data;
using BellHopBot.Localization;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace BellHopBot.Bot;

internal class UpdateHandler(ITelegramBotClient botClient, 
    ILogger<UpdateHandler> logger, 
    LocalizationProvider localizationProvider,
    IOptionsSnapshot<BotConfiguration> optionsSnapshot,
    UsersDbContext usersDbContext)
    : IUpdateHandler
{
    private readonly long _managersChatId = optionsSnapshot.Value.WorkGroupId;
    
    public async Task HandleUpdateAsync(ITelegramBotClient _, Update update, CancellationToken cancellationToken)
    {
        try
        {
            var handler = update switch
            {
                { Message: { } message } => OnMessage(message, cancellationToken),
                { CallbackQuery: { Data: { } } query } => OnCallback(query),
                _ => UnknownUpdateHandlerAsync(update, cancellationToken),
            };
            await handler;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "UpdateHandler error");
        }
    }

    private async Task OnCallback(CallbackQuery query)
    {
        try
        {
            await (query.Data switch
            {
                UpdateHandlerConsts.ReplyEn => usersDbContext.SetUserLanguage(query.From.Id, "en"),
                UpdateHandlerConsts.ReplyRu => usersDbContext.SetUserLanguage(query.From.Id, "ru"),
                UpdateHandlerConsts.ReplyHr => usersDbContext.SetUserLanguage(query.From.Id, "hr"),
                { } data when IsAdmin(query.From.Id) && data.Contains(UpdateHandlerConsts.UnblockBtn) =>
                    UnBlockUser(data),
                { } data when data.Contains(UpdateHandlerConsts.BlockBtn) => BlockUser(query.Message.Chat.Id, 
                    data,
                    UpdateHandlerConsts.MessageLanguage),
                _ => Task.CompletedTask
            });
        }
        finally
        {
            await botClient.AnswerCallbackQueryAsync(query.Id);
        }
        
        
    }

    private async Task OnMessage(Message message, CancellationToken cancellationToken)
    {
        switch (message)
        {
            case Message s when s is { Chat: { Type: ChatType.Private } chat } && s.Text == "/start":
                string language = UpdateHandlerConsts.MessageLanguage;
                await botClient.SendTextMessageAsync(message.Chat.Id, 
                    localizationProvider.Value("Greetings", language), 
                    parseMode: ParseMode.Html, 
                    disableWebPagePreview: true,
                    cancellationToken: cancellationToken);
                break;
            case Message s when s is { ReplyToMessage: { From.IsBot: true, ReplyMarkup: { } } replyToMessage, From.IsBot: false } 
                                && s.Chat.Id == _managersChatId
                                && replyToMessage.From.Id == botClient.BotId:
                await TransferManagerMessage(cancellationToken, replyToMessage, s);
                break;
            case Message s when s is { Chat.Type: ChatType.Private, From.IsBot: false } && s.Text?.StartsWith("/") != true:
                if (!UsersBlacklist.Blocked.Contains(s.From.Id))
                {
                    await TransferUserMessage(cancellationToken, s);
                }
                break;
            case Message s when IsAdmin(s.From.Id) && s is { From.IsBot: false, Chat.Type: ChatType.Private } 
                                && s.Text == "/blacklist":
                var blockedUsers = await usersDbContext.BlockedUsers(cancellationToken);
                string lang = UpdateHandlerConsts.MessageLanguage;

                if (!blockedUsers.Any())
                {
                    await botClient.SendTextMessageAsync(message.Chat.Id, 
                        localizationProvider.Value("ListIsEmpty", lang), 
                        parseMode: ParseMode.Html, 
                        disableWebPagePreview: true,
                        cancellationToken: cancellationToken);
                    break;
                }
                
                foreach (var blockedUser in blockedUsers)
                {
                    await botClient.SendTextMessageAsync(message.Chat.Id, 
                        $"{blockedUser.UserName}", 
                        parseMode: ParseMode.Html, 
                        disableWebPagePreview: true,
                        replyMarkup: new InlineKeyboardMarkup([InlineKeyboardButton.WithCallbackData(localizationProvider.Value("RemoveFromBlacklist", UpdateHandlerConsts.MessageLanguage), 
                            $"{UpdateHandlerConsts.UnblockBtn}{UpdateHandlerConsts.QueryDataSplitter}{blockedUser.UserId}")]),
                        cancellationToken: cancellationToken);
                }
                break;
            default:
                break;
        }
    }

    private async Task TransferManagerMessage(CancellationToken cancellationToken, Message replyToMessage, Message s)
    {
        InlineKeyboardButton? replyButton = null;

        foreach (var markup in replyToMessage.ReplyMarkup.InlineKeyboard)
        {
            replyButton = markup.FirstOrDefault(e => e.CallbackData?.Contains(UpdateHandlerConsts.InlineButtonPrefix) == true);
            if (replyButton != null) break;
        }

        if (replyButton != null)
        {
            if (long.TryParse(replyButton.CallbackData?.Split(UpdateHandlerConsts.InlineButtonPrefix)[1],
                    out long chatId))
            {
                if (await botClient.SendTextMessageAsync(chatId,
                        s.Text,
                        cancellationToken: cancellationToken) != null)
                {
                    await botClient.EditMessageReplyMarkupAsync(replyToMessage.Chat.Id,
                        replyToMessage.MessageId,
                        replyMarkup: new InlineKeyboardMarkup(replyToMessage.ReplyMarkup.InlineKeyboard
                            .SelectMany(btns => btns)
                            .Where(c => c.CallbackData?.Contains(UpdateHandlerConsts.InlineButtonPrefix) != true)),
                        cancellationToken: cancellationToken);
                }
            }
        }
    }

    private async Task TransferUserMessage(CancellationToken cancellationToken, Message s)
    {
        string userNumber = BuildUserNumber(s.Chat.Id);
        Message? sentMessage = null;
        
        sentMessage = s switch
        {
            { Document: { } } => await botClient.SendDocumentAsync(_managersChatId,
                InputFile.FromFileId(s.Document.FileId),
                caption: $"#user{userNumber}\n{s.Caption}",
                thumbnail: InputFile.FromFileId(s.Document.Thumbnail.FileId),
                replyMarkup: ReplyMarkupForUserMessage(s.Chat.Id, s.From.Username, s.From.Id, UpdateHandlerConsts.MessageLanguage),
                cancellationToken: cancellationToken),
            { Photo: {} } => await botClient.SendPhotoAsync(_managersChatId, 
                InputFile.FromFileId(s.Photo[^1].FileId), 
                caption: $"#user{userNumber}\n{s.Caption}",
                replyMarkup: ReplyMarkupForUserMessage(s.Chat.Id, s.From.Username, s.From.Id, UpdateHandlerConsts.MessageLanguage),
                cancellationToken: cancellationToken),
            { Text: {} } => await botClient.SendTextMessageAsync(_managersChatId, 
                $"#user{userNumber}\n{s.Text}",
                replyMarkup: ReplyMarkupForUserMessage(s.Chat.Id, s.From.Username, s.From.Id, UpdateHandlerConsts.MessageLanguage),
                cancellationToken: cancellationToken),
            _ => null
        };

        if (sentMessage != null)
        {
            await botClient.SendTextMessageAsync(s.Chat.Id, 
                localizationProvider.Value("Sent", UpdateHandlerConsts.MessageLanguage),
                cancellationToken: cancellationToken);
        }
    }

    private InlineKeyboardMarkup ReplyMarkupForUserMessage(long chatId, string username, long userId, string language)
    {
        return new([
            InlineKeyboardButton.WithCallbackData("\u2709\ufe0f", $"{UpdateHandlerConsts.InlineButtonPrefix}{chatId}"),
            InlineKeyboardButton.WithCallbackData(localizationProvider.Value("AddToBlacklist", language),
                    $"{UpdateHandlerConsts.BlockBtn}{UpdateHandlerConsts.QueryDataSplitter}{userId}{UpdateHandlerConsts.QueryDataSplitter}{username}")
            ]);
    }

    private string BuildUserNumber(long id)
    {
        var userId = BitConverter.GetBytes(id.GetHashCode());
        
        return $"{userId[^1]}{userId[0]*userId[^2]}{userId[0]}{userId[1]*userId[2]}";
    }

    private Task UnknownUpdateHandlerAsync(Update update, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public async Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        string errorMessage = exception switch
        {
            ApiRequestException apiRequestException => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
            _ => exception.ToString()
        };

        logger.LogError($"HandleError: {errorMessage}", exception);

        if (exception is RequestException)
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
    }

    private async Task BlockUser(long chatId, string messageData, string language)
    {
        string[] messageParts = MessageParts(messageData);
        if (messageParts.Length > 2
            && !string.IsNullOrEmpty(messageParts[1])
            && long.TryParse(messageParts[1], out long userId)
            && !string.IsNullOrEmpty(messageParts[2]))
        {
            await usersDbContext.AddUserToBlacklist(userId, messageParts[2],
                TimeProvider.System.GetUtcNow().UtcDateTime);
            
            await botClient.SendTextMessageAsync(chatId, 
            localizationProvider.Value("UserBlocked", language), 
            parseMode: ParseMode.Html, 
            disableWebPagePreview: true);
            
            if(!UsersBlacklist.Blocked.Contains(userId))
                UsersBlacklist.Blocked.Add(userId);
        }
    }    

    private async Task UnBlockUser(string messageData)
    {
        string[] messageParts = MessageParts(messageData);

        if (messageParts.Length > 1
            && !string.IsNullOrEmpty(messageParts[1])
            && long.TryParse(messageParts[1], out long userId))
        {
            await usersDbContext.RemoveUserFromBlacklist(userId);
            UsersBlacklist.Blocked.Remove(userId);
        }
    }

    private static string[] MessageParts(string messageData)
    {
        return messageData.Split(UpdateHandlerConsts.QueryDataSplitter);
    }

    private bool IsAdmin(long userId) => optionsSnapshot.Value.Admins.Contains(userId);
}