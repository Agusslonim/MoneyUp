document.addEventListener('DOMContentLoaded', () => {
  const forms = document.querySelectorAll('form');

  forms.forEach((form) => {
    const messageTarget = form.querySelector('[data-valmsg-summary="true"]') ?? form.querySelector('.validation-summary-errors');

    form.addEventListener('submit', (event) => {
      const invalidInputs = form.querySelectorAll('input:invalid');
      if (invalidInputs.length === 0) {
        return;
      }

      event.preventDefault();
      invalidInputs[0].focus();
      invalidInputs[0].reportValidity();

      if (messageTarget) {
        messageTarget.textContent = 'Corregí los campos marcados antes de continuar.';
      }
    });
  });

  document.querySelectorAll('.toggle-password').forEach((button) => {
    button.addEventListener('click', () => {
      const targetId = button.dataset.target;
      const input = document.getElementById(targetId);
      if (!input) return;
      const willShow = input.type === 'password';
      input.type = willShow ? 'text' : 'password';
      button.textContent = willShow ? 'Ocultar' : 'Mostrar';
      button.setAttribute('aria-label', willShow ? 'Ocultar contraseña' : 'Mostrar contraseña');
    });
  });
});
