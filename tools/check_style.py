import ast
import io
import os
import re
import struct
import sys
import tokenize

DASHES = (chr(0x2014), chr(0x2013))
TEXT = (".cs", ".py", ".ps1", ".md", ".txt", ".gitignore")
SKIP_DIRS = {"build", "__pycache__", ".git"}
DRIVE = re.compile(r"(?<![A-Za-z])[A-Za-z]:[\\/]")
HOME = os.path.basename(os.path.expanduser("~"))
WORDS = [w for w in (HOME, "Tavern" + "Dev") if len(w) >= 4]


def cs_problems(text):
    out = []
    i, n, line = 0, len(text), 1
    while i < n:
        c = text[i]
        nx = text[i + 1] if i + 1 < n else ""
        if c == "\n":
            line += 1
            i += 1
        elif c == "/" and nx == "/":
            out.append((line, "comment"))
            j = text.find("\n", i)
            i = n if j < 0 else j
        elif c == "/" and nx == "*":
            out.append((line, "comment"))
            j = text.find("*/", i + 2)
            j = n if j < 0 else j + 2
            line += text.count("\n", i, j)
            i = j
        elif c == "@" and nx == '"':
            j = i + 2
            while j < n and not (text[j] == '"' and text[j + 1:j + 2] != '"'):
                j += 2 if text[j] == '"' else 1
            out += string_problems(line, text[i:j])
            line += text.count("\n", i, j)
            i = j + 1
        elif c in "\"'":
            j = i + 1
            while j < n and text[j] != c and text[j] != "\n":
                j += 2 if text[j] == "\\" else 1
            out += string_problems(line, ESCAPE.sub(lambda m: UNESCAPE.get(m.group(1), m.group(1)), text[i:j]))
            i = j + 1
        else:
            i += 1
    return out


ESCAPE = re.compile(r"\\(.)")
UNESCAPE = {"n": "\n", "r": "\r", "t": "\t", "0": "\0"}


def string_problems(line, s):
    out = []
    if " -- " in s:
        out.append((line, "dash substitute in a string"))
    if DRIVE.search(s):
        out.append((line, "local path in a string"))
    return out


def py_problems(text):
    out = []
    for tok in tokenize.generate_tokens(io.StringIO(text).readline):
        if tok.type == tokenize.COMMENT:
            out.append((tok.start[0], "comment"))
    tree = ast.parse(text)
    for node in [tree] + [n for n in ast.walk(tree) if isinstance(n, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef))]:
        body = node.body
        if body and isinstance(body[0], ast.Expr) and isinstance(body[0].value, ast.Constant) and isinstance(body[0].value.value, str):
            out.append((body[0].lineno, "docstring"))
    return out


def ps1_problems(text):
    out = []
    for i, raw in enumerate(text.split("\n"), 1):
        s = raw.strip()
        if s.startswith("#") or s.startswith("<#"):
            out.append((i, "comment"))
    return out


def check_sources(root):
    problems = []
    for base, dirs, files in os.walk(root):
        dirs[:] = [d for d in dirs if d not in SKIP_DIRS]
        for name in files:
            ext = os.path.splitext(name)[1].lower() or name
            if ext not in TEXT:
                continue
            path = os.path.join(base, name)
            text = open(path, encoding="utf-8").read()
            found = []
            for i, raw in enumerate(text.split("\n"), 1):
                if any(d in raw for d in DASHES):
                    found.append((i, "em or en dash"))
                if any(w.lower() in raw.lower() for w in WORDS):
                    found.append((i, "local path"))
            if ext == ".cs":
                found += cs_problems(text)
            elif ext == ".py":
                found += py_problems(text)
            elif ext == ".ps1":
                found += ps1_problems(text)
            elif ext == ".md":
                found += [(i, "dash substitute") for i, raw in enumerate(text.split("\n"), 1) if " -- " in raw]
            problems += [(os.path.relpath(path, root), i, what) for i, what in found]
    return problems


def heaps(data):
    root = data.find(b"BSJB")
    if root < 0:
        raise ValueError("no metadata")
    vlen = struct.unpack_from("<I", data, root + 12)[0]
    p = root + 16 + vlen + 2
    count = struct.unpack_from("<H", data, p)[0]
    p += 2
    found = {}
    for _ in range(count):
        off, size = struct.unpack_from("<II", data, p)
        end = data.index(b"\0", p + 8)
        found[data[p + 8:end].decode("ascii")] = data[root + off:root + off + size]
        p += 8 + ((end - p - 8) // 4 + 1) * 4
    return found


def entries(heap):
    out, i = [], 1
    while i < len(heap):
        b = heap[i]
        if b & 0x80 == 0:
            ln, i = b, i + 1
        elif b & 0xC0 == 0x80:
            ln, i = ((b & 0x3F) << 8) | heap[i + 1], i + 2
        else:
            ln, i = ((b & 0x1F) << 24) | (heap[i + 1] << 16) | (heap[i + 2] << 8) | heap[i + 3], i + 4
        out.append(heap[i:i + ln])
        i += ln
    return out


def user_strings(heap):
    return [e[:-1].decode("utf-16-le") for e in entries(heap) if e]


def attribute_strings(heap):
    return [e[2:].decode("utf-8", "replace") for e in entries(heap) if e[:2] == b"\x01\x00"]


def check_dll(dll, pack):
    data = open(dll, "rb").read()
    blob = open(pack, "rb").read()
    at = data.find(blob)
    if at < 0:
        return [(dll, 0, "does not carry assets/armor.pack")]
    data = data[:at] + data[at + len(blob):]
    problems = []
    for word in WORDS + ["TavernLib", ".pdb"]:
        for enc in ("ascii", "utf-16-le"):
            if word.encode(enc) in data:
                problems.append((dll, 0, "holds %s (%s)" % (word, enc)))
    h = heaps(data)
    strings = user_strings(h.get("#US", b"")) + attribute_strings(h.get("#Blob", b""))
    strings += [s.decode("utf-8", "replace") for s in h.get("#Strings", b"").split(b"\0") if s]
    if len(strings) < 100:
        problems.append((dll, 0, "only %d metadata strings found" % len(strings)))
    for s in strings:
        if any(d in s for d in DASHES):
            problems.append((dll, 0, "em or en dash in %r" % s[:60]))
        if " -- " in s:
            problems.append((dll, 0, "dash substitute in %r" % s[:60]))
        if DRIVE.search(s):
            problems.append((dll, 0, "local path in %r" % s[:60]))
    return problems


def main():
    args = sys.argv[1:]
    root = os.path.abspath(args[0] if args else os.path.join(os.path.dirname(__file__), ".."))
    problems = check_sources(root)
    checked = "source"
    if len(args) > 1:
        problems += check_dll(args[1], os.path.join(root, "assets", "armor.pack"))
        checked = "source and DLL"
    for path, line, what in sorted(problems):
        print("%s:%d: %s" % (path, line, what))
    print("style (%s): %d problem(s)" % (checked, len(problems)))
    sys.exit(1 if problems else 0)


if __name__ == "__main__":
    main()
