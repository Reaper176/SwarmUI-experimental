from krita import Extension, InfoObject, Krita
try:
    from PyQt5.QtCore import QBuffer, QByteArray, QIODevice
    from PyQt5.QtWidgets import QInputDialog, QMessageBox

    QT_WRITE_ONLY = QIODevice.WriteOnly
except ModuleNotFoundError:
    from PyQt6.QtCore import QBuffer, QByteArray, QIODevice
    from PyQt6.QtWidgets import QInputDialog, QMessageBox

    QT_WRITE_ONLY = QIODevice.OpenModeFlag.WriteOnly
from urllib import request
import base64
import json
import os
import tempfile


class SwarmKritaBridge(Extension):
    def __init__(self, parent):
        super().__init__(parent)
        self.swarm_url = "http://127.0.0.1:7801/API/ImportKritaImage"
        self.session_url = "http://127.0.0.1:7801/API/GetActiveKritaSession"

    def get_target_session(self):
        req = request.Request(
            self.session_url,
            data=json.dumps({"session_id": "placeholder"}).encode("utf-8"),
            headers={"Content-Type": "application/json"},
        )
        with request.urlopen(req) as response:
            data = json.loads(response.read().decode("utf-8"))
        return data.get("session_id")

    def setup(self):
        pass

    def createActions(self, window):
        action = window.createAction("send_to_swarm", "Send to Swarm", "tools/scripts")
        action.triggered.connect(self.send_to_swarm)

    def send_to_swarm(self):
        window = Krita.instance().activeWindow()
        if window is None or window.activeView() is None:
            QMessageBox.warning(None, "Swarm Krita Bridge", "No active Krita document is open.")
            return
        document = window.activeView().document()
        if document is None:
            QMessageBox.warning(None, "Swarm Krita Bridge", "No active Krita document is open.")
            return
        temp_file = None
        temp_path = None
        try:
            temp_file = tempfile.NamedTemporaryFile(suffix=".png", delete=False)
            temp_path = temp_file.name
            temp_file.close()
            saved = document.exportImage(temp_path, InfoObject())
            document.waitForDone()
            if not saved:
                QMessageBox.critical(None, "Swarm Krita Bridge", "Failed to export the active document as PNG.")
                return
            with open(temp_path, "rb") as handle:
                png_bytes = handle.read()
        finally:
            if temp_path and os.path.exists(temp_path):
                os.unlink(temp_path)
        try:
            target_session = self.get_target_session()
        except Exception as ex:
            QMessageBox.critical(None, "Swarm Krita Bridge", f"Failed to resolve the active Swarm session: {ex}")
            return
        if not target_session:
            QMessageBox.critical(None, "Swarm Krita Bridge", "No active Swarm session is registered. Use Send to Krita from Swarm first.")
            return
        payload = json.dumps({
            "session_id": target_session,
            "imageBase64": base64.b64encode(png_bytes).decode("ascii"),
            "targetSession": target_session
        }).encode("utf-8")
        req = request.Request(self.swarm_url, data=payload, headers={"Content-Type": "application/json"})
        try:
            with request.urlopen(req) as response:
                data = json.loads(response.read().decode("utf-8"))
        except Exception as ex:
            QMessageBox.critical(None, "Swarm Krita Bridge", f"Failed to reach SwarmUI: {ex}")
            return
        if not data.get("success"):
            QMessageBox.critical(None, "Swarm Krita Bridge", data.get("error", "SwarmUI rejected the image."))
            return
        QMessageBox.information(None, "Swarm Krita Bridge", "Image sent to SwarmUI.")
