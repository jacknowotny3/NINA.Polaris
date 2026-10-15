using Moq;
using NINA.Image.Interfaces;
using NINA.INDI.Client;
using NINA.INDI.Devices;
using NINA.Polaris.Services;
using NUnit.Framework;

namespace NINA.Polaris.Test;

[TestFixture]
public class IndiCoordinateEpochTests {
    private static readonly DateTime Date = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);

    [TestCase(20.690532, 45.280338)] // Deneb
    [TestCase(0.001, -60)]
    [TestCase(23.999, 85)]
    [TestCase(12, -89)]
    public void EpochConversions_RoundTrip(double ra, double dec) {
        var now = IndiCoordinateEpoch.J2000ToJNow(ra, dec, Date);
        var restored = IndiCoordinateEpoch.JNowToJ2000(now.raHours, now.decDeg, Date);
        Assert.That(restored.raHours, Is.EqualTo(ra).Within(1e-9));
        Assert.That(restored.decDeg, Is.EqualTo(dec).Within(1e-9));
        Assert.That(now.raHours, Is.InRange(0, 24));
    }

    [Test]
    public void J2000Epoch_IsIdentity() {
        var epoch = new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var forward = IndiCoordinateEpoch.J2000ToJNow(20.69, 45.28, epoch);
        var inverse = IndiCoordinateEpoch.JNowToJ2000(20.69, 45.28, epoch);
        Assert.That(forward.raHours, Is.EqualTo(20.69).Within(1e-12));
        Assert.That(inverse.decDeg, Is.EqualTo(45.28).Within(1e-12));
    }

    [Test]
    public async Task IndiBoundary_ReadSlewAndSyncUseOppositeConversions() {
        using var client = new IndiClient();
        var scope = new RecordingIndiTelescope(client);
        var jNow = IndiCoordinateEpoch.J2000ToJNow(20.690532, 45.280338, DateTime.UtcNow);
        scope.Ra = jNow.raHours;
        scope.Dec = jNow.decDeg;
        var ui = IndiCoordinateEpoch.ReadForUi(scope);
        Assert.That(ui.raHours, Is.EqualTo(20.690532).Within(1e-7));
        Assert.That(ui.decDeg, Is.EqualTo(45.280338).Within(1e-7));

        await IndiCoordinateEpoch.SlewFromJ2000Async(scope, ui.raHours, ui.decDeg);
        Assert.That(scope.Slew.ra, Is.EqualTo(jNow.raHours).Within(1e-7));
        Assert.That(scope.Slew.dec, Is.EqualTo(jNow.decDeg).Within(1e-7));
        await IndiCoordinateEpoch.SyncFromJ2000Async(scope, ui.raHours, ui.decDeg);
        Assert.That(scope.Sync.ra, Is.EqualTo(jNow.raHours).Within(1e-7));
        Assert.That(scope.Sync.dec, Is.EqualTo(jNow.decDeg).Within(1e-7));
        Assert.That(Math.Abs(scope.Slew.ra - ui.raHours), Is.GreaterThan(0.001),
            "J2000 numbers must not be written unchanged to EOD");
    }

    [Test]
    public async Task OtherBackends_AreUnchanged() {
        var scope = new Mock<ITelescope>();
        scope.SetupGet(s => s.RightAscension).Returns(12);
        scope.SetupGet(s => s.Declination).Returns(-30);
        scope.Setup(s => s.SlewAsync(12, -30, default)).Returns(Task.CompletedTask);
        scope.Setup(s => s.SyncAsync(12, -30, default)).Returns(Task.CompletedTask);
        Assert.That(IndiCoordinateEpoch.ReadForUi(scope.Object), Is.EqualTo((12.0, -30.0)));
        await IndiCoordinateEpoch.SlewFromJ2000Async(scope.Object, 12, -30);
        await IndiCoordinateEpoch.SyncFromJ2000Async(scope.Object, 12, -30);
        scope.Verify(s => s.SlewAsync(12, -30, default), Times.Once);
        scope.Verify(s => s.SyncAsync(12, -30, default), Times.Once);
    }

    // Reimplement the interface boundary to record commands without a socket or physical mount.
    private sealed class RecordingIndiTelescope(IndiClient client) : IndiTelescope(client, "Test"), ITelescope {
        public double Ra, Dec;
        public (double ra, double dec) Slew, Sync;
        public new double RightAscension => Ra;
        public new double Declination => Dec;
        public new Task SlewAsync(double ra, double dec, CancellationToken ct = default) {
            Slew = (ra, dec);
            return Task.CompletedTask;
        }
        public new Task SyncAsync(double ra, double dec, CancellationToken ct = default) {
            Sync = (ra, dec);
            return Task.CompletedTask;
        }
    }
}
