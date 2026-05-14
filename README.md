# Virtual Mouse

Project for physical mouse forwarding transports.

## Projects

- `src/PhysicalMouse`: core contracts
- `src/PhysicalMouse.Viiper`: VIIPER transport
- `src/PhysicalMouse.Teensy`: Teensy 4.0 transport
- `src/VirtualMouse`: input contracts
- `src/VirtualMouse.RawInput`: Windows Raw Input source
- `tests/PhysicalMouse.Tests`: tests
- `tests/VirtualMouse.Tests`: virtual mouse tests
- `tools/PhysicalMouse.Cli`: CLI harness
- `firmware`: microcontroller-side work

## Scripts

- `.\scripts\build.ps1`
- `.\scripts\test.ps1`
- `.\scripts\cli.ps1`

## TODO

- Replace the current single-owner VIIPER session model with a broker only if concurrent sessions need to share one output mouse.
