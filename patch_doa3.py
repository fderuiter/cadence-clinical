with open("apps/execution/routers/doa.py") as f:
    content = f.read()

# find the docstring end
import re

match = re.search(r'"""\n', content)
if match:
    idx = match.end()
    # move the 3 imports to after the docstring
    # wait, they are at the very top.
    lines = content.split("\n")
    imports = lines[:3]
    rest = lines[3:]
    # join rest
    rest_str = "\n".join(rest)
    match2 = re.search(r'"""\n', rest_str)
    if match2:
        idx2 = match2.end()
        final_str = rest_str[:idx2] + "\n" + "\n".join(imports) + "\n" + rest_str[idx2:]
        with open("apps/execution/routers/doa.py", "w") as f:
            f.write(final_str)
