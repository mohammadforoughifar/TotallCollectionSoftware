"""کتابخانه کمکی تست یکپارچه آرشیو اسناد"""
import json, urllib.request, urllib.error, datetime, sys

import os
B = os.environ.get("API_BASE", "http://localhost:5100")
PASS, FAIL, WARN = [], [], []


def call(method, path, token=None, body=None, raw=False, files=None):
    url = B + path
    data, headers = None, {}
    if token:
        headers["Authorization"] = "Bearer " + token
    if files:
        boundary = "----e2eBoundary"
        parts = []
        for name, (fn, content, ct) in files.items():
            parts.append(("--" + boundary + "\r\n"
                          f'Content-Disposition: form-data; name="{name}"; filename="{fn}"\r\n'
                          f"Content-Type: {ct}\r\n\r\n").encode())
            parts.append(content if isinstance(content, bytes) else content.encode())
            parts.append(b"\r\n")
        parts.append(("--" + boundary + "--\r\n").encode())
        data = b"".join(parts)
        headers["Content-Type"] = "multipart/form-data; boundary=" + boundary
    elif body is not None:
        data = json.dumps(body).encode()
        headers["Content-Type"] = "application/json"

    req = urllib.request.Request(url, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req) as r:
            payload = r.read()
            if raw:
                return r.status, payload, dict(r.headers)
            try:
                return r.status, json.loads(payload) if payload else None, dict(r.headers)
            except Exception:
                return r.status, payload.decode("utf-8", "replace"), dict(r.headers)
    except urllib.error.HTTPError as e:
        payload = e.read()
        try:
            return e.code, json.loads(payload) if payload else None, dict(e.headers)
        except Exception:
            return e.code, payload.decode("utf-8", "replace"), dict(e.headers)
    except Exception as e:
        return 0, str(e), {}


def login(u, p):
    st, d, _ = call("POST", "/api/auth/login", body={"username": u, "password": p})
    if st != 200:
        raise RuntimeError(f"login {u} failed: {st} {d}")
    return d["token"], d.get("userId")


def check(name, cond, detail=""):
    if cond:
        PASS.append(name)
        print(f"  \033[92m✔\033[0m {name}" + (f"  \033[90m{detail}\033[0m" if detail else ""))
    else:
        FAIL.append((name, detail))
        print(f"  \033[91m✘ {name}\033[0m  {detail}")
    return cond


def warn(name, detail=""):
    WARN.append((name, detail))
    print(f"  \033[93m⚠ {name}\033[0m  {detail}")


def section(t):
    print(f"\n\033[1;96m{'='*72}\n{t}\n{'='*72}\033[0m")


def sub(t):
    print(f"\n\033[1m── {t}\033[0m")


def days(n):
    return (datetime.date.today() + datetime.timedelta(days=n)).isoformat()


def summary():
    print(f"\n\033[1;96m{'='*72}\033[0m")
    print(f"\033[1mنتیجه: \033[92m{len(PASS)} موفق\033[0m"
          f"  \033[91m{len(FAIL)} ناموفق\033[0m"
          f"  \033[93m{len(WARN)} هشدار\033[0m")
    if FAIL:
        print("\n\033[91mموارد ناموفق:\033[0m")
        for n, d in FAIL:
            print(f"  ✘ {n}  {d}")
    if WARN:
        print("\n\033[93mهشدارها:\033[0m")
        for n, d in WARN:
            print(f"  ⚠ {n}  {d}")
    print(f"\033[1;96m{'='*72}\033[0m")
    return 1 if FAIL else 0
