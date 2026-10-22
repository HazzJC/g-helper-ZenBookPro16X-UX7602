# ASUS Dial (ZenBook Pro 16X OLED, UX7602BZ) — Findings

Session 2026-10-14. Everything marked **confirmed** was observed live on this machine
with `dialprobe/` (standalone C# HID tool; `DialProbe.exe list|caps|listen|getfeature|setfeature`).

## Software on the machine
- `B9ECED6F.ASUSDialASUSControlPanel` 2.1.51 (Store app, UI = `AsusDCP_UWP`, `AsusDialDeviceHelper.dll`)
- Toolkit in `C:\Program Files\ASUS\ASUS Dial Control Panel Toolkit\`: `AsusDialService.exe`
  (service `ASUSDialService`), `AsusDialAgent.exe` (does the work), per-app extension DLLs
  (`Extensions\*\config.json` define dial functions per application).
- Config/state: `C:\Program Files\ASUS\...\Settings\DialSetting_<uuid>.json` (profiles/pages, per-app
  functions, `acceleration`, `longpress_enable`), `C:\ProgramData\ASUS\ASUS Dial Control Panel Toolkit\`
  (`PreferenceSetting.ini`, `Log\*.etl`). Device uuid for this laptop: `...0004`.
- The agent maps dial events to (a) extension-DLL get/set calls (e.g. `GetVolume/SetVolume`,
  `GetBrightness/SetBrightness`, `SwitchApps`) or (b) synthetic key chords (`"press":[vk,...]`).
- "Control Panel" appears to be the on-screen overlay (`AsusDial.exe` / `AsusControlPanel.exe`),
  not separate hardware: no other HID source produced events (see below). **Unconfirmed** — user to
  confirm no physical panel/buttons exist besides the dial.

## Hardware (confirmed)
I2C-HID device, ACPI `ASUS2020` at `\_SB.PC00.I2C4.DIDV`, VID `0x0B05` PID `0x0220`, 3 collections:

| Collection | Top usage | Reports | Role |
|---|---|---|---|
| COL01 | 0x01/0x0E System Multi-Axis (Windows Radial Controller) | In 0x01 (4B): button + Dial(0x37) ±3600; Feature 0x01 (3B): resolution mult. (0x48) | Standard Windows dial path. **Silent** while ASUS agent runs — no events seen. |
| COL02 | vendor 0xFFC0 | In 0x0A (4B); Feature 0x0A (13B) | **Real event stream.** |
| COL03 | vendor 0xFFC1 | In 0x0D (20B); Feature 0x0D (5B) | Silent in all tests; purpose unknown. |

Touchpad is a separate generic Precision Touchpad (`ASUP1413`, VID 093A) — not part of the dial.

## Input protocol (confirmed, COL02 report 0x0A)
`0A <button> <delta_lo> <delta_hi>` — delta is int16 per report.

| Action | Bytes |
|---|---|
| Rotate CW, 1 tick | `0A 00 01 00` (+1) |
| Rotate CCW, 1 tick | `0A 00 FF FF` (−1) |
| Button down | `0A 01 00 00` |
| Button up | `0A 00 00 00` |

- Ticks arrive ~50–150 ms apart while turning; observed delta only ±1 (accel not in hardware; ASUS config has its own `acceleration`).
- Long press is **not** a hardware event: it's just a longer gap between down and up (observed ~2.7 s). Long-press logic is software.
- During press+turn the button byte read 0 in our test (rotation reports carry button=0). Needs a re-test
  to verify whether the user actually held the button; if so, "press-and-turn" must be tracked in software from down/up state.
- Reading from a **second handle works while ASUS's agent is running** (non-exclusive), so G-Helper
  can listen without stopping ASUS services; to *own* the dial, the ASUS agent/service must be stopped,
  otherwise both react to the same events.

## Feature-report protocol on COL02 report 0x0A (confirmed live, 13-byte reports)
Found by locating the only `HidD_SetFeature` wrapper in `AsusDialAgent.exe` (0x14003BDC0) and its 4
callers (tools: `dialprobe` `disasm` / `callers` / `strat`). Request/response model: **write a
`C1` query, then `HidD_GetFeature(0x0A, 13)`** — a bare GetFeature fails (err 122) because nothing
has been queued. Writes are `HidD_SetFeature` of 13 bytes, zero-padded.

| Write | Meaning |
|---|---|
| `0A C1 <reg> <offset> <len>` then GetFeature | Read register `reg`; reply `0A C1 <reg> <data...>` (data from byte 3) |
| `0A C0 03 <v>` | Set register 3 (interface) |
| `0A 01 00 <b> <lo> <hi>` | **Loopback: injects input report `0A <b> <lo> <hi>`** into the vendor input stream (confirmed: `…02 00`→`0A 02 00 00`, `…00 01 00`→`0A 00 01 00`) |

Registers (read live on this unit):
- `01` = `04` — device type (matches ASUS dial uuid `…0004`)
- `02` = ASCII `20211222` + `01-0A` (offset 0 and 8, len 8 each) — firmware build id
- `03` = `02` — **interface**: 2 = vendor stream on COL02 (current), 1 = presumably the standard
  Windows radial path on COL01. Write + readback confirmed (`C0 03 01` → reads 1; restored to 2).
  Live behaviour of mode 1 (which collection then reports) **not yet tested with real turns**.
- `04` = `00 00 00` (len 3) — unknown/status
- `05`–`3F` → zeros (not implemented)

**Haptics / LED / extra modes:** ASUS's agent contains no other feature-report writers (only the
four above), and no other Dial binary imports `HidD_SetFeature` except `AsusDisplayUUID.dll`
(display related). So the shipped software exposes **no haptic or LED control** for this dial.
Unused firmware commands can't be ruled out, but nothing suggests any; I did not fuzz writes.

Handy for development: the loopback lets G-Helper's listener be tested without touching the dial.

## Not understood / next steps
1. (Done, see protocol section above.) Still open: COL03 report 0x0D (5B feature / 20B input) and
   COL01 feature 0x01 have no known users in ASUS's agent; likely unused on this model.
2. Live test of interface 1: user turns the dial while a script sets 1, listens on COL01/COL02, restores 2.
3. Ownership: G-Helper can listen non-exclusively next to ASUS's agent, but both would react.
4. G-Helper integration plan: add a `DialListener` (HID read thread on COL02, report 0x0A), gated by
   `AppConfig.IsZenbookPro16X()`; emit Tick(+/-n), Down, Up, LongPress(timer) events; wire to existing
   G-Helper actions (volume/brightness/keyboard backlight/fan profile/refresh rate) with a mode
   cycle on button press (hold = alternate).

## Interface 1 live test (confirmed, 2026-10-14)
Set `0A C0 03 01`, turned the dial for 60 s, restored `0A C0 03 02` (readback confirmed 2).
- Events moved entirely to **COL01 (report 0x01)**; COL02 and COL03 were silent.
- Format: `01 <button> <dial_lo> <dial_hi>` (int16 dial).
- One detent = **two reports**: CW `01 00 20 00` (+32) then `01 00 01 00` (+1); CCW `01 00 E0 FF` (-32) then `01 00 FF FF` (-1). Net 33 units/detent, ~100 ms apart.
- Button: down `01 01 00 00`, up `01 00 00 00` (long press ~2.4 s again just a long gap).
- Conclusion: mode 2 (COL02, ±1 per detent) is the cleaner stream for G-Helper; mode 1 is the Windows radial-controller feed. G-Helper should leave the interface at 2 (ASUS's default) and only read.

## Implemented in G-Helper (2026-10-14)
- `app/Input/DialListener.cs`: read-only HID listener (HidSharp, VID 0B05 PID 0220, collection with input report 0x0A,
  auto-reconnect every 5 s). Started from `Program.cs` only when `AppConfig.IsZenbookPro16X()`; disposed on exit.
- Config keys (all unset by default = do nothing): `dial_cw`, `dial_ccw`, `dial_press`, `dial_long`,
  `dial_press_cw`, `dial_press_ccw`; optional `dial_long_ms` (default 600). Values are the normal
  `KeyProcess` actions (volume, brightness, backlight, performance, aura, custom command, ...). Added
  `next_track` / `prev_track` actions. Volume/brightness/backlight repeat per detent; toggle-type
  actions fire once per rotation burst (400 ms gap).
- UI: six rows appended to the Key Bindings table in Extra Settings (`Extra.AddDialBindings`).
- Verified with injected loopback events through the real class (local test harness, not in this repo): cw/ccw, press,
  long press, press+turn (no stray press on release). Not yet verified: actions via the full running app UI,
  or behaviour alongside ASUS's agent (both will react to the same turns; stop `ASUSDialService`/`AsusDialAgent`
  or leave ASUS's mapping unused to avoid double actions).

## Dial modes + UI pass (2026-10-16)
- **Dial modes** (`app/Input/DialModes.cs`): ordered, user-picked list of modes (volume, screen brightness, keyboard
  backlight, next/previous track, scroll, performance mode, keyboard lighting mode). Config: `dial_modes_on`,
  `dial_modes` (ordered ids), `dial_mode_current`. While on, press = next mode (toast), turn = adjust current mode;
  double/long press stay separate bindings. `InputDispatcher.RunAction(action)` now runs an action without a config key.
- **Toasts**: icons for the new modes are font glyphs (Segoe Fluent Icons, falls back to MDL2) so they scale crisply;
  width follows the measured text; the whole toast scales with system DPI (it was fixed 100 px tall, ~57 DIPs at 175%).
- **Extra form DPI gotcha**: the form is authored at 192 DPI and auto-scaled on show, but controls created in code in the
  constructor are NOT scaled. Use `Dpx()` (designer-px -> current DPI) for sizes/margins, and fit list heights after the
  handle exists (`ItemHeight` is only final then).

## Updates (2026-10-17/18)
- **The dial drops back to interface 1 after sleep.** After a resume the interface register (register 3) reads 1 (the Windows radial
  controller on COL01) instead of 2, so a listener on the vendor collection hears nothing until it is set back with `0A C0 03 02`.
  `DialListener` now re-asserts interface 2 on resume and every 10 s from a separate handle, reconnecting if the register was reset or the
  device went away (`Dial: interface was 1, setting 2` / `Dial: reconnecting (...)` in the log).
- **Backlight mode uses fine brightness**, stepping along 34 stops (1% per click below ~12%, growing to 5%), applied immediately on a
  background worker that always sends the newest value. Details and measurements: [`UX7602-LIGHTING.md`](UX7602-LIGHTING.md#fine-brightness-implemented).
- Cycle-type actions (lighting / performance mode) step once per 300 ms while the dial keeps turning, instead of once per burst.

## v1.3 (2026-10-22)
- Settings moved to their own window (`Zenbook16XDial`, main window -> Laptop Keyboard -> ASUS Dial) with live status and a ring that
  follows the dial (`DialListener.IsConnected`, `Activity` event).
- On-screen ring (`DialOsd`) for dial modes, replacing toasts while showing (`ToastForm.SuppressUntil`); optional via `dial_osd`.
  Volume now uses Core Audio directly (`Audio.AdjustVolume`, 2 % steps), screen brightness WMI (`ScreenBrightness.Adjust`, 5 %).
- Hold-to-pick mode wheel (`dial_wheel`, on by default): hold past `dial_long_ms` opens it, turns move the highlight, a press picks,
  3 s idle cancels. Verified with `DialListener.Simulate` (no hardware) through every path.
