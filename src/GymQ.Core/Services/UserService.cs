using GymQ.Models;
using GymQ.Repository;

namespace GymQ.Services;

public class UserService : IUserService 
{
    private readonly IUserRepository userRepository;

    public UserService(IUserRepository userRepository)
    {
        this.userRepository = userRepository;
    }

    public Member? Login(string userName, string password)
    {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrEmpty(password))
            return null;

        var member = userRepository.FindUserByUserName(userName);
        return member != null && member.CheckPassword(password) ? member : null;
    }

}
