using GoToElizarov;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services.Configure<TelegramSettings>(context.Configuration.GetSection("Telegram"));
        services.Configure<ResendSettings>(context.Configuration.GetSection("ResendSettings"));
        services.AddHostedService<Worker>();
    })
    .Build();

host.Run();