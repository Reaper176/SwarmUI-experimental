from krita import Krita

from .swarm_krita_bridge import SwarmKritaBridge

app = Krita.instance()
extension = SwarmKritaBridge(parent=app)
app.addExtension(extension)
