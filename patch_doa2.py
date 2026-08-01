with open("apps/execution/routers/doa.py") as f:
    content = f.read()

content = content.replace(
    "import packages  # noqa: F401\nfrom apps.execution.services.doa_service import DOAService\nfrom packages.security.middleware import get_current_user",
    "",
)

content = (
    "import packages  # noqa: F401\nfrom apps.execution.services.doa_service import DOAService\nfrom packages.security.middleware import get_current_user\n"
    + content
)

with open("apps/execution/routers/doa.py", "w") as f:
    f.write(content)
