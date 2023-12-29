using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using TL;
using WTelegram;

namespace GoToElizarov;

public class Worker : BackgroundService
{
    private IOptions<TelegramSettings> _tgOptions;
    private IOptions<ResendSettings> _resendSettings;
    private IReadOnlySet<int>? _alreadyForwarded;

    public Worker(IOptions<TelegramSettings> tgOptions, IOptions<ResendSettings> resendSettings)
    {
        _resendSettings = resendSettings;
        _tgOptions = tgOptions;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var client = new Client(
            _tgOptions.Value.ApiId, 
            _tgOptions.Value.ApiHash,
            Path.Combine(_tgOptions.Value.SessionFileDirectory, _tgOptions.Value.PhoneNumber.Replace("+", ""))
        );

        await DoLogin(_tgOptions.Value.PhoneNumber);
        var channels = await client.Messages_GetAllChats();
        var regexes = _resendSettings.Value.SearchRegexes.Select(x => new Regex(x, RegexOptions.IgnoreCase))
            .ToList();
        
        var sourceChat =
            (await client.Contacts_ResolveUsername(_resendSettings.Value.SourceChannel)).Chat; //TODO log error
        if (!channels.chats.TryGetValue(sourceChat.ID, out _))
        {
            Console.WriteLine($"Подпишемся на канал {sourceChat.ID} {sourceChat.Title} {sourceChat.MainUsername}");
            await client.Channels_JoinChannel(sourceChat as Channel);
        }
        else
        {
            Console.WriteLine(
                $"Уже подписаны на канал {sourceChat.ID} {sourceChat.Title} {sourceChat.MainUsername}");
        }

        
        if (!channels.chats.TryGetValue(_resendSettings.Value.TargetChannelId, out var destinationChat))
        {
            throw new Exception($"FATAL!! Нет чата с ид={_resendSettings.Value.TargetChannelId}");
        }

        var barrier = DateTimeOffset.Now.AddDays(-2);
        
        await RefreshAlreadyForwarder();
        
        client.OnUpdate += async updates =>
        {
            try
            {
                foreach (var update in updates.UpdateList ?? Array.Empty<Update>() )
                {
                    switch (update)
                    {
                        case UpdateNewMessage { message: Message msg } when msg.From.ID == sourceChat.ID:
                            Console.WriteLine($"Обработаем сообщение из наблюдаемого канала {msg.Date} {msg.message}");
                            await RefreshAlreadyForwarder();
                            await ProcessMessage(msg);
                            break;
                    }
                }

            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                Console.WriteLine(JsonSerializer.Serialize(updates));
                throw;
            }
        };

        var initialMessages = GetAllMessages(sourceChat)
            .OfType<Message>()
            .TakeWhile(x => new DateTimeOffset(x.Date) > barrier);
        await foreach (Message message in initialMessages.WithCancellation(stoppingToken))
        {
            await ProcessMessage(message);
        }

        while (!stoppingToken.IsCancellationRequested)
            await Task.Delay(Int32.MaxValue, stoppingToken);

        async Task ProcessMessage(Message message)
        {
            if (regexes.Any(y => y.IsMatch(message.message)))
            {
                if (_alreadyForwarded!.Contains(message.ID))
                    Console.WriteLine($"Уже пересылали сообщение {message.date} {message.message}");
                else
                {
                    var response = await client.Messages_ForwardMessages(sourceChat, new[] { message.ID },
                        new[] { Helpers.RandomLong() }, destinationChat);
                    await client.Messages_SendMedia(destinationChat, new InputMediaPoll
                        {
                            poll = new Poll
                            {
                                id = Helpers.RandomLong(),
                                question = "Ну так что, пойдешь на Елизарова?",
                                answers = new[]
                                {
                                    new PollAnswer { text = "Да!", option = new byte[1] { 1 } },
                                    new PollAnswer { text = "Да, если будет ком", option = new byte[1] { 2 } },
                                    new PollAnswer { text = "Нет!", option = new byte[1] { 3 } },
                                },
                                flags = Poll.Flags.public_voters
                            }
                        },
                        "", Helpers.RandomLong());
                }
            }
            else
            {
                Console.WriteLine($"Сообщение не подходит под паттерны поиска: {message.date} {message.message}");
            }
        }

        
        
        
        async Task RefreshAlreadyForwarder()
        {
            _alreadyForwarded = await GetAllMessages(destinationChat)
                .OfType<Message>()
                .TakeWhile(x => new DateTimeOffset(x.Date) > barrier)
                .Where(x => x.fwd_from != null)
                .Select(x => x.fwd_from!.channel_post)
                .ToHashSetAsync(cancellationToken: stoppingToken);
        }

        async IAsyncEnumerable<MessageBase> GetAllMessages(InputPeer from)
        {
            int offsetId = 0;
            for (int i = 0; i < 1000; i++) //чисто защита от дурака (себя)
            {
                var messages = await client.Messages_GetHistory(from, offsetId);
                if (messages.Messages.Length == 0) break;
                foreach (var message in messages.Messages)
                {
                    yield return message;
                }

                offsetId = messages.Messages[^1].ID;
            }
        }

        async Task DoLogin(string loginInfo)
        {
            while (client.User == null)
                switch (await client.Login(loginInfo))
                {
                    case "verification_code":
                        Console.Write("Enter verification code: ");
                        loginInfo = Console.ReadLine();
                        break;
                    case "password":
                        Console.Write("Enter 2FA Password: ");
                        loginInfo = Console.ReadLine();
                        break;
                    default:
                        loginInfo = null;
                        break;
                }

            Console.WriteLine($"We are logged-in as {client.User} (id {client.User.id})");
        }
    }
}