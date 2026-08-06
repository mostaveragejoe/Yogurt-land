"""Launcher for Lead Engine v2 on Windows.

Double-click this file.  It starts the server with no console window.
Then it opens the app in the default browser.  If the server already
runs, it only opens a new browser tab.
"""

import socket
import subprocess
import sys
import time
import webbrowser
from pathlib import Path

HOST = "127.0.0.1"
PORT = 5002
APP_DIR = Path(__file__).resolve().parent
APP_URL = f"http://{HOST}:{PORT}"


def port_is_open():
    """Return True if the server already listens on the port."""
    try:
        with socket.create_connection((HOST, PORT), timeout=1):
            return True
    except OSError:
        return False


def find_pythonw():
    """Find pythonw.exe so that the server gets no console window."""
    exe = Path(sys.executable)
    if exe.name.lower() == "pythonw.exe":
        return str(exe)
    candidate = exe.with_name("pythonw.exe")
    if candidate.exists():
        return str(candidate)
    return str(exe)


def main():
    if port_is_open():
        # The server already runs.  Only open a browser tab.
        webbrowser.open(APP_URL)
        return

    creation_flags = 0
    if hasattr(subprocess, "CREATE_NO_WINDOW"):
        creation_flags = subprocess.CREATE_NO_WINDOW
    subprocess.Popen(
        [find_pythonw(), str(APP_DIR / "lead_engine.py")],
        cwd=str(APP_DIR),
        creationflags=creation_flags,
    )

    # Wait until the server answers, for a maximum of 30 seconds.
    for _ in range(60):
        if port_is_open():
            break
        time.sleep(0.5)
    webbrowser.open(APP_URL)


if __name__ == "__main__":
    main()
