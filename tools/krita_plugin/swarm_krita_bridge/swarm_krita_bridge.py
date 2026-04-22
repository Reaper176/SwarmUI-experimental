from krita import Extension, Krita
from PyQt5.QtCore import QBuffer, QByteArray, QIODevice
from PyQt5.QtWidgets import QInputDialog, QMessageBox
from urllib import request
import base64
import json


class SwarmKritaBridge(Extension):
    def __init__(self, parent):
        super().__init__(parent)
        self.swarm_url = "http://127.0.0.1:7801/API/ImportKritaImage"
        self.target_session = ""

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
        merged = document.projection()
        byte_array = QByteArray()
        buffer = QBuffer(byte_array)
        buffer.open(QIODevice.WriteOnly)
        merged.save(buffer, "PNG")
        if not self.target_session:
            session_id, ok = QInputDialog.getText(None, "Swarm Krita Bridge", "Swarm session ID")
            if not ok or not session_id:
                return
            self.target_session = session_id
        payload = json.dumps({
            "imageBase64": base64.b64encode(bytes(byte_array)).decode("ascii"),
            "targetSession": self.target_session
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
