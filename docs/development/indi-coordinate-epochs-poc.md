# INDI coordinate epoch proof of concept

This experiment targets INDI mounts using `EQUATORIAL_EOD_COORD`.

- `ITelescope` remains JNow, including its slew and sync inputs. Mount safety,
  meridian timing and polar alignment continue to read that native contract.
- The INDI coordinates in `/ws/status`, `/api/telescope/status` and
  `/api/telescope/refresh-position` are converted to J2000 for the UI. Their
  existing `ra` (hours) and `dec` (degrees) keys remain unchanged.
- UI state is named `mountJ2000` and `skyTargetJ2000`. The other mount state
  fields retain their meanings. The INDI mount RA/Dec readouts now show J2000.
- UI slew and sync requests supply J2000. TelescopeEndpoints converts these
  back to JNow immediately before invoking the INDI mount.
- SKY slew-and-center jobs explicitly declare J2000. Existing internal callers
  retain their native input convention; INDI jobs normalize their targets to
  J2000 once at creation. Solver hints, residuals and the already-on-target
  comparison then use J2000. Commands and solved sync positions convert back
  to JNow at the hardware boundary. Safety checks use JNow.
- The nearest-planet probe accepts an optional `j2000` input flag so the
  INDI UI's converted pointing can be compared in the same frame.

`IndiCoordinateEpoch.J2000ToJNow` reuses the existing polar alignment IAU 1976
precession. `JNowToJ2000` applies its inverse rotation. Both accept an explicit
UTC date. This is mean-equator/equinox precession, not a full apparent-place
calculation with nutation, aberration or proper motion.

Other mount adapters retain their existing behavior. The new UI state names
are an INDI PoC convention, not a guarantee that non-INDI drivers are normalized.
Solar-system ephemerides, all sequencer paths and FITS coordinate metadata are
not comprehensively migrated by this experiment.

## Manual verification with an INDI simulator

1. Set a known object's JNow pointing in the simulator from an independent
   client. Polaris should show the blue mount FOV at its J2000 catalog position.
2. Select Deneb in SKY. The red target frame and the blue mount frame should
   coincide in position when the simulator points at Deneb; their rotations
   can differ independently.
3. Exercise Slew Only with the simulator and inspect its EOD property. The
   written numbers should be the object's JNow position, not its J2000 numbers.
4. Exercise Refresh Pos and reconnect. Neither should shift the blue frame.
5. With a simulated camera and solver, check sync and Slew & Center. Sync
   should write the precessed solution; the residual is measured in J2000.

Rebuild the host before testing; the backend conversion is C#. Reload the
browser to load the renamed UI state and updated app.js cache version.
