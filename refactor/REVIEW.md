# Refactor Review

## Projects

- `app/VirtualMouse.csproj`
- `src/Forwarding/Forwarding.csproj`
- `src/Hosting/Hosting.csproj`
- `src/Inputs/RawInput/Inputs.RawInput.csproj`
- `src/Inputs/Sdl/Inputs.Sdl.csproj`
- `src/Outputs/Teensy/Outputs.Teensy.csproj`
- `src/Outputs/Viiper/Outputs.Viiper.csproj`
- `src/Runtime/Runtime.csproj`
- `src/Settings/Settings.csproj`
- `src/Steam/Steam.csproj`
- `tests/VirtualMouse.Tests.csproj`

## `src/Outputs/Teensy`

Teensy placeholder. Keep, and mark as a planned feature.

## `src/Outputs/Viiper`

Keep. The project is mostly necessary transport work: VIIPER mouse, Xbox 360
controller output, rumble feedback, owned-device loopback filtering, cleanup on
dispose/failure, and stale-device reclaim.

Not a major simplification target. Revisit file shape later if needed, but do
not cut reclaim or shared device lifecycle code without a measured reason.

## `src/Inputs/RawInput`

Keep. Raw Input is the mouse source.

Filtering should mirror SDL architecturally: the input layer should expose or
accept filtering in a reusable way, instead of keeping Raw Input filtering as
server-only Hosting logic.

## `src/Inputs/Sdl`

Review against the client-only design before keeping the current shape.

Likely keep: SDL discovery/filtering, controller open, event-driven reads,
button/axis mapping, motion reads when needed, and rumble feedback.

Questionable in MVP: Steam-to-physical matching, physical companion fallback,
server-side physical controller pump, client controller registration/indexing,
pipe-oriented feedback routing, hotplug/retry management, and external physical
motion toggles.

## `src/Steam`

Support explicit profile runs that either launch the game or attach to an
already-running game. Attach mode is useful for crash recovery and manual
launcher flows.

Steam game listing is not required for attach mode when the user still runs
`run <profile>`; profile config or Steam launch environment can provide the app
id.
