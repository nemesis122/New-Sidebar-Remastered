# Validation — Sidebar Remastered v1.00.0.0

Each release is produced by the release workflow on a Windows runner. Publishing happens only after all three suites pass:

- **Data and configuration checks** (`Tests/`): reading formats, settings persistence, migrations and history bounds.
- **Sensor mapping checks** (`SensorTests/`): synthetic AMD Ryzen/X3D, Intel hybrid, NVIDIA, AMD Radeon and Intel Arc profiles under both sensor sources. These are simulated mapping profiles, not physical tests of every listed model.
- **Native UI checks** (`UITests/`): every glass design rendered with every color theme at several widths, text roles, colors, sizes and visibility switches, one to four columns, stretched cards, IP rows, the shared HW Sensors frame, settings pages and reset buttons, and the app lifecycle.

The exact counts of each run are written to VALIDATION.txt in the release bundle. Physical testing was limited to a Ryzen 9 5900X / RTX 3080 / ASUS X570 system. No universal hardware guarantee, reboot/sign-in test or long-duration soak is claimed.
