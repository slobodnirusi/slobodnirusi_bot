namespace BellHopBot.Configuration;

public class BotConfiguration
{
    public string Token { get; set; } = "";
    public long WorkGroupId { get; set; }
    
    public DbConfiguration Db { get; set; }
    
    public long[] Admins { get; set; }
}