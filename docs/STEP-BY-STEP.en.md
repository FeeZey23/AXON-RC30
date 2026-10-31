# AXON RC30 — Step.by.Step.Guide

[← README / Language selection](../README.md)

## Step.by.Step.Guide

This guide covers installation, the first confirmed connection, daily use, recovery and optional development. **The product is AXON RC30.** The current interface uses German labels; relevant English meanings are shown below.

### 1. Decide: use the app or develop it

- **Use AXON RC30:** Windows x64, internet access, an Arena account and Microsoft Edge WebView2 Runtime are required. The release MSI bundles .NET; you do not need Visual Studio or a developer SDK just to use it.
- **Build or modify AXON RC30:** also install Visual Studio with the **.NET desktop development** workload, an SDK compatible with `global.json`, Git and Node.js 20 or compatible. Use the developer section below.
- Obtain WebView2 only through [Microsoft's official WebView2 page](https://developer.microsoft.com/microsoft-edge/webview2/). Follow your device's security policies; do not disable Defender or SmartScreen.

### 2. Download and verify the installer

1. Sign in to a GitHub account allowed to access this **private repository**. A 404 while signed out is not proof that the release was deleted.
2. Open the [verified Windows release](https://github.com/FeeZey23/AXON-RC30/releases/tag/rc30-isas-0.2.306).
3. Under **Assets**, download `ISAS-AXON-RC30-0.2.306-win-x64.msi` and its `.sha256` file. The source ZIP is for development, not the installer.
4. Open PowerShell in the download folder and run this check for **this exact release**:

```powershell
$msi = '.\ISAS-AXON-RC30-0.2.306-win-x64.msi'
$expected = ((Get-Content -LiteralPath ($msi + '.sha256') -Raw).Trim() -split '\s+')[0]
if ($expected -notmatch '^[a-fA-F0-9]{64}$') { throw 'Invalid SHA-256 file.' }
$actual = (Get-FileHash -LiteralPath $msi -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actual -ne $expected) { throw 'SHA-256 mismatch. Do not install.' }
'Expected SHA-256 matches.'
```

5. The expected hash is release-specific; do not reuse it for a later MSI. The hash file and release asset digest provide additional comparisons. A matching hash is **not a digital publisher signature**. This MSI is unsigned.

### 3. Install and start AXON RC30

1. If updating an existing RC30 installation, save work and close **RC30 normally**. Do not stop or replace rc26/rc27. For a backup of private RC30 data, copy `%LOCALAPPDATA%\AXON\RC30` to a private location while RC30 is closed; back up project files separately.
2. Double-click the verified MSI and follow Windows Installer. Do not ignore an unexpected security-policy block or a hash mismatch.
3. The per-user installation lives at `%LOCALAPPDATA%\Programs\ISAS\AXON-RC30`. Start **AXON RC30** from its Start menu entry, or run `AXON.RC30.exe` there.
4. Check that the application window opens and existing tabs/profile assignments are present. An open window alone does not confirm the connector.
5. If the embedded browser cannot start, check WebView2 Runtime and private diagnostics before reinstalling or deleting data.

**For maintainers:** `scripts/Install-Local.ps1` is not a stand-alone download installer. It also requires the matching local `installer-manifest.json` and, for `-Install`, matching private install/smoke/uninstall evidence. The release's `.sha256` file does not replace that manifest. Never remove those gates to make the script run on a fresh machine; use the published MSI for ordinary installation.

### 4. Create or select a workspace

In **Settings**, choose **English** or **Deutsch**, connect GitHub through its official device-code page if needed, and view the official repository. GitHub credentials are not stored in AXON settings. Updates are checked separately from installation: **Check repository for update**, then **Update** only if offered; do not update while jobs are running. New workspaces default to read-only; select the required permission profile deliberately. Running local programs grants Windows-user rights, not OS isolation.

If **Connect** is pressed without a project, a dialog offers an existing folder, an HTTPS GitHub repository link or automatic creation. Automatic projects are created under the *actual program directory* at `.axon/projects/0001.axon.dd-MM-yyyy-HH-mm`, with persistent increasing numbers. No administrative elevation or silent alternate folder is used. GitHub clone requires Git; cloning does not execute downloaded code.


1. Select an existing AXON tab, or use **＋** to create an independent workspace.
2. Open **Einstellungen** (Settings). Select the actual Windows folder for the project you want the agent to work on. Do not select `.arena-inbox`, a browser profile or the application's installation folder as a shortcut.
3. Check the visible tab and selected project path **before every write/build request**.
4. Sign in to Arena in this tab's embedded browser and open a new or existing Agent Mode chat. Recognized chat addresses/names are recorded automatically; there is no required manual chat-address field.
5. Use another AXON tab for another independent project. A separate Arena chat inside the same AXON tab is not the same isolation boundary.

### 5. Connect and verify the round trip

1. In the intended AXON tab and Arena chat, press **Connect**.
2. Inspect the text inserted into the current chat input. The capsule contains session/project pairing data: treat it as private, not as an example to paste into issues.
3. **Send the draft yourself.** AXON RC30 does not send it automatically. Clipboard content alone or a missing draft is not a successful connection.
4. Let the agent initialize the supplied client and perform a fresh status/handshake round trip. The generated client requires a compatible Node.js runtime in the agent workspace; Windows and cloud prerequisites are separate.
5. Ask the agent to confirm the actual Windows project path and perform a harmless read/listing first. Compare the response to the selected project.
6. Only then authorize the planned write/build work. Do not share passwords or API tokens in chat and do not accept an unverified “connected” claim.

### 6. Daily use and returning later

New tabs ask whether to remember the login: **Yes** retains the independent profile; **No** uses a real InPrivate profile. Existing profiles are preserved. External links open separate WebView windows; the main view remains on Arena. OAuth popups retain their native opener/profile. Provider restrictions still apply; no login restrictions are bypassed.

The **Publish project as GitHub repository** wizard requires a GitHub login and Git. Enter repository name/description, keep private visibility unless intentionally publishing, review the safe file list, select files explicitly, then confirm the final summary. It creates a new repository only, never overwrites existing ones, and does not modify your project's Git history. Inspect files yourself too: no scanner can guarantee absence of secrets.


1. **Projektdateien** = project files; **Protokolle** = logs; **Einstellungen** = settings; **Info** = information. Panels can be docked, resized and detached.
2. Keep independent work in separate AXON tabs and confirm the active project before each consequential action. Local commands have the signed-in Windows user's permissions, not OS-container isolation.
3. Existing tabs/profile identities are persisted. After restarting RC30, check the restored tab, chat and project; verify contact before resuming consequential work. Persisted settings alone are not proof of a fresh connection.
4. **Verlauf ▾** (History) can reopen closed tabs. Permanent removal is a separate deliberate action; do not remove a project or browser profile to troubleshoot casually.
5. Changing to another Arena chat ID **within the same tab** can invalidate the old pairing in Fix4. Navigating back does not automatically restore it. Reconnect and verify explicitly when required.
6. Download redirection into the project inbox is **off by default**. Enable optional inbox handling only deliberately, inspect conflicts and retain backups.

### 7. If something fails

| Symptom | Safe next step |
|---|---|
| “Contact old” / older than three minutes | Check a fresh round trip. It does not by itself mean the local service stopped. |
| Connect does not insert a draft | Verify the correct chat and input focus. Do not auto-send or claim success based on the clipboard. |
| HTTP 403 | Record the refusal; do not guess credentials or loop indefinitely. |
| HTTP 404 | Check the exact endpoint/file and access context; a missing file is not proof the whole app stopped. |
| HTTP 429 | Reduce request frequency; use bounded retries, not a retry storm. |
| HTTP 503 / 507 | Treat this as a possible relay availability/storage problem; do not claim Windows disk exhaustion without evidence. |
| Job timeout | The action may already have run. Inspect the existing job ID, journal and local state before retrying; do not duplicate installation/build/write commands blindly. |
| Missing SDK/WebView2 | Install the appropriate official dependency; do not download unknown replacement DLLs. |

Keep logs private first. Before sharing diagnostics, remove secrets, cookies, chat IDs, personal paths and profile data. See [operations and recovery](../docs/OPERATIONS.md) and [security](../SECURITY.md).

### 8. Open and build the source in Visual Studio

1. For the latest documentation/source, use **Code → Download ZIP** on the repository's `main` branch, or clone it with an authorized Git client. The existing release source ZIP is a snapshot at that release tag, not automatically the latest documentation.
2. Extract the source into a separate folder named **`AXON RC30 (by ISAS)`**. Do not overwrite the installed runtime or private AppData.
3. Open **[`AXON RC30.sln`](../AXON%20RC30.sln)** in Visual Studio or a compatible Insiders/Preview edition. Install **.NET desktop development** if necessary and allow NuGet restore.
4. Choose **AXON RC30** as the startup project. The Core, Connector and test projects are included in the solution. Do not run a second RC30 instance against the same productive state while another one is running.
5. The old `ArenaDesktopCompanion.sln`, internal directories and namespaces remain compatibility aliases for existing tests/build tooling. They are not the product name and should not be renamed blindly.
6. In PowerShell at the source root, inspect and run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Test-Repository.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Build.ps1
```

The full build performs restore, non-incremental Debug/Release builds, Core/connector/relay checks and a dedicated native UI smoke test. **That test opens its own windows and briefly moves the mouse inside them.** Use an interactive desktop and avoid simultaneous important input. Its smoke profile is separate from productive data. Check `.artifacts/checks/` privately; skipped native gestures keep the release gate closed.

`BAUEN-RC30.cmd` additionally builds the MSI after successful validation; it does **not** productively install it. `Build-Installer.ps1` requires matching full Windows evidence; test a newly built MSI before installation. `Build.ps1 -SkipUiSmoke` is headless/reduced validation, not full installer acceptance. A changed source digest requires new matching validation before rebuilding an installer.

`ExecutionPolicy Bypass` above applies only to that explicit script process; it is not an instruction to disable Windows security. The optional GitHub workflow is manual only; do not start it if you do not want to consume Actions allowances. Consult [Windows setup](../docs/SETUP-WINDOWS.md) and [installer gates](../installer/README.md).

### 9. Completion checklist

- [ ] Expected release/branch selected, and MSI SHA-256 verified if installing.
- [ ] App starts; WebView2 works; existing profiles and projects are intact.
- [ ] Intended AXON tab, Arena chat and Windows project folder confirmed.
- [ ] Connect draft reviewed and **manually sent**.
- [ ] A fresh round trip and harmless project read confirmed before writes.
- [ ] Sensitive settings, logs and profiles remain outside Git.

The current release remains unsigned and is not a fully VM-/upgrade-/mixed-DPI-certified product. External relay stability and productive chat-return/in-flight/negative acceptance are not guaranteed. Use the [current evidence](../docs/RELEASE.EVIDENCE.json), not assumptions based on a green status label.

