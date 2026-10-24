# Changelog

All notable changes to this project will be documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed
- **Dial screen brightness jumping** (e.g. 70 > 65 > 80 > 60 %) when turned quickly: each click read the level back from Windows, which
  lags behind a change, so it now steps from the last level it set while the dial is turning.
- **Lighting mode out of sync** with the keyboard after fast dial turns: overlapping applies could finish out of order; they now run one
  at a time and coalesce to the latest mode.

### Changed
- Readme rewritten as the fork's own, crediting and linking the original G-Helper (its readme is kept in `docs/README.md`); the
  rendered laptop image is replaced by a linked review photo.

## [1.3.0] - 2026-10-22

### Added
- **ASUS Dial window** (main window, Laptop Keyboard > *ASUS Dial*): live connection status and a ring that follows the dial, a switch
  between *Dial modes* and *Simple bindings* (only the active one is shown), double press / hold, and the options. Replaces the dial
  rows in Extra settings, which now link to it.
- **Dial on-screen ring**: in dial modes, turning shows the mode's icon in a ring with its value (volume, screen brightness, backlight
  as an arc; performance and lighting as segments) instead of a text toast. Volume (2 %) and screen brightness (5 %) are set directly,
  so steps are exact and no Windows flyout appears.
- **Hold-to-pick mode wheel**: hold the dial to see every dial mode around a wheel, turn to choose, press to pick; 3 s idle cancels.
  Optional (ASUS Dial window); off, a press cycles modes as before. The on-screen ring is optional too (off: text toasts).
- **Smooth brightness fades**: brightness changes ease over ~120 ms and the idle timeout fades out (and back in) over ~400 ms, through
  the same no-blank path (`kb_fade_ms`, `kb_fade_idle_ms`, 0 = instant).
- **Smooth dimming for Rainbow and Colour Cycle** (Extra > Laptop Backlight, off by default): G-Helper draws them so the brightness
  slider dims them fully; off, the keyboard's own firmware versions run (they keep going without G-Helper, 4-step dimming).

### Changed
- **One keyboard brightness slider** in the main window (*Backlight brightness*, 0 = off), replacing the two sliders in Extra.
  For firmware effects that choose their own colours it moves through the four hardware steps.
- Main window: Lighting Studio, Per-Key Editor and ASUS Dial sit in one row of matching buttons.
- Dial backlight steps taper to 2 % near the top (42 stops), so full brightness is approached more gently.
- No "Max" toast before the backlight percentage; lighting-mode toasts use the lighting icon.

## [1.2.0] - 2026-10-18

### Added
- **GPU resume guard (UX7602)**: if G-Helper is started within 10 minutes of waking from sleep, its first contact with the NVIDIA GPU is delayed by 30 s (`gpu_resume_guard`, `gpu_resume_window_min`, `gpu_resume_delay`). A mitigation for a bluescreen (0x10E in Windows' video memory manager, minutes after a resume) whose cause is not proven.
- **Fine keyboard brightness (UX7602)**: a 1-100% control that multiplies with the four hardware levels by scaling colours on output,
  with every percentage distinct and 1% still visible. Slider in Extra Settings > Laptop Backlight, and the dial's *Keyboard backlight* mode now uses it.
  New actions *Backlight brighter / dimmer (fine)*.
- Option **Stop MyASUS lighting agent**: stops `AsusExclusiveAgent.exe` (checked every 10 s) so it no longer re-applies ASUS's effect on Fn+F7.

### Fixed
- **Brightness flicker**: changing fine brightness no longer blanks the keyboard. Each step used to reset the frame buffer (a brief blackout), repeated on every dial click, which looked like flashing and is a photosensitivity risk. Steps now only change colour values (verified at packet level: no enable/blank packet and no mode re-send for per-key modes).
- Dial: after sleep the dial dropped back to the Windows radial interface and stopped reporting. The listener now re-asserts interface 2 on resume and checks every 10 s, reconnecting when the interface was reset or the device went away.
- Dial: brightness steps follow a non-linear scale (1% per click up to 12%, growing to 5%), apply immediately (no debounce) and never block the dial thread.
- Dial: cycling the lighting / performance mode while the dial keeps turning got stuck after one or two steps; it now steps every 300 ms.
- Dial: the *Keyboard backlight* mode did nothing when ASUS Optimization was running (the hardware write was skipped as if it were a hotkey).
- Documented that the hardware brightness byte only accepts 0-3 (anything higher turns the keyboard off).

## [1.1.0] - 2026-10-16

### Added
- **ASUS Dial support (UX7602)**: the dial's event stream (vendor HID report `0x0A`) is read directly. Turn right/left,
  press, double press and hold are bindable to any G-Helper action in Extra Settings, under a new "ASUS Dial" section.
- **Dial modes**: an ordered, user-picked list of modes (Volume, Screen brightness, Keyboard backlight, Next / previous
  track, Scroll, Performance mode, Keyboard lighting mode). A press switches mode with a toast, turning adjusts the current one.
- **Take over from ASUS software**: optional, stops and disables only `ASUSDialService` so actions don't fire twice;
  restores it when un-ticked. Needs administrator rights.
- New actions: next / previous track, scroll up / down, performance mode backwards, lighting mode backwards.
- **Lighting Studio** and five animated per-key effects (Aurora, Lava, Sonar, Fireflies, Ember).
- Tray menu entry for *Run on startup*.
- `docs/UX7602-DIAL.md` (protocol notes) and `dialprobe` (small HID probe/disassembly helper).

### Changed
- Toast notifications scale with the system DPI (they were a fixed 100 px tall) and size to their text.
- Keyboard-mode list and per-key editor toolbar layout tweaks; the charge-limit task registration is skipped
  when G-Helper isn't elevated instead of failing and retrying.

## [1.0.0] - 2026-09-10

### Added
- **ZenBook Pro 16X OLED (UX7602) Support**: Model-gated backlight control for UX7602BZ and UX7602ZM.
- **Per-Key RGB Lighting Editor**: Top-down keyboard visualization for painting individual keys, lid logo, and side lightbars with custom profile saving.
- **Four Dimming Levels**: Graduated keyboard backlight brightness control.
- **Hardware Effects**: Static, Breathing, Color Cycle, Rainbow, Strobe, Raindrop, and Flash animations.
- **Software Effects**: Heatmap, GPU Mode, Ambient, Battery, Audio Spectrum, and Colour Rain.
- **Enable Packet Discovery**: Documented `[0x5C, 0xA2, 0x00, 0x00]` enable sequence preventing controller blanking.
