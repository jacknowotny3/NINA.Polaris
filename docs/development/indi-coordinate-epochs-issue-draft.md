# INDI JNow/J2000 mismatch in SKY pointing, slew and plate-solve sync

## Problem

Polaris mixes J2000 catalog/map coordinates with JNow mount coordinates on the
INDI path. This can displace the mount FOV on SKY and send coordinates in the
wrong epoch to the mount.

I reproduced the display problem on Linux with Ekos and an INDI simulator:
when Ekos sets the correct JNow position in `EQUATORIAL_EOD_COORD`, Polaris
places the mount FOV away from the corresponding J2000 catalog position.

Selecting Deneb in Polaris can nevertheless look correct. The red target FOV
normally follows the map center, so its position does not validate the mount
coordinates. Furthermore, writing J2000 numbers to EOD and reading those same
numbers back as J2000 can mask the mismatch on the map. Visual agreement alone
therefore does not prove that the mount received the correct coordinates.

The reproduction and source review are based on a checkout of `v0.98.176`.
This is not a claim that all current releases or other mount backends have
been tested.

## Findings in the source

- `ITelescope` explicitly documents JNow for its pointing, slew and sync.
- `IndiTelescope` reads RA/Dec from `EQUATORIAL_EOD_COORD` and writes slew/sync
  coordinates to that property without epoch conversion, consistent with the
  interface contract.
- The frontend copies mount RA/Dec into its state and sends them to the J2000
  map overlay without converting from JNow.
- SKY catalog targets reach `/api/telescope/slew` and then the JNow mount
  interface without converting from J2000.
- `SlewCenterService` passes target coordinates to slew and solved coordinates
  to sync directly. Its comparisons and solver hints also need a consistent
  frame.
- Generic names such as `mount.ra` and `skyTarget.ra` do not identify the epoch.
  Units are another distinction: RA is usually hours in the app but degrees
  in the map bridge.

`EQUATORIAL_COORD` is described in the property help as J2000, but the inspected
INDI mount adapter uses `EQUATORIAL_EOD_COORD`. Switching properties is not
required for the proposed fix.

## Proposed solution

Keep the existing JNow contract of `ITelescope` and explicitly convert at the
application boundaries:

```text
INDI EOD / ITelescope JNow -> JNowToJ2000 -> SKY, catalog comparisons, solver hints
SKY target / solved J2000 -> J2000ToJNow -> INDI EOD slew or sync
```

Use two shared conversion methods with an explicit UTC date, documented units,
RA wrapping and tests. Name application coordinate state explicitly, for
example `skyTargetJ2000` and `mountJ2000`. A JNow target can be derived when a
command is sent rather than maintained as a second mutable copy.

Audit each consumer before changing a shared status payload. Meridian checks,
altitude calculations and other time-dependent operations must keep the frame
they expect. Each input should be converted exactly once.

## Local INDI proof of concept

I have implemented a local PoC covering the INDI status/read path, SKY FOV,
GoTo, sync and SKY Slew & Center. It preserves the JNow mount interface and
converts the UI coordinates to J2000. It also distinguishes SKY J2000 inputs
from legacy internal inputs to the centering service.

After the change, I verified that an EOD position set from Ekos appears at the
expected location in Polaris. I then tested plate solving and it behaved as
expected. These are encouraging manual results, not a complete regression
suite or an on-hardware validation of every mount operation.

The host build, JS syntax check, method-reference check and a standalone C#
check of conversion round trips and INDI read/slew/sync boundaries passed.
The full NUnit suite could not run in the development environment because
required packages were unavailable and network access was restricted.

The PoC uses the existing IAU 1976 mean-equator/equinox precession calculation
and its inverse. It is not a complete apparent-place transformation including
nutation, aberration or proper motion. The production implementation should
make that precision choice explicit.

## Scope and acceptance

An initial fix can focus on Linux + INDI while preserving existing behavior
for other backends. Acceptance should cover:

- An independently set JNow EOD position maps to the correct J2000 position.
- A J2000 catalog GoTo writes the corresponding JNow coordinates to EOD.
- A J2000 plate-solve sync writes JNow; centering residuals use one frame.
- Refresh/reconnect and mount-centered map views retain correct positions.
- Meridian, altitude and already-on-target checks retain their intended frame.
- Internal centering callers, solver hints and API consumers are audited for
  missing or double conversions.

I can help turn the INDI PoC into a focused PR once the preferred conversion
boundary and scope are agreed. I can test Linux + INDI, but I do not have an
Alpaca setup or a Windows environment; validating those paths would need help
from someone with access to them.
