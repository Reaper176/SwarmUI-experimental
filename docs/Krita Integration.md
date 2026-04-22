# Krita Integration

## What v1 does

- `Send to Krita` exports the current Swarm image and opens it in Krita.
- `Send to Swarm` in Krita flattens the current document and sends it back to Swarm.

## Limits

- Image only
- Local machine only
- No layer sync
- No metadata sync

## Swarm setup

Set `KritaBridge.KritaExecutablePath` in server settings if Krita is not on the default OS path.

## Krita plugin install

Copy the contents of `tools/krita_plugin/` into Krita's Python plugin directory, then enable `Swarm Krita Bridge` in Krita's plugin manager.

## Manual verification

1. Open an image in SwarmUI.
2. Click `Send to Krita`.
3. Confirm Krita opens with the exported image.
4. Edit the image in Krita.
5. Copy the Swarm browser session ID into the Krita plugin when prompted.
6. Click `Send to Swarm`.
7. Confirm the active Swarm init/editor image updates within a few seconds.
