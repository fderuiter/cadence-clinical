import re

with open("apps/execution/routers/doa.py") as file:
    content = file.read()

content = re.sub(
    r"<<<<<<< HEAD\n(.*?)\n=======\n(.*?)>>>>>>> [^\n]*\n",
    r"\2",
    content,
    flags=re.DOTALL,
)

with open("apps/execution/routers/doa.py", "w") as file:
    file.write(content)
