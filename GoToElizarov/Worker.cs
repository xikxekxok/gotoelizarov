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
    private IOptions<TelegramSettings> _tgOptions;
    private IOptions<ResendSettings> _resendSettings;

    public Worker(ILogger<Worker> logger, IOptions<TelegramSettings> tgOptions, IOptions<ResendSettings> resendSettings)
    {
        _resendSettings = resendSettings;
        _tgOptions = tgOptions;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var client = new WTelegram.Client(_tgOptions.Value.ApiId, _tgOptions.Value.ApiHash, Path.Combine(_tgOptions.Value.SessionFileDirectory, _tgOptions.Value.PhoneNumber));
        await DoLogin(_tgOptions.Value.PhoneNumber);
        // await client.Login(_tgOptions.Value.PhoneNumber);
        var channels = await client.Messages_GetAllChats();
        var regexes = _resendSettings.Value.SearchRegexes.Select(x => new Regex(x, RegexOptions.IgnoreCase)).ToList();
        // var sourceChat = channels.chats[_resendSettings.Value.SourceChannelId];
        var sourceChat = (await client.Contacts_ResolveUsername("rymochnayazyzino")).Chat;
        var destinationChat = channels.chats[_resendSettings.Value.TargetChannelId];

        var barrier = DateTimeOffset.Now.AddHours(-120);
        var destinationMessages = await GetAllMessages(destinationChat)
            .OfType<Message>()
            .TakeWhile(x => new DateTimeOffset(x.Date) > barrier)
            .ToListAsync(cancellationToken: stoppingToken);
        await foreach (var message in GetAllMessages(sourceChat).OfType<Message>().TakeWhile(x => new DateTimeOffset(x.Date) > barrier).WithCancellation(stoppingToken))
        {
            if (regexes.Any(y => y.IsMatch(message.message)))
            {
                if (destinationMessages.Any(x => x.fwd_from?.channel_post == message.ID))
                    Console.WriteLine($"AlreadySent {message.date} {message.message}");
                else
                {
                    await client.Messages_ForwardMessages(sourceChat, new[] { message.ID },
                        new[] { WTelegram.Helpers.RandomLong() }, destinationChat);
                    await client.Messages_SendMedia(destinationChat, new InputMediaPoll
                        {
                            poll = new Poll
                            {
                                id = Helpers.RandomLong(),
                                question = "Ну так что, пойдешь на Елизарова?",
                                answers = new []
                                {
                                    new PollAnswer{text = "Да!", option = new byte[1]{1}},
                                    new PollAnswer{text = "Нет!", option = new byte[1]{2}},
                                    new PollAnswer{text = "Заебал, сраный робот!", option = new byte[1]{3}},
                                },
                                flags = Poll.Flags.public_voters
                            }
                        },
                        "", WTelegram.Helpers.RandomLong());
                }
            }
        }
        
        async IAsyncEnumerable<MessageBase> GetAllMessages(InputPeer from)
        {
            int offsetId = 0;
            for (int i = 0;i<100;i++) //чисто защита от дурака (себя)
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
                    case "verification_code": Console.Write("Code: "); loginInfo = Console.ReadLine(); break;
                    case "password": Console.Write("Password: "); loginInfo = Console.ReadLine(); break;
                    default: loginInfo = null; break;
                }
            Console.WriteLine($"We are logged-in as {client.User} (id {client.User.id})");
        }
    }
}