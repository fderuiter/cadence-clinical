import os
import tempfile
import pytest
import pytest_asyncio
import xml.etree.ElementTree as ET
import httpx

from playwright.async_api import async_playwright

from apps.execution.database.core import db_manager
from apps.execution.database.models import Base, TranslationJob
from apps.execution.main import app
from tests.test_translator import get_auth_headers

def check_overlap(box1, box2):
    return not (
        box1["x"] >= box2["x"] + box2["width"] or
        box1["x"] + box1["width"] <= box2["x"] or
        box1["y"] >= box2["y"] + box2["height"] or
        box1["y"] + box1["height"] <= box2["y"]
    )

async def validate_layout_html(html_content: str):
    style_injection = """
    <style>
      xf\\:input { display: block; margin-bottom: 10px; padding: 5px; border: 1px solid #ccc; }
      xf\\:label { display: block; font-weight: bold; margin-bottom: 5px; }
      .clinical-input { display: block; margin-bottom: 10px; }
      label { display: block; font-weight: bold; }
      input { display: block; }
    </style>
    """
    if "<head>" in html_content:
        html_content = html_content.replace("<head>", f"<head>{style_injection}")
    else:
        html_content = f"<html><head>{style_injection}</head><body>{html_content}</body></html>"

    with tempfile.NamedTemporaryFile(suffix=".html", mode="w", delete=False) as f:
        f.write(html_content)
        temp_path = f.name
        
    try:
        async with async_playwright() as p:
            browser = await p.chromium.launch(headless=True)
            page = await browser.new_page()
            await page.goto(f"file://{os.path.abspath(temp_path)}")
            await page.wait_for_timeout(100)
            
            elements_data = await page.evaluate("""() => {
                const results = [];
                const nodes = document.querySelectorAll('xf\\\\:input, div.clinical-input, xf\\\\:label, label, input');
                nodes.forEach((node, index) => {
                    const rect = node.getBoundingClientRect();
                    const style = window.getComputedStyle(node);
                    const isVisible = style.display !== 'none' && style.visibility !== 'hidden' && rect.width > 0 && rect.height > 0;
                    
                    let parent = node.parentElement;
                    const ancestorIndices = [];
                    while(parent) {
                        const parentIndex = Array.from(nodes).indexOf(parent);
                        if (parentIndex !== -1) {
                            ancestorIndices.push(parentIndex);
                        }
                        parent = parent.parentElement;
                    }
                    
                    results.push({
                        id: index,
                        tag: node.tagName.toLowerCase(),
                        className: node.className,
                        x: rect.x,
                        y: rect.y,
                        width: rect.width,
                        height: rect.height,
                        isVisible: isVisible,
                        ancestors: ancestorIndices
                    });
                });
                return results;
            }""")
            
            await browser.close()
            
            visible_elements = [e for e in elements_data if e["isVisible"]]
            if len(elements_data) > 0 and len(visible_elements) == 0:
                raise ValueError("No visible elements found.")
            
            invisible_elements = [e for e in elements_data if not e["isVisible"]]
            if invisible_elements:
                raise ValueError(f"Found {len(invisible_elements)} invisible elements which should be visible.")
                
            wrappers = [e for e in visible_elements if e["tag"] == "xf:input" or "clinical-input" in e["className"]]
            for i in range(1, len(wrappers)):
                prev = wrappers[i-1]
                curr = wrappers[i]
                if curr["y"] < prev["y"]:
                    raise ValueError(f"Element sequence scrambled: {curr['tag']} is above {prev['tag']}")
            
            for i in range(len(visible_elements)):
                for j in range(i + 1, len(visible_elements)):
                    e1 = visible_elements[i]
                    e2 = visible_elements[j]
                    if e1["id"] in e2["ancestors"] or e2["id"] in e1["ancestors"]:
                        continue
                    if check_overlap(e1, e2):
                        raise ValueError(f"Elements overlapping: element {e1['id']} and element {e2['id']}")
            return True
            
    finally:
        os.unlink(temp_path)

@pytest.mark.asyncio
async def test_layout_validation_valid():
    html = """
    <html xmlns="http://www.w3.org/1999/xhtml" xmlns:xf="http://www.w3.org/2002/xforms">
      <head><title>Test</title></head>
      <body>
        <xf:input ref="/item_1">
          <xf:label>Item 1</xf:label>
        </xf:input>
        <xf:input ref="/item_2">
          <xf:label>Item 2</xf:label>
        </xf:input>
      </body>
    </html>
    """
    assert await validate_layout_html(html) == True

@pytest.mark.asyncio
async def test_layout_validation_overlap():
    html = """
    <html xmlns="http://www.w3.org/1999/xhtml" xmlns:xf="http://www.w3.org/2002/xforms">
      <head>
        <style>
          .overlap { position: absolute; top: 10px; left: 10px; }
        </style>
      </head>
      <body>
        <xf:input ref="/item_1" class="overlap">
          <xf:label>Item 1</xf:label>
        </xf:input>
        <xf:input ref="/item_2" class="overlap">
          <xf:label>Item 2</xf:label>
        </xf:input>
      </body>
    </html>
    """
    with pytest.raises(ValueError, match="Elements overlapping"):
        await validate_layout_html(html)

@pytest.mark.asyncio
async def test_layout_validation_scrambled_sequence():
    html = """
    <html xmlns="http://www.w3.org/1999/xhtml" xmlns:xf="http://www.w3.org/2002/xforms">
      <head>
        <style>
          .wrapper { position: absolute; }
          #el1 { top: 100px; }
          #el2 { top: 50px; }
        </style>
      </head>
      <body>
        <xf:input id="el1" ref="/item_1" class="wrapper">
          <xf:label>Item 1</xf:label>
        </xf:input>
        <xf:input id="el2" ref="/item_2" class="wrapper">
          <xf:label>Item 2</xf:label>
        </xf:input>
      </body>
    </html>
    """
    with pytest.raises(ValueError, match="Element sequence scrambled"):
        await validate_layout_html(html)

@pytest.mark.asyncio
async def test_layout_validation_invisible():
    html = """
    <html xmlns="http://www.w3.org/1999/xhtml" xmlns:xf="http://www.w3.org/2002/xforms">
      <body>
        <xf:input ref="/item_1" style="display: none;">
          <xf:label>Item 1</xf:label>
        </xf:input>
        <xf:input ref="/item_2">
          <xf:label>Item 2</xf:label>
        </xf:input>
      </body>
    </html>
    """
    with pytest.raises(ValueError, match="invisible elements"):
        await validate_layout_html(html)

@pytest_asyncio.fixture(autouse=True)
async def setup_test_db():
    import os
    db_manager.init_db(
        os.getenv(
            "TEST_DATABASE_URL",
            "sqlite+aiosqlite:///:memory:",
        )
    )
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()

@pytest.mark.asyncio
async def test_layout_validation_integration():
    study_payload = {
        "study_id": "test_layout_study_123",
        "payload": {
            "name": "Acme Clinical Trial Layout Test",
            "protocol": {
                "items": [
                    {"id": "q1", "name": "Question 1", "type": "string"},
                    {"id": "q2", "name": "Question 2", "type": "int"},
                    {"id": "q3", "name": "Question 3", "type": "date"},
                ]
            },
        },
    }

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        response = await client.post(
            "/events/study-published", json=study_payload, headers=get_auth_headers()
        )
    assert response.status_code == 200

    async with db_manager.get_session_maker()() as session:
        result = await session.execute(
            TranslationJob.__table__.select().where(
                TranslationJob.study_id == "test_layout_study_123"
            )
        )
        job = result.mappings().first()
        
        assert job is not None
        assert job["status"] == "COMPLETED"
        assert job["openrosa_payload"] is not None
        
        openrosa_xml = job["openrosa_payload"]
        
    assert await validate_layout_html(openrosa_xml) == True
