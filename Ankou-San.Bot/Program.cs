using System.Reflection;
using System.Text;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

[Flags]
public enum DayOfWeekFlag
{
    Sunday = 1,
    Monday = 2,
    Tuesday = 4,
    Wednesday = 8,
    Thursday = 16,
    Friday = 32,
    Saturday = 64,

    // 休日の前日
    TomorrowIsHoliday = Friday | Saturday,
    // 平日の前日
    TomorrowIsWeekday = ~TomorrowIsHoliday,

    All = int.MaxValue,
}

public class RemindData
{
    public readonly int Hour;

    public readonly int Minute;

    public readonly DayOfWeekFlag DayOfWeekFlag;

    public readonly string Content;

    public readonly Embed Embed;

    public RemindData(int hour, int minute, DayOfWeekFlag dayOfWeekFlag, string content, Embed embed = null)
    {
        Hour = hour; 
        Minute = minute;
        DayOfWeekFlag = dayOfWeekFlag;
        Content = content;
        Embed = embed;
    }

    public TimeSpan ToTimeSpan()
    {
        return new TimeSpan(Hour, Minute, 0);
    }
}

public static class DayOfWeekExtensions
{
    public static DayOfWeekFlag ToFlag(this DayOfWeek dayOfWeek)
    {
        switch (dayOfWeek)
        {
            case DayOfWeek.Sunday:
                return DayOfWeekFlag.Sunday;
            case DayOfWeek.Monday:
                return DayOfWeekFlag.Monday;
            case DayOfWeek.Tuesday:
                return DayOfWeekFlag.Tuesday;
            case DayOfWeek.Wednesday:
                return DayOfWeekFlag.Wednesday;
            case DayOfWeek.Thursday:
                return DayOfWeekFlag.Thursday;
            case DayOfWeek.Friday:
                return DayOfWeekFlag.Friday;
            case DayOfWeek.Saturday:
                return DayOfWeekFlag.Saturday;

            default:
                throw new ArgumentException();
        }
    }
}

class Program
{
    private DiscordSocketClient client;
    private CommandService commands;
    private IServiceProvider services;

    private ProcessOnceDayTimer[] remindTimers;

    static void Main(string[] args)
    {
        var program = new Program();
        program.MainAsync().GetAwaiter().GetResult();
    }

    public async Task MainAsync()
    {
        Console.CancelKeyPress += OnExited;

        client = new DiscordSocketClient(new DiscordSocketConfig
        {
            LogLevel = LogSeverity.Info
        });

        client.Log += Log;
        commands = new CommandService();
        services = new ServiceCollection().BuildServiceProvider();

        // 秘密ファイルからトークン取得
        string secret = File.ReadAllText("secret.ankou.json", Encoding.UTF8);
        JObject secretData = JObject.Parse(secret);
        string token = (string)secretData["token"];
        
        // ログイン
        await commands.AddModulesAsync(Assembly.GetEntryAssembly(), services);
        await client.LoginAsync(TokenType.Bot, token);
        await client.StartAsync();

        // UnityTimeのリマインダーをセットアップ
        await SetupUnityTimeReminder(client);

        await Task.Delay(-1);
    }

    /// <summary>
    /// UnityTimeのリマインダーをセットアップ
    /// </summary>s
    private async Task SetupUnityTimeReminder(DiscordSocketClient client)
    {
        // UnityTimeのマニュアルを取得する
        var manualForumChannel = (IForumChannel)(await client.GetChannelAsync(1082281906269331516));
        var activeThreads = (await Task.WhenAll(
            manualForumChannel.GetActiveThreadsAsync(),
            manualForumChannel.GetPublicArchivedThreadsAsync()))
            .SelectMany(x => x.Select(x => x));
        var unityTimeThread = activeThreads.First(x => x.Id == 1082623925827149916);

        var embed = new EmbedBuilder();
        embed.WithTitle(unityTimeThread.Name);
        embed.WithDescription(
            $"UnityTimeとは、「毎日決まった時間に集まって、短い時間集中して作業しよう！」という企画です！" +
            $"詳細は[こちら](https://discord.com/channels/712937279118901248/1082623925827149916)！");

        string remindText =
            "本日も**{0}~{1}**までの間でUnityTimeを行います！\n" +
            "ボイスチャンネルの**UnityTime**までお越しください！\n" +
            "お時間合えば是非！";

        // 各種リマインドの時刻設定
        var remindData = new List<RemindData>();
        
        // 事前リマインド（平日の前日）
        remindData.Add(new RemindData(19, 00, DayOfWeekFlag.TomorrowIsWeekday, string.Format(remindText, "21:00", "22:00"), embed.Build()));
        // 事前リマインド（休日の前日）
        remindData.Add(new RemindData(19, 00, DayOfWeekFlag.TomorrowIsHoliday, string.Format(remindText, "21:00", "22:30"), embed.Build()));

        // 共通
        remindData.Add(new RemindData(21, 00, DayOfWeekFlag.All,
            "UnityTimeを開始します！\n" +
            "**21:00~21:05**：目標決めです！"));
        remindData.Add(new RemindData(21, 05, DayOfWeekFlag.All,
            "**21:05~21:30**：前半です！"));
        remindData.Add(new RemindData(21, 30, DayOfWeekFlag.All,
            "**21:30~21:35**：5分間休憩です！"));
        // 平日の前日
        remindData.Add(new RemindData(21, 35, DayOfWeekFlag.TomorrowIsWeekday,
            "**21:35~22:00**：後半です！"));
        remindData.Add(new RemindData(22, 00, DayOfWeekFlag.TomorrowIsWeekday,
            "**22:00**：終了です！\n" +
            "お疲れさまでした！！！！！！！！！！！！！！！"));
        // 休日の前日
        remindData.Add(new RemindData(21, 35, DayOfWeekFlag.TomorrowIsHoliday, 
            "**21:35~22:00**：中盤です！"));
        remindData.Add(new RemindData(22, 00, DayOfWeekFlag.TomorrowIsHoliday, 
            "**22:00~22:05**：5分間休憩です！"));
        remindData.Add(new RemindData(22, 05, DayOfWeekFlag.TomorrowIsHoliday,
            "**22:05~22:30**：後半です！"));
        remindData.Add(new RemindData(22, 30, DayOfWeekFlag.TomorrowIsHoliday,
            "**22:30**：終了です！\n" +
            "お疲れさまでした！！！！！！！！！！！！！！！"));

        // 対象のチャンネルID（ひとまず直指定）
        ulong targetChannelId = 1079783008657219594;
        var channel = (IMessageChannel)(await client.GetChannelAsync(targetChannelId));

        remindTimers = remindData
            .Select(x => CreateTimerEvent(x, channel))
            .ToArray();

        // タイマー開始
        foreach (var t in remindTimers)
        {
            t.Start();
        }
    }

    /// <summary>
    /// タイマーイベント作成
    /// </summary>
    private ProcessOnceDayTimer CreateTimerEvent(RemindData remindData, IMessageChannel channel)
    {
        // タイマー生成
        var timer = new ProcessOnceDayTimer(
            TimeSpan.FromSeconds(1),
            DateTimeUtil.GetTime(remindData.Hour, remindData.Minute));

        // イベント登録
        timer.Elapsed = () => SendMessageIfElapsed(channel, remindData);

        return timer;
    }

    /// <summary>
    /// 指定時間が経過したらメッセージを送信
    /// </summary>
    private void SendMessageIfElapsed(IMessageChannel channel, RemindData remindData)
    {
        var now = DateTime.Now;
        if (remindData.DayOfWeekFlag.HasFlag(now.DayOfWeek.ToFlag()))
            channel.SendMessageAsync(remindData.Content, embed: remindData.Embed).Forget();
    }

    /// <summary>
    /// ログ表示
    /// </summary>
    private Task Log(LogMessage message)
    {
        Console.WriteLine(message.ToString());
        return Task.CompletedTask;
    }

    /// <summary>
    /// 終了
    /// </summary>
    private void OnExited(object? sender, ConsoleCancelEventArgs e)
    {
        Console.CancelKeyPress -= OnExited;
        var _ = OnExitedAsync(sender, e);
    }
    private async Task OnExitedAsync(object? sender, ConsoleCancelEventArgs e)
    {
        await client.LogoutAsync();
        foreach (var timer in remindTimers)
        {
            timer.Dispose();
        }
    }
}
