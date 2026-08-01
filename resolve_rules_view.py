import re

with open("apps/web/src/views/RulesView.vue") as f:
    text = f.read()

parts = re.split(
    r"<<<<<<< HEAD\n(.*?)\n=======\n(.*?)\n>>>>>>> origin/main\n", text, flags=re.DOTALL
)

# parts[0] is text before conflict 1
# parts[1] is HEAD 1
# parts[2] is origin 1
# parts[3] is text between 1 and 2
# parts[4] is HEAD 2
# parts[5] is origin 2
# etc.

# Conflict 1: use origin
parts[1] = parts[2]

# Conflict 2: merge
parts[4] = """          <p class="rules-gating-text">
            You do not have the required <strong>STUDY_DESIGNER</strong> or <strong>DATA_MANAGER</strong> role to
            view or interact with clinical rules and queries. Please
            authenticate with an authorized token or consult your system
            administrator."""

# Conflict 3: use origin
parts[7] = parts[8]

# Conflict 4: use origin
parts[10] = parts[11]

# Conflict 5: use origin
parts[13] = parts[14]

# Conflict 6: use origin
parts[16] = parts[17]

# Reassemble
res = ""
for i in range(0, len(parts)):
    if i % 3 == 0 or i % 3 == 1:
        res += parts[i]
    # skip the origin parts as we already replaced HEAD parts with what we want

with open("apps/web/src/views/RulesView.vue", "w") as f:
    f.write(res)
