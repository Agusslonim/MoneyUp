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

  const passwordButtons = document.querySelectorAll('.toggle-password');

  passwordButtons.forEach((button) => {
    const input = document.getElementById(button.dataset.target);
    const icon = button.querySelector('.toggle-password-icon');

    if (!input || !icon) {
      return;
    }

    const setState = (isVisible) => {
      input.type = isVisible ? 'text' : 'password';
      button.setAttribute('aria-label', isVisible ? 'Ocultar contraseña' : 'Mostrar contraseña');
      button.setAttribute('aria-pressed', String(isVisible));
      button.title = isVisible ? 'Ocultar contraseña' : 'Mostrar contraseña';
      icon.classList.toggle('is-visible', isVisible);
    };

    setState(false);

    button.addEventListener('click', (event) => {
      event.preventDefault();
      const isVisible = input.type === 'text';
      setState(!isVisible);
    });
  });
});
