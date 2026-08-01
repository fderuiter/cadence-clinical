import re

with open("apps/web/src/views/RulesView.vue") as f:
    content = f.read()


# We want to replace all conflict blocks with the 'theirs' part (from ======= to >>>>>>> origin/main)
def replacer(match):
    # match.group(1) is HEAD
    # match.group(2) is theirs
    return match.group(2).strip("\n")


new_content = re.sub(
    r"<<<<<<< HEAD\n(.*?)\n=======\n(.*?)\n>>>>>>> origin/main",
    replacer,
    content,
    flags=re.DOTALL,
)

with open("apps/web/src/views/RulesView.vue", "w") as f:
    f.write(new_content)
