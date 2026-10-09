namespace GymQ.Persistence;

public interface IGymStateStore
{
    GymStateSnapshot? Load();
    void Save(GymStateSnapshot state);
}
