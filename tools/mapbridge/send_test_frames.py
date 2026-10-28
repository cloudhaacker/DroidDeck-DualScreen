#!/usr/bin/env python3
import select
import socket
import struct
import sys
import time
import zlib

HOST = "127.0.0.1"
PORT = 47200
WIDTH = 480
HEIGHT = 360
TYPE_IMAGE = 1
TYPE_TITLE = 2
TYPE_COMMAND = 3


def png(width, height, pixel):
    rows = bytearray()
    for y in range(height):
        rows.append(0)
        for x in range(width):
            rows += bytes(pixel(x, y))

    def chunk(tag, data):
        body = tag + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)

    header = struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header) + chunk(b"IDAT", zlib.compress(bytes(rows))) + chunk(b"IEND", b"")


def frame(view, tick):
    marker_x = 40 + (tick * 24) % (WIDTH - 80)
    marker_y = HEIGHT // 2

    def pixel(x, y):
        if abs(x - marker_x) < 8 and abs(y - marker_y) < 8:
            return (230, 60, 60)
        if view == "world":
            land = (x // 80 + y // 80) % 2 == 0
            return (60, 130, 70) if land else (30, 70, 150)
        if x % 40 == 0 or y % 40 == 0:
            return (20, 60, 20)
        return (45, 110, 50)

    return png(WIDTH, HEIGHT, pixel)


def send(conn, kind, payload):
    conn.sendall(struct.pack(">IB", len(payload) + 1, kind) + payload)


def read_exact(conn, count):
    data = b""
    while len(data) < count:
        part = conn.recv(count - len(data))
        if not part:
            raise ConnectionError("closed")
        data += part
    return data


def main():
    conn = socket.create_connection((HOST, PORT), timeout=5)
    conn.settimeout(None)
    view = "area"
    tick = 0
    print("connected to %s:%d" % (HOST, PORT), flush=True)
    while True:
        send(conn, TYPE_TITLE, ("test " + view).encode())
        send(conn, TYPE_IMAGE, frame(view, tick))
        tick += 1
        ready, _, _ = select.select([conn], [], [], 0.5)
        while ready:
            length = struct.unpack(">I", read_exact(conn, 4))[0]
            kind = read_exact(conn, 1)[0]
            payload = read_exact(conn, length - 1)
            if kind == TYPE_COMMAND:
                command = payload.decode()
                print("command: " + command, flush=True)
                if command.startswith("view:"):
                    view = command.split(":", 1)[1]
            ready, _, _ = select.select([conn], [], [], 0)


if __name__ == "__main__":
    try:
        main()
    except (ConnectionError, OSError) as error:
        print("bridge not reachable: %s" % error, file=sys.stderr)
        sys.exit(1)
