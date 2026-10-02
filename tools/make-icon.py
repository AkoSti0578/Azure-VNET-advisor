"""Generates src/AzureSubnetPlanner.App/Assets/app.ico (and a 256px PNG preview).

Usage: python3 tools/make-icon.py src/AzureSubnetPlanner.App/Assets/app.ico preview.png
Colours: Yellow Spring yellow (#F3E04F) tile with Yellow Spring purple (#6F2C91) address-space rows.
"""
import struct, zlib, sys

YELLOW = (0xF3, 0xE0, 0x4F)
PURPLE = (0x6F, 0x2C, 0x91)
SIZE = 256

def rrect(x, y, x0, y0, x1, y1, r):
    cx = min(max(x, x0 + r), x1 - r)
    cy = min(max(y, y0 + r), y1 - r)
    return (x - cx) ** 2 + (y - cy) ** 2 <= r * r

def sample(u, v):
    # Yellow tile with purple "address space" rows: occupied blocks as filled bars,
    # the recommended free block as an outlined slot.
    if not rrect(u, v, 0.02, 0.02, 0.98, 0.98, 0.22):
        return (0, 0, 0, 0)
    bars = [
        (0.16, 0.17, 0.84, 0.33),   # row 1: fully occupied
        (0.16, 0.42, 0.50, 0.58),   # row 2: occupied part
        (0.16, 0.67, 0.36, 0.83),   # row 3: occupied part
    ]
    for b in bars:
        if rrect(u, v, *b, 0.05):
            return PURPLE + (255,)
    slot = (0.44, 0.67, 0.84, 0.83)  # row 3: free slot (outline)
    t = max(0.045, 1.3 / SIZE)
    if rrect(u, v, *slot, 0.05) and not rrect(u, v, slot[0] + t, slot[1] + t, slot[2] - t, slot[3] - t, 0.02):
        return PURPLE + (255,)
    return YELLOW + (255,)

def render(size, ss=4):
    global SIZE
    SIZE = size
    px = []
    for y in range(size):
        row = []
        for x in range(size):
            acc = [0, 0, 0, 0]
            for sy in range(ss):
                for sx in range(ss):
                    r, g, b, a = sample((x + (sx + .5) / ss) / size, (y + (sy + .5) / ss) / size)
                    acc[0] += r * a; acc[1] += g * a; acc[2] += b * a; acc[3] += a
            n = ss * ss
            a = acc[3] / n
            if acc[3]:
                row.append((round(acc[0] / acc[3]), round(acc[1] / acc[3]), round(acc[2] / acc[3]), round(a)))
            else:
                row.append((0, 0, 0, 0))
        px.append(row)
    return px

def png(px):
    size = len(px)
    raw = b''.join(b'\x00' + bytes(c for p in row for c in p) for row in px)
    def chunk(t, d):
        return struct.pack('>I', len(d)) + t + d + struct.pack('>I', zlib.crc32(t + d) & 0xffffffff)
    return b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', size, size, 8, 6, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(raw, 9)) + chunk(b'IEND', b'')

def dib(px):
    size = len(px)
    header = struct.pack('<IiiHHIIiiII', 40, size, size * 2, 1, 32, 0, 0, 0, 0, 0, 0)
    xor = b''.join(bytes((b, g, r, a)) for row in reversed(px) for (r, g, b, a) in row)
    row_bytes = ((size + 31) // 32) * 4
    andmask = b''
    for row in reversed(px):
        bits = bytearray(row_bytes)
        for x, p in enumerate(row):
            if p[3] == 0:
                bits[x // 8] |= 0x80 >> (x % 8)
        andmask += bytes(bits)
    return header + xor + andmask

sizes = [16, 24, 32, 48, 64, 256]
images = []
for s in sizes:
    px = render(s, ss=8 if s < 64 else 3)
    images.append(png(px) if s == 256 else dib(px))
out = struct.pack('<HHH', 0, 1, len(sizes))
offset = 6 + 16 * len(sizes)
for s, data in zip(sizes, images):
    out += struct.pack('<BBBBHHII', s % 256, s % 256, 0, 0, 1, 32, len(data), offset)
    offset += len(data)
out += b''.join(images)
open(sys.argv[1], 'wb').write(out)
open(sys.argv[2], 'wb').write(png(render(256, 3)))
