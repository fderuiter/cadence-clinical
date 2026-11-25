// Accessible inline validation messages for generated form fields.

function appendDescribedByToken(el, token) {
  if (!el || !token) return;
  const existing = (el.getAttribute("aria-describedby") || "")
    .trim()
    .split(/\s+/)
    .filter(Boolean);
  if (!existing.includes(token)) {
    existing.push(token);
  }
  el.setAttribute("aria-describedby", existing.join(" "));
}

function removeDescribedByToken(el, token) {
  if (!el || !token) return;
  const remaining = (el.getAttribute("aria-describedby") || "")
    .trim()
    .split(/\s+/)
    .filter((item) => item && item !== token);
  if (remaining.length > 0) {
    el.setAttribute("aria-describedby", remaining.join(" "));
  } else {
    el.removeAttribute("aria-describedby");
  }
}

// Removes a field's previous error message and its aria-invalid /
// aria-describedby error token while preserving base describedby links.
export function clearFieldInvalid(fieldId) {
  const errId = `error-msg-${fieldId}`;
  const container = document.getElementById(`field-container-${fieldId}`);
  if (container) {
    container.classList.remove("has-error");
    container
      .querySelectorAll(".validation-error-msg")
      .forEach((oldErr) => oldErr.remove());

    const radioInputs = container.querySelectorAll(`input[name="${fieldId}"]`);
    radioInputs.forEach((radio) => {
      removeDescribedByToken(radio, errId);
      radio.removeAttribute("aria-invalid");
    });
  }

  const input = document.getElementById(fieldId);
  if (input) {
    removeDescribedByToken(input, errId);
    input.removeAttribute("aria-invalid");
  }
  const radios = document.querySelectorAll(`input[name="${fieldId}"]`);
  radios.forEach((r) => {
    removeDescribedByToken(r, errId);
    r.removeAttribute("aria-invalid");
  });
}

// Marks a field invalid with an inline role="alert" message linked through aria-describedby.
export function markFieldInvalid(fieldId, msg) {
  const errId = `error-msg-${fieldId}`;
  const container = document.getElementById(`field-container-${fieldId}`);
  if (container) {
    container.classList.add("has-error");
    container
      .querySelectorAll(
        `#${CSS.escape ? CSS.escape(errId) : errId}, .validation-error-msg`
      )
      .forEach((oldErr) => oldErr.remove());

    const errDiv = document.createElement("div");
    errDiv.id = errId;
    errDiv.className = "validation-error-msg";
    errDiv.setAttribute("role", "alert");
    errDiv.style.color = "var(--danger)";
    errDiv.style.fontSize = "12px";
    errDiv.style.fontWeight = "600";
    errDiv.style.marginTop = "4px";
    errDiv.textContent = msg;
    container.appendChild(errDiv);

    // Link directly to the input elements using aria-describedby and aria-invalid
    const input = document.getElementById(fieldId);
    if (input) {
      appendDescribedByToken(input, errId);
      input.setAttribute("aria-invalid", "true");
    }
    const radios = document.querySelectorAll(`input[name="${fieldId}"]`);
    radios.forEach((r) => {
      appendDescribedByToken(r, errId);
      r.setAttribute("aria-invalid", "true");
    });
  }
}
