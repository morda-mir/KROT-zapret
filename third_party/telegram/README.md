# TG WS Proxy runtime

KROT bundles a console-only Windows x64 build of
[Flowseal/tg-ws-proxy](https://github.com/Flowseal/tg-ws-proxy) `v1.10.4`.

- Upstream commit: `70b982da2ca75637b61f281170e4ed57df763db8`
- Output: `tg-ws-proxy-v1.10.4/TgWsProxy_console.exe`
- SHA-256: `a8c3e1ef738a2cf4bb7b9480bc620ab4a259cdd82a2b70a1c98283c55586575d`
- Python: `3.13.3`
- PyInstaller: `6.16.0`
- cryptography: `46.0.5`
- certifi: `2025.10.5`
- psutil: `7.0.0`

The binary uses the unmodified upstream console entry point
`proxy/tg_ws_proxy.py`. UI-only modules are excluded from the PyInstaller
bundle because KROT owns the process lifecycle and user interface.

The reproducible build command is documented in
`scripts/build-tg-ws-proxy.ps1`. The upstream MIT license is stored at
`third_party/licenses/TgWsProxy-MIT.txt`.
