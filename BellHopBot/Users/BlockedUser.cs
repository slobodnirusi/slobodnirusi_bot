namespace BellHopBot.Users;

public class BlockedUser: IBlockedUser
{
    public long UserId { get; set; }
    public string UserName { get; set; }
    public DateTime Blocked { get; set; }
}