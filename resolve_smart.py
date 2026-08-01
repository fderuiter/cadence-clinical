import os
import re

files = [
    "apps/execution/routers/doa.py",
    "apps/web/src/components/SignatureCaptureModal.vue",
    "apps/web/src/components/auditor/AuditTrailViewer.vue",
    "apps/web/src/components/auditor/AuditorExportModal.vue",
    "apps/web/src/components/clinical/ClinicalQueryPanel.vue",
    "apps/web/src/components/clinical/ClinicalSoAMatrix.vue",
    "apps/web/src/components/econsent/ComprehensionQuizBuilder.vue",
    "apps/web/src/components/etmf/DocumentGrid.vue",
    "apps/web/src/components/etmf/PdfPreviewModal.vue",
    "apps/web/src/views/AuditView.vue",
    "apps/web/src/views/ConsentAuthoringView.vue",
    "apps/web/src/views/EcrfView.vue",
    "apps/web/src/views/ICFBuilderView.vue",
    "apps/web/src/views/MdrView.vue",
    "apps/web/src/views/RulesView.vue",
]


def clean_str(s):
    return re.sub(r"\s+", "", s)


for f in files:
    if not os.path.exists(f):
        continue
    with open(f) as file:
        content = file.read()

    matches = re.findall(
        r"<<<<<<< HEAD\n(.*?)\n=======\n(.*?)>>>>>>> [^\n]*\n", content, re.DOTALL
    )
    if not matches:
        continue

    new_content = content
    manual_needed = False

    for head, other in matches:
        conflict_block = f"<<<<<<< HEAD\n{head}\n=======\n{other}>>>>>>> "
        # Check if it's just formatting
        if clean_str(head) == clean_str(other):
            # Take other (the refactored format)
            new_content = re.sub(
                r"<<<<<<< HEAD\n.*?\n=======\n.*?>>>>>>> [^\n]*\n",
                other,
                new_content,
                count=1,
                flags=re.DOTALL,
            )
        else:
            print(f"Substantive conflict in {f}")
            manual_needed = True
            break

    if not manual_needed:
        with open(f, "w") as file:
            file.write(new_content)
        print(f"Auto-resolved formatting conflicts in {f}")
