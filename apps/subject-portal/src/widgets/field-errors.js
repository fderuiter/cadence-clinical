// Accessible inline validation messages for generated form fields.

// Removes a field's previous error message and its aria-invalid /
// aria-describedby links (text input or radio group).
export function clearFieldInvalid(fieldId) {
  const container = document.getElementById(`field-container-${fieldId}`);
  if (container) {
    container.classList.remove("has-error");
    const oldErr = container.querySelector(".validation-error-msg");
    if (oldErr) oldErr.remove();

    const input = document.getElementById(fieldId);
    if (input) {
      input.removeAttribute("aria-describedby");
      input.removeAttribute("aria-invalid");
    } else {
      const radioInputs = container.querySelectorAll(
        `input[name="${fieldId}"]`
      );
      radioInputs.forEach((radio) => {
        radio.removeAttribute("aria-describedby");
        radio.removeAttribute("aria-invalid");
      });
    }
  }
  const input = document.getElementById(fieldId);
  if (input) {
    input.removeAttribute("aria-invalid");
    input.removeAttribute("aria-describedby");
  }
  const radios = document.querySelectorAll(`input[name="${fieldId}"]`);
  radios.forEach((r) => {
    r.removeAttribute("aria-invalid");
    r.removeAttribute("aria-describedby");
  });
}

// Marks a field invalid with an inline message linked through aria-describedby.
export function markFieldInvalid(fieldId, msg) {
  const container = document.getElementById(`field-container-${fieldId}`);
  if (container) {
    container.classList.add("has-error");
    const errDiv = document.createElement("div");
    errDiv.id = `error-msg-${fieldId}`;
    errDiv.className = "validation-error-msg";
    errDiv.style.color = "var(--danger)";
    errDiv.style.fontSize = "var(--font-size-2xs)";
    errDiv.style.fontWeight = "600";
    errDiv.style.marginTop = "4px";
    errDiv.textContent = msg;
    container.appendChild(errDiv);

    // Link directly to the input elements using aria-describedby and aria-invalid
    const input = document.getElementById(fieldId);
    if (input) {
      input.setAttribute("aria-describedby", `error-msg-${fieldId}`);
      input.setAttribute("aria-invalid", "true");
    }
    const radios = document.querySelectorAll(`input[name="${fieldId}"]`);
    radios.forEach((r) => {
      r.setAttribute("aria-describedby", `error-msg-${fieldId}`);
      r.setAttribute("aria-invalid", "true");
    });
  }
}
