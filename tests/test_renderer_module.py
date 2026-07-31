"""
Unit test suite for the new renderer module (apps/designer/renderer.py).
Verifies exact Jinja2 environment config, PDF rendering via WeasyPrint fallback/real,
and DOCX rendering via docxtpl, including async rendering and error handling.
"""

import pytest
from jinja2 import Environment

from apps.designer.renderer import (
    env as renderer_env,
)
from apps.designer.renderer import (
    render_protocol_to_docx,
    render_protocol_to_docx_async,
    render_protocol_to_html,
    render_protocol_to_pdf,
    render_protocol_to_pdf_async,
)
from apps.designer.rendering import TemplateRenderingError
from tests.test_protocol_render import get_sample_rendered_document


def test_renderer_jinja_environment_config():
    """
    Asserts that the Jinja2 Environment is configured exactly as expected.
    """
    assert isinstance(renderer_env, Environment)
    # Check autoescape option
    assert renderer_env.autoescape is not None
    # Check template directory structure loader
    assert renderer_env.loader is not None


def test_renderer_module_pdf_rendering():
    """
    Asserts that render_protocol_to_pdf produces valid bytes and filename.
    """
    doc = get_sample_rendered_document()
    res = render_protocol_to_pdf(doc, "combined")
    assert res.content is not None
    assert isinstance(res.content, bytes)
    assert len(res.content) > 0
    assert res.filename.startswith("protocol_study_test_v2")
    assert res.media_type == "application/pdf"


def test_renderer_module_docx_rendering():
    """
    Asserts that render_protocol_to_docx produces valid bytes and filename.
    """
    doc = get_sample_rendered_document()
    res = render_protocol_to_docx(doc, "combined")
    assert res.content is not None
    assert isinstance(res.content, bytes)
    assert len(res.content) > 0
    assert res.filename.startswith("protocol_study_test_v2")
    assert (
        res.media_type
        == "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
    )


def test_renderer_module_html_rendering():
    """
    Asserts that render_protocol_to_html produces HTML string containing key tokens.
    """
    doc = get_sample_rendered_document()
    html_str = render_protocol_to_html(doc, "combined")
    assert isinstance(html_str, str)
    assert "<!DOCTYPE html>" in html_str
    assert "Test Title 123" in html_str


@pytest.mark.asyncio
async def test_renderer_module_async_pdf_rendering():
    """
    Asserts that the async PDF renderer runs properly off the event loop.
    """
    doc = get_sample_rendered_document()
    res = await render_protocol_to_pdf_async(doc, "combined")
    assert res.content is not None
    assert isinstance(res.content, bytes)
    assert len(res.content) > 0
    assert res.filename.startswith("protocol_study_test_v2")
    assert res.media_type == "application/pdf"


@pytest.mark.asyncio
async def test_renderer_module_async_docx_rendering():
    """
    Asserts that the async DOCX renderer runs properly off the event loop.
    """
    doc = get_sample_rendered_document()
    res = await render_protocol_to_docx_async(doc, "combined")
    assert res.content is not None
    assert isinstance(res.content, bytes)
    assert len(res.content) > 0
    assert res.filename.startswith("protocol_study_test_v2")
    assert (
        res.media_type
        == "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
    )


def test_renderer_module_error_handling(monkeypatch):
    """
    Asserts that actual rendering failures are raised as TemplateRenderingError.
    """

    # Force load_docx_template to raise an exception
    def mock_load_docx_template():
        raise ValueError("Simulated template load error")

    monkeypatch.setattr(
        "apps.designer.renderer.load_docx_template", mock_load_docx_template
    )

    doc = get_sample_rendered_document()
    with pytest.raises(TemplateRenderingError) as exc_info:
        render_protocol_to_docx(doc, "combined")
    assert "Docxtpl Word rendering failed" in str(exc_info.value)
