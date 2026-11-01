# AXON RC30 — English Description and Overview

[← README / Language selection](../README.md)

## What is AXON RC30?

**AXON RC30**, by ISAS, is a Windows desktop application that brings Arena chats and explicitly selected local project folders into separate, persistent workspaces. Each AXON tab has its own browser profile, project assignment, connector identity, logs and panel layout.

It is not a new AI service, an official Arena integration, or an operating-system sandbox. No paid API service is configured by this project. Arena accounts, provider permissions, internet access and external relay availability remain separate requirements.

### Main features

- Independent persistent tabs, browser profiles and project assignments.
- New or existing Arena chats, automatic capture of recognized chat addresses and chat names.
- Closed-tab history with reopening and a separate permanent-removal action.
- **Connect** prepares a session-bound client and inserts a draft into the current input. **You review and send it yourself; no automatic sending.**
- Encrypted connector transport using AES-256-GCM, explicit project/session binding and a confirmation round trip.
- Authorized project-file reads/writes, manifests, local tool execution and a persistent job journal.
- Optional inbox/archive import with backups and conflict handling. Redirecting downloads into the project inbox is off by default.
- Dockable, resizable and detachable panels for project files, logs, settings and information.

### Architecture and data

```text
Arena Agent workspace  <-->  external relay  <-->  AXON RC30 on Windows
                                                    |-- tab-specific browser profile
                                                    |-- selected project folder
                                                    |-- connector and job journal
                                                    `-- panels, settings and diagnostics
```

A cloud workspace is not your Windows folder. A visible app window is not proof of a working connector. Commands run with the Windows user's permissions; a working-directory restriction does not isolate processes.

Private runtime state is stored under `%LOCALAPPDATA%\AXON\RC30`. Do not commit settings, profiles, cookies, pairing data, raw logs, chat identifiers or backup archives. The source folder is named `AXON RC30 (by ISAS)`; the project and application are named **AXON RC30**.

### Release history and current patch

The figures below belong to the previously verified **0.2.304** release. The new **0.2.307** feature patch is described in the [changelog](../CHANGELOG.md); use its own release validation asset for the new build/installer results.


- Technical baseline: **0.2.0-rc30 / Fix4**; MSI version **0.2.304**, Windows x64.
- Actual Windows full rebuild: **692 Core**, **23 connector**, **19 relay** and **75 native UI** checks passed, with no skipped native gesture.
- MSI installation, installed-app smoke test and uninstall passed in a separate target folder. Productive installation and preservation of existing profiles/project assignments were verified.
- The installer is **unsigned**. Hash verification does not establish a trusted publisher and does not remove Windows/SmartScreen warnings.
- No OS-VM acceptance, complete upgrade/repair/mixed-DPI matrix or guarantee of public relay availability is claimed. Productive chat-return/in-flight/negative acceptance remains open.
- Repository and release are **private**. The CI workflow runs only when manually dispatched; publishing these documents does not start CI. Manual Actions runs can consume account allowances.

### Choose your next step

- [English Description and Overview — separate page](../docs/OVERVIEW.en.md)
- [English Step.by.Step.Guide — separate page](../docs/STEP-BY-STEP.en.md)
- [Complete technical catalogue and diagrams, German](../PROJECT.OVERVIEW.md)
- [Security boundaries](../SECURITY.md) · [Validation evidence](../docs/VALIDATION.md)
- [Installer and source release](https://github.com/FeeZey23/AXON-RC30/releases/tag/rc30-isas-0.2.304)

License: MIT, with the existing upstream copyright and third-party notices retained. AXON RC30 is the product name; historical internal identifiers are compatibility details, not a different product.

