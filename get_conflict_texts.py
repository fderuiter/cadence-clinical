import re

with open("apps/web/src/views/RulesView.vue") as f:
    text = f.read()

parts = re.split(
    r"<<<<<<< HEAD\n(.*?)\n=======\n(.*?)\n>>>>>>> origin/main\n", text, flags=re.DOTALL
)
print("--- CONFLICT 4 HEAD ---")
print(parts[4 * 3 - 2][-200:])
print("--- CONFLICT 4 ORIGIN ---")
print(parts[4 * 3 - 1])

print("--- CONFLICT 5 HEAD ---")
print(parts[5 * 3 - 2])
print("--- CONFLICT 5 ORIGIN ---")
print(parts[5 * 3 - 1])

print("--- CONFLICT 6 HEAD ---")
print(parts[6 * 3 - 2])
print("--- CONFLICT 6 ORIGIN ---")
print(parts[6 * 3 - 1])
