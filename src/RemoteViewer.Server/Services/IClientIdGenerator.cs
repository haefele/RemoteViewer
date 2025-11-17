using System.Collections.Concurrent;

namespace RemoteViewer.Server.Services;

public interface IClientIdGenerator
{
    ClientId Generate();

    void Free(string id);
}

public record struct ClientId(string Id, string Password);

public class ClientIdGenerator : IClientIdGenerator
{
    private ConcurrentDictionary<string, object?> _usedIds = new();

    private const string IdChars = "0123456789";
    private const string PasswordChars = "abcdefghijklmnopqrstuvwxyz0123456789";

    public ClientId Generate()
    {
        string id = string.Empty;
        while (true)
        {
            id = Random.Shared.GetString(IdChars, 10);
            if (_usedIds.TryAdd(id, null))
            {
                break;
            }
        }

        string password = Random.Shared.GetString(PasswordChars, 8);

        return new ClientId(id, password);
    }

    public void Free(string id)
    {
        _usedIds.TryRemove(id, out _);
    }
}