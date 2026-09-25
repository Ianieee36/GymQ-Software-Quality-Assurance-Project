using GymQ.Models;

namespace GymQ.Repository;

/// <summary>Shares the existing equipment instances between application services.</summary>
public sealed class InMemoryUserRepository : IUserRepository
{   
    private readonly Dictionary<string, Member> _users;

    public InMemoryUserRepository(IEnumerable<Member> users)
    {
        if (users == null) throw new ArgumentNullException(nameof(users));
        _users = users.ToDictionary(u => u.UserName, StringComparer.OrdinalIgnoreCase);
    }
    public Member? FindUserByUserName(string userName)
        => string.IsNullOrWhiteSpace(userName)
            ? null
            : _users.TryGetValue(userName.Trim(), out var user) ? user : null;
    
}