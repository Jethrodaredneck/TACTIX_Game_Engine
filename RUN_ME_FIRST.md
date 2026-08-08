# TACTIX Stage 2 bring-up

From Terminal in this folder:

```bash
chmod +x build.sh run.sh diagnose.sh
./run.sh
```

Expected result: a native macOS TACTIX window with the rotating colored Metal triangle.

If it does not open, run:

```bash
./diagnose.sh
cat /tmp/tactix_boottrace.txt
```

The boot trace markers identify how far startup reached:
- A-C: application + window
- D: Metal view
- F: shader load
- G/H: Metal renderer
- Z: startup complete

This hardened base also removes duplicate shader/resource declarations from the project file and updates the included CLI to the current Stage 2 asset database API.
