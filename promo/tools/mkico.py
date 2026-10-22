import struct, sys
# usage: mkico.py out.ico a-16.png a-32.png ...  (PNG-compressed ICO entries)
out, files = sys.argv[1], sys.argv[2:]
imgs = []
for f in files:
    d = open(f, 'rb').read()
    w, h = struct.unpack('>II', d[16:24])
    imgs.append((w, h, d))
hdr = struct.pack('<HHH', 0, 1, len(imgs)); off = 6 + 16 * len(imgs); body = b''
for w, h, d in imgs:
    hdr += struct.pack('<BBBBHHII', w % 256, h % 256, 0, 0, 1, 32, len(d), off); off += len(d); body += d
open(out, 'wb').write(hdr + body)
