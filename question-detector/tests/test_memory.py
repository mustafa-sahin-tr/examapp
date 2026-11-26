"""Boşalan belleği geri verme: glibc'siz ortamda (macOS) da hata vermez."""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(__file__)))
import memory  # noqa: E402


def test_trim_runs_without_glibc_too(monkeypatch):
    memory.trim()
    monkeypatch.setattr(memory, "_malloc_trim", None)
    memory.trim()
