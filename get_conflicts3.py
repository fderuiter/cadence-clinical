import re

files = [
    "apps/web/src/views/ConsentAuthoringView.vue",
    "apps/web/src/views/EcrfView.vue",
    "apps/web/src/views/MdrView.vue",
]

for f in files:
    with open(f) as file:
        content = file.read()

    matches = re.findall(
        r"<<<<<<< HEAD\n(.*?)\n=======\n(.*?)>>>>>>> [^\n]*\n", content, re.DOTALL
    )
    for i, (head, other) in enumerate(matches):
        print(f"--- {f} CONFLICT {i} ---")
        if len(head) > 500 or len(other) > 500:
            print("HEAD length:", len(head), "OTHER length:", len(other))
        else:
            print("HEAD:\n", head)
            print("OTHER:\n", other)
