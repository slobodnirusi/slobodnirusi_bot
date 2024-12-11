using BellHopBot.Configuration;
using BellHopBot.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NLog.Targets;

namespace BellHopBot.Data;

public class UsersDbContext: DbContext
{
    private readonly string _connection;
    public DbSet<User> Users { get; protected set; }
    public DbSet<BlockedUser> UsersBlacklist { get; protected set; }
    
    public UsersDbContext(DbContextOptions<UsersDbContext> options, IOptionsSnapshot<BotConfiguration> configuration)
    {
        _connection = configuration.Value.Db.Connection;
        Console.WriteLine(_connection);
    }

    public async Task SetUserLanguage(long userId, string language)
    {
        if(Users.Any(e => e.UserId == userId))
        {
            await Users
            .Where(e => e.UserId == userId)
            .ExecuteUpdateAsync(e => e.SetProperty(u => u.Language, language));
        }
        else
        {
            await Users.AddAsync(new User { UserId = userId, Language = language });
        }
        await SaveChangesAsync();
    }

    public string? UserLanguage(long userId)
    {
        var firstOrDefault = Users.Where(e => e.UserId == userId).Select(e => e.Language).FirstOrDefault();
        return firstOrDefault;
    }
    
    public async Task AddUserToBlacklist(long userId, string username, DateTime time)
    {
        if(!UsersBlacklist.Any(e => e.UserId == userId))
        {
            await UsersBlacklist.AddAsync(new BlockedUser { UserId = userId, UserName = username, Blocked = time });
            await SaveChangesAsync();
        }
    }
    
    public async Task RemoveUserFromBlacklist(long userId)
    {
        await UsersBlacklist
            .Where(e => e.UserId == userId)
            .ExecuteDeleteAsync();
    }

    public async Task<IEnumerable<IBlockedUser>> BlockedUsers(CancellationToken cancellationToken)
    {
        return await UsersBlacklist.ToListAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(b =>
        {
            b.ToTable("Users");
            b.HasKey(u => u.UserId);
            b.Property(u => u.Language)
                .HasMaxLength(4)
                .IsRequired()
                .HasDefaultValue("en");
        });
        
        modelBuilder.Entity<BlockedUser>(b =>
        {
            b.ToTable("UsersBlacklist");
            b.HasKey(u => u.UserId);
            b.Property(u => u.UserName)
                .HasMaxLength(1000)
                .IsRequired();
            b.Property(u => u.Blocked)
                .HasColumnName("Inserted");
        });
        
        base.OnModelCreating(modelBuilder);
    }
    
    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseSqlite(_connection);
}