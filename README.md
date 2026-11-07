# Xiaomi BE6500 Manager (.NET)

Desktop app (Linux + Windows) for the Xiaomi BE6500 router web UI, which is Chinese-only. It shows the router's own
pages inside the app window, translated to **English** or **Română** on the fly. .NET 10 port of the Flutter app.

## How it works

1. Home screen: router IP (default `192.168.31.1`, private IPv4 only) and language.
2. **Connect** starts a local reverse proxy on `127.0.0.1:<random port>` and opens the router login in an embedded
   web view. The proxy keeps the router's cookies, rewrites `Origin`/`Referer`/`Location`, and injects the translator
   (`dictionary.json` + `translator.js`, ~1,230 entries) into full HTML pages only.
3. Toolbar: home (stops the proxy and discards the session), language (中文 / English / Română), back, forward, reload.
   Strings missing from the dictionary are saved to `~/XiaomiBE6500Manager/untranslated.json`.

## Layout

| Project | Purpose |
|---|---|
| `src/XiaomiBE6500Manager` | Avalonia 12 UI (`NativeWebView`: WebKitGTK on Linux, WebView2 on Windows) |
| `src/XiaomiBE6500Manager.Core` | Proxy (Kestrel), settings, IP validation, UI strings, embedded translation assets |
| `src/XiaomiBE6500Manager.Tests` | xUnit tests for the proxy and IP validation (fake router) |

## Build and run

```bash
dotnet run --project src/XiaomiBE6500Manager
dotnet test
```

Linux needs `libwebkit2gtk-4.1` (`sudo apt install libwebkit2gtk-4.1-0`). Windows needs the WebView2 runtime (present on Windows 11).

## Release builds

```bash
scripts/publish.sh linux-x64   # -> artifacts/linux-x64
scripts/publish.sh win-x64     # -> artifacts/win-x64 (can be cross-built from Linux)
packaging/linux/install.sh     # per-user menu/dock launcher (--uninstall to remove)
```

Translations: edit `src/XiaomiBE6500Manager.Core/Resources/dictionary.json`.
