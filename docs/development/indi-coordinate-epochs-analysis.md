# Coordinate epoch analysis and implementation brief

## Observed defect and evidence

In the `v0.98.176` checkout, the INDI adapter exposes
`EQUATORIAL_EOD_COORD` through an interface whose documented frame is JNow.
SKY expects J2000 catalog positions. The position can cross that boundary
without conversion in either direction.

The externally controlled reproduction is important: Ekos supplies EOD using
its own coordinate handling, so Polaris cannot hide the display error by
writing and reading the same wrongly interpreted numbers itself. After the
local PoC was applied, Jan reported that the Ekos-set pointing appeared in
the expected place in Polaris. He subsequently reported that plate solving
worked as expected.

The session log identifies Telescope Simulator and CCD Simulator on Linux.
No quantitative residual, complete sync trace, physical-mount validation,
Alpaca test or Windows test was recorded. The manual reports support the
INDI hypothesis and the PoC, without establishing full product correctness.

## Existing contracts and affected paths

| Surface | Existing meaning | Required treatment |
| --- | --- | --- |
| INDI `EQUATORIAL_EOD_COORD` | Current-date equatorial coordinates | Keep as the INDI native interface |
| `ITelescope.RightAscension` / `Declination` | Documented JNow, RA hours / Dec degrees | Preserve the contract |
| `ITelescope.SlewAsync` / `SyncAsync` | Documented JNow input | Convert J2000 before invoking |
| SKY catalog target and map bridge | J2000; bridge RA is degrees | Keep J2000, distinguish units |
| Plate-solve result | J2000 on the inspected workflow | Compare in J2000, convert before sync |
| Mount FOV and map centering | Currently derived from native pointing | Convert JNow to J2000 |
| Status and refresh APIs | Generic `ra` / `dec` fields | Define epoch explicitly for every consumer |
| Meridian/altitude safety | Time-dependent, based on native pointing | Preserve intended of-date inputs |

The INDI adapter does not need to change the meaning of its public properties.
A change to that contract would affect backend safety, polar alignment,
sequencing and image metadata as well as the browser.

## Proposed application model

Use `skyTargetJ2000` for catalog/map targets and `mountJ2000` for the UI's
converted pointing. The current mount object also carries connection,
tracking, parking, capabilities, slew rates and alt/az; those fields do not
change meaning when equatorial coordinates are converted.

A derived `skyTargetJNow` can exist as a local command value. A persistent
`mountJNow` is optional in the browser because the backend retains the native
pointing. Avoid mutable parallel coordinate copies unless their timestamp and
update ownership are explicit.

Two shared methods should express the conversion:

```csharp
(double raHours, double decDeg) J2000ToJNow(
    double raHours, double decDeg, DateTime atUtc);

(double raHours, double decDeg) JNowToJ2000(
    double raHours, double decDeg, DateTime atUtc);
```

Use the same time for both coordinates in a conversion. Normalize RA to
`[0, 24)` hours and retain unavailable positions as unavailable. Document the
precession model and precision; mean JNow, apparent place and ICRS/J2000 are
not exact synonyms at higher precision.

## What the PoC changes

`IndiCoordinateEpoch` provides both conversions and INDI-specific wrappers for
reading UI coordinates and sending slew/sync commands. The forward calculation
reuses `PolarAlignmentMath.PrecessJ2000ToDate`; the reverse uses the inverse
rotation rather than simply negating elapsed time.

`EquipmentManager` and TelescopeEndpoints convert INDI status and refresh
coordinates to J2000. The frontend state is renamed, including its HTML
bindings. SKY mount FOV creation and redraw use that converted state.

TelescopeEndpoints converts UI GoTo and sync input to JNow. SlewCenterService
stores INDI job targets in J2000, uses J2000 hints/comparisons, and converts
slew and solved sync positions to JNow. The SKY endpoint explicitly marks its
input as J2000; legacy internal callers retain their previous input convention.
The SKY safety check converts the target to JNow for its existing calculation.
The nearest-planet probe accepts an epoch flag for converted INDI UI inputs.

This is an experimental implementation. In particular, the legacy centering
input convention is a compatibility mechanism, not evidence that every
internal caller actually supplies JNow. A production audit must classify each
caller rather than assume its epoch from a default boolean.

A regression from the rename changed a stylesheet URL as well as the intended
state references. That URL was corrected in the source and local publish
output; Jan then confirmed SKY displayed normally. Asset paths must remain
unchanged by the final state rename.

## Implementation tasks for a production patch

1. Inventory coordinate producers and consumers. Record epoch, units and
   whether values are catalog, solved, native mount or solar-system positions.
2. Agree on the boundary for normalization. Preserve `ITelescope` JNow unless
   a separate migration explicitly changes all its consumers.
3. Consolidate and test the forward/inverse conversion, including independent
   reference values, RA wrap, high declinations and unavailable inputs.
4. Define API coordinate semantics. The PoC changes the meaning of existing
   status `ra`/`dec` values for INDI without adding an epoch field; review API
   compatibility and prefer an explicit contract for a production change.
5. Apply the INDI display and command conversions once per boundary. Name
   coordinate state explicitly and keep non-coordinate device state intact.
6. Audit all centering callers: SKY, sequencer, live-stack recentering,
   meridian flip, guiding calibration and solar-system centering. Replace
   implicit epoch assumptions with declared inputs or typed coordinates.
7. Check solver hints, refresh/reconnect, auxiliary overlays, nearest-object
   lookup and mount RA/Dec readouts. Define how the readout epoch is conveyed
   using the existing UI conventions and translations.
8. Test safety calculations using simulator or automated fixtures. Keep native
   of-date meridian/altitude calculations consistent; verify that a conversion
   cannot accidentally change the already-on-target decision.
9. Review solar-system ephemerides and FITS metadata separately. They must not
   inherit the catalog frame merely because their variables are also RA/Dec.
10. Run the repository checks and regression suite. Obtain independent testing
    for other backends before claiming support for them.

## Backend scope

The initial contribution can be limited to Linux + INDI. Jan has no Alpaca
setup and no Windows environment. The issue should state that directly and
request additional testers rather than promise unsupported validation.

Alpaca and Windows/ASCOM adapters require their own audit of the driver's
reported equatorial system. Do not infer their actual frame solely from the
common interface documentation or apply the INDI conversion unconditionally.
Their current behavior is retained by the PoC, but that is not proof that
those paths are epoch-correct.

## Verification status

Passed in this session: host build with shared compilation disabled, JS
syntax and the repository method checker through SpiderMonkey, a standalone
C# check against the built assembly covering identity, four forward/inverse
round trips and recorded INDI read/slew/sync boundaries, and a static asset
URL comparison after repairing the stylesheet reference.

Added NUnit tests are present, but the NUnit suite was not executed: the
required test packages and some dependencies were unavailable locally and
network access was restricted. Browser and mount integration evidence is
limited to Jan's reported tests above. GoTo, meridian flip, all sequencer
paths and other backends have not been comprehensively validated.
