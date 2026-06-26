/**
 * Theme Toggle Script
 * Handles switching between light and dark themes
 * Persists user preference in localStorage
 */

(function() {
    'use strict';

    // Theme configuration
    const THEMES = {
        LIGHT: {
            name: 'light',
            stylesheet: '/css/light.css',
            icon: 'fa-sun',
            text: 'Light Mode',
            bodyClass: 'light-theme'
        },
        DARK: {
            name: 'dark',
            stylesheet: '/css/dark.css',
            icon: 'fa-moon',
            text: 'Dark Mode',
            bodyClass: 'dark-theme'
        }
    };

    const STORAGE_KEY = 'accountgo-theme-preference';
    const DEFAULT_THEME = THEMES.DARK; // Default to dark theme

    /**
     * Get the current theme from localStorage or default
     */
    function getCurrentTheme() {
        const savedTheme = localStorage.getItem(STORAGE_KEY);
        return savedTheme === 'light' ? THEMES.LIGHT : THEMES.DARK;
    }

    /**
     * Apply the theme to the page
     */
    function applyTheme(theme) {
        const themeStylesheet = document.getElementById('theme-stylesheet');
        const themeIcon = document.getElementById('theme-icon');
        const themeText = document.getElementById('theme-text');
        const body = document.body;

        if (!themeStylesheet) {
            console.error('Theme stylesheet link not found');
            return;
        }

        // Update stylesheet
        themeStylesheet.href = theme.stylesheet;

        // Update body class
        body.classList.remove(THEMES.LIGHT.bodyClass, THEMES.DARK.bodyClass);
        body.classList.add(theme.bodyClass);

        // Update toggle button
        if (themeIcon) {
            themeIcon.className = 'fas ' + theme.icon;
        }

        if (themeText) {
            themeText.textContent = theme.text;
        }

        // Save preference
        localStorage.setItem(STORAGE_KEY, theme.name);

        // Dispatch custom event for other scripts to react
        window.dispatchEvent(new CustomEvent('themeChanged', { 
            detail: { theme: theme.name } 
        }));

        console.log('Theme applied:', theme.name);
    }

    /**
     * Toggle between light and dark themes
     */
    function toggleTheme() {
        const currentTheme = getCurrentTheme();
        const newTheme = currentTheme.name === 'light' ? THEMES.DARK : THEMES.LIGHT;
        applyTheme(newTheme);
    }

    /**
     * Initialize theme on page load
     */
    function initializeTheme() {
        // Apply saved theme immediately (before page renders)
        const savedTheme = getCurrentTheme();
        applyTheme(savedTheme);

        // Set up toggle button
        const toggleButton = document.getElementById('theme-toggle');
        if (toggleButton) {
            toggleButton.addEventListener('click', toggleTheme);
            
            // Add smooth transition
            toggleButton.style.transition = 'all 0.3s ease';
        }

        console.log('Theme system initialized');
    }

    // Initialize as soon as the DOM is ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initializeTheme);
    } else {
        initializeTheme();
    }

    // Expose theme functions globally for debugging or external use
    window.AccountGoTheme = {
        toggle: toggleTheme,
        apply: applyTheme,
        getCurrent: getCurrentTheme,
        themes: THEMES
    };

})();
