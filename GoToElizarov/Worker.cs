using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using TL;
using WTelegram;

namespace GoToElizarov;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private IOptions<TelegramSettings> _options;
    private IOptions<ResendSettings> _resendSettings;

    public Worker(ILogger<Worker> logger, IOptions<TelegramSettings> options, IOptions<ResendSettings> resendSettings)
    {
        _resendSettings = resendSettings;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string Config(string what)
        {
            switch (what)
            {
                case "api_id":
                    return _options.Value.ApiId;
                case "api_hash":
                    return _options.Value.ApiHash;
                // case "phone_number":
                //     return _options.Value.PhoneNumber;
                // case "verification_code":
                //     Console.Write("Code: ");
                //     return Console.ReadLine();
                default:
                    return null; // let WTelegramClient decide the default config
            }
        }

        using var client = new WTelegram.Client(Config);
        await client.LoginUserIfNeeded();
        var channels = await client.Messages_GetAllChats();
        var regexes = _resendSettings.Value.SearchRegexes.Select(x => new Regex(x, RegexOptions.IgnoreCase)).ToList();
        var sourceChat = channels.chats[_resendSettings.Value.SourceChannelId];
        var destinationChat = channels.chats[_resendSettings.Value.TargetChannelId];

        var lastForwardedId = await GetAllMessages(destinationChat)
            .OfType<Message>()
            .Where(x => x.fwd_from.from_id.ID == sourceChat.ID)
            .Select(x => x.fwd_from.channel_post)
            .DefaultIfEmpty(-1)
            .MaxAsync();
        
        var firstRun = lastForwardedId == -1;
        while (!stoppingToken.IsCancellationRequested)
        {
            var interestingMessages = await GetAllMessages(sourceChat, lastForwardedId)
                .OfType<Message>()
                .Where(x => regexes.Any(y => y.IsMatch(x.message)))
                .ToListAsync();
            var q = await GetAllMessages(sourceChat, 1448).ToListAsync();
            if (interestingMessages.Count == 0)
            {
                Console.WriteLine("В канале-источнике нет подходящих сообщений");
            }
            else
            {
                lastForwardedId = interestingMessages.Max(x => x.ID);
                Console.WriteLine($"Ид последнего интересного сообщения: {lastForwardedId}");
                if (firstRun)
                {
                    Console.WriteLine($"Первый запуск бота: ничего не пересылаем");
                    firstRun = false;
                }
                else
                {
                    foreach (var needResend in interestingMessages.Where(x => x.ID > lastForwardedId))
                    {
                        Console.WriteLine($"Пересылаем сообщение {needResend.ID}");
                        await client.Messages_ForwardMessages(sourceChat, new[] { needResend.ID },
                            new[] { WTelegram.Helpers.RandomLong() }, destinationChat);
                    }
                }
            }
            await Task.Delay(60000, stoppingToken);
        }

        // for (int offset_id = 0;;)
        // {
        //     var messages = await client.Messages_GetHistory(sourceChat, lastForwardedId);
        //     if (messages.Messages.Length == 0) break;
        //     foreach (var msgBase in messages.Messages)
        //     {
        //         if (msgBase is Message msg)
        //         {
        //             if (regexes.Any(y => y.IsMatch(msg.message)))
        //             {
        //                 Console.WriteLine($"AT {msg.date}: {msg.id}");
        //                 // Console.WriteLine(msg.message);
        //                 // if (!alreadyForwardedIds.Contains(msgBase.ID))
        //                 // {
        //                 //     await client.Messages_ForwardMessages(sourceChat, new[] { msg.ID },
        //                 //         new[] { WTelegram.Helpers.RandomLong() }, destinationChat);
        //                 //     return;
        //                 // }
        //             }
        //         }
        //     }
        //
        //     offset_id = messages.Messages[^1].ID;
        // }


        // foreach (var (id, value) in channels.chats)
        // {
        //     
        //     Console.WriteLine($"{id}: {value.Title}");
        //     Console.WriteLine((value as Channel)?.access_hash);
        // }
        // var chats = await client.Messages_GetChats(_resendSettings.Value.SourceChannelIds.ToArray());
        // foreach (var chat in _resendSettings.Value.SourceChannelIds)
        // {
        //     // var channel = chat.Value as Channel;
        //     var messages = await client.Channels_GetMessages(new InputChannel(chat, 6504325914482688110));
        //     foreach (var message in messages.Messages)
        //     {
        //         Console.WriteLine(message.Date);
        //     }
        //         
        //
        async IAsyncEnumerable<MessageBase> GetAllMessages(InputPeer from, int lastInterestingId)
        {
            int offsetId = 0;
            for (int i = 0;i<100;i++) //чисто защита от дурака (себя)
            {
                var messages = await client.Messages_GetHistory(from, offsetId);
                if (messages.Messages.Length == 0) break;
                foreach (var message in messages.Messages)
                {
                    if (_lastKnownId.GetValueOrDefault(from.ID) < message.ID)
                        _lastKnownId[from.ID] = message.ID;
                    yield return message;
                    if (message.ID <= lastInterestingId)
                        yield break;
                }

                offsetId = messages.Messages[^1].ID;
            }
        }
    }

    private Dictionary<long, long> _lastKnownId = new Dictionary<long, long>();




    // while (!stoppingToken.IsCancellationRequested)
    // {
    //     _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
    //     await Task.Delay(1000, stoppingToken);
    // }
}

public record TelegramSettings
{
    public string ApiId { get; set; }
    public string ApiHash { get; set; }
    public string PhoneNumber { get; set; }
}

public record ResendSettings
{
    public long SourceChannelId { get; set; }
    public long TargetChannelId { get; set; }
    public List<string> SearchRegexes { get; set; }
}