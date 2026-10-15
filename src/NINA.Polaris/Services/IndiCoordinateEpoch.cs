using NINA.Image.Interfaces;
using NINA.INDI.Devices;

namespace NINA.Polaris.Services;

/// <summary>
/// Bridges J2000 UI and solver coordinates to the JNow ITelescope contract.
/// This proof of concept applies conversions only to INDI mounts.
/// </summary>
public static class IndiCoordinateEpoch {
    /// <summary>Precess J2000 to the mean equator and equinox of the given UTC date, for INDI slew/sync.</summary>
    public static (double raHours, double decDeg) J2000ToJNow(double raHours, double decDeg, DateTime atUtc)
        => PolarAlignmentMath.PrecessJ2000ToDate(raHours, decDeg, atUtc);

    /// <summary>Apply the inverse precession for J2000 map and solver consumers.</summary>
    public static (double raHours, double decDeg) JNowToJ2000(double raHours, double decDeg, DateTime atUtc) {
        double t = (atUtc.ToOADate() + 2415018.5 - 2451545.0) / 36525.0;
        const double arcsecToRad = Math.PI / 180.0 / 3600.0;
        double zeta = (2306.2181 * t + 0.30188 * t * t + 0.017998 * t * t * t) * arcsecToRad;
        double z = (2306.2181 * t + 1.09468 * t * t + 0.018203 * t * t * t) * arcsecToRad;
        double theta = (2004.3109 * t - 0.42665 * t * t - 0.041833 * t * t * t) * arcsecToRad;
        double a = raHours * Math.PI / 12.0, d = decDeg * Math.PI / 180.0;
        double x = Math.Cos(d) * Math.Sin(a - z);
        double y = Math.Cos(theta) * Math.Cos(d) * Math.Cos(a - z) + Math.Sin(theta) * Math.Sin(d);
        double v = Math.Cos(theta) * Math.Sin(d) - Math.Sin(theta) * Math.Cos(d) * Math.Cos(a - z);
        double ra = (Math.Atan2(x, y) - zeta) * 12.0 / Math.PI;
        return (((ra % 24.0) + 24.0) % 24.0, Math.Asin(Math.Clamp(v, -1.0, 1.0)) * 180.0 / Math.PI);
    }

    /// <summary>Read an INDI pointing as J2000 for EquipmentManager and SKY; other backends retain their current behavior.</summary>
    public static (double raHours, double decDeg) ReadForUi(ITelescope scope) {
        var pointing = (scope.RightAscension, scope.Declination);
        return scope is IndiTelescope
            ? JNowToJ2000(pointing.RightAscension, pointing.Declination, DateTime.UtcNow)
            : pointing;
    }

    /// <summary>Translate a J2000 application target for INDI slew, retaining other backends' behavior.</summary>
    public static Task SlewFromJ2000Async(ITelescope scope, double ra, double dec, CancellationToken ct = default) {
        var target = scope is IndiTelescope ? J2000ToJNow(ra, dec, DateTime.UtcNow) : (ra, dec);
        return scope.SlewAsync(target.Item1, target.Item2, ct);
    }

    /// <summary>Translate a J2000 plate-solve or UI position for INDI sync.</summary>
    public static Task SyncFromJ2000Async(ITelescope scope, double ra, double dec, CancellationToken ct = default) {
        var target = scope is IndiTelescope ? J2000ToJNow(ra, dec, DateTime.UtcNow) : (ra, dec);
        return scope.SyncAsync(target.Item1, target.Item2, ct);
    }
}
