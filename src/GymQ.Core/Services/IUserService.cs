using GymQ.Models;

namespace GymQ.Services;

public interface IUserService
{
    Member? Login(string userName, string password);
}