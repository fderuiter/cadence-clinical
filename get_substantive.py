import os
import re

files = [
    "apps/execution/routers/doa.py",
    "apps/web/src/components/auditor/AuditTrailViewer.vue",
    "apps/web/src/components/auditor/AuditorExportModal.vue",
    "apps/web/src/components/econsent/ComprehensionQuizBuilder.vue",
    "apps/web/src/views/EcrfView.vue",
    "apps/web/src/views/MdrView.vue",
    "apps/web/src/views/RulesView.vue",
]


def clean_str(s):
    s = re.sub(r"\s+", "", s)
    s = s.replace("/>", ">")
    return s


for f in files:
    if not os.path.exists(f):
        continue
    with open(f) as file:
        content = file.read()

    matches = re.findall(
        r"<<<<<<< HEAD\n(.*?)\n=======\n(.*?)>>>>>>> [^\n]*\n", content, re.DOTALL
    )

    new_content = content
    manual_needed = False

    for head, other in matches:
        if clean_str(head) == clean_str(other):
            new_content = re.sub(
                r"<<<<<<< HEAD\n.*?\n=======\n.*?>>>>>>> [^\n]*\n",
                other,
                new_content,
                count=1,
                flags=re.DOTALL,
            )
        else:
            print(f"--- {f} CONFLICT ---")
            if len(head) > 500 or len(other) > 500:
                print("HEAD len:", len(head), "OTHER len:", len(other))
            else:
                print("HEAD:\n", head)
                print("OTHER:\n", other)
            manual_needed = True

    if not manual_needed:
        with open(f, "w") as file:
            file.write(new_content)
        print(f"Auto-resolved formatting conflicts in {f}")
