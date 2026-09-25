using GymQ.Models;

namespace GymQ.Repository;

public interface IUserRepository
{
    Member? FindUserByUserName(string userName);

}