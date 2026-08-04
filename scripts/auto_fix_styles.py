#!/usr/bin/env python3
import os
import re

REPLACEMENTS = [
    # Media Queries
    (r"@media\s*\(\s*max-width\s*:\s*1024px\s*\)", "@media (min-width: 1024px)"),
    (r"@media\s*\(\s*max-width\s*:\s*768px\s*\)", "@media (min-width: 768px)"),
    (r"@media\s*\(\s*max-width\s*:\s*900px\s*\)", "@media (min-width: 900px)"),
    # Radio Button heights
    (
        r"width\s*:\s*18px\s*;\s*height\s*:\s*18px\s*;",
        "width: var(--touch-target-min, 44px);\n  height: var(--touch-target-min, 44px);",
    ),
    (
        r"width\s*:\s*18px\s*;\s*height\s*:\s*18px\s*;",
        "width: var(--touch-target-min, 44px);\n  height: var(--touch-target-min, 44px);",
    ),
    # Variable name updates to standard design tokens
    (r"\bvar\(--primary-light\)", "var(--color-primary-light)"),
    (r"\bvar\(--primary-dark\)", "var(--color-primary-dark)"),
    (r"\bvar\(--primary\)", "var(--color-primary)"),
    (r"\bvar\(--accent-light\)", "var(--color-primary-light)"),
    (r"\bvar\(--accent-bg\)", "var(--color-primary-light)"),
    (r"\bvar\(--accent\)", "var(--color-accent)"),
    (r"\bvar\(--success-bg\)", "var(--color-success-bg)"),
    (r"\bvar\(--success\)", "var(--color-success)"),
    (r"\bvar\(--warning-bg\)", "var(--color-warning-bg)"),
    (r"\bvar\(--warning\)", "var(--color-warning)"),
    (r"\bvar\(--error-bg\)", "var(--color-error-bg)"),
    (r"\bvar\(--error\)", "var(--color-error)"),
    (r"\bvar\(--danger-bg\)", "var(--color-error-bg)"),
    (r"\bvar\(--danger\)", "var(--color-error)"),
    (r"\bvar\(--neutral-light\)", "var(--color-surface-muted)"),
    (r"\bvar\(--neutral-dark\)", "var(--color-text)"),
    (r"\bvar\(--border-color\)", "var(--color-border)"),
    (r"\bvar\(--border\)", "var(--color-border)"),
    (r"\bvar\(--text-main\)", "var(--color-text)"),
    (r"\bvar\(--text-muted\)", "var(--color-text-muted)"),
    (r"\bvar\(--bg-main\)", "var(--color-surface-muted)"),
    (r"\bvar\(--bg-card\)", "var(--color-surface)"),
    # Remaining hex / functional / rgba colors
    (r"rgba\(0,\s*0,\s*0,\s*0\.01\)", "var(--color-surface-muted)"),
    (r"rgba\(0,\s*0,\s*0,\s*0\.1\)", "var(--color-border)"),
    (r"#f0fdf4\b", "var(--color-success-bg)"),
    (r"#92400e\b", "var(--color-warning)"),
    (r"#e0e7ff\b", "var(--color-primary-light)"),
    (r"rgba\(22,\s*163,\s*74,\s*0\.2\)", "var(--color-success)"),
    (r"rgba\(220,\s*38,\s*38,\s*0\.2\)", "var(--color-error)"),
    (r"rgba\(234,\s*88,\s*12,\s*0\.2\)", "var(--color-warning)"),
    # Hex Colors
    (r"#026597\b", "var(--color-primary)"),
    (r"#0284c7\b", "var(--color-primary)"),
    (r"#0369a1\b", "var(--color-primary)"),
    (r"#014d76\b", "var(--color-primary-dark)"),
    (r"#e0f2fe\b", "var(--color-primary-light)"),
    (r"#f0f9ff\b", "var(--color-primary-light)"),
    (r"#4338ca\b", "var(--color-accent)"),
    (r"#15803d\b", "var(--color-success)"),
    (r"#dcfce7\b", "var(--color-success-bg)"),
    (r"#854d0e\b", "var(--color-warning)"),
    (r"#fef9c3\b", "var(--color-warning-bg)"),
    (r"#b91c1c\b", "var(--color-error)"),
    (r"#fee2e2\b", "var(--color-error-bg)"),
    (r"#ffffff\b", "var(--color-surface)"),
    (r"#fff\b", "var(--color-surface)"),
    (r"#f8fafc\b", "var(--color-surface-muted)"),
    (r"#f1f5f9\b", "var(--color-surface-muted)"),
    (r"#fafafa\b", "var(--color-surface-muted)"),
    (r"#faf5ff\b", "var(--color-surface-muted)"),
    (r"#fef2f2\b", "var(--color-error-bg)"),
    (r"#f0fdfa\b", "var(--color-success-bg)"),
    (r"#eff6ff\b", "var(--color-primary-light)"),
    (r"#dbeafe\b", "var(--color-primary-light)"),
    (r"#0f766e\b", "var(--color-success)"),
    (r"#0f172a\b", "var(--color-text)"),
    (r"#334155\b", "var(--color-text)"),
    (r"#1e293b\b", "var(--color-text)"),
    (r"#475569\b", "var(--color-text-muted)"),
    (r"#64748b\b", "var(--color-text-muted)"),
    (r"#94a3b8\b", "var(--color-text-muted)"),
    (r"#cbd5e1\b", "var(--color-border)"),
    (r"#e2e8f0\b", "var(--color-border)"),
    (r"#3b82f6\b", "var(--color-accent)"),
    (r"#bfdbfe\b", "var(--color-primary-light)"),
    (r"#1e40af\b", "var(--color-primary-dark)"),
    (r"#6b21a8\b", "var(--color-accent)"),
    (r"#f3e8ff\b", "var(--color-primary-light)"),
    (r"#38bdf8\b", "var(--color-primary-light)"),
    (r"#93c5fd\b", "var(--color-primary-light)"),
    (r"#2563eb\b", "var(--color-accent)"),
    (r"#1d4ed8\b", "var(--color-primary-dark)"),
    (r"#166534\b", "var(--color-success)"),
    (r"#ef4444\b", "var(--color-error)"),
    (r"#fca5a5\b", "var(--color-error-bg)"),
    (r"#991b1b\b", "var(--color-error)"),
    (r"#b45309\b", "var(--color-warning)"),
    (r"#fef3c7\b", "var(--color-warning-bg)"),
    (r"#d97706\b", "var(--color-warning)"),
    (r"#fffbeb\b", "var(--color-warning-bg)"),
    (r"#f59e0b\b", "var(--color-warning)"),
    (r"#78350f\b", "var(--color-warning)"),
    (r"#16a34a\b", "var(--color-success)"),
    (r"#dc2626\b", "var(--color-error)"),
    (r"#bbf7d0\b", "var(--color-success-bg)"),
    (r"#475569\b", "var(--color-text-muted)"),
    (r"#1e1b4b\b", "var(--color-primary-dark)"),
    (r"#c7d2fe\b", "var(--color-primary-light)"),
    (r"#86efac\b", "var(--color-success-bg)"),
    # Literal colors
    (r"(\bcolor\s*:\s*)white\b", r"\1var(--color-surface)"),
    (r"(\bbackground-color\s*:\s*)white\b", r"\1var(--color-surface)"),
    (r"(\bbackground\s*:\s*)white\b", r"\1var(--color-surface)"),
    # Spacing Units
    (r"(\bpadding\s*:\s*)16px\s+24px", r"\1var(--spacing-md) var(--spacing-xl)"),
    (r"(\bpadding\s*:\s*)24px\s+16px", r"\1var(--spacing-xl) var(--spacing-md)"),
    (r"(\bpadding\s*:\s*)12px\s+16px", r"\1var(--spacing-sm) var(--spacing-md)"),
    (r"(\bpadding\s*:\s*)8px\s+12px", r"\1var(--spacing-xs) var(--spacing-sm)"),
    (r"(\bpadding\s*:\s*)6px\s+12px", r"\1var(--spacing-2xs) var(--spacing-sm)"),
    (r"(\bpadding\s*:\s*)4px\s+10px", r"\1var(--spacing-2xs) var(--spacing-xs)"),
    (r"(\bpadding\s*:\s*)2px\s+6px", r"\1var(--spacing-2xs) var(--spacing-xs)"),
    (r"(\bpadding\s*:\s*)2px\s+8px", r"\1var(--spacing-2xs) var(--spacing-xs)"),
    (r"(\bpadding\s*:\s*)4px\s+8px", r"\1var(--spacing-2xs) var(--spacing-xs)"),
    (
        r"(\bpadding\s*:\s*)6px\s+16px\s+6px\s+8px",
        r"\1var(--spacing-xs) var(--spacing-xl) var(--spacing-xs) var(--spacing-xs)",
    ),
    (r"(\bpadding\s*:\s*)10px\s+14px", r"\1var(--spacing-sm) var(--spacing-md)"),
    (r"(\bpadding\s*:\s*)10px\s+16px", r"\1var(--spacing-sm) var(--spacing-md)"),
    (r"(\bpadding\s*:\s*)10px\s+12px", r"\1var(--spacing-sm) var(--spacing-sm)"),
    (r"(\bpadding\s*:\s*)18px\s+24px", r"\1var(--spacing-lg) var(--spacing-xl)"),
    (r"(\bpadding\s*:\s*)14px\s+18px", r"\1var(--spacing-md) var(--spacing-lg)"),
    (r"(\bpadding\s*:\s*)10px\s+20px", r"\1var(--spacing-sm) var(--spacing-lg)"),
    (r"(\bpadding\s*:\s*)12px\s+20px", r"\1var(--spacing-sm) var(--spacing-lg)"),
    (r"(\bpadding\s*:\s*)16px\s+20px", r"\1var(--spacing-md) var(--spacing-lg)"),
    (r"(\bpadding\s*:\s*)1px\s+5px", r"\1var(--spacing-2xs) var(--spacing-2xs)"),
    (r"padding:\s*3px\s+8px", "padding: var(--spacing-2xs) var(--spacing-xs)"),
    (
        r"padding:\s*var\(--spacing-xs\)\s+18px",
        "padding: var(--spacing-xs) var(--spacing-lg)",
    ),
    (r"padding:\s*0\s+4px", "padding: 0 var(--spacing-2xs)"),
    (r"padding-bottom:\s*4px\b", "padding-bottom: var(--spacing-2xs)"),
    (r"padding:\s*1px\s+4px", "padding: var(--spacing-2xs) var(--spacing-2xs)"),
    (r"padding-bottom:\s*16px\b", "padding-bottom: var(--spacing-md)"),
    (r"margin:\s*0\s+0\s+6px\s+0", "margin: 0 0 var(--spacing-2xs) 0"),
    (r"margin:\s*0\s+0\s+4px\s+0", "margin: 0 0 var(--spacing-2xs) 0"),
    (r"padding:\s*14px\s+16px", "padding: var(--spacing-sm) var(--spacing-md)"),
    (
        r"padding:\s*var\(--spacing-2xl\)\s+16px",
        "padding: var(--spacing-2xl) var(--spacing-md)",
    ),
    (
        r"padding:\s*var\(--spacing-xs\)\s+16px",
        "padding: var(--spacing-xs) var(--spacing-md)",
    ),
    (r"margin-top:\s*20px\b", "margin-top: var(--spacing-lg)"),
    (r"margin:\s*-20px\b", "margin: calc(-1 * var(--spacing-lg))"),
    (r"margin-top:\s*32px\b", "margin-top: var(--spacing-2xl)"),
    (r"margin-left:\s*8px\b", "margin-left: var(--spacing-xs)"),
    (r"margin-left:\s*16px\b", "margin-left: var(--spacing-md)"),
    (r"margin-left:\s*12px\b", "margin-left: var(--spacing-sm)"),
    (r"padding-left:\s*8px\b", "padding-left: var(--spacing-xs)"),
    (r"padding-right:\s*4px\b", "padding-right: var(--spacing-2xs)"),
    (r"padding-bottom:\s*10px\b", "padding-bottom: var(--spacing-xs)"),
    (r"margin:\s*0\s+0\s+10px\s+0", "margin: 0 0 var(--spacing-xs) 0"),
    (r"margin:\s*0\s+0\s+12px\s+0", "margin: 0 0 var(--spacing-sm) 0"),
    (r"margin:\s*0\s+0\s+8px\s+0", "margin: 0 0 var(--spacing-xs) 0"),
    (r"margin:\s*2px\s+0\s+0\s+0", "margin: var(--spacing-2xs) 0 0 0"),
    (r"padding:\s*0\s+8px", "padding: 0 var(--spacing-xs)"),
    (r"padding:\s*6px\s+10px", "padding: var(--spacing-2xs) var(--spacing-xs)"),
    (r"padding:\s*6px\b", "padding: var(--spacing-2xs)"),
    (
        r"padding:\s*var\(--spacing-xs\)\s+10px",
        "padding: var(--spacing-xs) var(--spacing-xs)",
    ),
    (r"(\bmargin\s*:\s*0\s+0\s+)16px(\s+0)", r"\1var(--spacing-md)\2"),
    (r"(\bmargin\s*:\s*0\s+0\s+)8px(\s+0)", r"\1var(--spacing-xs)\2"),
    (r"(\bmargin\s*:\s*0\s+0\s+)12px(\s+0)", r"\1var(--spacing-sm)\2"),
    (r"(\bmargin\s*:\s*0\s+0\s+)10px(\s+0)", r"\1var(--spacing-xs)\2"),
    (r"(\bmargin\s*:\s*0\s+0\s+)6px(\s+0)", r"\1var(--spacing-2xs)\2"),
    (r"(\bmargin\s*:\s*0\s+0\s+)4px(\s+0)", r"\1var(--spacing-2xs)\2"),
    (r"(\bpadding\s*:\s*)2px\s+4px", r"\1var(--spacing-2xs) var(--spacing-2xs)"),
    (r"(\bgap\s*:\s*var\(--spacing-2xs\)\s+)12px\b", r"\1var(--spacing-sm)"),
    (r"(\bgap\s*:\s*)24px\b", r"\1var(--spacing-xl)"),
    (r"(\bgap\s*:\s*)20px\b", r"\1var(--spacing-lg)"),
    (r"(\bgap\s*:\s*)16px\b", r"\1var(--spacing-md)"),
    (r"(\bgap\s*:\s*)14px\b", r"\1var(--spacing-lg)"),
    (r"(\bgap\s*:\s*)12px\b", r"\1var(--spacing-sm)"),
    (r"(\bgap\s*:\s*)10px\b", r"\1var(--spacing-xs)"),
    (r"(\bgap\s*:\s*)8px\b", r"\1var(--spacing-xs)"),
    (r"(\bgap\s*:\s*)6px\b", r"\1var(--spacing-2xs)"),
    (r"(\bgap\s*:\s*)4px\b", r"\1var(--spacing-2xs)"),
    (r"(\bmargin-top\s*:\s*)24px\b", r"\1var(--spacing-xl)"),
    (r"(\bmargin-top\s*:\s*)16px\b", r"\1var(--spacing-md)"),
    (r"(\bmargin-top\s*:\s*)12px\b", r"\1var(--spacing-sm)"),
    (r"(\bmargin-top\s*:\s*)8px\b", r"\1var(--spacing-xs)"),
    (r"(\bmargin-top\s*:\s*)6px\b", r"\1var(--spacing-2xs)"),
    (r"(\bmargin-top\s*:\s*)4px\b", r"\1var(--spacing-2xs)"),
    (r"(\bmargin-top\s*:\s*)2px\b", r"\1var(--spacing-2xs)"),
    (r"(\bmargin-bottom\s*:\s*)24px\b", r"\1var(--spacing-xl)"),
    (r"(\bmargin-bottom\s*:\s*)20px\b", r"\1var(--spacing-lg)"),
    (r"(\bmargin-bottom\s*:\s*)16px\b", r"\1var(--spacing-md)"),
    (r"(\bmargin-bottom\s*:\s*)12px\b", r"\1var(--spacing-sm)"),
    (r"(\bmargin-bottom\s*:\s*)10px\b", r"\1var(--spacing-xs)"),
    (r"(\bmargin-bottom\s*:\s*)8px\b", r"\1var(--spacing-xs)"),
    (r"(\bmargin-bottom\s*:\s*)6px\b", r"\1var(--spacing-2xs)"),
    (r"(\bmargin-bottom\s*:\s*)4px\b", r"\1var(--spacing-2xs)"),
    (r"(\bmargin-right\s*:\s*)8px\b", r"\1var(--spacing-xs)"),
    (r"(\bpadding-left\s*:\s*)14px\b", r"\1var(--spacing-md)"),
    (r"(\bpadding-left\s*:\s*)12px\b", r"\1var(--spacing-sm)"),
    (r"(\bpadding-bottom\s*:\s*)12px\b", r"\1var(--spacing-sm)"),
    (r"(\bpadding-bottom\s*:\s*)6px\b", r"\1var(--spacing-2xs)"),
    (r"(\bpadding-top\s*:\s*)16px\b", r"\1var(--spacing-md)"),
    (r"(\bpadding-top\s*:\s*)10px\b", r"\1var(--spacing-sm)"),
    (r"(\bpadding-top\s*:\s*)12px\b", r"\1var(--spacing-sm)"),
    (r"(\btop\s*:\s*)16px\b", r"\1var(--spacing-md)"),
    (r"(\bright\s*:\s*)16px\b", r"\1var(--spacing-md)"),
    (r"(\bright\s*:\s*)6px\b", r"\1var(--spacing-2xs)"),
    (r"(\bpadding\s*:\s*)48px\b", r"\1var(--spacing-4xl)"),
    (r"(\bpadding\s*:\s*)40px\b", r"\1var(--spacing-3xl)"),
    (r"(\bpadding\s*:\s*)32px\b", r"\1var(--spacing-2xl)"),
    (r"(\bpadding\s*:\s*)30px\b", r"\1var(--spacing-2xl)"),
    (r"(\bpadding\s*:\s*)24px\b", r"\1var(--spacing-xl)"),
    (r"(\bpadding\s*:\s*)20px\b", r"\1var(--spacing-lg)"),
    (r"(\bpadding\s*:\s*)16px\b", r"\1var(--spacing-md)"),
    (r"(\bpadding\s*:\s*)12px\b", r"\1var(--spacing-sm)"),
    (r"(\bpadding\s*:\s*)10px\b", r"\1var(--spacing-xs)"),
    (r"(\bpadding\s*:\s*)8px\b", r"\1var(--spacing-xs)"),
    (r"(\bpadding\s*:\s*)4px\b", r"\1var(--spacing-2xs)"),
    (r"(\bmargin\s*:\s*)12px\s+0", r"\1var(--spacing-sm) 0"),
]


def fix_css_content(content):
    for pattern, repl in REPLACEMENTS:
        content = re.sub(pattern, repl, content, flags=re.IGNORECASE)
    return content


def fix_file(filepath):
    print(f"Fixing {filepath}...")
    with open(filepath, encoding="utf-8") as f:
        content = f.read()

    if filepath.endswith(".css"):
        fixed_content = fix_css_content(content)
    elif filepath.endswith(".vue"):
        fixed_content = content
        # Find style block(s) and only perform replacements inside them
        style_matches = list(
            re.finditer(r"(<style[^>]*>)(.*?)(</style>)", content, re.DOTALL)
        )
        for match in reversed(style_matches):
            start_tag = match.group(1)
            style_inner = match.group(2)
            end_tag = match.group(3)

            fixed_inner = fix_css_content(style_inner)
            fixed_content = (
                fixed_content[: match.start()]
                + start_tag
                + fixed_inner
                + end_tag
                + fixed_content[match.end() :]
            )

    with open(filepath, "w", encoding="utf-8") as f:
        f.write(fixed_content)


def main():
    # Standalone Stylesheets
    standalone_files = [
        "packages/ui/tokens.css",
        "packages/ui/responsive.css",
        "apps/subject-portal/style.css",
        "apps/web/src/style.css",
    ]
    files_to_fix = [os.path.join("/app", f) for f in standalone_files]

    # Vue components directories
    vue_dirs = [
        "packages/ui/src/components/clinical",
        "apps/subject-portal",
        "apps/web",
    ]

    for vdir in vue_dirs:
        abs_vdir = os.path.join("/app", vdir)
        if not os.path.exists(abs_vdir):
            continue
        for dirpath, dirnames, filenames in os.walk(abs_vdir):
            if any(
                ignored in dirpath.split(os.sep)
                for ignored in (
                    "node_modules",
                    "dist",
                    ".nuxt",
                    "build",
                    ".git",
                    ".cache",
                    "coverage",
                    ".pytest_cache",
                )
            ):
                continue
            for filename in filenames:
                if filename.endswith(".vue"):
                    files_to_fix.append(os.path.join(dirpath, filename))

    files_to_fix = sorted(list(set(files_to_fix)))
    for f in files_to_fix:
        if "tokens.css" in f:
            continue
        fix_file(f)

    print("\n[Auto-fixing completed successfully!]\n")


if __name__ == "__main__":
    main()
