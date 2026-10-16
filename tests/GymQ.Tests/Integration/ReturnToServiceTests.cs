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

    [TestMethod]
    public void ReturnedEquipment_CanBeUsedAndQueuedAgain()
    {
        var gym = new GymSession(false);

        // Confirm a fault and return the equipment to service
        ConfirmFault(gym, "E1");
        gym.ReturnToService("E1", gym.FindMember("S001"));

        // One user starts using the equipment, and another user joins the queue
        gym.Start("E1", gym.FindMember("M001"));
        gym.Join("E1", gym.FindMember("M002"));

        // Check that the equipment is in use and that the second user is in the queue
        Assert.AreEqual(EquipmentStatus.InUse, gym.Equipment["E1"].Status);
        Assert.AreEqual(1, gym.Queue.GetQueuePosition("E1", "M002"));
    }

    [TestMethod]
    public void ReturnToService_OnWorkingEquipment_IsRejected()
    {
        var gym = new GymSession(false);

        // Start using the equipment
        gym.Start("E1", gym.FindMember("M001"));

        // Check that returning working equipment to service is rejected
        Assert.ThrowsExactly<InvalidOperationException>(() => gym.ReturnToService("E1", gym.FindMember("S001")));
        Assert.ThrowsExactly<InvalidOperationException>(() => gym.ReturnToService("E2", gym.FindMember("S001")));

        // Check that the equipment returns to what it should be, in use or available
        Assert.AreEqual(EquipmentStatus.InUse, gym.Equipment["E1"].Status);
        Assert.AreEqual(EquipmentStatus.Available, gym.Equipment["E2"].Status);
    }


    // Submit a fault report and have it confirmed by staff
    private static void ConfirmFault(GymSession gym, string equipmentId)
    {
        gym.Report(equipmentId, gym.FindMember("M003"), "Broken handle");
        var report = gym.Faults.GetPendingReports().Single(r => r.EquipmentId == equipmentId);
        gym.Review(report.ReportId, gym.FindMember("S001"), true);
    }
}
