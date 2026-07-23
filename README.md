# [[project_name]]

[[description]]

## Build

```bash
# Linux / macOS
./scripts/build_rust.sh

# Windows (PowerShell)
.\scripts\build_rust.ps1
```

This compiles `chemistry_core` and copies the platform DLL to `Assets/Plugins/`.

## Requirements

- Unity 2022.3 LTS or later
- Rust (stable toolchain)
- Unity packages: `com.unity.burst`, `com.unity.mathematics`, `com.unity.collections`

## Architecture

See `docs/architecture.md`.

## License

MIT
