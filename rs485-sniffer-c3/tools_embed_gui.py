"""Kompresuje gui/index.html i wstawia go jako tablicę index_html_gz do src/main.cpp."""
import gzip, re, pathlib
root = pathlib.Path(__file__).parent
gz = gzip.compress((root / "gui/index.html").read_bytes(), compresslevel=9, mtime=0)
rows = [", ".join("0x%02X" % b for b in gz[i:i + 16]) for i in range(0, len(gz), 16)]
arr = ",\n".join("  " + r for r in rows) + ",\n  0x00, 0x00\n"
src = (root / "src/main.cpp").read_text()
src = re.sub(r"// HTML GUI GZIP \d+ B", "// HTML GUI GZIP %d B" % len(gz), src)
src = re.sub(r"index_html_gz_len = \d+;", "index_html_gz_len = %d;" % len(gz), src)
src = re.sub(r"(index_html_gz\[\] PROGMEM = \{\n).*?(\};)", lambda m: m.group(1) + arr + m.group(2), src, count=1, flags=re.S)
(root / "src/main.cpp").write_text(src)
print("GUI gzip:", len(gz), "B")
