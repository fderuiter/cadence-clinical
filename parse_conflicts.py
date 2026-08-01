import re

with open("apps/web/src/views/RulesView.vue") as f:
    text = f.read()

parts = re.split(
    r"<<<<<<< HEAD\n(.*?)\n=======\n(.*?)\n>>>>>>> origin/main\n", text, flags=re.DOTALL
)
print(f"Number of parts: {len(parts)}. (Means {len(parts) // 3} conflicts)")
for i in range(1, len(parts), 3):
    print(f"--- Conflict {i // 3 + 1} ---")
    print("HEAD starts with:", parts[i][:50].replace("\n", " "))
    print("origin starts with:", parts[i + 1][:50].replace("\n", " "))
