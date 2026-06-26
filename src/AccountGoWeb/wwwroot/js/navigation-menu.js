/**
 * Navigation Menu Controller
 * Handles menu dropdown collapse/expand and active menu item highlighting
 */

(function () {
    'use strict';

    /**
     * Initialize navigation on page load
     */
    function initializeNavigation() {
        setupMobileSidebarToggle();
        setupDropdownToggles();
        restoreMenuState();
        highlightActiveMenuItem();
        expandParentOfActiveItem();
    }

    /**
     * Set up mobile sidebar show/hide behavior for the header hamburger button
     */
    function setupMobileSidebarToggle() {
        const sidebarToggle = document.querySelector('.sidebar-toggler');
        const sidebarLinks = document.querySelectorAll('.sidebar .nav-link:not(.nav-dropdown-toggle)');

        if (!sidebarToggle) {
            return;
        }

        sidebarToggle.setAttribute('aria-controls', 'sidebar');
        sidebarToggle.setAttribute('aria-expanded', document.body.classList.contains('sidebar-show').toString());

        sidebarToggle.addEventListener('click', function (e) {
            e.preventDefault();

            document.body.classList.toggle('sidebar-show');
            sidebarToggle.setAttribute('aria-expanded', document.body.classList.contains('sidebar-show').toString());
        });

        sidebarLinks.forEach(link => {
            link.addEventListener('click', function () {
                if (window.innerWidth < 992) {
                    document.body.classList.remove('sidebar-show');
                    sidebarToggle.setAttribute('aria-expanded', 'false');
                }
            });
        });
    }

    /**
     * Set up click handlers for dropdown toggles
     */
    function setupDropdownToggles() {
        const dropdownToggles = document.querySelectorAll('.nav-dropdown-toggle');

        dropdownToggles.forEach(toggle => {
            toggle.addEventListener('click', function (e) {
                e.preventDefault();

                const parentItem = this.closest('.nav-dropdown');
                if (parentItem) {
                    toggleDropdown(parentItem);
                }
            });
        });
    }

    /**
     * Toggle a dropdown menu open/closed
     */
    function toggleDropdown(dropdownElement) {
        const isOpen = dropdownElement.classList.contains('open');

        if (isOpen) {
            // Close the dropdown
            dropdownElement.classList.remove('open');
        } else {
            // Open the dropdown
            dropdownElement.classList.add('open');
        }

        // Save state to sessionStorage
        saveMenuState();
    }

    /**
     * Highlight the active menu item based on current URL
     */
    function highlightActiveMenuItem() {
        const currentPath = window.location.pathname.toLowerCase();
        const menuLinks = document.querySelectorAll('.sidebar .nav-link:not(.nav-dropdown-toggle)');

        // Get last known active item from sessionStorage
        const lastActiveHref = sessionStorage.getItem('accountgo-active-item');

        // Remove active class from all links first
        menuLinks.forEach(link => {
            link.classList.remove('active');
        });

        // Find and highlight the matching link
        let matchedLink = null;
        let longestMatch = 0;
        let matchType = '';

        // First pass: Look for exact or prefix matches
        menuLinks.forEach(link => {
            const href = link.getAttribute('href');

            if (href && href !== '#' && href !== 'javascript:void(0)') {
                const hrefLower = href.toLowerCase();

                // Exact match gets highest priority
                if (currentPath === hrefLower) {
                    matchedLink = link;
                    longestMatch = 999999; // Ensure exact match wins
                    matchType = 'exact';
                }
                // Check if current path starts with href (for subroutes like /Sales/Orders/Edit/123)
                else if (matchType !== 'exact' && currentPath.startsWith(hrefLower) && href.length > longestMatch && hrefLower !== '/') {
                    matchedLink = link;
                    longestMatch = href.length;
                    matchType = 'prefix';
                }
            }
        });

        // If we found a match, use it
        if (matchedLink) {
            matchedLink.classList.add('active');

            // Store active item URL in sessionStorage for persistence
            sessionStorage.setItem('accountgo-active-item', matchedLink.getAttribute('href'));
        } else {
            // No exact or prefix match found - restore last known active item
            if (lastActiveHref) {
                const lastActiveLink = Array.from(menuLinks).find(link =>
                    link.getAttribute('href')?.toLowerCase() === lastActiveHref.toLowerCase()
                );
                if (lastActiveLink) {
                    lastActiveLink.classList.add('active');
                }
            }
        }
    }

    /**
     * Expand the parent dropdown of the active menu item
     */
    function expandParentOfActiveItem() {
        const activeLink = document.querySelector('.sidebar .nav-link.active');

        if (activeLink) {
            // Find parent dropdown
            const parentDropdown = activeLink.closest('.nav-dropdown');

            if (parentDropdown && !parentDropdown.classList.contains('open')) {
                parentDropdown.classList.add('open');
                // Don't save state here - restoreMenuState should handle it
            }
        }
    }

    /**
     * Save menu state to sessionStorage
     */
    function saveMenuState() {
        const openDropdowns = [];
        const dropdowns = document.querySelectorAll('.nav-dropdown');

        dropdowns.forEach((dropdown, index) => {
            if (dropdown.classList.contains('open')) {
                openDropdowns.push(index);
            }
        });

        sessionStorage.setItem('accountgo-menu-state', JSON.stringify(openDropdowns));
    }

    /**
     * Restore menu state from sessionStorage
     */
    function restoreMenuState() {
        const savedState = sessionStorage.getItem('accountgo-menu-state');

        if (savedState) {
            try {
                const openDropdowns = JSON.parse(savedState);
                const dropdowns = document.querySelectorAll('.nav-dropdown');

                openDropdowns.forEach(index => {
                    if (dropdowns[index]) {
                        dropdowns[index].classList.add('open');
                    }
                });
            } catch (e) {
                console.error('Error restoring menu state:', e);
            }
        }
    }

    /**
     * Clear menu state (useful for testing)
     */
    function clearMenuState() {
        sessionStorage.removeItem('accountgo-menu-state');
    }

    // Initialize when DOM is ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initializeNavigation);
    } else {
        initializeNavigation();
    }

    // Expose functions globally for debugging
    window.NavigationMenu = {
        clearState: clearMenuState,
        saveState: saveMenuState,
        restoreState: restoreMenuState,
        highlightActive: highlightActiveMenuItem
    };

})();
