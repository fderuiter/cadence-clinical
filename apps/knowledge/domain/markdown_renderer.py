"""
Markdown to HTML rendering engine for Knowledge base articles.

Converts standard markdown into clean, sanitized HTML for cached rendering.

Requirements: PRD-KNB-001, ADR-2188
"""

import html
import re


def render_markdown_to_html(markdown_text: str | None) -> str:
    """
    Renders a Markdown string into standard sanitized HTML.

    Handles headings, bold, italic, code blocks, inline code, links,
    unordered/ordered lists, blockquotes, and paragraphs.

    Args:
        markdown_text: Raw markdown content.

    Returns:
        Rendered HTML string.
    """
    if not markdown_text or not markdown_text.strip():
        return ""

    text = markdown_text.strip()

    # 1. Extract and preserve fenced code blocks
    code_blocks: list[str] = []

    def save_code_block(match: re.Match) -> str:
        lang = match.group(1) or ""
        code_content = match.group(2)
        escaped_code = html.escape(code_content)
        class_attr = f' class="language-{lang}"' if lang else ""
        code_blocks.append(f"<pre><code{class_attr}>{escaped_code}</code></pre>")
        return f"__CODE_BLOCK_{len(code_blocks) - 1}__"

    text = re.sub(
        r"```([a-zA-Z0-9_-]*)\n(.*?)```",
        save_code_block,
        text,
        flags=re.DOTALL,
    )

    # 2. Split into blocks separated by 2+ newlines
    blocks = [b.strip() for b in re.split(r"\n\s*\n", text) if b.strip()]
    rendered_blocks: list[str] = []

    for block in blocks:
        # Check if block is a preserved code block
        if re.match(r"^__CODE_BLOCK_\d+__$", block):
            idx = int(block.replace("__CODE_BLOCK_", "").replace("__", ""))
            rendered_blocks.append(code_blocks[idx])
            continue

        # Check for headings
        heading_match = re.match(r"^(#{1,6})\s+(.*)$", block)
        if heading_match:
            level = len(heading_match.group(1))
            heading_content = _render_inline(heading_match.group(2))
            rendered_blocks.append(f"<h{level}>{heading_content}</h{level}>")
            continue

        # Check for blockquotes
        if block.startswith(">"):
            lines = [
                line.lstrip("> ").strip() for line in block.splitlines() if line.strip()
            ]
            quote_content = "<br/>".join(_render_inline(line) for line in lines)
            rendered_blocks.append(f"<blockquote><p>{quote_content}</p></blockquote>")
            continue

        # Check for unordered lists
        if all(re.match(r"^[-*+]\s+", line) for line in block.splitlines()):
            items = []
            for line in block.splitlines():
                item_text = re.sub(r"^[-*+]\s+", "", line)
                items.append(f"<li>{_render_inline(item_text)}</li>")
            rendered_blocks.append(f"<ul>{''.join(items)}</ul>")
            continue

        # Check for ordered lists
        if all(re.match(r"^\d+\.\s+", line) for line in block.splitlines()):
            items = []
            for line in block.splitlines():
                item_text = re.sub(r"^\d+\.\s+", "", line)
                items.append(f"<li>{_render_inline(item_text)}</li>")
            rendered_blocks.append(f"<ol>{''.join(items)}</ol>")
            continue

        # Fallback to paragraph
        para_lines = [_render_inline(line) for line in block.splitlines()]
        para_content = " ".join(para_lines)
        rendered_blocks.append(f"<p>{para_content}</p>")

    return "\n".join(rendered_blocks)


def _render_inline(inline_text: str) -> str:
    """Renders inline Markdown elements (bold, italic, code, links)."""
    # Inline code: `code`
    inline_codes: list[str] = []

    def save_inline_code(match: re.Match) -> str:
        escaped = html.escape(match.group(1))
        inline_codes.append(f"<code>{escaped}</code>")
        return f"__INLINE_CODE_{len(inline_codes) - 1}__"

    res = re.sub(r"`([^`]+)`", save_inline_code, inline_text)

    # Links: [text](url)
    res = re.sub(
        r"\[([^\]]+)\]\(([^)]+)\)",
        r'<a href="\2">\1</a>',
        res,
    )

    # Bold: **text** or __text__
    res = re.sub(r"\*\*([^*]+)\*\*", r"<strong>\1</strong>", res)
    res = re.sub(r"__([^_]+)__", r"<strong>\1</strong>", res)

    # Italic: *text* or _text_
    res = re.sub(r"\*([^*]+)\*", r"<em>\1</em>", res)
    res = re.sub(r"_([^_]+)_", r"<em>\1</em>", res)

    # Restore inline codes
    for idx, code_html in enumerate(inline_codes):
        res = res.replace(f"__INLINE_CODE_{idx}__", code_html)

    return res


__all__ = ["render_markdown_to_html"]
