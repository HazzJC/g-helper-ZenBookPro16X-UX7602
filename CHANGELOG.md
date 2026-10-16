# Changelog

All notable changes to this project will be documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
