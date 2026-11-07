// Close the mobile sidebar when tapping outside it.
document.addEventListener('click', function (e) {
    if (!document.body.classList.contains('pm-nav-open')) return;
    if (e.target.closest('.pm-sidebar') || e.target.closest('.pm-menu-btn')) return;
    document.body.classList.remove('pm-nav-open');
});
