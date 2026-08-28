---
name: grill-me
description: "Grill the user relentlessly about a plan, decision, or architecture. Use when the user wants to stress-test their thinking or uses any 'grill' trigger phrase."
license: MIT
metadata:
  author: Cadence Team
  version: "2.0"
---

# Cadence Grilling Protocol

Interview the user relentlessly until reaching a shared, unambiguous understanding. Map this as a **design tree**: every decision branches into the decisions that depend on it.

## The Grilling Loop

1. **Rounds & The Frontier**:
   - The **frontier** contains every decision whose prerequisites are settled.
   - Ask the entire frontier in one round.
   - Number each question and provide your recommended answer.

2. **Formatting**:
   ```
   ❓ **Q1** - **<question title>**: <question body with clear trade-offs and options>

   ➡️ <your recommended answer>
   ```

3. **Autonomous Fact-Finding**:
   - Facts are the agent's job. When a question requires repo, file, or schema facts, look them up or dispatch a `research` subagent. Never ask the user for information available in the environment.

4. **Iterate**:
   - Each round of user answers settles decisions and advances the frontier outward.
   - The session ends when the frontier is empty and no unspoken assumptions remain.
