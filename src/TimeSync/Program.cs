Console.WriteLine("From?");
if (!DateOnly.TryParse(Console.ReadLine(), out var from))
    throw new InvalidOperationException("Failed to parse value. Expected format yyyy-MM-dd");

Console.WriteLine("To?");
if (!DateOnly.TryParse(Console.ReadLine(), out var to))
    throw new InvalidOperationException("Failed to parse value. Expected format yyyy-MM-dd");

var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .AddJsonFile("appsettings.development.json", optional: true)
    .Build();

var services = new ServiceCollection();

services.Configure<JsonSerializerOptions>(config.GetSection("Json"))
    .Configure<JsonSerializerOptions>(x => x.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);

services.AddSingleton<CsvTimeLog>()
    .Configure<CsvTimeLog.Options>(config.GetSection("TimeLog"));

services.AddHttpClient();

services.AddSingleton<TimeCamp>()
    .Configure<TimeCamp.Options>(config.GetSection("TimeCamp"));

var serviceProvider = services.BuildServiceProvider();

Task.Run(async () =>
{
    using var scope = serviceProvider.CreateScope();

    var timeLog = scope.ServiceProvider.GetRequiredService<CsvTimeLog>();
    var timeCamp = scope.ServiceProvider.GetRequiredService<TimeCamp>();

    var logs = await timeLog.GetEntries(from, to);

    await timeCamp.SetEntries(from, to, logs);

    Console.WriteLine($"{logs.Count()} entries recorded.");
}).Wait();
