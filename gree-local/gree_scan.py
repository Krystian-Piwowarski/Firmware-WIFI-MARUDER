"""Test lokalnego sterowania pompą ciepła Gree (moduł Wi-Fi z aplikacji Gree+).

Tylko ODCZYT: wyszukuje urządzenia Gree w sieci, paruje się (bind) i odczytuje parametry.
Niczego nie zmienia w pompie.

Wymaga:  pip install pycryptodome
Użycie:  python gree_scan.py                 (szuka przez broadcast)
         python gree_scan.py 10.100.21.50    (gdy znasz IP modułu)
"""
import base64, json, socket, sys

from Crypto.Cipher import AES

PORT = 7000
ECB_KEY = b"a3K8Bx%2r8Y7#xDh"            # klucz ogólny, starsze moduły (AES-ECB)
GCM_KEY = b"{yxAHAY_Lm6pbC/<"            # klucz ogólny, nowsze moduły (AES-GCM)
GCM_IV = bytes.fromhex("5440784449675a516c5e6313")
GCM_AAD = b"qualcomm-test"

# Parametry znane z pomp Gree Versati i klimatyzatorów - nieznane moduł zwykle zwraca jako 0/""
COLS = [
    "Pow", "Mod", "HeWatOutTemSet", "CoWatOutTemSet", "WatBoxTemSet", "TemUn",
    "AllInWatTemHi", "AllInWatTemLo", "AllOutWatTemHi", "AllOutWatTemLo",
    "HepOutWatTemHi", "HepOutWatTemLo", "WatBoxTemHi", "WatBoxTemLo",
    "RteFlg", "ColHtWter", "FastHtWter", "TemRec", "SwhSlp", "Quiet", "LefHom",
    "EnvTemHi", "EnvTemLo", "WdSpd", "SetTem", "TemSen",
]


def pad(b):
    n = 16 - len(b) % 16
    return b + bytes([n]) * n


def ecb_enc(key, obj):
    return base64.b64encode(AES.new(key, AES.MODE_ECB).encrypt(pad(json.dumps(obj).encode()))).decode()


def ecb_dec(key, s):
    d = AES.new(key, AES.MODE_ECB).decrypt(base64.b64decode(s))
    return json.loads(d[: -d[-1]] if d[-1] <= 16 else d.rstrip(b"\x00"))


def gcm_enc(key, obj):
    c = AES.new(key, AES.MODE_GCM, nonce=GCM_IV)
    c.update(GCM_AAD)
    ct, tag = c.encrypt_and_digest(json.dumps(obj).encode())
    return base64.b64encode(ct).decode(), base64.b64encode(tag).decode()


def gcm_dec(key, s, tag):
    c = AES.new(key, AES.MODE_GCM, nonce=GCM_IV)
    c.update(GCM_AAD)
    return json.loads(c.decrypt_and_verify(base64.b64decode(s), base64.b64decode(tag)))


def request(ip, msg, timeout=3):
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    s.settimeout(timeout)
    s.sendto(json.dumps(msg).encode(), (ip, PORT))
    data, _ = s.recvfrom(4096)
    s.close()
    return json.loads(data)


def scan(target):
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    s.setsockopt(socket.SOL_SOCKET, socket.SO_BROADCAST, 1)
    s.settimeout(4)
    s.sendto(b'{"t":"scan"}', (target, PORT))
    found = []
    try:
        while True:
            data, addr = s.recvfrom(4096)
            found.append((addr[0], json.loads(data)))
    except socket.timeout:
        pass
    return found


def decode_pack(r, key_ecb, key_gcm):
    if "tag" in r:
        return gcm_dec(key_gcm, r["pack"], r["tag"]), "gcm"
    return ecb_dec(key_ecb, r["pack"]), "ecb"


def main():
    target = sys.argv[1] if len(sys.argv) > 1 else "255.255.255.255"
    print(f"Szukam urządzeń Gree ({target}:{PORT})...")
    devs = scan(target)
    if not devs:
        print("Brak odpowiedzi. Sprawdź, czy komputer jest w tej samej sieci co moduł Wi-Fi pompy,"
              " albo podaj IP modułu jako argument.")
        return
    for ip, r in devs:
        info, mode = decode_pack(r, ECB_KEY, GCM_KEY)
        print(f"\n=== {ip}  szyfrowanie: {mode}")
        print(json.dumps(info, indent=1, ensure_ascii=False))
        mac = info.get("mac") or r.get("cid")

        # Bind - pobranie klucza urządzenia
        bind = {"mac": mac, "t": "bind", "uid": 0}
        msg = {"cid": "app", "i": 1, "t": "pack", "uid": 0, "tcid": mac}
        if mode == "gcm":
            msg["pack"], msg["tag"] = gcm_enc(GCM_KEY, bind)
        else:
            msg["pack"] = ecb_enc(ECB_KEY, bind)
        b, _ = decode_pack(request(ip, msg), ECB_KEY, GCM_KEY)
        key = b["key"].encode()
        print(f"bind OK, klucz urządzenia: {b['key']}")

        # Status
        st = {"cols": COLS, "mac": mac, "t": "status"}
        msg = {"cid": "app", "i": 0, "t": "pack", "uid": 0, "tcid": mac}
        if mode == "gcm":
            msg["pack"], msg["tag"] = gcm_enc(key, st)
        else:
            msg["pack"] = ecb_enc(key, st)
        d, _ = decode_pack(request(ip, msg), key, key)
        print("\nParametry:")
        for c, v in zip(d.get("cols", []), d.get("dat", [])):
            print(f"  {c:16} = {v}")


if __name__ == "__main__":
    main()
