using Avalonia.Platform;
using GymQ.Desktop.ViewModels;
using GymQ.Models;
using GymQ.Services;

namespace GymQ.Tests;

[TestClass]
public class ReturnToServiceTests
{
    [TestMethod]
    public void Staff_ReturnsOutOfServiceEquipment_ToAvailable()
    {
        var gym = new GymSession(false);

        ConfirmFault(gym, "E1");

        // Check that the equipment is marked as unavailable
        Assert.AreEqual(EquipmentStatus.Unavailable, gym.Equipment["E1"].Status);

        // Check that the equipment is marked available after being returned to service
        gym.ReturnToService("E1", gym.FindMember("S001"));
        Assert.AreEqual(EquipmentStatus.Available, gym.Equipment["E1"].Status);
    }



    // Submit a fault report and have it confirmed by staff
    private static void ConfirmFault(GymSession gym, string equipmentId)
    {
        gym.Report(equipmentId, gym.FindMember("M003"), "Broken handle");
        var report = gym.Faults.GetPendingReports().Single(r => r.EquipmentId == equipmentId);
        gym.Review(report.ReportId, gym.FindMember("S001"), true);
    }
}
