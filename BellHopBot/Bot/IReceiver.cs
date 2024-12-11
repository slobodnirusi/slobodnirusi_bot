namespace BellHopBot.Bot;

public interface IReceiver
{
    Task ReceiveAsync(CancellationToken stoppingToken);
}