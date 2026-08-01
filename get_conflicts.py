import re

files = [
    "apps/execution/routers/doa.py",
    "apps/web/src/components/SignatureCaptureModal.vue",
]

for f in files:
    with open(f) as file:
        content = file.read()

    matches = re.findall(
        r"<<<<<<< HEAD\n(.*?)\n=======\n(.*?)>>>>>>> [^\n]*\n", content, re.DOTALL
    )
    for i, (head, other) in enumerate(matches):
        print(f"--- {f} CONFLICT {i} ---")
        print("HEAD:\n", head)
        print("OTHER:\n", other)
