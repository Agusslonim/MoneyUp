"use strict";
// Interacciones de la landing. No guardan datos ni llaman a una base de datos.
const toggle = document.querySelector(".menu-toggle");
const nav = document.querySelector(".main-nav");
function closeMenu() {
    if (!toggle || !nav) return;
    nav.classList.remove("is-open");
    toggle.setAttribute("aria-expanded", "false");
    toggle.setAttribute("aria-label", "Abrir menú");
}
if (toggle && nav) {
    toggle.addEventListener("click", () => {
        const open = nav.classList.toggle("is-open");
        toggle.setAttribute("aria-expanded", String(open));
        toggle.setAttribute("aria-label", open ? "Cerrar menú" : "Abrir menú");
    });
    nav.querySelectorAll("a").forEach(link => link.addEventListener("click", closeMenu));
    document.addEventListener("keydown", event => { if (event.key === "Escape") closeMenu(); });
    window.matchMedia("(min-width: 961px)").addEventListener("change", event => { if (event.matches) closeMenu(); });
}
const help = document.getElementById("ayuda-dialog");
document.querySelectorAll("[data-help]").forEach(button => button.addEventListener("click", () => help?.showModal()));
document.querySelector(".close-dialog")?.addEventListener("click", () => help?.close());
help?.addEventListener("click", event => { if (event.target === help) { const r = help.getBoundingClientRect(); if (event.clientX < r.left || event.clientX > r.right || event.clientY < r.top || event.clientY > r.bottom) help.close(); } });
