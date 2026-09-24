using Avalonia.Media.Imaging;
using Avalonia.Platform;
namespace GymQ.Desktop.Presentation;
public static class EquipmentImages
{
    private static readonly Dictionary<string, Bitmap> Images = new();
    public static Bitmap Get(string id)
    {
        if (Images.TryGetValue(id, out var result)) return result;
        var file = id switch { "E1" => "treadmill", "E2" => "rack", "E3" => "elliptical", _ => "bike" };
        return Images[id] = new Bitmap(AssetLoader.Open(new Uri($"avares://GymQ.Desktop/Assets/Equipment/{file}.png")));
    }
}
