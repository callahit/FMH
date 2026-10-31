#!/usr/bin/env python3
"""Decoder fuer FM2002 Textm.sav / Textm.txt (Textmodus-Update III, 2.11.2001).
Format (belegt):
  Kopf 16 Byte: 8 Byte Kennung, u32 (=2), u32 (=0)
  dann N Knoten in Preorder, je:
    u32 id, u32 n, n*u32 Zeilennummern (Textm.txt), 44 Byte Parameterblock, u32 Kinderzahl
Usage: python3 tools/textmode/decode_textm.py data/textmode
       schreibt <dir>/work/nodes.json (Baum flach, mit parent/depth/category).
       --catalog ruft build_catalog.py auf (erwartet work/nodes.json neben dem Skript;
       dafür nodes.json nach tools/textmode/work kopieren oder das Skript aus dem Zielordner starten).
"""
import struct, json, sys, os
ARGS = [a for a in sys.argv[1:] if not a.startswith('--')]
D = ARGS[0] if ARGS else os.path.dirname(os.path.abspath(__file__))

def load_lines(path):
    L = {}
    for raw in open(path, 'rb').read().split(b'\r\n'):
        if not raw.strip(): continue
        s = raw.decode('cp1252')
        k, _, v = s.partition(' ')
        L[int(k)] = v
    return L

def parse_sav(path):
    d = open(path, 'rb').read()
    hdr = dict(magic=d[:8].hex(' '), u32_8=struct.unpack_from('<I', d, 8)[0], u32_12=struct.unpack_from('<I', d, 12)[0])
    off = 16; nodes = []
    while off < len(d):
        nid, n = struct.unpack_from('<II', d, off)
        refs = list(struct.unpack_from('<%dI' % n, d, off + 8))
        p = off + 8 + 4 * n
        blk = d[p:p + 44]
        cc = struct.unpack_from('<I', d, p + 44)[0]
        nodes.append(dict(idx=len(nodes), off=off, id=nid, refs=refs, blk=list(blk), cc=cc))
        off = p + 48
    assert off == len(d), (off, len(d))
    return hdr, nodes

def build(nodes):
    i = 0
    def rec(parent, depth, cat):
        nonlocal i
        n = nodes[i]; i += 1
        n['parent'] = parent; n['depth'] = depth
        if depth == 1: cat = n['idx']
        n['cat'] = cat
        n['kids'] = []
        for k in range(n['cc']):
            c = rec(n['idx'], depth + 1, cat); n['kids'].append(c)
        return n['idx']
    roots = []
    while i < len(nodes): roots.append(rec(None, 0, None))
    return roots

if __name__ == '__main__':
    L = load_lines(os.path.join(D, 'Textm.txt'))
    hdr, nodes = parse_sav(os.path.join(D, 'Textm.sav'))
    roots = build(nodes)
    os.makedirs(os.path.join(D, 'work'), exist_ok=True)
    json.dump(dict(header=hdr, roots=roots, lines={k: v for k, v in L.items()}, nodes=nodes),
              open(os.path.join(D, 'work', 'nodes.json'), 'w', encoding='utf-8'), ensure_ascii=False)
    print('header', hdr, 'nodes', len(nodes), 'roots', roots, 'lines', len(L))
    if '--catalog' in sys.argv:
        import subprocess
        subprocess.run([sys.executable, os.path.join(os.path.dirname(os.path.abspath(__file__)), 'build_catalog.py')], check=True)
