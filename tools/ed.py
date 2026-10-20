"""edit(path, [(old, new), ...]): exact replacements, each old must match once. Keeps the file's line endings
(old/new are written with \n and converted to CRLF when the file uses CRLF) and a UTF-8 BOM if present."""
def edit(path, pairs):
    raw = open(path, 'rb').read()
    bom = raw.startswith(b'\xef\xbb\xbf')
    text = raw[3:].decode('utf-8') if bom else raw.decode('utf-8')
    crlf = '\r\n' in text
    for old, new in pairs:
        if crlf:
            old = old.replace('\r\n', '\n').replace('\n', '\r\n')
            new = new.replace('\r\n', '\n').replace('\n', '\r\n')
        n = text.count(old)
        assert n == 1, f'{path}: expected 1 match, found {n}: {old[:80]!r}'
        text = text.replace(old, new)
    out = text.encode('utf-8')
    open(path, 'wb').write((b'\xef\xbb\xbf' if bom else b'') + out)
    print('edited', path, 'CRLF' if crlf else 'LF', 'BOM' if bom else '')
