"""HTTP resource-limit tests; the speech model is replaced and never downloaded."""
import http.client
import importlib.util
from pathlib import Path
import sys
import threading
import types
import unittest
from unittest.mock import patch

class Model:
    def __init__(self, *args, **kwargs): pass
    def transcribe(self, *args, **kwargs):
        raise RuntimeError("synthetic-private-path-and-host")

class SidecarTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        fake=types.SimpleNamespace(WhisperModel=Model)
        spec=importlib.util.spec_from_file_location("sidecar_test_module",Path(__file__).with_name("whisper_sidecar.py"))
        cls.module=importlib.util.module_from_spec(spec)
        with patch.dict(sys.modules,{"faster_whisper":fake}):
            spec.loader.exec_module(cls.module)
        cls.completed=threading.Event()
        class Handler(cls.module.H):
            def do_POST(self):
                try: super().do_POST()
                finally: cls.completed.set()
        cls.server=cls.module.LimitedServer(("127.0.0.1",0),Handler)
        cls.thread=threading.Thread(target=cls.server.serve_forever,daemon=True)
        cls.thread.start()

    @classmethod
    def tearDownClass(cls):
        cls.server.shutdown()
        cls.server.server_close()
        cls.thread.join(timeout=5)

    def post(self,length,body=b"",extra=None):
        self.completed.clear()
        connection=http.client.HTTPConnection(*self.server.server_address,timeout=5)
        try:
            connection.request("POST","/transcribe",body,{"Content-Length":str(length),**(extra or {})})
            response=connection.getresponse()
            body=response.read()
            self.assertTrue(self.completed.wait(timeout=5))
            return response.status,body
        finally:
            connection.close()

    def test_rejects_oversize_before_reading(self):
        self.assertEqual(self.post(self.module.MAX_AUDIO_BYTES+1)[0],413)

    def test_rejects_invalid_length(self):
        self.assertEqual(self.post("invalid")[0],400)
        self.assertEqual(self.post(-1)[0],400)

    def test_rejects_transfer_encoding(self):
        self.assertEqual(self.post(4,b"test",{"Transfer-Encoding":"chunked"})[0],400)

    def test_busy_model_does_not_queue(self):
        self.module.MODEL_SLOT.acquire()
        try:
            self.assertEqual(self.post(4,b"test")[0],429)
        finally:
            self.module.MODEL_SLOT.release()

    def test_errors_are_sanitized_and_audio_removed(self):
        import tempfile
        with tempfile.TemporaryDirectory() as directory:
            with patch.object(self.module.tempfile,"tempdir",directory):
                status,body=self.post(4,b"test")
                self.assertEqual(status,500)
                self.assertNotIn(b"synthetic-private",body)
                self.assertEqual(list(Path(directory).iterdir()),[])
        self.assertTrue(self.module.MODEL_SLOT.acquire(blocking=False))
        self.module.MODEL_SLOT.release()

if __name__=="__main__": unittest.main()
