import re

files = [
    "apps/web/src/components/auditor/AuditorExportModal.vue",
    "apps/web/src/components/clinical/ClinicalSoAMatrix.vue",
    "apps/web/src/components/econsent/ComprehensionQuizBuilder.vue",
    "apps/web/src/components/etmf/DocumentGrid.vue",
    "apps/web/src/components/etmf/PdfPreviewModal.vue",
    "apps/web/src/views/AuditView.vue",
    "apps/web/src/views/ICFBuilderView.vue",
    "apps/web/src/views/RulesView.vue",
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
