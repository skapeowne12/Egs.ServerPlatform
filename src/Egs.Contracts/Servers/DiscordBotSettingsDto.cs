namespace Egs.Contracts.Servers;

public sealed class DiscordBotSettingsDto
{
    public bool Enabled { get; set; }
    public string BotTokenReference { get; set; } = string.Empty;
    public string GuildId { get; set; } = string.Empty;
    public string ChannelId { get; set; } = string.Empty;
    public bool AnnounceStartStop { get; set; }
    public bool AnnouncePlayers { get; set; }
}