import re

with open("apps/web/src/views/RulesView.vue", "r") as file:
    content = file.read()

# Since we just want the origin/main version of the text, let's just strip out the conflict markers and keep the main part, or remove HEAD block.
content = re.sub(r"<<<<<<< HEAD\n(.*?)\n=======\n", "", content, flags=re.DOTALL)
content = re.sub(r">>>>>>> origin/main\n", "", content, flags=re.DOTALL)

with open("apps/web/src/views/RulesView.vue", "w") as file:
    file.write(content)
