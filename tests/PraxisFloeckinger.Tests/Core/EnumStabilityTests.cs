using AwesomeAssertions;
using PraxisFloeckinger.Core.Compliance;
using PraxisFloeckinger.Core.Identity;
using PraxisFloeckinger.Core.Tenancy;
using PraxisFloeckinger.Core.Therapy;

namespace PraxisFloeckinger.Tests.Core;

/// <summary>
/// Sichert die numerischen Werte aller Enums gegen versehentliches Umordnen.
/// Eine Änderung hier bedeutet eine potenziell brechende DB-Migration — bewusst entscheiden!
/// </summary>
public class EnumStabilityTests
{
    [Fact]
    public void UserRole_ValuesAreStable()
    {
        ((int)UserRole.Patient).Should().Be(1);
        ((int)UserRole.Sekretaerin).Should().Be(2);
        ((int)UserRole.Therapeut).Should().Be(3);
        ((int)UserRole.TherapeutSupervisor).Should().Be(4);
        ((int)UserRole.PraxisAdmin).Should().Be(5);
        ((int)UserRole.SystemAdmin).Should().Be(6);
    }

    [Fact]
    public void TherapyMode_ValuesAreStable()
    {
        ((int)TherapyMode.Anzug).Should().Be(1);
        ((int)TherapyMode.BusinessCasual).Should().Be(2);
        ((int)TherapyMode.Casual).Should().Be(3);
        ((int)TherapyMode.MitHund).Should().Be(4);
        ((int)TherapyMode.MitPferd).Should().Be(5);
        ((int)TherapyMode.Online).Should().Be(6);
        ((int)TherapyMode.Mittagessen).Should().Be(7);
        ((int)TherapyMode.Sportbegleitet).Should().Be(8);
    }

    [Fact]
    public void AppointmentStatus_ValuesAreStable()
    {
        ((int)AppointmentStatus.Booked).Should().Be(1);
        ((int)AppointmentStatus.Cancelled).Should().Be(2);
        ((int)AppointmentStatus.Completed).Should().Be(3);
        ((int)AppointmentStatus.NoShow).Should().Be(4);
    }

    [Fact]
    public void SlotStatus_ValuesAreStable()
    {
        ((int)SlotStatus.Free).Should().Be(1);
        ((int)SlotStatus.Booked).Should().Be(2);
        ((int)SlotStatus.Blocked).Should().Be(3);
    }

    [Fact]
    public void EmergencyRequestStatus_ValuesAreStable()
    {
        ((int)EmergencyRequestStatus.Pending).Should().Be(1);
        ((int)EmergencyRequestStatus.Approved).Should().Be(2);
        ((int)EmergencyRequestStatus.Rejected).Should().Be(3);
        ((int)EmergencyRequestStatus.CounterProposed).Should().Be(4);
    }

    [Fact]
    public void LicenseTier_ValuesAreStable()
    {
        ((int)LicenseTier.Trial).Should().Be(1);
        ((int)LicenseTier.Standard).Should().Be(2);
        ((int)LicenseTier.Professional).Should().Be(3);
        ((int)LicenseTier.Enterprise).Should().Be(4);
    }

    [Fact]
    public void LicenseEventType_ValuesAreStable()
    {
        ((int)LicenseEventType.TrialStarted).Should().Be(1);
        ((int)LicenseEventType.Activated).Should().Be(2);
        ((int)LicenseEventType.Renewed).Should().Be(3);
        ((int)LicenseEventType.Suspended).Should().Be(4);
        ((int)LicenseEventType.Cancelled).Should().Be(5);
    }

    [Fact]
    public void AuditAction_ValuesAreStable()
    {
        ((int)AuditAction.Create).Should().Be(1);
        ((int)AuditAction.Read).Should().Be(2);
        ((int)AuditAction.Update).Should().Be(3);
        ((int)AuditAction.Delete).Should().Be(4);
        ((int)AuditAction.Login).Should().Be(5);
        ((int)AuditAction.Logout).Should().Be(6);
        ((int)AuditAction.Export).Should().Be(7);
        ((int)AuditAction.Print).Should().Be(8);
    }
}
