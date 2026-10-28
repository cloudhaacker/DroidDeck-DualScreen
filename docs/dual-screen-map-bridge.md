# Dual-screen map bridge

DroidDeck Dual Screen adds a second-screen mode, **Game map**, that shows frames sent by a program running inside the game session. It is meant for a game mod that renders a map or menu and wants it on the handheld's second display.

## Using it

1. Start a game session on a dual-screen device.
2. Open the session drawer and choose Second screen mode, then **Game map**.
3. The second display shows a grid and "Waiting for the game" until a program connects.

## Protocol

The app listens on `127.0.0.1:47200` for the length of the session. The guest sees the address in the `BL_MAP_BRIDGE` environment variable. One client at a time; a new connection replaces the old one.

Every message, in both directions: a big-endian `u32` length that counts the type byte plus the payload, a `u8` type, then the payload.

| Type | Direction | Payload |
| --- | --- | --- |
| 1 | game to app | PNG or JPEG image, drawn scaled to fit the second display |
| 2 | game to app | UTF-8 title, drawn in the top-left corner |
| 3 | app to game | UTF-8 command: `view:area` or `view:world` from the on-screen buttons |

Messages over 16 MiB close the connection.

## Testing without a game

`tools/mapbridge/send_test_frames.py` connects, sends an animated test image, and switches between an "area" and a "world" pattern when the buttons are pressed. It needs only Python 3.
