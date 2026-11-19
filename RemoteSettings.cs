namespace ZephyrRemote;

public class RemoteSettings
{
    public bool Enabled { get; set; }
    public string DiscordWebhook { get; set; } = "";
    public string BotTokenEncrypted { get; set; } = "";
    public string OwnerId { get; set; } = "";
    public string GuildId { get; set; } = "";
    public string ChannelId { get; set; } = "";
}
