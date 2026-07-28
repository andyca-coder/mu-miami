# Brief 000: Client Viability Spike — MU running on the Mac

| | |
|---|---|
| **Status** | approved |
| **Depends on** | none |
| **Parallel-safe with** | 001, recon pass (no shared files — this brief touches no repo) |
| **Owns files** | none (produces `docs/client-notes.md` content, committed during 001 merge) |
| **Risk level** | low |
| **Executor** | **Andy** — this is hands-on hardware validation, not agent work |

## Objective

A MU Online Season 6 Episode 3 client launches on the M5 Pro MacBook Pro and reaches its login screen. Connection to a server is **not** required — the server doesn't exist yet. This spike answers the project's only remaining kill-switch question: *can this Mac render the client at all, and via which of two paths?*

## Verified context (researched 2026-07-27)

- **The client of record is [sven-n/MuMain](https://github.com/sven-n/MuMain)** — open-source Season 6 Ep3-compatible client sources, actively maintained, built for exactly this server. Critical properties: **no GameGuard/anti-cheat**, **OpenGL renderer** (not legacy DirectX), uses OpenMU's own network library, and **game assets are bundled — the post-build step copies them automatically**. This removes the "acquire a client from a sketchy forum" problem entirely.
- It connects to OpenMU on **dedicated port 44406** and identifies as version `2.04d`, serial `k1Pk2jcET48mxL3b`. OpenMU is pre-configured to accept it — zero server-side client config.
- **CrossOver's official rating for MU Online is 2/5** (CodeWeavers, CrossOver 25.0.1). That rating is against the *official* client with its protection layers. MuMain strips those and renders OpenGL, so CrossOver may beat its rating — but it's the experiment, not the plan.
- **Plan of record: Parallels + Windows 11 ARM.** Officially licensed by Microsoft on Apple Silicon; Prism x86-emulation handles a 2003-era OpenGL game trivially. On an M5 Pro this is massive overkill in the right direction.
- Client quirk (upstream-documented): the client **refuses 127.0.0.1**. Loopback tests use any other 127.x.x.x (e.g. `127.127.127.127`). From a VM this is moot — you'll use the Mac's LAN IP.

## Out of Scope

- Connecting to a server (001 delivers the server; the merge is 002's start)
- CrossOver purchase unless the trial actually works
- Any server-side work
- Building MuMain from source *on the Mac natively* — no Mac build target exists; don't pioneer one

## Steps

1. Install Parallels Desktop (trial) → Windows 11 ARM (Parallels automates the download/install).
2. In the VM: grab a MuMain build — check the repo's **Actions → MinGW Build** artifacts for a prebuilt zip first; only build from source (CMake + VS, per repo README) if no artifact is available.
3. Launch it. Target: login screen renders, mouse/keyboard respond, no crash within 2 minutes.
4. Note resolution behavior, frame feel, and any dialog weirdness in a scratch note → becomes `docs/client-notes.md`.
5. **Optional 30-min experiment:** CrossOver trial on the Mac, same MuMain build. If it renders → note it as a future convenience path. If it fights you at all → stop; Parallels is already proven.
6. Set Parallels networking to **Bridged** (not Shared/NAT) now — 002 needs the VM to reach the Mac's LAN IP, and this is the #1 silent-failure config.

## Acceptance Criteria

- [ ] MuMain client reaches its login screen inside Windows 11 ARM on the M5 Pro
- [ ] Input works; no crash in the first 2 minutes idle at login
- [ ] VM network mode set to Bridged and the Mac's LAN IP recorded
- [ ] Scratch notes captured (build source used, settings touched, anything weird)
- [ ] CrossOver verdict recorded: works / fails / not-attempted

## Stop conditions

- Windows 11 ARM won't install on the M5 Pro (would indicate a Parallels-version-vs-macOS issue — check for a Parallels update before concluding anything)
- MuMain has no CI artifact **and** source build fails in the VM → report the exact failure; do not burn hours on toolchain archaeology
