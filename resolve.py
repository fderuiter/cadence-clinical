import os

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

for f in files:
    if not os.path.exists(f):
        print(f"NOT FOUND: {f}")
        continue
    with open(f) as file:
        content = file.read()
    if "<<<<<<<" in content:
        print(f"CONFLICT IN {f}")
