namespace BellHopBot.Users;

public interface IBlockedUser
{
    long UserId { get; }
    string UserName { get; }
}