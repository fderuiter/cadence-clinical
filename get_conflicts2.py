import re

files = [
    "apps/web/src/components/auditor/AuditTrailViewer.vue",
    "apps/web/src/components/clinical/ClinicalQueryPanel.vue",
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
